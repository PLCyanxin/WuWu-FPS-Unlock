using System.Text;

internal static partial class Program
{
    static Snapshot StageUpdateSnapshot(string root, string backup, string updaterDirectory, List<Entry> entries)
    {
        foreach (var entry in entries)
        {
            entry.Staged = Scoped(backup, "staged/" + entry.Relative);
            CopyFromHandle(entry.SourceHandle!, entry.Staged, entry.NewHash);
        }
        File.WriteAllLines(Scoped(backup, "files.txt"), entries.Select(e => $"{e.Relative}\told={e.OldHash ?? "(absent)"}\tnew={e.NewHash}"), Encoding.UTF8);
        // The full, verified snapshot is also the transaction's rollback source.
        // Never hard-link an active installation or maintain a second original copy.
        // Capture failure precedes every target write, so it requires no restoration.
        var snapshot = CaptureSnapshot(root, backup, updaterDirectory, entries);
        foreach (var entry in entries)
            if (entry.OldHash is not null)
            {
                var saved = snapshot.Files.SingleOrDefault(f => f.Relative.Equals(entry.Relative, StringComparison.OrdinalIgnoreCase));
                if (saved?.Hash != entry.OldHash) throw new InvalidDataException("完整快照缺少更新前文件：" + entry.Relative);
                entry.Backup = Scoped(backup, "snapshot/" + saved.Relative);
            }
        return snapshot;
    }

    static void ApplyUpdateEntries(List<Entry> entries, Action<int>? afterWrite = null)
    {
        int appliedCount = 0;
        foreach (var entry in entries)
        {
            NoLinks(entry.Target); NoLinks(entry.Staged!);
            Directory.CreateDirectory(Path.GetDirectoryName(entry.Target)!);
            entry.TargetHandle?.Dispose();
            // Windows replacement needs the destination handle closed; recheck immediately.
            EnsureUnchanged(entry.Target, entry.OldHash);
            if (entry.OldHash is null) File.Move(entry.Staged!, entry.Target, false);
            else File.Replace(entry.Staged!, entry.Target, null);
            entry.Applied = true;
            EnsureUnchanged(entry.Target, entry.NewHash);
            Console.WriteLine("已更新：" + entry.Relative);
            afterWrite?.Invoke(++appliedCount);
        }
    }

    static bool RestoreFailedUpdate(List<Entry> entries, string? backup, Action? afterRestoreStaged = null)
    {
        bool restored = true;
        foreach (var entry in entries.Where(e => e.Applied).Reverse())
        {
            try
            {
                NoLinks(entry.Target);
                EnsureUnchanged(entry.Target, entry.NewHash);
                if (entry.Backup is null) File.Delete(entry.Target);
                else
                {
                    NoLinks(entry.Backup);
                    string restore = Path.Combine(backup!, "restore-" + Guid.NewGuid().ToString("N"));
                    using var original = new FileStream(entry.Backup, FileMode.Open, FileAccess.Read, FileShare.Read);
                    CopyFromHandle(original, restore, entry.OldHash!);
                    afterRestoreStaged?.Invoke();
                    File.Replace(restore, entry.Target, null);
                    EnsureUnchanged(entry.Target, entry.OldHash);
                }
            }
            catch (Exception ex) { restored = false; Console.Error.WriteLine($"回滚失败：{entry.Target}\n{ex.Message}"); }
        }
        return restored;
    }
}
