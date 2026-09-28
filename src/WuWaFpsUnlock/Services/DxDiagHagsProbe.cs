using System.Diagnostics;
using WuWaFpsUnlock.Core;
namespace WuWaFpsUnlock.Services;

/// <summary>Bounded read-only system diagnostic; EnvironmentProbe.Read runs off the UI thread.</summary>
internal static class DxDiagHagsProbe
{
    private static readonly object Gate = new();
    private static string? _report;
    private static DateTime _expires;
    public static bool? Read(string gpu, Action<string> log)
    {
        lock (Gate)
        {
            if (DateTime.UtcNow >= _expires)
            {
                _report = Collect(log);
                _expires = DateTime.UtcNow.AddSeconds(_report is null ? 30 : 120);
            }
            if (_report is null) return null;
            try { return EnvironmentStatus.ParseDxDiagHags(_report, gpu); }
            catch (Exception e) { log("HAGS 系统诊断解析失败：" + e.Message); return null; }
        }
    }
    private static string? Collect(Action<string> log)
    {
        string directory = Path.Combine(Path.GetTempPath(), "WuWaEnvironment-" + Guid.NewGuid().ToString("N"));
        string report = Path.Combine(directory, "dxdiag.xml");
        try
        {
            Directory.CreateDirectory(directory);
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "dxdiag.exe")) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            start.ArgumentList.Add("/whql:off"); start.ArgumentList.Add("/x"); start.ArgumentList.Add(report);
            using var process = LauncherScheduling.StartProcess(start) ?? throw new IOException("无法启动系统诊断。");
            if (!process.WaitForExit(30_000))
            {
                // Only terminate the process we created, never a user/game process or process tree.
                try { process.Kill(); process.WaitForExit(2000); } catch { }
                log("HAGS 系统诊断超时；运行态保持未确认。"); return null;
            }
            if (process.ExitCode != 0 || !File.Exists(report) || new FileInfo(report).Length > 8 * 1024 * 1024)
                throw new IOException("系统诊断未生成有效报告。");
            return File.ReadAllText(report);
        }
        catch (Exception e) { log("HAGS 系统诊断未完成：" + e.Message); return null; }
        finally
        {
            // This unique directory contains only our diagnostic report; no user files.
            try { if (File.Exists(report)) File.Delete(report); if (Directory.Exists(directory)) Directory.Delete(directory); } catch { }
        }
    }
}
