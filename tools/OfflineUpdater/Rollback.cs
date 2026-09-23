using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static partial class Program
{
    static readonly string[] SnapshotDirectories = ["components", "payload", "licenses", "data"];
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    static Snapshot CaptureSnapshot(string root, string backup, string updaterDirectory, List<Entry> entries)
    {
        var files = new List<SnapshotFile>();
        var directories = new List<string>();
        var sourceFiles = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root))
            if (RootRuntimeFile(Path.GetFileName(file)) && !Path.GetFullPath(file).Equals(Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                sourceFiles.Add(file);
        foreach (string name in SnapshotDirectories)
        {
            string directory = Path.Combine(root, name);
            NoLinks(directory);
            if (!Directory.Exists(directory)) continue;
            Gather(directory);
        }
        void Gather(string directory)
        {
            NoLinks(directory);
            if (Path.TrimEndingDirectorySeparator(directory).Equals(Path.TrimEndingDirectorySeparator(updaterDirectory), StringComparison.OrdinalIgnoreCase))
                throw new IOException("更新器不能放在 components、payload、licenses 或 data 内，请移到安装目录或独立子目录。");
            directories.Add(Path.GetRelativePath(root, directory).Replace('\\', '/'));
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                NoLinks(path);
                if (Directory.Exists(path)) Gather(path); else sourceFiles.Add(path);
            }
        }
        foreach (string source in sourceFiles)
        {
            NoLinks(source);
            string relative = Path.GetRelativePath(root, source).Replace('\\', '/');
            string destination = Scoped(backup, "snapshot/" + relative);
            var held = entries.FirstOrDefault(e => e.Target.Equals(source, StringComparison.OrdinalIgnoreCase))?.TargetHandle;
            FileStream? opened = null;
            try
            {
                var stream = held ?? (opened = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read));
                string hash = Hash(stream);
                CopyFromHandle(stream, destination, hash);
                files.Add(new(relative, hash, stream.Length));
            }
            finally { opened?.Dispose(); }
        }
        foreach (string directory in directories) Directory.CreateDirectory(Scoped(backup, "snapshot/" + directory));
        var snapshot = new Snapshot(1, Path.GetFullPath(root), false, files, directories,
            entries.Select(e => new UpdatedFile(e.Relative, e.OldHash, e.NewHash)).ToList());
        WriteSnapshot(backup, snapshot);
        // Verify the complete backup, including data, before allowing the first update write.
        ValidateSnapshot(backup, snapshot);
        return snapshot;
    }

    static bool RootRuntimeFile(string name) => !name.Equals("WuWaUpdater.exe", StringComparison.OrdinalIgnoreCase)
        && new[] { ".exe", ".dll", ".json", ".config", ".pdb", ".ico", ".ini", ".txt", ".md" }.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);

    static bool SnapshotPath(string relative) => !relative.Contains('\\') && (!relative.Contains('/') ? RootRuntimeFile(relative)
        : SnapshotDirectories.Any(d => relative.StartsWith(d + "/", StringComparison.OrdinalIgnoreCase)));

    static string Scoped(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':')
            || relative.Replace('\\', '/').Split('/').Any(s => s is "" or "." or ".." || s.EndsWith('.') || s.EndsWith(' ') || s.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0))
            throw new InvalidDataException("备份包含无效相对路径：" + relative);
        string path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("备份路径越界：" + relative);
        NoLinks(path);
        return path;
    }

    static void WriteSnapshot(string backup, Snapshot snapshot)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);
        File.WriteAllBytes(Scoped(backup, "snapshot.json"), bytes);
        File.WriteAllText(Scoped(backup, "snapshot.sha256"), Convert.ToHexString(SHA256.HashData(bytes)), Encoding.ASCII);
    }

    static Snapshot ReadSnapshot(string backup)
    {
        byte[] bytes = File.ReadAllBytes(Scoped(backup, "snapshot.json"));
        if (Convert.ToHexString(SHA256.HashData(bytes)) != File.ReadAllText(Scoped(backup, "snapshot.sha256")).Trim())
            throw new InvalidDataException("备份清单已损坏，未写入任何安装文件。");
        var snapshot = JsonSerializer.Deserialize<Snapshot>(bytes) ?? throw new InvalidDataException("备份清单为空。");
        ValidateSnapshot(backup, snapshot);
        return snapshot;
    }

    static void ValidateSnapshot(string backup, Snapshot snapshot)
    {
        if (snapshot.Schema != 1 || snapshot.Files is null || snapshot.Directories is null || snapshot.Updated is null)
            throw new InvalidDataException("备份格式不受支持。");
        string root = Path.GetFullPath(snapshot.Root);
        NoLinks(root); NoLinks(backup);
        if (!string.Equals(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(backup)), Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(Path.TrimEndingDirectorySeparator(backup)).StartsWith("update-backup-", StringComparison.Ordinal))
            throw new InvalidDataException("备份必须保留在原安装目录中，不能指向其他目录。");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in snapshot.Files)
        {
            if (!SnapshotPath(file.Relative) || !seen.Add(file.Relative)) throw new InvalidDataException("备份文件范围或重复路径无效。");
            Scoped(root, file.Relative);
            string path = Scoped(backup, "snapshot/" + file.Relative);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length != file.Size || Hash(stream) != file.Hash) throw new InvalidDataException("备份文件损坏：" + file.Relative);
        }
        foreach (var directory in snapshot.Directories)
        {
            if (!SnapshotDirectories.Any(d => directory.Equals(d, StringComparison.OrdinalIgnoreCase) || directory.StartsWith(d + "/", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("备份目录范围无效。");
            Scoped(root, directory);
        }
        seen.Clear();
        foreach (var updated in snapshot.Updated)
        {
            if (!Allowed(updated.Relative) || !seen.Add(updated.Relative)) throw new InvalidDataException("更新文件范围无效。");
            Scoped(root, updated.Relative);
            var original = snapshot.Files.SingleOrDefault(f => f.Relative.Equals(updated.Relative, StringComparison.OrdinalIgnoreCase));
            if (updated.OldHash != original?.Hash) throw new InvalidDataException("快照与更新前记录不一致：" + updated.Relative);
        }
    }

    static void PrepareRollbackLauncher(string backup, string updaterDirectory)
    {
        string self = Environment.ProcessPath ?? throw new IOException("无法定位回退器。");
        NoLinks(self);
        using (var input = new FileStream(self, FileMode.Open, FileAccess.Read, FileShare.Read))
            CopyFromHandle(input, Scoped(backup, "WuWaUpdater.exe"), Hash(input));
        const string local = "@echo off\r\n\"%~dp0WuWaUpdater.exe\" --rollback \"%~dp0.\"\r\n";
        File.WriteAllText(Scoped(backup, "rollback.cmd"), local, Encoding.ASCII);
        string relative = Path.GetRelativePath(updaterDirectory, backup);
        // Backup names and parent components are generated ASCII; never interpolate user paths.
        if (relative.Any(c => c > 127 || c is '"' or '%' or '\r' or '\n')) throw new IOException("无法生成相对回退入口。");
        File.WriteAllText(Scoped(updaterDirectory, "回退.cmd"), "@echo off\r\n\"%~dp0" + relative + "\\WuWaUpdater.exe\" --rollback \"%~dp0" + relative + "\"\r\n", Encoding.ASCII);
    }

    static int Rollback(string input)
    {
        string backup = Path.TrimEndingDirectorySeparator(Path.GetFullPath(input));
        NoLinks(backup);
        var snapshot = ReadSnapshot(backup);
        if (!snapshot.Complete) throw new InvalidDataException("此备份对应的更新未完成；请先检查当时的失败恢复记录。");
        string exe = Scoped(snapshot.Root, "WuWaFpsUnlock.exe");
        ProductVersion(exe);
        ProductVersion(Scoped(backup, "snapshot/WuWaFpsUnlock.exe"));
        RejectRunning(exe);
        Console.WriteLine("回退安装目录：" + snapshot.Root + "\n恢复更新前程序和材料；保留当前 data 配置及部署记录，之后需要重新部署。");
        RestoreSnapshot(backup, snapshot, () => RejectRunning(exe));
        RefreshDesktopShortcut(snapshot.Root);
        Console.WriteLine("回退完成。程序与材料已恢复更新前状态；当前配置、部署记录和后来新增的其他文件已保留。\n请打开启动器，在设置中重新部署一次");
        return 0;
    }

    static void RestoreSnapshot(string backup, Snapshot snapshot, Action beforeWrites, Action<int>? afterWrite = null)
    {
        ValidateSnapshot(backup, snapshot);
        var operations = new List<RestoreFile>();
        var held = new List<FileStream>();
        string transaction = Path.Combine(snapshot.Root, "rollback-attempt-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var file in snapshot.Files.Where(f => !f.Relative.StartsWith("data/", StringComparison.OrdinalIgnoreCase)))
                Plan(file.Relative, Scoped(backup, "snapshot/" + file.Relative), file.Hash, null);
            foreach (var file in snapshot.Updated.Where(f => f.OldHash is null))
                Plan(file.Relative, null, null, file.NewHash);
            void Plan(string relative, string? source, string? desired, string? removable)
            {
                string target = Scoped(snapshot.Root, relative);
                if (Directory.Exists(target)) throw new IOException("回退目标是目录：" + target);
                FileStream? current = null;
                string? hash = null;
                if (File.Exists(target))
                {
                    current = new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    held.Add(current); hash = Hash(current);
                }
                if (hash == desired) return;
                if (removable is not null && hash != removable)
                { Console.WriteLine("保留后来更改的新增文件：" + target); return; }
                operations.Add(new(relative, target, source, desired, hash, current));
            }
            NoLinks(transaction); Directory.CreateDirectory(transaction);
            foreach (var operation in operations)
            {
                if (operation.Current is not null)
                    CopyFromHandle(operation.Current, Scoped(transaction, "before/" + operation.Relative), operation.Before!);
                if (operation.Source is not null)
                {
                    using var source = new FileStream(operation.Source, FileMode.Open, FileAccess.Read, FileShare.Read);
                    CopyFromHandle(source, Scoped(transaction, "staged/" + operation.Relative), operation.Desired!);
                }
            }
            File.WriteAllText(Scoped(transaction, "operations.json"), JsonSerializer.Serialize(operations.Select(o => new { o.Relative, o.Before, o.Desired }), JsonOptions));
            beforeWrites();
            foreach (var operation in operations)
            {
                operation.Current?.Dispose();
                EnsureUnchanged(operation.Target, operation.Before);
                NoLinks(operation.Target);
                if (operation.Desired is null) File.Delete(operation.Target);
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(operation.Target)!);
                    string staged = Scoped(transaction, "staged/" + operation.Relative);
                    if (operation.Before is null) File.Move(staged, operation.Target, false);
                    else File.Replace(staged, operation.Target, null);
                }
                operation.Applied = true;
                EnsureUnchanged(operation.Target, operation.Desired);
                afterWrite?.Invoke(operations.Count(o => o.Applied));
            }
            foreach (string directory in snapshot.Directories.Where(d => !d.Equals("data", StringComparison.OrdinalIgnoreCase) && !d.StartsWith("data/", StringComparison.OrdinalIgnoreCase)))
                Directory.CreateDirectory(Scoped(snapshot.Root, directory));
            Console.WriteLine("回退前状态备份：" + transaction);
        }
        catch
        {
            foreach (var stream in held) stream.Dispose();
            bool complete = true;
            foreach (var operation in operations.Where(o => o.Applied).Reverse())
            {
                try
                {
                    EnsureUnchanged(operation.Target, operation.Desired);
                    if (operation.Before is null) File.Delete(operation.Target);
                    else
                    {
                        string temp = Scoped(transaction, "recover-" + Guid.NewGuid().ToString("N"));
                        using var source = new FileStream(Scoped(transaction, "before/" + operation.Relative), FileMode.Open, FileAccess.Read, FileShare.Read);
                        CopyFromHandle(source, temp, operation.Before);
                        if (operation.Desired is null) File.Move(temp, operation.Target, false); else File.Replace(temp, operation.Target, null);
                        EnsureUnchanged(operation.Target, operation.Before);
                    }
                }
                catch (Exception ex) { complete = false; Console.Error.WriteLine("回退失败后的恢复错误：" + operation.Target + "；" + ex.Message); }
            }
            Console.Error.WriteLine(complete ? "回退未完成；本次文件变更已恢复至回退前状态。" : "回退失败且未能完整恢复；请勿启动应用，按事务备份手动恢复。");
            Console.Error.WriteLine("回退事务目录：" + transaction);
            throw;
        }
        finally { foreach (var stream in held) stream.Dispose(); }
    }
}

internal sealed record Snapshot(int Schema, string Root, bool Complete, List<SnapshotFile> Files, List<string> Directories, List<UpdatedFile> Updated);
internal sealed record SnapshotFile(string Relative, string Hash, long Size);
internal sealed record UpdatedFile(string Relative, string? OldHash, string NewHash);
internal sealed class RestoreFile(string relative, string target, string? source, string? desired, string? before, FileStream? current)
{
    public string Relative { get; } = relative;
    public string Target { get; } = target;
    public string? Source { get; } = source;
    public string? Desired { get; } = desired;
    public string? Before { get; } = before;
    public FileStream? Current { get; } = current;
    public bool Applied { get; set; }
}
