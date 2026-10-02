using System.Text;
using System.Text.RegularExpressions;

namespace WuWaFpsUnlock.Services;

public static class DiagnosticExport
{
    private const int MaxLogBytes = 16 * 1024 * 1024;
    private static readonly Regex Secrets = new(@"(?i)(\b(?:token|access_token|refresh_token|api[_-]?key|authorization|password|secret)\s*[:=]\s*)([^\s&;,]+)|\bBearer\s+[^\s]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Redact(string text) => Secrets.Replace(text, match => match.Groups[1].Success ? match.Groups[1].Value + "[已隐藏]" : "Bearer [已隐藏]");

    // Callers supply only the recognized proxy's loading directory, never a searched game root.
    public static IReadOnlyList<string> RecentLauncherLogs(string currentLog)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(currentLog))!;
        var recent = new List<string> { Path.GetFullPath(currentLog) };
        try
        {
            recent.AddRange(Directory.EnumerateFiles(directory, "*.log", SearchOption.TopDirectoryOnly)
                .Where(path => !path.Equals(currentLog, StringComparison.OrdinalIgnoreCase))
                .Where(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
                .OrderByDescending(File.GetLastWriteTimeUtc).Take(2));
        }
        catch (IOException) { /* The current session remains exportable. */ }
        catch (UnauthorizedAccessException) { /* The current session remains exportable. */ }
        string startupError = Path.Combine(Directory.GetParent(directory)!.FullName, "startup-error.log");
        if (File.Exists(startupError)) recent.Add(startupError);
        return recent;
    }

    public static async Task WriteAsync(string destination, IReadOnlyList<string> details, IReadOnlyList<string> launcherLogs, string? reShadeDirectory, string reShadeSelection, CancellationToken token = default)
    {
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous);
        await using var writer = new StreamWriter(output, new UTF8Encoding(false));
        await writer.WriteLineAsync("鸣潮 FPS Unlock 诊断日志");
        foreach (var detail in details) await writer.WriteLineAsync(Redact(detail));
        await writer.WriteLineAsync("\n=== 启动器详细日志 ===");
        foreach (var launcherLog in launcherLogs)
        {
            await writer.WriteLineAsync("日志文件：" + Redact(launcherLog));
            await writer.WriteLineAsync(await ReadBoundedAsync(launcherLog, "暂无启动器日志", token));
        }
        await writer.WriteLineAsync("\n=== 鸣潮 ReShade.log ===");
        await writer.WriteLineAsync(Redact(reShadeSelection));
        if (reShadeDirectory is null) await writer.WriteLineAsync("暂无 ReShade 日志");
        else await writer.WriteLineAsync(await ReadBoundedAsync(Path.Combine(reShadeDirectory, "ReShade.log"), "暂无 ReShade 日志", token));
    }

    public static async Task<string> ReadBoundedAsync(string path, string missingText, CancellationToken token = default)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return "日志路径为链接，未读取：" + Redact(path);
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            long available = file.Length;
            long skipped = Math.Max(0, available - MaxLogBytes);
            if (skipped > 0) file.Seek(skipped, SeekOrigin.Begin);
            int limit = (int)Math.Min(MaxLogBytes, available - skipped);
            byte[] bytes = new byte[limit];
            int count = 0;
            while (count < limit)
            {
                int read = await file.ReadAsync(bytes.AsMemory(count, limit - count), token);
                if (read == 0) break;
                count += read;
            }
            string content = Encoding.UTF8.GetString(bytes, 0, count).TrimStart('\uFEFF');
            return (skipped > 0 ? $"[日志超过 {MaxLogBytes / 1024 / 1024} MiB，已省略前 {skipped} 字节]\n" : "") + Redact(content);
        }
        catch (FileNotFoundException) { return missingText; }
        catch (DirectoryNotFoundException) { return missingText; }
        catch (UnauthorizedAccessException error) { return "日志存在但读取被拒绝：" + Redact(error.Message); }
        catch (IOException error) { return "日志读取失败：" + Redact(error.Message); }
    }
}
