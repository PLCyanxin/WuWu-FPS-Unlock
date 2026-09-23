using WuWaFpsUnlock.Core;
namespace WuWaFpsUnlock.Services;
public sealed class DeploymentService(Action<string> log)
{
    private static async Task CheckBaselineAsync(UserSettings s,CancellationToken token)
    {
        var path=AppPaths.Baseline(s.GameExe);
        if(File.Exists(path))GameFileBaselineStore.RequirePresent(JsonFiles.Read<GameFileBaseline>(path),s.GameRoot,s.GameExe);
        var observed=await GameFileBaselineStore.CaptureAsync(s.GameRoot,s.GameExe,token);
        var baseline=GameFileBaselineStore.MergeAndSave(path,observed);
        GameFileBaselineStore.RequirePresent(baseline,s.GameRoot,s.GameExe);
    }
    public string ApprovalFingerprint { get; private set; } = "";
    public void UseApprovedFingerprint(string fingerprint) => ApprovalFingerprint = fingerprint;
    private static string Fingerprint(object value) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, JsonFiles.Options)));
    private static async Task<string> PlanFingerprint(UserSettings s, List<PlannedFile> plan, ReShadeInfo info, MfgDeploymentConfiguration configuration, CancellationToken token) => Fingerprint(new { GameRoot=Path.GetFullPath(s.GameRoot), GameExe=Path.GetFullPath(s.GameExe), ManifestHash=await SafePaths.HashAsync(s.PackageManifest,token), Plan=plan, ReShade=info, MfgConfiguration=configuration, IniHash=File.Exists(info.Ini)?await SafePaths.HashAsync(info.Ini,token):null, ProxyHash=info.Proxy is not null && File.Exists(info.Proxy)?await SafePaths.HashAsync(info.Proxy,token):null });
    public async Task<string> PreviewAsync(UserSettings s,CancellationToken token=default)
    {
        string exe=GameProcesses.ValidateExe(s);GameProcesses.RequireStopped(s.GameRoot);
        if(!s.MfgSelected)return "未选多帧生成，不写入游戏文件。";
        await CheckBaselineAsync(s,token);
        var manifest=PackageReader.Load(s.PackageManifest);var before=new ReShadeService(log).Inspect(s,manifest.ReShade);
        if(before.State=="Conflict")throw new IOException(before.Description);
        var plan=await PackageReader.PlanAsync(manifest,s.PackageManifest,s.GameRoot,Path.GetDirectoryName(exe)!,before.AddonDirectory,token);
        var configuration=MfgDeploymentConfiguration.Create(s,manifest,EnvironmentProbe.Read(log));
        ApprovalFingerprint=await PlanFingerprint(s,plan,before,configuration,token);
        var lines=new List<string>{"游戏根："+s.GameRoot,"原装 Shipping："+exe,"ReShade："+before.Description,"ReShade 配置："+before.Ini};
        lines.Add(configuration.PreviewText);
        lines.AddRange(plan.Select(f=>$"{f.Source} → {f.Target}"));
        foreach(var f in manifest.Files.Where(f=>f.Kind==PayloadKind.Vendor))
            if(!plan.Any(p=>p.Kind==PayloadKind.Vendor&&Path.GetFileName(p.Target).Equals(Path.GetFileName(f.Target),StringComparison.OrdinalIgnoreCase)))lines.Add("无同名目标，跳过："+f.Target);
        lines.Add("仅替换现存同名 DLL，不备份原文件；游戏内效果仍待验证。");
        return string.Join(Environment.NewLine,lines);
    }
    private sealed record CleanPlan(DeploymentReceipt? Receipt, MaterialRemovalPlan? Materials);
    private async Task<CleanPlan> PrepareCleanAsync(UserSettings s,CancellationToken token)
    {
        GameProcesses.ValidateExe(s);GameProcesses.RequireStopped(s.GameRoot);
        var receipt=AppPaths.LoadReceipt(s);
        if(receipt is not null && (!receipt.GameRoot.Equals(Path.GetFullPath(s.GameRoot),StringComparison.OrdinalIgnoreCase)||!receipt.GameExe.Equals(Path.GetFullPath(s.GameExe),StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("部署记录与当前所选游戏不一致；没有清除任何文件。");
        MaterialRemovalPlan? materials=null;
        if(File.Exists(s.PackageManifest))
        {
            materials=await UserMaterialRemoval.PlanAsync(s.PackageManifest,s.GameRoot,token);
            // Normal ownership remains a separate, narrower authority. Do not list a file twice.
            materials=materials with { Candidates=materials.Candidates.Where(c=>receipt is null || !receipt.Files.Any(f=>f.Completed&&f.Path.Equals(c.Path,StringComparison.OrdinalIgnoreCase)&&f.InstalledHash.Equals(c.Sha256,StringComparison.OrdinalIgnoreCase)&&((f.Kind=="Vendor"&&f.ReplacedByTool)||(f.Kind=="Addon"&&f.CreatedByTool)))).ToList() };
        }
        else if(receipt is null)throw new InvalidOperationException("没有本工具部署记录，也没有可验证的用户材料清单；不能猜测清除对象。请先指定材料清单。");
        return new(receipt,materials);
    }
    private static string CleanFingerprint(UserSettings s,CleanPlan plan)=>Fingerprint(new{GameRoot=Path.GetFullPath(s.GameRoot),GameExe=Path.GetFullPath(s.GameExe),Plan=plan});
    public async Task<string> CleanPreviewAsync(UserSettings s,CancellationToken token=default)
    {
        var plan=await PrepareCleanAsync(s,token);ApprovalFingerprint=CleanFingerprint(s,plan);
        var lines=new List<string>{"游戏根："+s.GameRoot,"原装 Shipping："+s.GameExe};
        if(plan.Receipt is not null)
        {
            lines.Add("有本工具所有权登记（仅哈希仍匹配且登记完成时删除）：");
            lines.AddRange(plan.Receipt.Files.Where(f=>(f.Kind=="Vendor"&&f.ReplacedByTool)||(f.Kind=="Addon"&&f.CreatedByTool)||ReShadeOwnership.CanClean(plan.Receipt,f)).Select(f=>f.Path));
        }
        if(plan.Materials is not null)
        {
            lines.Add("注意：以下为既有用户材料指纹匹配，不是本工具安装证明。只有确认本整份清单后才移除，不认领安装所有权：");
            lines.AddRange(plan.Materials.Candidates.Select(f=>$"{f.Path}  SHA-256={f.Sha256}"));
            lines.AddRange(plan.Materials.Preserved);
        }
        lines.Add("保留用户已有或来源未知的 ReShade、滤镜、EXE、其他插件；仅清单中本工具新建且来源/哈希明确的 ReShade 本体可移除；没有所有权的 INI 项保持不动。不恢复原版，也不保证游戏自动补齐。是否按以上完整清单清除？");
        return string.Join(Environment.NewLine,lines);
    }
    public async Task DeployAsync(UserSettings s,bool upgradeApproved,CancellationToken token=default)
    {
        string exe=GameProcesses.ValidateExe(s);GameProcesses.RequireStopped(s.GameRoot);
        if(!s.MfgSelected){log("没有选择多帧生成部署：未改动 ReShade、DLSS 和 Streamline。");return;}
        await CheckBaselineAsync(s,token);
        if(!File.Exists(s.PackageManifest))throw new FileNotFoundException("尚未提供完整的 MFG 文件包清单。");
        var manifest=PackageReader.Load(s.PackageManifest);var hardware=EnvironmentProbe.Read(log);
        if(!hardware.IsAdaGeForce)throw new InvalidOperationException("未确认 GeForce RTX 40 系显卡。仅阻止 MFG 部署，不影响普通 FPS 启动。");
        if(hardware.Driver is null)log("驱动状态未知，不能确认 Dynamic；不把未知报告为不支持。");
        if(manifest.FixedMinimumDriver is int minimum && (hardware.Driver is null || hardware.Driver<minimum))throw new InvalidOperationException("驱动不满足该文件包声明的 Fixed 条件。");
        if(hardware.HagsConfigured==false)throw new InvalidOperationException("HAGS 已配置关闭。请在 Windows 中处理并重启后再检查；工具不会擅自修改系统。");
        var configuration=MfgDeploymentConfiguration.Create(s,manifest,hardware);
        bool dynamic=configuration.DynamicEnabled;
        log(dynamic?"Dynamic 驱动门槛通过；运行时支持仍待游戏内确认。":"不满足 Dynamic 门槛：只部署 Fixed 兼容配置，不拦截整个 MFG 功能。");
        var reshade=new ReShadeService(log);var before=reshade.Inspect(s,manifest.ReShade);
        if(before.State=="Conflict")throw new IOException(before.Description);
        // Fully validate payload and ALL paths before running an external installer.
        var plan=await PackageReader.PlanAsync(manifest,s.PackageManifest,s.GameRoot,Path.GetDirectoryName(exe)!,before.AddonDirectory,token);
        if(string.IsNullOrEmpty(ApprovalFingerprint) || ApprovalFingerprint!=await PlanFingerprint(s,plan,before,configuration,token))throw new IOException("文件计划或 MFG 配置与确认时不同，或未经预览，请重新预览并确认。");
        var skippedVendors=manifest.Files.Where(f=>f.Kind==PayloadKind.Vendor&&!plan.Any(p=>p.Kind==PayloadKind.Vendor&&Path.GetFileName(p.Target).Equals(Path.GetFileName(f.Target),StringComparison.OrdinalIgnoreCase))).Select(f=>Path.GetFileName(f.Target)).ToList();
        log($"DLL 同名映射：{plan.Count(f=>f.Kind==PayloadKind.Vendor)} 个实际目标；{skippedVendors.Count} 个材料名称无同名目标而跳过。"+(skippedVendors.Count>0?" 跳过："+string.Join("、",skippedVendors):""));
        foreach(var f in plan){WriteProbe.Check(f.Target);if(File.Exists(f.Target)){using var lockTest=new FileStream(f.Target,FileMode.Open,FileAccess.ReadWrite,FileShare.None);}}
        WriteProbe.Check(before.Ini);WriteProbe.Check(before.Proxy??Path.Combine(Path.GetDirectoryName(exe)!,manifest.ReShade.ProxyApi+".dll"));
        var initialIniHash=File.Exists(before.Ini)?await SafePaths.HashAsync(before.Ini,token):null;
        var previousReceipt=AppPaths.LoadReceipt(s);
        if(previousReceipt is not null)DeploymentNoticeStore.InitializeLegacy(previousReceipt);
        // A clean cycle ends ownership. Do not inherit ownership over files another tool installs later.
        var receipt=previousReceipt is not null && !previousReceipt.Status.StartsWith("Cleaned",StringComparison.Ordinal)?previousReceipt:previousReceipt is not null?DeploymentNoticeStore.BeginAfterClean(previousReceipt,Path.GetFullPath(s.GameRoot),exe):new(){GameRoot=Path.GetFullPath(s.GameRoot),GameExe=exe,Notice=new()};
        if(!receipt.GameRoot.Equals(Path.GetFullPath(s.GameRoot),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("部署记录与当前游戏根目录不匹配。");
        ApprovalFingerprint="";
        receipt.SkippedVendorNames=skippedVendors;
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
            if(proxyEntry is null){proxyEntry=new(){Path=receipt.ProxyPath,Kind="ReShade",CreatedByTool=false,SourceKind="UserExisting"};receipt.Files.Add(proxyEntry);}
            else if(before.State=="Reusable"&&!proxyEntry.InstalledHash.Equals(await SafePaths.HashAsync(receipt.ProxyPath,token),StringComparison.OrdinalIgnoreCase)){proxyEntry.CreatedByTool=false;proxyEntry.SourceKind="LegacyUnknown";}
            proxyEntry.InstalledHash=await SafePaths.HashAsync(receipt.ProxyPath,token);proxyEntry.Completed=true;
            var ini=IniDocument.Load(ready.Ini);
            configuration.ApplyOwned(ini,receipt,manifest.MfgConfig);
            string early=ini.MergeCsv("ADDON","LoadFromDllMain","renodx-mfgunlock.addon64");
            ini.ApplyOwned("ADDON","LoadFromDllMain",early,receipt);
            AppPaths.SaveReceipt(receipt);ini.Save(ready.Ini);
            receipt.PendingDeploymentChanges|=initialIniHash!=await SafePaths.HashAsync(ready.Ini,token);
            receipt.Status="Deployed";AppPaths.SaveReceipt(receipt);
            if(!await DeploymentFiles.IsIntactAsync(receipt,token))throw new IOException("最终文件或配置校验失败。");
            bool actualChanges=receipt.PendingDeploymentChanges;
            DeploymentNoticeStore.CompleteSuccessfulDeployment(receipt,actualChanges);AppPaths.SaveReceipt(receipt);
            log(actualChanges?"本次实际部署变化已完成，首次部署须知等待确认。":"本次仅复用相同文件/配置，未重置须知确认。");
            log($"匹配项部署完成：{plan.Count(f=>f.Kind==PayloadKind.Vendor)} 个 DLL 目标与 addon/配置已校验，{skippedVendors.Count} 个材料名称因无同名目标未部署；MFG 实际启用仍待游戏内确认。");
        }
        catch
        {
            receipt.Status="PartialFailure";AppPaths.SaveReceipt(receipt);
            log("本次未完成。已保存逐文件记录；未备份原文件，不声称已回滚。关闭游戏后可重试同一包。");throw;
        }
    }
    public async Task CleanAsync(UserSettings s,CancellationToken token=default)
    {
        var plan=await PrepareCleanAsync(s,token);
        if(string.IsNullOrEmpty(ApprovalFingerprint)||ApprovalFingerprint!=CleanFingerprint(s,plan))throw new IOException("清除材料、文件或记录与确认时不同，请重新预览并确认；没有扩大清除范围。");
        var receipt=plan.Receipt??new DeploymentReceipt{GameRoot=Path.GetFullPath(s.GameRoot),GameExe=Path.GetFullPath(s.GameExe)};
        foreach(var f in receipt.Files.Where(f=>(f.Kind=="Addon"&&f.CreatedByTool)||(f.Kind=="Vendor"&&f.ReplacedByTool)||ReShadeOwnership.CanClean(receipt,f)))WriteProbe.Check(f.Path);
        if(plan.Materials is not null)foreach(var f in plan.Materials.Candidates)WriteProbe.Check(f.Path);
        if(!string.IsNullOrWhiteSpace(receipt.IniPath))WriteProbe.Check(receipt.IniPath);
        ApprovalFingerprint="";
        // Persist operation status, never manufacture ownership over existing material files.
        receipt.Status="PartialClean";AppPaths.SaveReceipt(receipt);
        try
        {
            MaterialRemovalResult? materialResult=null;
            if(plan.Materials is not null)materialResult=await UserMaterialRemoval.ExecuteAsync(plan.Materials,log,token);
            await DeploymentFiles.CleanOwnedAddonsAsync(receipt,()=>AppPaths.SaveReceipt(receipt),log,token);
            if(materialResult?.Failed>0)throw new IOException("部分既有用户材料移除失败；详情见逐文件日志，可重新预览后重试。");
            if(materialResult?.Preserved>0){receipt.Status="CleanedWithSkips";AppPaths.SaveReceipt(receipt);}
            DeploymentNoticeStore.MarkCleaned(receipt);AppPaths.SaveReceipt(receipt);
            log($"既有用户材料清除结果：移除 {materialResult?.Removed??0}，变化保留 {materialResult?.Preserved??0}；没有把材料相同当成本工具安装证明。");
        }
        catch{receipt.Status="PartialClean";AppPaths.SaveReceipt(receipt);throw;}
    }
}
