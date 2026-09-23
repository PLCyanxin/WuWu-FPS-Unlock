internal static partial class Program
{
    static void WaitModeTests(Action<string, Action> test)
    {
        string expected = Path.Combine(Path.GetTempPath(), "WuWaUpdater-wait-fixture", "WuWaFpsUnlock.exe");
        static void Check(bool ok) { if (!ok) throw new Exception("wait API assertion failed"); }
        static void Reject(Action action)
        {
            try { action(); }
            catch (Exception ex) when (ex is IOException or InvalidDataException) { return; }
            throw new Exception("wait operation should have been rejected");
        }
        test("wait accepts absent or naturally exited PID without waiting", () =>
        {
            WaitForLauncherExit(123, expected, _ => null);
            var process = new FakeLauncherProcess { HasExited = true };
            WaitForLauncherExit(123, expected, _ => process);
            Check(process.WaitMilliseconds == 0 && process.Disposed);
        });
        test("wait validates path and waits at most thirty seconds", () =>
        {
            var process = new FakeLauncherProcess { ExecutablePath = expected, Exits = true };
            WaitForLauncherExit(123, expected, _ => process);
            Check(process.WaitMilliseconds == 30_000 && process.Disposed);
        });
        test("wait refuses a different executable for the same PID", () =>
        {
            var process = new FakeLauncherProcess { ExecutablePath = Path.Combine(Path.GetTempPath(), "other", "WuWaFpsUnlock.exe") };
            Reject(() => WaitForLauncherExit(123, expected, _ => process));
            Check(process.WaitMilliseconds == 0 && process.Disposed);
        });
        test("wait timeout cancels without terminating a process", () =>
        {
            var process = new FakeLauncherProcess { ExecutablePath = expected, Exits = false };
            Reject(() => WaitForLauncherExit(123, expected, _ => process));
            Check(process.WaitMilliseconds == 30_000 && process.Disposed);
        });
        test("wait permission error refuses update", () =>
        {
            var process = new FakeLauncherProcess { PathError = new System.ComponentModel.Win32Exception(5) };
            Reject(() => WaitForLauncherExit(123, expected, _ => process));
            Check(process.WaitMilliseconds == 0 && process.Disposed);
        });
        test("wait target must match installation and be fully qualified", () =>
        {
            MatchExpectedLauncher(expected, expected);
            Reject(() => MatchExpectedLauncher(expected, "WuWaFpsUnlock.exe"));
            Reject(() => MatchExpectedLauncher(expected, Path.Combine(Path.GetTempPath(), "other", "WuWaFpsUnlock.exe")));
        });
        test("native limited-rights query and wait observe a naturally exiting child", () =>
        {
            string self = Environment.ProcessPath ?? throw new Exception("No self executable path");
            var start = new System.Diagnostics.ProcessStartInfo(self) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("--self-test-child");
            using var child = System.Diagnostics.Process.Start(start) ?? throw new Exception("Child failed to start");
            using var identity = LauncherProcess.Open(child.Id) ?? throw new Exception("Child exited before identity capture");
            Check(identity.ExecutablePath.Equals(self, StringComparison.OrdinalIgnoreCase));
            WaitForLauncherExit(child.Id, self, OpenLauncherProcess);
            Check(identity.HasExited && child.WaitForExit(1000) && child.ExitCode == 0);
        });
    }

    static int SelfTestChild() { Thread.Sleep(1500); return 0; }
}

internal sealed class FakeLauncherProcess : ILauncherProcess
{
    public bool HasExited { get; init; }
    string? path;
    public string? ExecutablePath { get { if (PathError is not null) throw PathError; return path; } init => path = value; }
    public Exception? PathError { get; init; }
    public bool Exits { get; init; }
    public int WaitMilliseconds { get; private set; }
    public bool Disposed { get; private set; }
    public bool WaitForExit(int milliseconds) { WaitMilliseconds = milliseconds; return Exits; }
    public void Dispose() => Disposed = true;
}
