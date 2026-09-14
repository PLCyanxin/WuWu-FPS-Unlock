namespace WuWaFpsUnlock.Core;

public static class DeploymentFiles
{
    private static async Task<bool> IsCompletedMatchAsync(DeploymentReceipt receipt, PlannedFile file, CancellationToken token) =>
        receipt.Files.Any(r => r.Path.Equals(file.Target, StringComparison.OrdinalIgnoreCase) && r.Completed && r.InstalledHash == file.Sha256)
        && await SafePaths.HashAsync(file.Target, token) == file.Sha256;
    public static async Task ApplyAsync(IReadOnlyList<PlannedFile> plan, DeploymentReceipt receipt, Action persist, Action<string> log, CancellationToken token = default)
    {
        foreach (var f in plan)
        {
            token.ThrowIfCancellationRequested();
            SafePaths.EnsureInside(receipt.GameRoot, f.Target); SafePaths.EnsureNoLinks(receipt.GameRoot, f.Target);
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
                { entry.InstalledHash = f.Sha256; entry.Completed = true; persist(); log("已一致，跳过：" + f.Target); continue; }
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
                    entry.Completed = true; persist(); log("已写入并校验：" + f.Target);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
        }
        catch { receipt.Status = "PartialFailure"; persist(); throw; }
    }
    public static async Task CleanOwnedAddonsAsync(DeploymentReceipt receipt, Action persist, Action<string> log, CancellationToken token = default)
    {
        bool failures = false, skipped = false;
        foreach (var f in receipt.Files)
        {
            token.ThrowIfCancellationRequested();
            bool ownedAddon = f.Kind == "Addon" && f.CreatedByTool && Path.GetFileName(f.Path).Equals("renodx-mfgunlock.addon64", StringComparison.OrdinalIgnoreCase);
            bool ownedVendor = f.Kind == "Vendor" && f.ReplacedByTool && PackageReader.VendorNames.Contains(Path.GetFileName(f.Path));
            bool ownedReShade = ReShadeOwnership.CanClean(receipt, f);
            if (!ownedAddon && !ownedVendor && !ownedReShade) continue;
            try
            {
                SafePaths.EnsureInside(receipt.GameRoot, f.Path); SafePaths.EnsureNoLinks(receipt.GameRoot, f.Path);
                if (!File.Exists(f.Path)) { f.Completed = false; persist(); continue; }
                if (!f.Completed) { skipped = true; log("保留未完成登记的文件：" + f.Path); continue; }
                if (!await OwnedFileDeletion.DeleteMatchingAsync(f.Path, f.InstalledHash, token)) { skipped = true; log("保留已被他人修改的文件：" + f.Path); continue; }
                f.Completed = false; f.ReplacedByTool = false; f.CreatedByTool = false; persist(); log("已移除本工具部署文件：" + f.Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { failures = true; log("清除失败，保留记录：" + f.Path + "；" + ex.Message); }
        }
        try
        {
            if (!string.IsNullOrWhiteSpace(receipt.IniPath) && File.Exists(receipt.IniPath))
            {
                SafePaths.EnsureInside(receipt.GameRoot, receipt.IniPath); SafePaths.EnsureNoLinks(receipt.GameRoot, receipt.IniPath);
                var ini = IniDocument.Load(receipt.IniPath); ini.RemoveOwnedEdits(receipt, log); ini.Save(receipt.IniPath);
            }
            receipt.IniEdits.Clear();
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