using System.Diagnostics;

internal static partial class Program
{
    static int WaitThenUpdate(string pidText, string expectedLauncher)
    {
        if (!int.TryParse(pidText, out int pid) || pid <= 0) throw new ArgumentException("启动器 PID 无效。");
        string updaterDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        NoLinks(updaterDirectory);
        string root = FindInstallation(updaterDirectory);
        string exe = Path.Combine(root, "WuWaFpsUnlock.exe");
        MatchExpectedLauncher(exe, expectedLauncher);
        ProductVersion(exe);
        Console.WriteLine("等待启动器自然退出（最多 30 秒）：" + exe);
        WaitForLauncherExit(pid, exe, OpenLauncherProcess);
        // Re-resolve and compare the installation after waiting. Update also checks
        // for any remaining launcher instance and locks destinations before writes.
        return Update(exe);
    }

    static void MatchExpectedLauncher(string installationExe, string expectedLauncher)
    {
        if (!Path.IsPathFullyQualified(expectedLauncher)) throw new InvalidDataException("启动器预期路径必须为完整路径。");
        NoLinks(expectedLauncher); NoLinks(installationExe);
        if (!Path.GetFullPath(expectedLauncher).Equals(Path.GetFullPath(installationExe), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("启动器预期路径与找到的安装目录不一致，已停止更新。");
    }

    static void WaitForLauncherExit(int pid, string expectedLauncher, Func<int, ILauncherProcess?> open)
    {
        if (pid <= 0) throw new ArgumentException("启动器 PID 无效。");
        try
        {
            using var process = open(pid);
            // The launcher may naturally exit before the worker reaches this point.
            // An already-absent PID is not waited on or treated as a verified live process.
            if (process is null || process.HasExited) return;
            string? actual;
            try { actual = process.ExecutablePath; }
            catch (InvalidOperationException) when (process.HasExited) { return; }
            if (string.IsNullOrWhiteSpace(actual) || !Path.IsPathFullyQualified(actual)
                || !Path.GetFullPath(actual).Equals(Path.GetFullPath(expectedLauncher), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("目标 PID 的程序路径与本次启动器不符，已停止更新。");
            if (!process.WaitForExit(30_000))
                throw new IOException("启动器在 30 秒内未退出，已取消更新；请完全退出启动器后重试。不会结束任何进程。");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new IOException("无法读取启动器进程身份或等待其退出，已停止更新；请确认权限并完全退出启动器后重试。", ex);
        }
    }

    static ILauncherProcess? OpenLauncherProcess(int pid)
    {
        Process process;
        try { process = Process.GetProcessById(pid); }
        catch (ArgumentException) { return null; }
        return new LauncherProcess(process);
    }
}

internal interface ILauncherProcess : IDisposable
{
    bool HasExited { get; }
    string? ExecutablePath { get; }
    bool WaitForExit(int milliseconds);
}

internal sealed class LauncherProcess(Process process) : ILauncherProcess
{
    public bool HasExited => process.HasExited;
    public string? ExecutablePath => process.MainModule?.FileName;
    public bool WaitForExit(int milliseconds) => process.WaitForExit(milliseconds);
    public void Dispose() => process.Dispose();
}
