namespace WuWaFpsUnlock.Core;

public static class DeploymentFiles
{
    private static async Task<bool> IsCompletedMatchAsync(DeploymentReceipt receipt, PlannedFile file, CancellationToken token) =>
        receipt.Files.Any(r => r.Path.Equals(file.Target, StringComparison.OrdinalIgnoreCase) && r.Completed && r.InstalledHash == file.Sha256)
        && await SafePaths.HashAsync(file.Target, token) == file.Sha256;
    public static async Task ApplyAsync(IReadOnlyList<PlannedFile> plan, DeploymentReceipt receipt, Action persist, Action<string> log, CancellationToken token = default, Action? requireStopped = null)
    {
        foreach (var f in plan)
        {
            token.ThrowIfCancellationRequested();
            SafePaths.EnsureInside(receipt.GameRoot, f.Target); SafePaths.EnsureNoLinks(receipt.GameRoot, f.Target);
            MaterialSafety.RequireOutsideGame(receipt.GameRoot, Path.GetDirectoryName(f.Source)!);
            MaterialSafety.RequireDifferentFiles(f.Source, f.Target);
            if (new FileInfo(f.Source).Length != f.Size || await SafePaths.HashAsync(f.Source, token) != f.Sha256) throw new InvalidDataException("部署前源文件已改变：" + f.Source);
            if (f.Kind == PayloadKind.Vendor && !File.Exists(f.Target)) throw new IOException("同名目标已经消失，禁止新增：" + f.Target);
            if (f.ExpectedTargetHash is null && File.Exists(f.Target) && !await IsCompletedMatchAsync(receipt, f, token)) throw new IOException("目标在确认后新增，请重新预览：" + f.Target);
            if (File.Exists(f.Target)) { using var test = new FileStream(f.Target, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
            if (f.ExpectedTargetHash is not null && await SafePaths.HashAsync(f.Target, token) != f.ExpectedTargetHash
                && await SafePaths.HashAsync(f.Target, token) != f.Sha256) throw new IOException("目标在规划后已变化，请重新检查：" + f.Target);
        }
        receipt.Status = "Installing"; persist();
        try
        {
            foreach (var f in plan)
            {
                token.ThrowIfCancellationRequested();
                SafePaths.EnsureInside(receipt.GameRoot, f.Target); SafePaths.EnsureNoLinks(receipt.GameRoot, f.Target);
                bool exists = File.Exists(f.Target);
                if (f.Kind == PayloadKind.Vendor && !exists) throw new IOException("同名目标已经消失，禁止新增：" + f.Target);
                if (f.ExpectedTargetHash is null && exists && !await IsCompletedMatchAsync(receipt, f, token)) throw new IOException("目标在确认后新增：" + f.Target);
                var currentHash = exists ? await SafePaths.HashAsync(f.Target, token) : null;
                if (currentHash != f.Sha256 && currentHash != f.ExpectedTargetHash && f.ExpectedTargetHash is not null)
                    throw new IOException("目标在规划后已变化：" + f.Target);
                var entry = receipt.Files.FirstOrDefault(x => x.Path.Equals(f.Target, StringComparison.OrdinalIgnoreCase));
                if (entry is null)
                {
                    entry = new() { Path = f.Target, CreatedByTool = !exists, Kind = f.Kind.ToString() };
                    receipt.Files.Add(entry);
                }
                // Do not claim ownership over a same-hash file already present before this tool.
                if (currentHash == f.Sha256)
                { entry.InstalledHash = f.Sha256; entry.Completed = true; RecordSource(entry, f); persist(); log("已一致，跳过：" + f.Target); continue; }
                entry.InstalledHash = f.Sha256; entry.Completed = false; persist();
                Directory.CreateDirectory(Path.GetDirectoryName(f.Target)!);
                string temp = f.Target + ".wwfps-" + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await using (var src = new FileStream(f.Source, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true))
                    await using (var dst = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true))
                    { await src.CopyToAsync(dst, token); await dst.FlushAsync(token); }
                    if (await SafePaths.HashAsync(temp, token) != f.Sha256) throw new IOException("暂存文件校验失败。");
                    SafePaths.EnsureNoLinks(receipt.GameRoot, f.Target);
                    token.ThrowIfCancellationRequested();
                    // Persisting intent and staging can take time: never commit using the
                    // target fingerprint captured before those steps.
                    MaterialSafety.RequireDifferentFiles(f.Source, f.Target);
                    var finalHash = File.Exists(f.Target) ? await SafePaths.HashAsync(f.Target, token) : null;
                    if (finalHash != currentHash) throw new IOException("目标在暂存期间已变化，请重新预览：" + f.Target);
                    requireStopped?.Invoke();
                    if (f.Kind == PayloadKind.Vendor)
                    {
                        // File.Replace requires an existing destination; it cannot recreate a missing game DLL.
                        File.Replace(temp, f.Target, null);
                        entry.ReplacedByTool = true;
                    }
                    else if (f.ExpectedTargetHash is null) File.Move(temp, f.Target, false);
                    else File.Replace(temp, f.Target, null);
                    receipt.PendingDeploymentChanges = true;
                    if (await SafePaths.HashAsync(f.Target, token) != f.Sha256) throw new IOException("写入后校验失败：" + f.Target);
                    entry.Completed = true; RecordSource(entry, f); persist(); log("已写入并校验：" + f.Target);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
        }
        catch { receipt.Status = "PartialFailure"; persist(); throw; }
    }
    private static void RecordSource(FileReceipt entry, PlannedFile file)
    {
        // Vendor/addon provenance follows the last verified deployment. Ownership
        // flags are independent; ReShade retains its installer-source semantics.
        if (file.Kind is not (PayloadKind.Vendor or PayloadKind.Addon)) return;
        entry.SourcePath = Path.GetFullPath(file.Source);
        entry.SourceHash = file.Sha256;
    }
    public static bool CanClean(DeploymentReceipt receipt, FileReceipt file) =>
        (file.Kind == "Addon" && file.CreatedByTool && ManagedAddons.Contains(Path.GetFileName(file.Path))) ||
        (file.Kind == "Vendor" && file.ReplacedByTool && PackageReader.VendorNames.Contains(Path.GetFileName(file.Path))) ||
        ReShadeOwnership.CanClean(receipt, file);

    public static void ValidateCleanup(DeploymentReceipt receipt)
    {
        foreach (var file in receipt.Files.Where(f => CanClean(receipt, f)))
        { SafePaths.EnsureInside(receipt.GameRoot, file.Path); SafePaths.EnsureNoLinks(receipt.GameRoot, file.Path); }
        if (receipt.IniEdits.Count > 0)
        {
            SafePaths.EnsureInside(receipt.GameRoot, receipt.IniPath); SafePaths.EnsureNoLinks(receipt.GameRoot, receipt.IniPath);
            if (File.Exists(receipt.IniPath)) IniDocument.Load(receipt.IniPath).RemoveOwnedEdits(receipt, _ => { });
        }
    }

    public static async Task CleanOwnedAddonsAsync(DeploymentReceipt receipt, Action persist, Action<string> log, CancellationToken token = default, Action? requireStopped = null)
    {
        ValidateCleanup(receipt);
        IniDocument? preparedIni = null;
        string? initialIniHash = null;
        List<IniReceipt> preservedEdits = [];
        if (receipt.IniEdits.Count > 0 && File.Exists(receipt.IniPath))
        {
            initialIniHash = await SafePaths.HashAsync(receipt.IniPath, token);
            preparedIni = IniDocument.Load(receipt.IniPath);
            preservedEdits = preparedIni.RemoveOwnedEdits(receipt, log);
            if (await SafePaths.HashAsync(receipt.IniPath, token) != initialIniHash)
                throw new IOException("配置在清除检查期间已改变，请重新预览。");
        }
        requireStopped?.Invoke();
        bool failures = false, skipped = false;
        foreach (var f in receipt.Files)
        {
            token.ThrowIfCancellationRequested();
            if (!CanClean(receipt, f)) continue;
            requireStopped?.Invoke();
            try
            {
                SafePaths.EnsureInside(receipt.GameRoot, f.Path); SafePaths.EnsureNoLinks(receipt.GameRoot, f.Path);
                if (!File.Exists(f.Path)) { f.Completed = false; persist(); continue; }
                if (!f.Completed) { skipped = true; log("保留未完成登记的文件：" + f.Path); continue; }
                if (!await OwnedFileDeletion.DeleteMatchingAsync(f.Path, f.InstalledHash, token, requireStopped)) { skipped = true; log("保留已被他人修改的文件：" + f.Path); continue; }
                f.Completed = false; f.ReplacedByTool = false; f.CreatedByTool = false; persist(); log("已移除本工具部署文件：" + f.Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { failures = true; log("清除失败，保留记录：" + f.Path + "；" + ex.Message); }
        }
        try
        {
            if (!failures && preparedIni is not null)
            {
                requireStopped?.Invoke();
                SafePaths.EnsureInside(receipt.GameRoot, receipt.IniPath); SafePaths.EnsureNoLinks(receipt.GameRoot, receipt.IniPath);
                if (!File.Exists(receipt.IniPath) || await SafePaths.HashAsync(receipt.IniPath, token) != initialIniHash)
                    throw new IOException("配置在清除期间已改变，已保留当前配置和登记，请重新预览。");
                preparedIni.Save(receipt.IniPath);
                receipt.IniEdits = preservedEdits; skipped |= preservedEdits.Count > 0;
            }
            else if (!failures) receipt.IniEdits.Clear();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { failures = true; log("配置清理失败，保留记录：" + ex.Message); }
        receipt.Status = failures ? "PartialClean" : skipped ? "CleanedWithSkips" : "Cleaned"; receipt.Updated = DateTimeOffset.UtcNow; persist();
        log(failures ? "部分清除失败，请检查逐项记录后重试。" : skipped ? "清除已结束，部分文件因已变化或登记未完成而保留，详情见日志。" : "清除结束：移除本工具实际替换且哈希匹配的 DLL 与自有插件；保留用户原有或来源未知的 ReShade/滤镜。未恢复原版，游戏是否补齐文件尚待实测。");
        if (failures) throw new IOException("部分文件或配置清除失败，详情见日志。");
    }
    public static Task<DeploymentIntegrityReport> InspectIntegrityAsync(DeploymentReceipt receipt, CancellationToken token = default) => DeploymentIntegrity.InspectAsync(receipt, token);
    public static async Task<bool> IsIntactAsync(DeploymentReceipt receipt, CancellationToken token = default) =>
        (await InspectIntegrityAsync(receipt, token)).IsStrictlyIntact;
}
