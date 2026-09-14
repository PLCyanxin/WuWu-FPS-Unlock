namespace WuWaFpsUnlock.Core;

public enum DeploymentDifferenceSeverity { Blocking, Warning }
public sealed record DeploymentDifference(string Code, DeploymentDifferenceSeverity Severity, string Path, string Message,
    string? Expected = null, string? Actual = null, string? Section = null, string? Key = null);
public sealed record DeploymentIntegrityReport(bool FilesMatch, bool ConfigurationMatches, List<DeploymentDifference> Differences)
{
    public bool CanLaunch => Differences.All(d => d.Severity != DeploymentDifferenceSeverity.Blocking);
    public bool IsStrictlyIntact => FilesMatch && ConfigurationMatches && CanLaunch;
}

// Read-only. Runtime/menu changes do not become new ownership or silently rewrite INI.
public static class DeploymentIntegrity
{
    public static async Task<DeploymentIntegrityReport> InspectAsync(DeploymentReceipt receipt, CancellationToken token = default)
    {
        var differences = new List<DeploymentDifference>(); bool filesMatch = true, configMatch = true;
        void FileIssue(string code, string path, string message, string? expected = null, string? actual = null)
        { filesMatch = false; differences.Add(new(code, DeploymentDifferenceSeverity.Blocking, path, message, expected, actual)); }
        if (receipt.Status != "Deployed") FileIssue("ReceiptNotDeployed", "", "部署记录未完成：" + receipt.Status, "Deployed", receipt.Status);
        if (receipt.Files.Count == 0) FileIssue("NoTrackedFiles", "", "没有可核对的部署文件记录。");
        foreach (var file in receipt.Files)
        {
            token.ThrowIfCancellationRequested();
            try { SafePaths.EnsureInside(receipt.GameRoot, file.Path); SafePaths.EnsureNoLinks(receipt.GameRoot, file.Path); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { FileIssue("UnsafePath", file.Path, "拒绝越界或链接路径：" + ex.Message); continue; }
            if (!file.Completed) { FileIssue("FileUnverified", file.Path, "文件尚无完成校验记录。"); continue; }
            if (!File.Exists(file.Path)) { FileIssue("FileMissing", file.Path, "部署文件已不存在：" + file.Path, file.InstalledHash, "missing"); continue; }
            try
            {
                string current = await SafePaths.HashAsync(file.Path, token);
                if (!current.Equals(file.InstalledHash, StringComparison.OrdinalIgnoreCase))
                    FileIssue("FileHashMismatch", file.Path, "部署文件 SHA-256 已变化，需检查来源；不会自动覆盖。", file.InstalledHash, current);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { FileIssue("FileUnreadable", file.Path, "无法读取部署文件：" + ex.Message); }
        }
        if (receipt.SkippedVendorNames.Count > 0)
            differences.Add(new("SkippedVendorMaterials", DeploymentDifferenceSeverity.Warning, receipt.GameRoot,
                $"本次部署有 {receipt.SkippedVendorNames.Count} 个 DLL 名称因无同名目标跳过：{string.Join("、", receipt.SkippedVendorNames)}。未新增，不能保证游戏自动补齐或完整 MFG 可用。"));
        if (string.IsNullOrWhiteSpace(receipt.IniPath))
        {
            differences.Add(new("IniMissing", DeploymentDifferenceSeverity.Warning, receipt.IniPath, "ReShade INI 不存在；MFG 配置/加载状态未确认，不阻断独立 FPS/游戏启动。"));
            return new(filesMatch, false, differences);
        }
        try { SafePaths.EnsureInside(receipt.GameRoot, receipt.IniPath); SafePaths.EnsureNoLinks(receipt.GameRoot, receipt.IniPath); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { FileIssue("UnsafeIniPath", receipt.IniPath, "配置路径无法安全核对：" + ex.Message); return new(filesMatch, false, differences); }
        if (!File.Exists(receipt.IniPath))
        {
            differences.Add(new("IniMissing", DeploymentDifferenceSeverity.Warning, receipt.IniPath, "ReShade INI 不存在；MFG 配置/加载状态未确认，不阻断独立 FPS/游戏启动。"));
            return new(filesMatch, false, differences);
        }
        try
        {
            var ini = IniDocument.Load(receipt.IniPath);
            if (receipt.IniEdits.Count == 0)
            { configMatch = false; differences.Add(new("NoConfigOwnership", DeploymentDifferenceSeverity.Warning, receipt.IniPath, "没有本工具配置写入记录；保留现有配置，不能确认 MFG 原部署参数。")); }
            foreach (var edit in receipt.IniEdits)
            {
                string? current = ini.Get(edit.Section, edit.Key);
                bool csv = edit.Section.Equals("ADDON", StringComparison.OrdinalIgnoreCase) && edit.Key.Equals("LoadFromDllMain", StringComparison.OrdinalIgnoreCase);
                bool matches = csv
                    ? edit.Written.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).All((current ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase).Contains)
                    : string.Equals(current, edit.Written, StringComparison.Ordinal);
                if (!matches)
                {
                    configMatch = false;
                    differences.Add(new(csv ? "EarlyLoadChanged" : "IniValueChanged", DeploymentDifferenceSeverity.Warning, receipt.IniPath,
                        $"[{edit.Section}] {edit.Key} 已从登记值“{edit.Written}”变为“{current ?? "<缺失>"}”；保留运行时/用户设置，可继续启动，MFG 实际配置需在游戏内确认。",
                        edit.Written, current, edit.Section, edit.Key));
                }
            }
            if (ini.Get("RenoDX.MFGUnlock", "Enabled") != "1" && !differences.Any(d => d.Section == "RenoDX.MFGUnlock" && d.Key == "Enabled"))
            { configMatch = false; differences.Add(new("AddonDisabled", DeploymentDifferenceSeverity.Warning, receipt.IniPath, "MFG addon 未配置 Enabled=1；不阻断独立 FPS/游戏启动，也不声称 MFG 已启用。")); }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { configMatch = false; differences.Add(new("IniUnreadable", DeploymentDifferenceSeverity.Warning, receipt.IniPath, "无法解析 ReShade 配置；不改写用户文件，MFG 配置待检查：" + ex.Message)); }
        return new(filesMatch, configMatch, differences);
    }
}
