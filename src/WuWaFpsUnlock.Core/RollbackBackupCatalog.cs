using System.Security.Cryptography;
using System.Text.Json;

namespace WuWaFpsUnlock.Core;

public sealed record RollbackBackup(string Directory);

/// <summary>Read-only discovery. The independent updater revalidates before any restore.</summary>
public static class RollbackBackupCatalog
{
    public static RollbackBackup? Find(string installation, CancellationToken cancellation = default)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installation));
        NoLinks(root);
        // Completion rewrites snapshot.json; directory timestamps can change for unrelated files.
        foreach (var candidate in System.IO.Directory.EnumerateDirectories(root, "update-backup-*", SearchOption.TopDirectoryOnly)
            .OrderByDescending(p => File.GetLastWriteTimeUtc(Path.Combine(p, "snapshot.json")))
            .ThenByDescending(Path.GetFileName, StringComparer.Ordinal))
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                // A proven incomplete update must not hide the preceding successful backup.
                // Corrupt/unknown metadata and consumed completed backups fail closed.
                if (IsIncomplete(root, candidate)) continue;
                return Validate(root, candidate, cancellation);
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException) { return null; }
        }
        return null;
    }
    private static bool IsIncomplete(string root, string backup)
    {
        string marker = Path.Combine(backup, "rollback.completed");
        if (File.Exists(marker) || System.IO.Directory.Exists(marker)) return false;
        string manifest = Scoped(backup, "snapshot.json");
        if (new FileInfo(manifest).Length > 16 * 1024 * 1024) throw new InvalidDataException("备份清单过大。");
        byte[] bytes = File.ReadAllBytes(manifest);
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(File.ReadAllText(Scoped(backup, "snapshot.sha256")).Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        using var json = JsonDocument.Parse(bytes);
        var value = json.RootElement;
        return value.GetProperty("Schema").GetInt32() == 1
            && Path.TrimEndingDirectorySeparator(Path.GetFullPath(value.GetProperty("Root").GetString()!)).Equals(root, StringComparison.OrdinalIgnoreCase)
            && !value.GetProperty("Complete").GetBoolean();
    }
    public static RollbackBackup Validate(string installation, string backup, CancellationToken cancellation = default)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installation));
        backup = Path.TrimEndingDirectorySeparator(Path.GetFullPath(backup));
        if (!string.Equals(Path.GetDirectoryName(backup), root, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(backup).StartsWith("update-backup-", StringComparison.Ordinal)) throw new InvalidDataException("备份不属于当前安装。");
        NoLinks(backup);
        string marker = Path.Combine(backup, "rollback.completed");
        if (File.Exists(marker) || System.IO.Directory.Exists(marker)) throw new InvalidDataException("此版本已经回退。");
        string manifest = Scoped(backup, "snapshot.json");
        if (new FileInfo(manifest).Length > 16 * 1024 * 1024) throw new InvalidDataException("备份清单过大。");
        byte[] bytes = File.ReadAllBytes(manifest);
        string digest = File.ReadAllText(Scoped(backup, "snapshot.sha256")).Trim();
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(digest, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("备份清单损坏。");
        using var json = JsonDocument.Parse(bytes);
        var value = json.RootElement;
        if (value.GetProperty("Schema").GetInt32() != 1 || !value.GetProperty("Complete").GetBoolean()
            || !Path.TrimEndingDirectorySeparator(Path.GetFullPath(value.GetProperty("Root").GetString()!)).Equals(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("备份尚未完成或归属不符。");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool launcher = false;
        foreach (var entry in value.GetProperty("Files").EnumerateArray())
        {
            cancellation.ThrowIfCancellationRequested();
            string relative = entry.GetProperty("Relative").GetString()!;
            if (!SnapshotPath(relative) || !seen.Add(relative)) throw new InvalidDataException("备份路径重复。");
            string file = Scoped(backup, "snapshot/" + relative);
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length != entry.GetProperty("Size").GetInt64()
                || !Convert.ToHexString(SHA256.HashData(stream)).Equals(entry.GetProperty("Hash").GetString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("备份文件损坏。");
            launcher |= relative.Equals("WuWaFpsUnlock.exe", StringComparison.OrdinalIgnoreCase);
        }
        foreach (var directory in value.GetProperty("Directories").EnumerateArray())
        {
            string relative = directory.GetString()!;
            if (!new[] { "components", "payload", "licenses", "data" }.Any(d => relative.Equals(d, StringComparison.OrdinalIgnoreCase) || relative.StartsWith(d + "/", StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("备份目录范围无效。");
            Scoped(root, relative);
        }
        foreach (var entry in value.GetProperty("Updated").EnumerateArray())
        {
            string relative = entry.GetProperty("Relative").GetString()!;
            if (!SnapshotPath(relative) || relative.StartsWith("data/", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("更新目标范围无效。");
            Scoped(root, relative);
        }
        if (!launcher) throw new InvalidDataException("备份缺少启动器。");
        return new(backup);
    }
    private static bool SnapshotPath(string relative) => !relative.Contains('\\') && (!relative.Contains('/')
        ? !relative.Equals("WuWaUpdater.exe", StringComparison.OrdinalIgnoreCase) && new[] { ".exe", ".dll", ".json", ".config", ".pdb", ".ico", ".ini", ".txt", ".md" }.Contains(Path.GetExtension(relative), StringComparer.OrdinalIgnoreCase)
        : new[] { "components", "payload", "licenses", "data" }.Any(d => relative.StartsWith(d + "/", StringComparison.OrdinalIgnoreCase)));
    private static string Scoped(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains('\\') || relative.Contains(':') || Path.IsPathRooted(relative)
            || relative.Split('/').Any(p => p is "" or "." or ".." || p.EndsWith('.') || p.EndsWith(' '))) throw new InvalidDataException("无效备份路径。");
        string path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("备份路径越界。");
        NoLinks(path); return path;
    }
    private static void NoLinks(string path)
    {
        for (string? p = Path.GetFullPath(path); p is not null; p = Path.GetDirectoryName(p))
            if ((File.Exists(p) || System.IO.Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("备份路径不能包含链接。");
    }
}





