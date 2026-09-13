using System.Diagnostics;
using WuWaFpsUnlock.Core;

namespace WuWaFpsUnlock.Services;

public sealed class ExternalUnlockerService(Action<string> log)
{
    private static readonly LaunchExecution Execution=new();
    public Task<Process> LaunchAsync(LaunchPlan plan, UserSettings settings, Action<string> status, CancellationToken token)
    {
        return Execution.RunAsync(plan,Prepare,Start,(at,ct)=>GameProcesses.WaitForRenderer(plan.ShippingExePath,at,ct),
            state=>status(state switch { "StartingUnlocker"=>"正在启动外部解锁器…", "StartingShipping"=>"正在启动原装 Shipping…",
                "WaitingForRenderer"=>"等待游戏渲染窗口…", "GameRunning"=>"游戏运行中 · 游戏内实际效果待确认", "Cancelled"=>"启动监测已取消；未结束游戏或解锁器。", _=>"启动失败 · 请查看日志" }),
            TimeSpan.FromMinutes(2),token);
        async Task Prepare()
        {
        var receipt=AppPaths.LoadReceipt(new UserSettings{GameExe=plan.ShippingExePath});
        if(receipt?.Status is "PartialFailure" or "Installing" or "PartialClean")
            throw new IOException("部署维护未完成（"+receipt.Status+"）；请先在设置处理，未启动游戏或解锁器。");
        if(receipt?.Status=="CleanedWithSkips")log("上次清除有保留项；保持现有文件启动，游戏内效果待确认。");
        GameProcesses.RequireStopped(Path.GetDirectoryName(plan.ShippingExePath)!);
        if (plan.FpsEnabled)
        {
            await PreflightUnlockerAsync(plan,log);
        }
        }
        async Task<Process> Start()
        {
            if(plan.FpsEnabled)return await ExternalLaunchWorker.RunFromUi(settings,plan,log,token);
            var info=new ProcessStartInfo(plan.Executable){UseShellExecute=true,WorkingDirectory=plan.WorkingDirectory};
            var process=Process.Start(info)??throw new IOException("系统没有返回原装 Shipping 启动进程。");
            log($"已直接启动所选 Shipping：{plan.Executable}；PID={process.Id}。等待实际渲染窗口。");
            return process;
        }
    }
    internal static async Task PreflightUnlockerAsync(LaunchPlan plan,Action<string> log)
    {
        if(!string.Equals(await SafePaths.HashAsync(plan.Executable),UnlockerConfigAdapter.AuditedSha256,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("解锁器哈希与已审计用户版本不符；未执行。");
        RequireUnlockerStopped(plan.Executable);
        LaunchProcessGuard.RequireNamesStopped(["Wuthering Waves","Client-Win64-Shipping","nvngx_update"]);
        if(GameProcesses.HasWindowTitle("鸣潮"))throw new IOException("已有标题为鸣潮的窗口，已阻止外部工具管理该进程。");
        UnlockerConfigAdapter.Prepare(plan,write:false);
        await ExternalLaunchWorker.ValidateRuntimeAsync();
        log("外部启动预检查通过；用户 EXE 需要管理员权限，将由标准 UAC 启动独立工作进程，主界面保持普通权限。");
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
                try { path = GameProcesses.ImagePath(p.Id); }
                catch { throw new IOException("有无法核对路径的同名解锁器进程；请自行关闭后重试：" + name); }
                if (path is not null && File.Exists(path) && string.Equals(awaitHash(path), UnlockerConfigAdapter.AuditedSha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("同一用户解锁器已运行：" + path);
            }
        }
        static string awaitHash(string file) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)));
    }
}
