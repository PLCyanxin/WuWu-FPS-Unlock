using System.IO.Compression;
namespace WuWaFpsUnlock.Services;
internal static class RollbackWorkerResource
{
    public static string Extract()
    {
        string directory = Path.Combine(AppPaths.Base, ".updates", "rollback-" + Guid.NewGuid().ToString("N"));
        for (string? p = directory; p is not null; p = Path.GetDirectoryName(p))
            if (Directory.Exists(p) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw new IOException("回退暂存目录不能包含链接。");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "WuWaUpdater.exe");
        using var input = typeof(RollbackWorkerResource).Assembly.GetManifestResourceStream("WuWaFpsUnlock.RollbackWorker.zip")
            ?? throw new IOException("此启动器缺少内置回退程序。");
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        var entry = archive.GetEntry("WuWaUpdater.exe");
        if (archive.Entries.Count != 1 || entry is null || entry.Length <= 0 || entry.Length > 256L * 1024 * 1024)
            throw new IOException("内置回退程序资源无效。");
        try
        {
            using var source = entry.Open();
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            source.CopyTo(output);
            if (output.Length != entry.Length) throw new IOException("内置回退程序未完整解压。");
            output.Flush(true);
        }
        catch { if (File.Exists(path)) File.Delete(path); throw; }
        return path;
    }
}
