using System.Diagnostics;
using WuWaFpsUnlock.Core;

namespace WuWaFpsUnlock.Services;

public sealed class ExternalUnlockerService(Action<string> log)
{
    public async Task<Process> LaunchAsync(LaunchPlan plan, Action<string> status, CancellationToken token)
    {
        GameProcesses.RequireStopped(Path.GetDirectoryName(plan.ShippingExePath)!);
        if (plan.FpsEnabled)
        {
            if (!string.Equals(await SafePaths.HashAsync(plan.Executable), UnlockerConfigAdapter.AuditedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("解锁器哈希与已审计用户版本不符；未执行。");
            RequireUnlockerStopped(plan.Executable);
            // This exact binary kills these process names itself. Refuse to expose an existing process to that behavior.
            foreach (string name in new[] { "Wuthering Waves", "Client-Win64-Shipping", "nvngx_update" })
            {
                var processes = Process.GetProcessesByName(name);
                bool any = processes.Length != 0;
                foreach (var p in processes) p.Dispose();
                if (any) throw new IOException("外部解锁器会处理同名进程；请先自行关闭：" + name + "。未启动解锁器。");
            }
            if (GameProcesses.HasWindowTitle("鸣潮")) throw new IOException("已有标题为“鸣潮”的窗口；该用户解锁器会结束此窗口进程，已阻止启动。");
            WriteProbe.Check(plan.ConfigPath!);
            UnlockerConfigAdapter.Prepare(plan);
            log("已更新运行副本 INI：FPS / 路径 / AutoStart / GameLaunchExe=1；禁用无关高级功能，保留未知键及服务器设置。");
        }
        token.ThrowIfCancellationRequested();
        status(plan.FpsEnabled ? "正在启动外部解锁器…" : "正在启动原装 Shipping…");
        var info = new ProcessStartInfo(plan.Executable) { UseShellExecute = true, WorkingDirectory = plan.WorkingDirectory };
        string localRuntime=Path.Combine(AppPaths.Base,"components","dotnet8");
        if(plan.FpsEnabled && Directory.Exists(Path.Combine(localRuntime,"host","fxr")))
        {
            info.UseShellExecute=false;
            info.Environment["DOTNET_ROOT_X64"]=localRuntime;
            log("解锁器优先使用便携 .NET 8 运行库："+localRuntime);
        }
        foreach (var argument in plan.Arguments) info.ArgumentList.Add(argument);
        var startedAt = DateTime.UtcNow;
        using var initial = Start(info);
        Process Start(ProcessStartInfo start)
        {
            try { return Process.Start(start) ?? throw new IOException("系统没有返回启动进程："+start.FileName); }
            catch(System.ComponentModel.Win32Exception e) when(e.NativeErrorCode==740 && !start.UseShellExecute)
            {
                log("系统要求提升权限，交给标准 UAC；此分支由系统解析 .NET 8 运行库。");
                start.UseShellExecute=true;
                return Process.Start(start) ?? throw new IOException("UAC 后未返回启动进程。");
            }
        }
        log($"启动请求已提交：{plan.Executable}；PID={initial.Id}；工作目录={plan.WorkingDirectory}。等待目标 Shipping，不视为 FPS 已生效。");
        if (plan.FpsEnabled) log("用户解锁器可显示自己的窗口、托盘或 UAC；未验证无窗口行为，未接入实时 FPS 接口。");
        status("等待游戏渲染窗口…");
        return await GameProcesses.WaitForRenderer(plan.ShippingExePath, startedAt, token);
    }

    private static void RequireUnlockerStopped(string exe)
    {
        if (Mutex.TryOpenExisting("30launcher_WPF_WutheringWavesFPSunlocker", out var existing))
        { existing.Dispose(); throw new IOException("用户解锁器已运行；请从其托盘自行退出后重试。不会再开第二实例或改写运行中配置。"); }
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                string name; try { name = p.ProcessName; } catch { continue; }
                if (name != Path.GetFileNameWithoutExtension(exe) && name != "鸣潮" && name != "ww_unlockfps") continue;
                string? path;
                try { path = p.MainModule?.FileName; }
                catch { throw new IOException("有无法核对路径的同名解锁器进程；请自行关闭后重试：" + name); }
                if (path is not null && File.Exists(path) && string.Equals(awaitHash(path), UnlockerConfigAdapter.AuditedSha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("同一用户解锁器已运行：" + path);
            }
        }
        static string awaitHash(string file) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)));
    }
}
