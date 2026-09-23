using System.Reflection;
using System.Security.Cryptography;
using WuWaFpsUnlock.Core;
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
        using var input = typeof(RollbackWorkerResource).Assembly.GetManifestResourceStream("WuWaFpsUnlock.RollbackWorker.exe")
            ?? throw new IOException("此启动器缺少内置回退程序。");
        using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { input.CopyTo(output); output.Flush(true); }
        return path;
    }
}
