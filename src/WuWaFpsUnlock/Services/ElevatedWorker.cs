using System.Diagnostics;
using WuWaFpsUnlock.Core;
namespace WuWaFpsUnlock.Services;
public sealed class WorkerRequest
{
    public string Nonce {get;set;}="";
    public string Operation {get;set;}="";
    public UserSettings Settings {get;set;}=new();
    public bool ApproveReShadeUpgrade {get;set;}
}
public sealed class WorkerResult
{
    public string Nonce {get;set;}="";
    public bool Success {get;set;}
    public string Error {get;set;}="";
    public List<string> Log {get;set;}=[];
}
public static class ElevatedWorker
{
    public static async Task RunFromUi(string operation,UserSettings settings,bool upgrade,Action<string> log)
    {
        string jobs=Path.Combine(AppPaths.Data,"jobs");Directory.CreateDirectory(jobs);
        string nonce=Guid.NewGuid().ToString("N"),request=Path.Combine(jobs,nonce+".request.json"),result=Path.Combine(jobs,nonce+".result.json");
        JsonFiles.Save(request,new WorkerRequest{Nonce=nonce,Operation=operation,Settings=settings.Clone(),ApproveReShadeUpgrade=upgrade});
        try
        {
            string exe=Environment.ProcessPath??throw new IOException("无法定位提权工作进程。");
            if(!Path.GetFileName(exe).Equals("WuWaFpsUnlock.exe",StringComparison.OrdinalIgnoreCase))throw new IOException("请先发布为 Windows EXE 再使用提权功能；不从 dotnet 宿主提权。");
            // Only fixed deploy/clean operations, not arbitrary commands or caller-supplied file operations.
            var psi=new ProcessStartInfo(exe){UseShellExecute=true,Verb="runas",WorkingDirectory=AppPaths.Base};
            psi.ArgumentList.Add("--worker");psi.ArgumentList.Add(request);
            using var process=Process.Start(psi)??throw new IOException("管理员工作进程未启动。");
            await process.WaitForExitAsync();
            if(!File.Exists(result))throw new IOException("管理员操作没有返回结果。请查看逐文件部署记录。");
            var response=JsonFiles.Read<WorkerResult>(result);
            if(response.Nonce!=nonce)throw new InvalidDataException("工作进程响应不匹配。");
            foreach(string line in response.Log)log(line);
            if(!response.Success)throw new IOException(response.Error);
        }
        catch(System.ComponentModel.Win32Exception e) when(e.NativeErrorCode==1223){throw new OperationCanceledException("已取消管理员授权，未继续部署。");}
        finally {if(File.Exists(request))File.Delete(request);if(File.Exists(result))File.Delete(result);}
    }
    public static async Task<int> Execute(string requestPath)
    {
        string jobs=Path.Combine(AppPaths.Data,"jobs");SafePaths.EnsureInside(jobs,requestPath);SafePaths.EnsureNoLinks(jobs,requestPath);
        var req=JsonFiles.Read<WorkerRequest>(requestPath);
        if(req.Operation is not ("deploy" or "clean") || !Guid.TryParseExact(req.Nonce,"N",out _) || Path.GetFileName(requestPath)!=req.Nonce+".request.json")throw new InvalidDataException("工作请求无效。");
        string response=Path.Combine(jobs,req.Nonce+".result.json");var result=new WorkerResult{Nonce=req.Nonce};
        try
        {
            // Revalidate identity, stopped game, payload and target paths inside the elevated boundary.
            var deploy=new DeploymentService(result.Log.Add);
            if(req.Operation=="deploy")await deploy.DeployAsync(req.Settings,req.ApproveReShadeUpgrade);else await deploy.CleanAsync(req.Settings);
            result.Success=true;
        }
        catch(Exception e){result.Error=e.Message;result.Log.Add("管理员操作失败："+e.Message);}
        JsonFiles.Save(response,result);return result.Success?0:1;
    }
}
