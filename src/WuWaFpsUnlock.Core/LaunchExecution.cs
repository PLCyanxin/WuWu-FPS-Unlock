using System.Diagnostics;
namespace WuWaFpsUnlock.Core;

// Process execution boundary: preparation, exactly one launch request, then separately observed renderer.
// Disposing a Process handle never terminates its process. No game/renderer is fabricated here.
public sealed class LaunchExecution
{
    private readonly SemaphoreSlim _gate=new(1,1);
    public Task<Process> RunAsync(LaunchPlan plan,Func<Task> prepare,Func<Process> start,
        Func<DateTime,CancellationToken,Task<Process>> waitForRenderer,Action<string> state,
        TimeSpan timeout,CancellationToken cancellationToken)
        =>RunAsync(plan,prepare,()=>Task.FromResult(start()),waitForRenderer,state,timeout,cancellationToken);
    public async Task<Process> RunAsync(LaunchPlan plan,Func<Task> prepare,Func<Task<Process>> start,
        Func<DateTime,CancellationToken,Task<Process>> waitForRenderer,Action<string> state,
        TimeSpan timeout,CancellationToken cancellationToken)
    {
        if(!await _gate.WaitAsync(0,cancellationToken))throw new IOException("启动操作已在进行；未发起第二次启动。");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await prepare();
            cancellationToken.ThrowIfCancellationRequested();
            state(plan.FpsEnabled?"StartingUnlocker":"StartingShipping");
            var startedAt=DateTime.UtcNow;
            using var initial=await start();
            state("WaitingForRenderer");
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);
            try
            {
                var renderer=await waitForRenderer(startedAt,deadline.Token).WaitAsync(deadline.Token);
                state("GameRunning");
                return renderer;
            }
            catch(OperationCanceledException) when(!cancellationToken.IsCancellationRequested)
            {throw new TimeoutException("启动程序已执行，但限定时间内未确认目标 Shipping 渲染窗口；FPS 效果未验证。");}
        }
        catch(OperationCanceledException){state("Cancelled");throw;}
        catch{state("Failed");throw;}
        finally{_gate.Release();}
    }
}

public static class LaunchProcessGuard
{
    // The audited external tool manages these names across paths; paths must not bypass this guard.
    public static void RequireNamesStopped(IEnumerable<string> names)
    {
        foreach(string name in names)
        {
            var processes=Process.GetProcessesByName(name);
            bool any=processes.Length!=0;
            foreach(var process in processes)process.Dispose();
            if(any)throw new IOException("存在外部解锁器会处理的同名进程；请自行关闭后重试："+name);
        }
    }
}
