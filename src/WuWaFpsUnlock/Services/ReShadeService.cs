using System.Diagnostics;


using WuWaFpsUnlock.Core;
namespace WuWaFpsUnlock.Services;
public sealed record ReShadeInfo(string State,string? Proxy,string Ini,string AddonDirectory,string Description);
public sealed class ReShadeService(Action<string> log)
{
    public ReShadeInfo Inspect(UserSettings s,ReShadeSpec? spec=null)
    {
        string exe=GameProcesses.ValidateExe(s),dir=Path.GetDirectoryName(exe)!;
        var hits=new List<(string path,string state)>();
        foreach(string name in new[]{"dxgi.dll","d3d12.dll","d3d11.dll","d3d10.dll","d3d9.dll","opengl32.dll"})
        {
            string path=Path.Combine(dir,name);if(!File.Exists(path))continue;
            SafePaths.EnsureNoLinks(s.GameRoot,path);
            string state;
            try {state=PeInspector.AddonBuild(path);}catch{state="Unknown";}
            if(state=="Unknown")
                return new("Conflict",path,Path.Combine(dir,"ReShade.ini"),dir,"存在未知图形代理："+name+"；不会覆盖。");
            if(state!="Unknown")hits.Add((path,state));
        }
        if(hits.Count>1)return new("Conflict",null,Path.Combine(dir,"ReShade.ini"),dir,"检测到多个 ReShade 代理，须先处理冲突。");
        string? proxy=hits.Count==1?hits[0].path:null;
        string standard=Path.Combine(dir,"ReShade.ini"),alternate=proxy is null?standard:Path.ChangeExtension(proxy,".ini");
        if(File.Exists(standard)&&alternate!=standard&&File.Exists(alternate))return new("Conflict",proxy,standard,dir,"存在 ReShade.ini 与代理同名 INI，配置位置有歧义；未修改。");
        string ini=File.Exists(alternate)?alternate:standard;
        SafePaths.EnsureNoLinks(s.GameRoot,ini);
        string addon=dir;
        if(File.Exists(ini))
        {
            if(!string.IsNullOrWhiteSpace(IniDocument.Load(ini).Get("INSTALL","BasePath")))
                return new("Conflict",proxy,ini,dir,"ReShade 配置含安装重定向 BasePath；保留原安装并停止自动写入。");
            string? custom=IniDocument.Load(ini).Get("ADDON","AddonPath");
            if(!string.IsNullOrWhiteSpace(custom))
            {
                addon=Path.GetFullPath(Path.IsPathRooted(custom)?custom:Path.Combine(dir,custom));
                // Do not silently redirect a user's external shared add-on folder.
                if(!SafePaths.IsInside(s.GameRoot,addon)&&!Path.GetFullPath(s.GameRoot).TrimEnd('\\','/').Equals(addon.TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase))return new("Conflict",proxy,ini,addon,"ReShade 使用游戏目录外的共享 AddonPath；当前版保留原设置并阻止自动写入。");
                SafePaths.EnsureNoLinks(s.GameRoot,addon);
            }
        }
        if(proxy is null)return new("Missing",null,ini,addon,"未安装");
        string stateHit=hits[0].state,version=PeInspector.Version(proxy);
        if(stateHit=="UnknownReShade")return new("Conflict",proxy,ini,addon,"检测到无法确认构建类型的 ReShade 代理；保留原文件并停止自动升级。");
        var versionInfo=FileVersionInfo.GetVersionInfo(proxy);
        if(versionInfo.FileMajorPart>6)return new("Conflict",proxy,ini,addon,$"ReShade {version} 超出本版已适配的主版本范围；保留原安装并停止。");
        if(stateHit=="FullCandidate" && PeInspector.IsAmd64(proxy) && versionInfo.FileMajorPart==6 && versionInfo.FileMinorPart>=8)
            return new("Reusable",proxy,ini,addon,$"ReShade {version} / 完整 Add-on 特征匹配");
        return new("UpgradeRequired",proxy,ini,addon,$"ReShade {version}：需确认升级至 Full Add-on");
    }
    public async Task<ReShadeInfo> EnsureAsync(UserSettings s,ReShadeSpec spec,DeploymentReceipt receipt,bool upgradeApproved,CancellationToken token)
    {
        foreach(var candidate in Process.GetProcessesByName("ReShade_Setup_6.8.0_Addon"))
        { using(candidate) { if(!candidate.HasExited)throw new IOException("ReShade Setup 仍在运行；请等待该安装结束后重试。"); } }
        var info=Inspect(s,spec);if(info.State=="Conflict")throw new IOException(info.Description);
        if(info.State=="Reusable") {log("复用已有 "+info.Description+"；不覆盖 ReShade DLL。");return info;}
        if(info.State=="UpgradeRequired"&&!upgradeApproved)throw new InvalidOperationException("需要确认升级现有 ReShade。未改动运行库。");
        string api=info.Proxy is null?spec.ProxyApi:Path.GetFileNameWithoutExtension(info.Proxy);
        string expected=info.Proxy??Path.Combine(Path.GetDirectoryName(s.GameExe)!,api+".dll");
        WriteProbe.Check(expected);WriteProbe.Check(info.Ini);
        string setup=await ResolveLocalSetupAsync(s.PackageManifest,spec,token);
        GameProcesses.RequireStopped(s.GameRoot);
        bool wasMissing=!File.Exists(expected);
        byte[]? originalIni=File.Exists(info.Ini)?File.ReadAllBytes(info.Ini):null; // Transient preservation, no backup file.
        receipt.ProxyPath=expected;receipt.Status="InstallingReShade";AppPaths.SaveReceipt(receipt);
        var psi=new ProcessStartInfo(setup){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(setup)!};
        psi.ArgumentList.Add(s.GameExe);psi.ArgumentList.Add("--headless");psi.ArgumentList.Add("--api");psi.ArgumentList.Add(api);
        if(info.Proxy is not null){psi.ArgumentList.Add("--state");psi.ArgumentList.Add("update");}
        log(info.Proxy is null?"正在静默安装官方 ReShade Full Add-on…":"正在更新 ReShade 本体，保留原有配置…");
        using var process=Process.Start(psi)??throw new IOException("无法启动 ReShade Setup。");
        // Do not kill Setup midway or imply it is rolled back. UI stays in a real busy state.
        // Keep the operation busy until Setup exits, including its compatibility-list network wait.
        // Do not allow retry/cleanup to race an installer still writing the same directory.
        var exit=process.WaitForExitAsync();
        if(await Task.WhenAny(exit,Task.Delay(TimeSpan.FromSeconds(60)))!=exit)
            log($"ReShade Setup 仍在运行（PID {process.Id}），可能正在等待官方兼容表。继续等待真实退出；未报告成功。");
        try { await exit; }
        finally { if(originalIni is not null)File.WriteAllBytes(info.Ini,originalIni); }
        if(process.ExitCode!=0)throw new IOException($"ReShade Setup 退出码 {process.ExitCode}，未将部署标记成功。");
        var after=Inspect(s,spec);
        if(after.State!="Reusable"||after.Proxy is null||!File.Exists(expected)||!after.Proxy.Equals(expected,StringComparison.OrdinalIgnoreCase))throw new IOException("ReShade Setup 已退出，但代理文件/完整 Add-on 特征/目标布局未通过检查。");
        string hash=await SafePaths.HashAsync(after.Proxy,token);
        if(!string.IsNullOrEmpty(spec.FullRuntimeSha256)&&!hash.Equals(spec.FullRuntimeSha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("ReShade runtime SHA-256 与包清单不匹配。");
        var entry=receipt.Files.FirstOrDefault(f=>f.Path.Equals(expected,StringComparison.OrdinalIgnoreCase));
        if(entry is null){entry=new(){Path=expected,CreatedByTool=wasMissing,Kind="ReShade"};receipt.Files.Add(entry);}
        entry.InstalledHash=hash;entry.Completed=true;AppPaths.SaveReceipt(receipt);
        log("ReShade 本体检查完成。没有安装滤镜包。");return after;
    }
    public async Task<string> ResolveLocalSetupAsync(string manifestPath, ReShadeSpec spec, CancellationToken token=default)
    {
        if(spec.Version!="6.8.0")throw new InvalidDataException("未适配的 ReShade Setup 版本。");
        if(string.IsNullOrWhiteSpace(spec.LocalSetupPath))throw new FileNotFoundException("未提供用户本地 ReShade Setup；请重新导入桌面材料。");
        string sourceRoot=Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        string path=SafePaths.Under(sourceRoot,spec.LocalSetupPath);
        if(!File.Exists(path))throw new FileNotFoundException("本地 ReShade Setup 缺失。",path);
        if(!System.Text.RegularExpressions.Regex.IsMatch(spec.SetupSha256,"^[a-fA-F0-9]{64}$"))throw new InvalidDataException("本地 Setup 必须有导入时登记的 SHA-256。");
        string hash=await SafePaths.HashAsync(path,token);
        if(!hash.Equals(spec.SetupSha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("用户本地 ReShade Setup SHA-256 不匹配；未运行安装器。");
        var version=FileVersionInfo.GetVersionInfo(path);
        if(version.FileMajorPart!=6||version.FileMinorPart!=8||version.FileBuildPart!=0||!((version.ProductName??"")+(version.FileDescription??"")).Contains("ReShade",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("本地文件不是可识别的 ReShade 6.8.0 安装器。");
        log("使用用户本地 ReShade 6.8.0 Full Add-on Setup；SHA-256："+hash+"。该哈希证明副本一致，不代表官方签名信任。");
        return path;
    }
}

