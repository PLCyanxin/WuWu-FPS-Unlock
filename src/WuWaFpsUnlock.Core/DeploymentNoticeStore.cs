namespace WuWaFpsUnlock.Core;

public sealed class DeploymentNoticeState
{
    public int SchemaVersion { get; set; } = 1;
    public long InstallationGeneration { get; set; }
    public long AcknowledgedGeneration { get; set; }
    public bool Pending { get; set; }
    public bool Cleared { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
}
public sealed record DeploymentHistoryEntry(DateTimeOffset Updated, string PackageId, string Status, List<FileReceipt> Files, List<IniReceipt> IniEdits);

// One notice per changed, successfully verified deployment. Cancellation is deliberately
// represented by doing nothing; it must never acknowledge the current generation.
public static class DeploymentNoticeStore
{
    public static bool RequiresAcknowledgement(DeploymentReceipt? receipt) => receipt is not null && receipt.Status == "Deployed" &&
        (receipt.Notice is null || receipt.Notice.Pending);
    public static void InitializeLegacy(DeploymentReceipt receipt)
    {
        if (receipt.Notice is not null) return;
        receipt.Notice = new() { InstallationGeneration = receipt.Status == "Deployed" ? 1 : 0, Pending = receipt.Status == "Deployed" };
    }
    public static void CompleteSuccessfulDeployment(DeploymentReceipt receipt, bool actualChanges)
    {
        if (receipt.Status != "Deployed") throw new InvalidOperationException("只有已完成校验的部署才能设置须知状态。");
        receipt.Notice ??= new();
        actualChanges |= receipt.PendingDeploymentChanges;
        if (!actualChanges) return;
        receipt.PendingDeploymentChanges = false;
        receipt.Notice.InstallationGeneration++;
        receipt.Notice.Pending = true;
        receipt.Notice.Cleared = false;
    }
    public static void Acknowledge(DeploymentReceipt receipt)
    {
        if (receipt.Status != "Deployed") throw new InvalidOperationException("当前没有完成的部署可确认。");
        InitializeLegacy(receipt);
        receipt.Notice!.AcknowledgedGeneration = receipt.Notice.InstallationGeneration;
        receipt.Notice.Pending = false;
        receipt.Notice.AcknowledgedAt = DateTimeOffset.UtcNow;
    }
    public static void MarkCleaned(DeploymentReceipt receipt)
    {
        InitializeLegacy(receipt);
        receipt.PendingDeploymentChanges = false;
        receipt.Notice!.Cleared = true;
        receipt.Notice.Pending = false;
    }
    public static DeploymentReceipt BeginAfterClean(DeploymentReceipt previous, string gameRoot, string gameExe)
    {
        InitializeLegacy(previous);
        // Clone so future entries do not mutate the historical ownership/configuration evidence.
        var snapshot = System.Text.Json.JsonSerializer.Deserialize<DeploymentReceipt>(System.Text.Json.JsonSerializer.Serialize(previous, JsonFiles.Options), JsonFiles.Options)!;
        snapshot.History.Add(new(previous.Updated, previous.PackageId, previous.Status, snapshot.Files, snapshot.IniEdits));
        return new() { GameRoot = gameRoot, GameExe = gameExe, Notice = snapshot.Notice, History = snapshot.History };
    }
}

public static class ReShadeOwnership
{
    public static bool CanClean(DeploymentReceipt receipt, FileReceipt file)
    {
        if (file.Kind != "ReShade" || !file.CreatedByTool || !file.Completed || file.SourceKind != "ToolInstalled" ||
            string.IsNullOrWhiteSpace(file.SourcePath) || file.SourceHash.Length != 64 || !file.SourceHash.All(Uri.IsHexDigit) ||
            string.IsNullOrWhiteSpace(receipt.GameExe) || !file.Path.Equals(receipt.ProxyPath, StringComparison.OrdinalIgnoreCase)) return false;
        string name = Path.GetFileName(file.Path);
        return new[] { "dxgi.dll", "d3d12.dll", "d3d11.dll", "d3d10.dll", "d3d9.dll", "opengl32.dll" }.Contains(name, StringComparer.OrdinalIgnoreCase)
            && Path.GetDirectoryName(Path.GetFullPath(file.Path))!.Equals(Path.GetDirectoryName(Path.GetFullPath(receipt.GameExe)), StringComparison.OrdinalIgnoreCase)
            && SafePaths.IsInside(receipt.GameRoot, file.Path);
    }
}
