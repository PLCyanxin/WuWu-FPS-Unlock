using WuWaFpsUnlock.Core;
namespace WuWaFpsUnlock.Services;
public sealed class DeploymentService(Action<string> log)
{
    public string ApprovalFingerprint { get; private set; } = "";
    public void UseApprovedFingerprint(string fingerprint) => ApprovalFingerprint = fingerprint;
    private static string Fingerprint(object value) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, JsonFiles.Options)));
    private static async Task<string> PlanFingerprint(UserSettings s, List<PlannedFile> plan, ReShadeInfo info, CancellationToken token) => Fingerprint(new { GameRoot=Path.GetFullPath(s.GameRoot), GameExe=Path.GetFullPath(s.GameExe), ManifestHash=await SafePaths.HashAsync(s.PackageManifest,token), Plan=plan, ReShade=info, IniHash=File.Exists(info.Ini)?await SafePaths.HashAsync(info.Ini,token):null, ProxyHash=info.Proxy is not null && File.Exists(info.Proxy)?await SafePaths.HashAsync(info.Proxy,token):null });
    public async Task<string> PreviewAsync(UserSettings s,CancellationToken token=default)
    {
        string exe=GameProcesses.ValidateExe(s);GameProcesses.RequireStopped(s.GameRoot);
        if(!s.MfgSelected)return "未选多帧生成，不写入游戏文件。";
        var manifest=PackageReader.Load(s.PackageManifest);var before=new ReShadeService(log).Inspect(s,manifest.ReShade);
        if(before.State=="Conflict")throw new IOException(before.Description);
        var plan=await PackageReader.PlanAsync(manifest,s.PackageManifest,s.GameRoot,Path.GetDirectoryName(exe)!,before.AddonDirectory,token);
        ApprovalFingerprint=await PlanFingerprint(s,plan,before,token);
        var lines=new List<string>{"游戏根："+s.GameRoot,"原装 Shipping："+exe,"ReShade："+before.Description,"ReShade 配置："+before.Ini};
        lines.AddRange(plan.Select(f=>$"{f.Source} → {f.Target}"));
        foreach(var f in manifest.Files.Where(f=>f.Kind==PayloadKind.Vendor))
            if(!plan.Any(p=>p.Kind==PayloadKind.Vendor&&Path.GetFileName(p.Target).Equals(Path.GetFileName(f.Target),StringComparison.OrdinalIgnoreCase)))lines.Add("无同名目标，跳过："+f.Target);
        lines.Add("仅替换现存同名 DLL，不备份原文件；游戏内效果仍待验证。");
        return string.Join(Environment.NewLine,lines);
    }
    public string CleanPreview(UserSettings s)
    {
        GameProcesses.ValidateExe(s);GameProcesses.RequireStopped(s.GameRoot);
        var receipt=AppPaths.LoadReceipt(s)??throw new InvalidOperationException("没有本工具部署记录。");
        ApprovalFingerprint=Fingerprint(receipt);
        return "游戏根："+receipt.GameRoot+Environment.NewLine+string.Join(Environment.NewLine,receipt.Files.Where(f=>(f.Kind=="Vendor"&&f.ReplacedByTool)||(f.Kind=="Addon"&&f.CreatedByTool)).Select(f=>f.Path))+Environment.NewLine+"只删除当前哈希仍匹配的自有文件，并清理自有配置键；不恢复原版，不能保证游戏自动补齐。";
    }
    public async Task DeployAsync(UserSettings s,bool upgradeApproved,CancellationToken token=default)
    {
        string exe=GameProcesses.ValidateExe(s);GameProcesses.RequireStopped(s.GameRoot);
        if(!s.MfgSelected){log("没有选择多帧生成部署：未改动 ReShade、DLSS 和 Streamline。");return;}
        if(!File.Exists(s.PackageManifest))throw new FileNotFoundException("尚未提供完整的 MFG 文件包清单。");
        var manifest=PackageReader.Load(s.PackageManifest);var hardware=EnvironmentProbe.Read(log);
        if(!hardware.IsAdaGeForce)throw new InvalidOperationException("未确认 GeForce RTX 40 系显卡。仅阻止 MFG 部署，不影响普通 FPS 启动。");
        if(hardware.Driver is null)log("驱动状态未知，不能确认 Dynamic；不把未知报告为不支持。");
        if(manifest.FixedMinimumDriver is int minimum && (hardware.Driver is null || hardware.Driver<minimum))throw new InvalidOperationException("驱动不满足该文件包声明的 Fixed 条件。");
        if(hardware.HagsConfigured==false)throw new InvalidOperationException("HAGS 已配置关闭。请在 Windows 中处理并重启后再检查；工具不会擅自修改系统。");
        bool dynamic=manifest.PreferDynamic&&hardware.Driver is int detectedDriver&&detectedDriver>=manifest.DynamicMinimumDriver;
        log(dynamic?"Dynamic 驱动门槛通过；运行时支持仍待游戏内确认。":"不满足 Dynamic 门槛：只部署 Fixed 兼容配置，不拦截整个 MFG 功能。");
        var reshade=new ReShadeService(log);var before=reshade.Inspect(s,manifest.ReShade);
        if(before.State=="Conflict")throw new IOException(before.Description);
        // Fully validate payload and ALL paths before running an external installer.
        var plan=await PackageReader.PlanAsync(manifest,s.PackageManifest,s.GameRoot,Path.GetDirectoryName(exe)!,before.AddonDirectory,token);
        if(string.IsNullOrEmpty(ApprovalFingerprint) || ApprovalFingerprint!=await PlanFingerprint(s,plan,before,token))throw new IOException("文件计划与确认时不同或未经预览，请重新预览并确认。");
        foreach(var f in plan){WriteProbe.Check(f.Target);if(File.Exists(f.Target)){using var lockTest=new FileStream(f.Target,FileMode.Open,FileAccess.ReadWrite,FileShare.None);}}
        WriteProbe.Check(before.Ini);WriteProbe.Check(before.Proxy??Path.Combine(Path.GetDirectoryName(exe)!,manifest.ReShade.ProxyApi+".dll"));
        var previousReceipt=AppPaths.LoadReceipt(s);
        // A clean cycle ends ownership. Do not inherit ownership over files another tool installs later.
        var receipt=previousReceipt is not null && !previousReceipt.Status.StartsWith("Cleaned",StringComparison.Ordinal)?previousReceipt:new(){GameRoot=Path.GetFullPath(s.GameRoot),GameExe=exe};
        if(!receipt.GameRoot.Equals(Path.GetFullPath(s.GameRoot),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("部署记录与当前游戏根目录不匹配。");
        ApprovalFingerprint="";
        receipt.PackageId=manifest.PackageId;receipt.Status="Installing";AppPaths.SaveReceipt(receipt);
        try
        {
            var ready=await reshade.EnsureAsync(s,manifest.ReShade,receipt,upgradeApproved,token);
            if(!ready.AddonDirectory.Equals(before.AddonDirectory,StringComparison.OrdinalIgnoreCase))throw new IOException("ReShade 安装后 AddonPath 发生变化，停止以免写入错误位置。");
            GameProcesses.RequireStopped(s.GameRoot);
            await DeploymentFiles.ApplyAsync(plan,receipt,()=>AppPaths.SaveReceipt(receipt),log,token);
            receipt.IniPath=ready.Ini;
            receipt.ProxyPath=ready.Proxy ?? throw new IOException("没有确认 ReShade 代理。");
            var proxyEntry=receipt.Files.FirstOrDefault(f=>f.Path.Equals(receipt.ProxyPath,StringComparison.OrdinalIgnoreCase));
            if(proxyEntry is null){proxyEntry=new(){Path=receipt.ProxyPath,Kind="ReShade",CreatedByTool=false};receipt.Files.Add(proxyEntry);}
            proxyEntry.InstalledHash=await SafePaths.HashAsync(receipt.ProxyPath,token);proxyEntry.Completed=true;
            var ini=IniDocument.Load(ready.Ini);
            foreach(var pair in manifest.MfgConfig)ini.ApplyOwned("RenoDX.MFGUnlock",pair.Key,pair.Value,receipt);
            ini.ApplyOwned("RenoDX.MFGUnlock","Enabled","1",receipt);
            ini.ApplyOwned("RenoDX.MFGUnlock","DynamicMFG",dynamic?"1":"0",receipt);
            ini.ApplyOwned("RenoDX.MFGUnlock","ForceMultiplier",dynamic?"0":manifest.FixedMultiplier.ToString(),receipt);
            if(dynamic)ini.ApplyOwned("RenoDX.MFGUnlock","RuntimeSelectionMode","1",receipt);
            string early=ini.MergeCsv("ADDON","LoadFromDllMain","renodx-mfgunlock.addon64");
            ini.ApplyOwned("ADDON","LoadFromDllMain",early,receipt);
            AppPaths.SaveReceipt(receipt);ini.Save(ready.Ini);
            receipt.Status="Deployed";AppPaths.SaveReceipt(receipt);
            if(!await DeploymentFiles.IsIntactAsync(receipt,token))throw new IOException("最终文件或配置校验失败。");
            log("部署完成，文件与配置已校验；MFG 的实际启用状态仍需启动游戏确认。");
        }
        catch
        {
            receipt.Status="PartialFailure";AppPaths.SaveReceipt(receipt);
            log("本次未完成。已保存逐文件记录；未备份原文件，不声称已回滚。关闭游戏后可重试同一包。");throw;
        }
    }
    public async Task CleanAsync(UserSettings s,CancellationToken token=default)
    {
        GameProcesses.ValidateExe(s);GameProcesses.RequireStopped(s.GameRoot);
        var receipt=AppPaths.LoadReceipt(s)??throw new InvalidOperationException("没有本工具的部署记录，拒绝猜测或批量删除插件。");
        if(!receipt.GameRoot.Equals(Path.GetFullPath(s.GameRoot),StringComparison.OrdinalIgnoreCase)||!receipt.GameExe.Equals(Path.GetFullPath(s.GameExe),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("部署记录与当前所选游戏不一致；没有清除任何文件。");
        if(ApprovalFingerprint!=Fingerprint(receipt))throw new IOException("清除记录与确认时不同或未经预览，请重新确认。");
        foreach(var f in receipt.Files.Where(f=>(f.Kind=="Addon"&&f.CreatedByTool)||(f.Kind=="Vendor"&&f.ReplacedByTool)))WriteProbe.Check(f.Path);
        if(!string.IsNullOrWhiteSpace(receipt.IniPath))WriteProbe.Check(receipt.IniPath);
        await DeploymentFiles.CleanOwnedAddonsAsync(receipt,()=>AppPaths.SaveReceipt(receipt),log,token);
    }
}
