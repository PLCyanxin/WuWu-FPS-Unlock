namespace WuWaFpsUnlock.Core;

public sealed record MaterialRemovalCandidate(string Source, string Path, string Sha256, long Size, PayloadKind Kind);
public sealed record MaterialRemovalPlan(string GameRoot, string ManifestHash, List<MaterialRemovalCandidate> Candidates, List<string> Preserved);
public sealed record MaterialRemovalResult(int Removed, int Preserved, int Failed);

// Matching material is evidence of byte identity, never proof that this tool installed it.
// The caller must show and confirm the complete plan before calling ExecuteAsync.
public static class UserMaterialRemoval
{
    public static async Task<MaterialRemovalPlan> PlanAsync(string manifestPath, string gameRoot, CancellationToken token = default, IReadOnlySet<string>? ownedPaths = null)
    {
        gameRoot = SafePaths.GameRoot(gameRoot);
        var manifest = PackageReader.Load(manifestPath);
        string sourceRoot = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        MaterialSafety.RequireOutsideGame(gameRoot, sourceRoot);
        var candidates = new List<MaterialRemovalCandidate>(); var preserved = new List<string>();
        var targets = PackageReader.FindExistingMaterialTargets(gameRoot,
            manifest.Files.Select(f => Path.GetFileName(f.Target.Replace('\\', '/'))), token);
        foreach (var file in manifest.Files)
        {
            token.ThrowIfCancellationRequested();
            string name = Path.GetFileName(file.Target.Replace('\\', '/'));
            // Completed owned files use receipt fingerprints, not today's payload.
            // Unused or missing material must not prevent cleanup of those files.
            var matches = targets[name].Where(path => ownedPaths?.Contains(path) != true).ToList();
            if (matches.Count == 0) continue;
            string source = SafePaths.Under(sourceRoot, file.Source);
            if (!File.Exists(source))
            { preserved.Add("保留未登记的文件，源材料缺失，无法核对指纹：" + name); continue; }
            var (sha256, size) = await PackageReader.ReadSourceFingerprintAsync(source, token);
            foreach (var path in matches)
            {
                token.ThrowIfCancellationRequested(); SafePaths.EnsureNoLinks(gameRoot, path);
                MaterialSafety.RequireDifferentFiles(source, path);
                if (new FileInfo(path).Length == size && string.Equals(await SafePaths.HashAsync(path, token), sha256, StringComparison.OrdinalIgnoreCase))
                    candidates.Add(new(source, path, sha256, size, file.Kind));
                else preserved.Add("保留，材料哈希不匹配：" + path);
            }
        }
        return new(gameRoot, await SafePaths.HashAsync(manifestPath, token), candidates, preserved);
    }
    public static async Task<MaterialRemovalResult> ExecuteAsync(MaterialRemovalPlan plan, Action<string> log, CancellationToken token = default, Action? requireStopped = null)
    {
        // All sources and candidates are rechecked before the first removal.
        foreach (var file in plan.Candidates)
        {
            token.ThrowIfCancellationRequested();
            SafePaths.EnsureInside(plan.GameRoot, file.Path); SafePaths.EnsureNoLinks(plan.GameRoot, file.Path);
            MaterialSafety.RequireOutsideGame(plan.GameRoot, Path.GetDirectoryName(file.Source)!);
            MaterialSafety.RequireDifferentFiles(file.Source, file.Path);
            string name = Path.GetFileName(file.Path);
            if ((file.Kind == PayloadKind.Vendor && !PackageReader.VendorNames.Contains(name)) ||
                (file.Kind == PayloadKind.Addon && name != "renodx-mfgunlock.addon64") || !Enum.IsDefined(file.Kind))
                throw new InvalidDataException("清除候选不是允许的材料文件：" + file.Path);
            if (!File.Exists(file.Source) || new FileInfo(file.Source).Length != file.Size || await SafePaths.HashAsync(file.Source, token) != file.Sha256)
                throw new InvalidDataException("清除确认后源材料已改变：" + file.Source);
            if (!File.Exists(file.Path) || new FileInfo(file.Path).Length != file.Size || await SafePaths.HashAsync(file.Path, token) != file.Sha256)
                throw new IOException("清除确认后目标已改变，请重新预览：" + file.Path);
        }
        token.ThrowIfCancellationRequested(); requireStopped?.Invoke();
        int removed = 0, preserved = plan.Preserved.Count, failed = 0;
        foreach (string message in plan.Preserved) log(message);
        foreach (var file in plan.Candidates)
        {
            token.ThrowIfCancellationRequested();
            requireStopped?.Invoke();
            try
            {
                SafePaths.EnsureNoLinks(plan.GameRoot, file.Path);
                if (await OwnedFileDeletion.DeleteMatchingAsync(file.Path, file.Sha256, token, requireStopped))
                { removed++; log("按本次明确确认移除既有用户材料（非本工具安装证明）：" + file.Path); }
                else { preserved++; log("保留确认后变化的材料：" + file.Path); }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { failed++; log("材料清除失败，未认领所有权：" + file.Path + "；" + ex.Message); }
        }
        return new(removed, preserved, failed);
    }
}
