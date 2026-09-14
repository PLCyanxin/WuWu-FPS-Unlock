namespace WuWaFpsUnlock.Core;

public sealed record BaselineGameFile(string RelativePath, string Sha256, long Size);
public sealed class GameFileBaseline
{
    public int SchemaVersion { get; set; } = 1;
    public string GameRoot { get; set; } = "";
    public string GameExe { get; set; } = "";
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<BaselineGameFile> Files { get; set; } = [];
    public List<string> AbsentNamesForReferenceOnly { get; set; } = [];
}
public sealed record BaselinePresenceResult(List<string> MissingPaths)
{
    public bool IsComplete => MissingPaths.Count == 0;
    public string Message => IsComplete ? "" : GameFileBaselineStore.MissingFilesMessage + Environment.NewLine + string.Join(Environment.NewLine, MissingPaths);
}

// Presence baseline, not a backup and not a requirement to retain old file hashes.
// Official repair/update may legitimately change bytes. Only known exact paths are required.
public static class GameFileBaselineStore
{
    public const string MissingFilesMessage = "检测到文件缺失，请在鸣潮官方启动器启动一次游戏完成游戏文件修复";
    private static void ValidateIdentity(GameFileBaseline baseline, string gameRoot, string gameExe)
    {
        if (baseline.SchemaVersion != 1 || !Path.GetFullPath(baseline.GameRoot).Equals(Path.GetFullPath(gameRoot), StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFullPath(baseline.GameExe).Equals(Path.GetFullPath(gameExe), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("基线与当前所选游戏安装不一致，拒绝覆盖或缩减。");
        SafePaths.EnsureInside(gameRoot, gameExe);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in baseline.Files)
        {
            string path = SafePaths.Under(gameRoot, file.RelativePath);
            if (!PackageReader.VendorNames.Contains(Path.GetFileName(path)) || !paths.Add(path) || file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit) || file.Size < 0)
                throw new InvalidDataException("基线有未知、重复或无效文件记录：" + file.RelativePath);
        }
    }
    public static async Task<GameFileBaseline> CaptureAsync(string gameRoot, string gameExe, CancellationToken token = default)
    {
        gameRoot = SafePaths.GameRoot(gameRoot); gameExe = Path.GetFullPath(gameExe);
        SafePaths.EnsureInside(gameRoot, gameExe); SafePaths.EnsureNoLinks(gameRoot, gameExe);
        if (!File.Exists(gameExe)) throw new FileNotFoundException("所选原装游戏程序不存在。", gameExe);
        var baseline = new GameFileBaseline { GameRoot = gameRoot, GameExe = gameExe };
        foreach (var name in PackageReader.VendorNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            var found = PackageReader.FindExistingVendorTargets(gameRoot, name);
            if (found.Count == 0) baseline.AbsentNamesForReferenceOnly.Add(name);
            foreach (string path in found)
                baseline.Files.Add(new(Path.GetRelativePath(gameRoot, path), await SafePaths.HashAsync(path, token), new FileInfo(path).Length));
        }
        return baseline;
    }
    public static GameFileBaseline MergeAndSave(string path, GameFileBaseline observed)
    {
        ValidateIdentity(observed, observed.GameRoot, observed.GameExe);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var baselineLock = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        GameFileBaseline result;
        if (File.Exists(path))
        {
            result = JsonFiles.Read<GameFileBaseline>(path);
            ValidateIdentity(result, observed.GameRoot, observed.GameExe);
            var existing = result.Files.Select(f => SafePaths.Under(result.GameRoot, f.RelativePath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            // A later incomplete scan cannot erase evidence that a path previously existed.
            foreach (var file in observed.Files)
                if (existing.Add(SafePaths.Under(observed.GameRoot, file.RelativePath))) result.Files.Add(file);
        }
        else result = observed;
        JsonFiles.Save(path, result);
        return result;
    }
    public static BaselinePresenceResult Check(GameFileBaseline baseline, string gameRoot, string gameExe)
    {
        gameRoot = SafePaths.GameRoot(gameRoot);
        ValidateIdentity(baseline, gameRoot, gameExe);
        var missing = baseline.Files.Select(f => SafePaths.Under(gameRoot, f.RelativePath)).Where(path => !File.Exists(path)).ToList();
        return new(missing);
    }
    public static void RequirePresent(GameFileBaseline baseline, string gameRoot, string gameExe)
    {
        var result = Check(baseline, gameRoot, gameExe);
        if (!result.IsComplete) throw new IOException(result.Message);
    }
}
