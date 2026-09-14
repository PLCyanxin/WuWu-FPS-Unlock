using System.Diagnostics;
namespace WuWaFpsUnlock.Core;

public sealed record RestartRequest(string ExecutablePath,bool FpsEnabled);
public sealed record RestartProcessIdentity(int Pid,string ExecutablePath,DateTime CreatedUtc);
public interface IRestartProcess:IDisposable
{
    RestartProcessIdentity Identity {get;}
    void VerifyStillSameProcess();
    Task TerminateAndWaitAsync(TimeSpan timeout,CancellationToken token);
}
public interface IRestartProcessCatalog
{
    string NormalizeExecutablePath(string path);
    IReadOnlyList<IRestartProcess> CaptureMatching(string normalizedExecutablePath);
}
public sealed class RestartActions
{
    public required Func<CancellationToken,Task> Preflight {get;init;}
    public required Func<IReadOnlyList<RestartProcessIdentity>,CancellationToken,Task<bool>> ConfirmNotice {get;init;}
    public required Func<CancellationToken,Task> ReleaseOldSession {get;init;}
    public required Func<string,CancellationToken,Task<Process>> Start {get;init;}
    public Func<Process,CancellationToken,Task>? AttachFps {get;init;}
}
public sealed class RestartFailureException(string phase,IReadOnlyList<int> stopped,Process? started,Exception inner)
    :IOException($"重启未完成 [{phase}]：{inner.Message}",inner)
{
    public string Phase {get;}=phase;
    public IReadOnlyList<int> StoppedPids {get;}=stopped;
    public Process? StartedProcess {get;}=started;
}
public sealed class RestartController
{
    private readonly SemaphoreSlim _gate=new(1,1);
    public async Task<Process?> RunAsync(RestartRequest request,IRestartProcessCatalog catalog,RestartActions actions,
        Action<string> state,TimeSpan stopTimeout,CancellationToken token)
    {
        if(!await _gate.WaitAsync(0,token))throw new IOException("已有重启流程；本次点击未启动第二个流程。");
        var held=new List<IRestartProcess>();var stopped=new List<int>();Process? started=null;string phase="Preflight";
        void Phase(string next){phase=next;state(next);}
        try
        {
            Phase("Preflight");
            if(stopTimeout<=TimeSpan.Zero)throw new ArgumentOutOfRangeException(nameof(stopTimeout));
            if(request.FpsEnabled&&actions.AttachFps is null)throw new InvalidOperationException("FPS 开启但缺少已配置的 FPS 接入步骤。");
            string executable=catalog.NormalizeExecutablePath(request.ExecutablePath);
            await actions.Preflight(token);token.ThrowIfCancellationRequested();
            held.AddRange(await Task.Run(()=>catalog.CaptureMatching(executable),token));
            foreach(var process in held)
            {
                if(!string.Equals(process.Identity.ExecutablePath,executable,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("进程完整路径不匹配；拒绝终止。");
                process.VerifyStillSameProcess();
            }
            Phase("AwaitingNotice");
            if(!await actions.ConfirmNotice(held.Select(x=>x.Identity).ToArray(),token)){Phase("Cancelled");return null;}
            token.ThrowIfCancellationRequested();
            Phase("Preflight");await actions.Preflight(token);token.ThrowIfCancellationRequested();
            // Repeat all identity checks after consent, before releasing a session or terminating any process.
            foreach(var process in held)process.VerifyStillSameProcess();
            Phase("ReleaseOldSession");await actions.ReleaseOldSession(token);
            Phase("Stopping");
            foreach(var process in held)
            {
                token.ThrowIfCancellationRequested();process.VerifyStillSameProcess();
                await process.TerminateAndWaitAsync(stopTimeout,token);stopped.Add(process.Identity.Pid);
            }
            Phase("Rechecking");
            var appeared=await Task.Run(()=>catalog.CaptureMatching(executable),token);
            try{if(appeared.Count!=0)throw new IOException("终止后出现新的同安装游戏进程；进程竞态，未再终止或启动。");}
            finally{foreach(var process in appeared)process.Dispose();}
            token.ThrowIfCancellationRequested();
            Phase("Starting");started=await actions.Start(executable,token);
            if(started is null)throw new IOException("启动步骤没有返回进程；未执行第二次启动。");
            token.ThrowIfCancellationRequested();
            if(request.FpsEnabled){Phase("AttachingFps");await actions.AttachFps!(started,token);}
            Phase("Running");return started;
        }
        catch(OperationCanceledException error)
        {
            string cancelledPhase=phase;Phase("Cancelled");
            if(started is not null)throw new RestartFailureException(cancelledPhase,stopped.ToArray(),started,error);
            throw;
        }
        catch(Exception error)
        {
            string failedPhase=phase;Phase("Failed");
            throw new RestartFailureException(failedPhase,stopped.ToArray(),started,error);
        }
        finally{foreach(var process in held)process.Dispose();_gate.Release();}
    }
}
