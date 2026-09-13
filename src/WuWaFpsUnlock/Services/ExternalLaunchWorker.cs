using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using WuWaFpsUnlock.Core;
namespace WuWaFpsUnlock.Services;

// A fixed-purpose elevated boundary; never accepts a caller-provided executable or command line.
public static class ExternalLaunchWorker
{
    private static string Jobs=>Path.Combine(AppPaths.Data,"jobs");
    private static string RuntimeRoot=>Path.Combine(AppPaths.Base,"components","dotnet8");
    public static ProcessStartInfo BuildElevationStartInfo(string request,string nonce,string digest)
    {
        if(!string.Equals(Path.GetFullPath(request),ExternalLaunchProtocol.RequestPath(Jobs,nonce),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("启动工作请求路径无效。");
        if(digest.Length!=64||digest.Any(c=>!Uri.IsHexDigit(c)))throw new InvalidDataException("启动请求摘要无效。");
        string controller=Path.Combine(AppPaths.Base,"WuWaFpsUnlock.exe");
        if(!File.Exists(controller))throw new FileNotFoundException("请使用完整便携 EXE 包进行管理员启动。",controller);
        var info=new ProcessStartInfo(controller){UseShellExecute=true,Verb="runas",WorkingDirectory=AppPaths.Base};
        info.ArgumentList.Add("--external-launch-worker");info.ArgumentList.Add(request);info.ArgumentList.Add(nonce);info.ArgumentList.Add(digest);
        // Do not even access Environment on this shell/UAC request.
        return info;
    }
    public static async Task<Process> RunFromUi(UserSettings settings,LaunchPlan plan,Action<string> log,CancellationToken token)
    {
        Directory.CreateDirectory(Jobs);SafePaths.EnsureNoLinks(AppPaths.Data,Jobs);
        string nonce=Guid.NewGuid().ToString("N"),request=ExternalLaunchProtocol.RequestPath(Jobs,nonce),result=ExternalLaunchProtocol.ResultPath(Jobs,nonce);
        var snapshot=new ExternalLaunchRequest{Nonce=nonce,CreatedUtc=DateTimeOffset.UtcNow,Settings=settings.Clone(),PlanFingerprint=ExternalLaunchProtocol.Fingerprint(plan)};
        JsonFiles.Save(request,snapshot);bool workerFinished=false;
        try
        {
            token.ThrowIfCancellationRequested();
            var info=BuildElevationStartInfo(request,nonce,await SafePaths.HashAsync(request));
            log("请求标准 UAC：仅授权固定外部启动工作进程；本地 .NET 8 将在提升后通过子进程环境传入。");
            using var worker=Process.Start(info)??throw new IOException("UAC 未返回外部启动工作进程。");
            await worker.WaitForExitAsync(token).WaitAsync(TimeSpan.FromSeconds(60),token);workerFinished=true;
            SafePaths.EnsureNoLinks(Jobs,result);
            if(!File.Exists(result))throw new IOException("外部启动工作进程未返回结果；不会重试或再次启动。请求："+request);
            var response=JsonFiles.Read<ExternalLaunchResult>(result);foreach(var line in response.Log)log(line);
            ExternalLaunchProtocol.ValidateResult(response,nonce,worker.ExitCode);
            var child=Process.GetProcessById(response.ChildPid);
            try
            {
                if(!string.Equals(GameProcesses.ImagePath(child.Id),AppPaths.Unlocker,StringComparison.OrdinalIgnoreCase)||Math.Abs((child.StartTime.ToUniversalTime()-response.ChildStartedUtc).TotalSeconds)>1)
                    throw new IOException("外部启动返回的 PID/路径/时间不匹配；不连接其他进程。");
                return child;
            }
            catch{child.Dispose();throw;}
        }
        catch(System.ComponentModel.Win32Exception e) when(e.NativeErrorCode==1223)
        {workerFinished=true;throw new OperationCanceledException("已取消 UAC；未启动解锁器或游戏，不执行备用启动。");}
        finally
        {
            // On cancellation/timeout after dispatch, preserve protocol files: the worker might still be completing.
            if(workerFinished){if(File.Exists(request))File.Delete(request);if(File.Exists(result))File.Delete(result);}
        }
    }
    public static async Task<int> Execute(string requestPath,string nonce,string expectedDigest)
    {
        var request=ExternalLaunchProtocol.Read(Jobs,requestPath,nonce,expectedDigest,DateTimeOffset.UtcNow);
        var result=new ExternalLaunchResult{Nonce=nonce};string response=ExternalLaunchProtocol.ResultPath(Jobs,nonce);
        try
        {
            using(var claim=ExternalLaunchProtocol.Claim(Jobs,nonce))claim.WriteByte(1);
            using var identity=WindowsIdentity.GetCurrent();
            if(!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))throw new UnauthorizedAccessException("外部启动工作进程未获得 UAC 管理员权限。");
            using var launchGate=new Semaphore(1,1,@"Local\WuWaFPSUnlock_ExternalLaunch");
            if(!launchGate.WaitOne(0))throw new IOException("已有外部启动工作进程；未创建第二实例。");
            try
            {
                result.Phase="ValidatePlan";
                GameProcesses.ValidateExe(request.Settings);
                var plan=LaunchPlanBuilder.Build(request.Settings,AppPaths.Unlocker);ExternalLaunchProtocol.ValidatePlan(request,plan);
                GameProcesses.RequireStopped(request.Settings.GameRoot);
                var receipt=AppPaths.LoadReceipt(request.Settings);
                if(receipt?.Status is "PartialFailure" or "Installing" or "PartialClean")throw new IOException("部署维护未完成："+receipt.Status);
                await ExternalUnlockerService.PreflightUnlockerAsync(plan,result.Log.Add);
                // Hold the audited executable read-only until CreateProcess completes to prevent replacement between hash and launch.
                using var executable=new FileStream(AppPaths.Unlocker,FileMode.Open,FileAccess.Read,FileShare.Read);
                string hash=Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(executable));
                if(!hash.Equals(UnlockerConfigAdapter.AuditedSha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("执行前解锁器哈希变化。");
                result.Phase="PrepareConfig";WriteProbe.Check(plan.ConfigPath!);UnlockerConfigAdapter.Prepare(plan);
                result.Phase="CreateChild";
                var info=new ProcessStartInfo(AppPaths.Unlocker){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(AppPaths.Unlocker)!};
                info.Environment["DOTNET_ROOT_X64"]=RuntimeRoot;
                using var child=Process.Start(info)??throw new IOException("提升后 CreateProcess 未返回解锁器。");
                result.ChildPid=child.Id;result.ChildStartedUtc=child.StartTime.ToUniversalTime();result.Phase="ChildStarted";result.Success=true;
                result.Log.Add($"已授权启动原解锁器一次：{AppPaths.Unlocker}；PID={child.Id}；DOTNET_ROOT_X64={RuntimeRoot}；等待实际 Shipping 渲染窗口，不声称 FPS 生效。");
            }
            finally{launchGate.Release();}
        }
        catch(Exception e){result.Error=e.Message;result.Log.Add("外部启动失败 ["+result.Phase+"]："+e.Message);}
        SafePaths.EnsureNoLinks(Jobs,response);JsonFiles.Save(response,result);return result.Success?0:1;
    }
    private sealed class RuntimeFile
    {
        public string Path {get;set;}="";
        public long Length {get;set;}
        public string Sha256 {get;set;}="";
    }
    public static async Task ValidateRuntimeAsync()
    {
        using var stream=typeof(ExternalLaunchWorker).Assembly.GetManifestResourceStream("WuWaFpsUnlock.Runtime8Hashes.json")??throw new InvalidDataException("本地 .NET 8 校验清单缺失。");
        var files=await JsonSerializer.DeserializeAsync<List<RuntimeFile>>(stream,JsonFiles.Options)??throw new InvalidDataException("本地运行库清单为空。");
        if(!files.Any(x=>x.Path.Equals("dotnet.exe",StringComparison.OrdinalIgnoreCase))||!files.Any(x=>x.Path.Replace('\\','/').Contains("host/fxr/"))||!files.Any(x=>x.Path.Replace('\\','/').Contains("Microsoft.WindowsDesktop.App/")))throw new InvalidDataException("运行库清单不完整。");
        var expected=new HashSet<string>(files.Select(x=>Path.GetFullPath(SafePaths.Under(RuntimeRoot,x.Path))),StringComparer.OrdinalIgnoreCase);
        var pending=new Stack<string>();pending.Push(RuntimeRoot);
        while(pending.Count>0)
        {
            string directory=pending.Pop();SafePaths.EnsureNoLinks(RuntimeRoot,directory);
            foreach(string child in Directory.EnumerateDirectories(directory)){SafePaths.EnsureNoLinks(RuntimeRoot,child);pending.Push(child);}
            foreach(string file in Directory.EnumerateFiles(directory))
                if(!expected.Contains(Path.GetFullPath(file)))throw new InvalidDataException("便携运行库含未登记文件，拒绝混搭版本："+file);
        }
        foreach(var file in files)
        {
            string path=SafePaths.Under(RuntimeRoot,file.Path);
            if(!File.Exists(path)||new FileInfo(path).Length!=file.Length||!string.Equals(await SafePaths.HashAsync(path),file.Sha256,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("便携 .NET 8 运行库缺失或校验失败："+path+"；不依赖系统运行库假装便携启动成功。");
        }
    }
}
