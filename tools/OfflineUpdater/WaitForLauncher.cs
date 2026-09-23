using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

internal static partial class Program
{
    static int WaitThenUpdate(string pidText, string expectedLauncher)
    {
        if (!int.TryParse(pidText, out int pid) || pid <= 0) throw new ArgumentException("启动器 PID 无效。");
        string updaterDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        NoLinks(updaterDirectory);
        ValidateUpdateManifest(updaterDirectory, required: true);
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
        => LauncherProcess.Open(pid);
}

internal interface ILauncherProcess : IDisposable
{
    bool HasExited { get; }
    string? ExecutablePath { get; }
    bool WaitForExit(int milliseconds);
}

internal sealed class LauncherProcess(SafeProcessHandle handle) : ILauncherProcess
{
    // Querying the image requires only limited-information rights, unlike module
    // enumeration. The same handle pins process identity for the bounded wait.
    public static LauncherProcess? Open(int pid)
    {
        var handle = OpenProcess(0x00100000 | 0x1000, false, pid); // SYNCHRONIZE | QUERY_LIMITED_INFORMATION
        if (!handle.IsInvalid) return new LauncherProcess(handle);
        int error = Marshal.GetLastWin32Error(); handle.Dispose();
        if (error == 87) return null; // PID naturally exited before OpenProcess.
        throw new System.ComponentModel.Win32Exception(error);
    }
    public bool HasExited => WaitForExit(0);
    public string ExecutablePath
    {
        get
        {
            var path = new StringBuilder(32768); int length = path.Capacity;
            if (!QueryFullProcessImageName(handle, 0, path, ref length)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return path.ToString();
        }
    }
    public bool WaitForExit(int milliseconds)
    {
        uint result = WaitForSingleObject(handle, (uint)milliseconds);
        return result switch { 0 => true, 258 => false, _ => throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()) };
    }
    public void Dispose() => handle.Dispose();
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool QueryFullProcessImageName(SafeProcessHandle process, int flags, StringBuilder path, ref int length);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);
}
