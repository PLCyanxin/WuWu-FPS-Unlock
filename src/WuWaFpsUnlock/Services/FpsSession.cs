using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using WuWaFpsUnlock.Core;
namespace WuWaFpsUnlock.Services;

// Uses the source-built FPS-only core; its exact build is recorded in components/PROVENANCE.json.
// No database writes, FOV, UID changes, anti-cheat workarounds, or advanced plugin loading.
public sealed class FpsSession : IAsyncDisposable
{
    public const string PluginHash="3b4eb2a4f9ef1a6b07a9c53aba81374290e1288daa6fbd4d4f5bee4feca92c80";
    private const string PipeName="55984705-F24C-45C2-B2B7-27F047B43A56";
    private readonly FpsControlChannel _channel=new();
    private readonly CancellationTokenSource _monitorStop=new();
    private Task? _monitorTask;
    private FpsCoreStatus? _status;
    private Exception? _monitoringError;
    private Action<string>? _log;
    public event Action<FpsCoreStatus>? StatusChanged;
    public event Action<Exception>? MonitoringFailed;
    public FpsCoreStatus? Status=>Volatile.Read(ref _status);
    public Exception? MonitoringError=>Volatile.Read(ref _monitoringError);
    public Process? Game {get;private set;}
    public bool Connected
    {
        get{try{return _channel.Connected && Game is not null && !Game.HasExited;}catch(InvalidOperationException){return false;}}
    }
    public async Task ConnectAsync(Process game,string pluginPath,Action<string> log,CancellationToken token)
    {
        _log=log;
        await _channel.ConnectAsync(cancellation=>CreatePipeAsync(game,pluginPath,log,cancellation),stream=>
        {
            if(game.HasExited||stream is not NamedPipeClientStream pipe||!GetNamedPipeServerProcessId(pipe.SafePipeHandle,out uint server)||server!=(uint)game.Id)
                throw new IOException("FPS 通信管道不属于本次仍存活的游戏进程；没有发送设置。");
        },token);
    }
    private async Task<Stream> CreatePipeAsync(Process game,string pluginPath,Action<string> log,CancellationToken token)
    {
        using var sourceLock=new FileStream(pluginPath,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(await SafePaths.HashAsync(pluginPath,token)!=PluginHash)throw new InvalidDataException("FPS 插件哈希不符，拒绝载入。");
        if(!PeInspector.IsAmd64(pluginPath))throw new BadImageFormatException("FPS 插件不是 x64。");
        Game=game;
        log($"FPS阶段：进入内置FPS加载；PID={game.Id}");
        await Task.Run(()=>Inject(game,pluginPath,log),token);
        log($"FPS阶段：等待IPC连接；PID={game.Id}；超时20秒");
        var pipe=new NamedPipeClientStream(".",PipeName,PipeDirection.InOut,PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(20000,token);
            if(!GetNamedPipeServerProcessId(pipe.SafePipeHandle,out uint server)||server!=(uint)game.Id)
                throw new IOException("FPS 通信管道不属于本次游戏进程；没有发送设置。请关闭其他解锁器。");
            log("FPS 基础插件已加载，IPC 已连接；实际帧率仍需在游戏中确认。");return pipe;
        }
        catch(OperationCanceledException){await pipe.DisposeAsync();throw;}
        catch(Exception error){await pipe.DisposeAsync();throw BuiltinFpsService.ExplainFailure("FPS/IPC连接或PID校验",game.Id,error);}
    }
    public async Task SetFpsAsync(int fps,CancellationToken token=default)
    {
        var game=Game??throw new InvalidOperationException("FPS 会话尚未绑定游戏进程。");
        await _channel.SendAsync(fps,token);
        var status=await _channel.QueryStatusAsync(game.Id,token);
        if(status.Target!=fps)throw new IOException($"FPS 核心回执目标 {status.Target} 与发送目标 {fps} 不一致；不能确认本次指令。");
        Publish(status);
        if(status.State!=FpsCoreState.Stopped)lock(_monitorStop) _monitorTask??=MonitorAsync(game);
    }
    private async Task MonitorAsync(Process game)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while(await timer.WaitForNextTickAsync(_monitorStop.Token))
            {
                if(game.HasExited)return;
                var status=await _channel.QueryStatusAsync(game.Id,_monitorStop.Token);
                Publish(status);
                if(status.State==FpsCoreState.Stopped)return; // Retain the pipe and final diagnosis until session disposal.
            }
        }
        catch(OperationCanceledException) when(_monitorStop.IsCancellationRequested) { }
        catch(Exception error)
        {
            try
            {
                if(!game.HasExited && !_monitorStop.IsCancellationRequested)
                {
                    Interlocked.Exchange(ref _status,null);
                    Interlocked.Exchange(ref _monitoringError,error);
                    try{_log?.Invoke("FPS 状态监测通信失败："+error);}catch(Exception logError){Debug.WriteLine(logError);}
                    MonitoringFailed?.Invoke(error);
                }
            }
            catch(Exception callbackError){Debug.WriteLine(callbackError);}
        }
    }
    private void Publish(FpsCoreStatus status)
    {
        if(_monitorStop.IsCancellationRequested)return;
        Interlocked.Exchange(ref _monitoringError,null);
        var previous=Interlocked.Exchange(ref _status,status);
        if(previous is null||previous.State!=status.State||previous.Reason!=status.Reason||previous.Target!=status.Target||previous.Applied!=status.Applied)
        {
            try{_log?.Invoke("FPS 核心状态："+status.ToDiagnosticText());}catch(Exception error){Debug.WriteLine(error);}
            try{StatusChanged?.Invoke(status);}catch(Exception error){Debug.WriteLine(error);}
        }
    }
    public async ValueTask DisposeAsync()
    {
        _monitorStop.Cancel();
        var monitor=Volatile.Read(ref _monitorTask);
        if(monitor is not null)await monitor;
        await _channel.DisposeAsync();Interlocked.Exchange(ref _status,null);Game=null;
    }
    private static void Inject(Process game,string dll,Action<string> log)
    {
        string phase="FPS/验证渲染进程";
        try
        {
        if(game.HasExited||!GameProcesses.HasUnrealWindow(game.Id))throw new IOException("目标不是仍在运行的鸣潮渲染进程。");
        // Anchor the launcher's held process handle before any PID-based module or process lookup.
        using var identity=FpsTargetIdentity.Capture(game);
        identity.VerifyHandle(game.SafeHandle);
        identity.VerifyLive();
        phase="FPS/枚举目标模块";log($"{phase}；PID={game.Id}");
        var modules=game.Modules.Cast<ProcessModule>().ToList();
        if(modules.Any(m=>string.Equals(m.FileName,Path.GetFullPath(dll),StringComparison.OrdinalIgnoreCase))) {log("FPS 插件已存在，复用当前实例。");return;}
        if(modules.Any(m=>m.ModuleName.Contains("ww_ulk_plugin_base",StringComparison.OrdinalIgnoreCase)||m.ModuleName.Equals("ww_plugin_base.dll",StringComparison.OrdinalIgnoreCase)))throw new IOException("检测到其他解锁器的 FPS 插件。请退出游戏与旧解锁器后重试。");
        phase="FPS/申请加载所需进程权限";log($"{phase}；PID={game.Id}");
        using var handle=OpenProcess(0x0002|0x0008|0x0010|0x0020|0x0400,false,game.Id);
        if(handle.IsInvalid)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"无法访问游戏进程；不会绕过权限或反作弊限制。");
        phase="FPS/核对目标 PID、创建时间和完整路径";
        identity.VerifyHandle(handle);identity.VerifyLive();
        phase="FPS/定位系统加载模块";
        IntPtr localModule=GetModuleHandle("kernel32.dll"),proc=GetProcAddress(localModule,"LoadLibraryW");
        if(proc==IntPtr.Zero||!GetModuleHandleEx(0x00000004|0x00000002,proc,out var owner))throw new InvalidOperationException("无法定位系统加载函数。");
        var ownerPath=new StringBuilder(32768);GetModuleFileName(owner,ownerPath,ownerPath.Capacity);
        string ownerName=Path.GetFileName(ownerPath.ToString());
        var remoteOwner=modules.FirstOrDefault(m=>m.ModuleName.Equals(ownerName,StringComparison.OrdinalIgnoreCase))??throw new IOException("目标进程缺少系统加载模块。");
        IntPtr remoteFunction=new(remoteOwner.BaseAddress.ToInt64()+proc.ToInt64()-owner.ToInt64());
        byte[] bytes=Encoding.Unicode.GetBytes(Path.GetFullPath(dll)+"\0");
        phase="FPS/申请加载参数内存";
        IntPtr remote=VirtualAllocEx(handle,IntPtr.Zero,(nuint)bytes.Length,0x3000,0x04);bool threadStarted=false,threadFinished=false;
        if(remote==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            phase="FPS/写入DLL路径参数";
            if(!WriteProcessMemory(handle,remote,bytes,(nuint)bytes.Length,out nuint wrote)||wrote!=(nuint)bytes.Length)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            phase="FPS/调用系统DLL加载";
            identity.VerifyLive();identity.VerifyHandle(handle);
            using var thread=CreateRemoteThread(handle,IntPtr.Zero,0,remoteFunction,remote,0,out _);
            if(thread.IsInvalid)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());threadStarted=true;
            uint wait=WaitForSingleObject(thread,15000);if(wait!=0)throw new TimeoutException("插件加载未在 15 秒内结束；已停止后续操作，请检查游戏。不会提前释放仍可能使用的远程参数。");
            threadFinished=true;
            if(!GetExitCodeThread(thread,out uint result)||result==0)throw new IOException("FPS DLL 的加载调用失败。");
            phase="FPS/验证加载后的模块";
            identity.VerifyLive();
            game.Refresh();
            if(!game.Modules.Cast<ProcessModule>().Any(m=>string.Equals(m.FileName,Path.GetFullPath(dll),StringComparison.OrdinalIgnoreCase)))throw new IOException("未在目标进程中确认 FPS DLL，不能报告加载成功。");
        }
        finally{if(!threadStarted||threadFinished)VirtualFreeEx(handle,remote,0,0x8000);}
        }
        catch(Exception error){log($"FPS失败阶段：{phase}；PID={game.Id}；{error.GetType().Name}：{error.Message}");throw BuiltinFpsService.ExplainFailure(phase,game.Id,error);}
    }
    [DllImport("kernel32.dll",SetLastError=true)]private static extern SafeProcessHandle OpenProcess(uint access,bool inherit,int pid);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern IntPtr VirtualAllocEx(SafeProcessHandle process,IntPtr addr,nuint size,uint type,uint protect);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool VirtualFreeEx(SafeProcessHandle process,IntPtr addr,nuint size,uint type);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool WriteProcessMemory(SafeProcessHandle p,IntPtr address,byte[] b,nuint size,out nuint written);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern SafeWaitHandle CreateRemoteThread(SafeProcessHandle p,IntPtr attr,nuint size,IntPtr start,IntPtr arg,uint flags,out uint id);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern uint WaitForSingleObject(SafeWaitHandle handle,uint ms);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool GetExitCodeThread(SafeWaitHandle handle,out uint code);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32.dll",CharSet=CharSet.Ansi,ExactSpelling=true)]private static extern IntPtr GetProcAddress(IntPtr module,string name);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool GetModuleHandleEx(uint flags,IntPtr address,out IntPtr module);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern uint GetModuleFileName(IntPtr module,StringBuilder path,int size);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe,out uint pid);
}

