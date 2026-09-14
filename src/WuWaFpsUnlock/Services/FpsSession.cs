using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using WuWaFpsUnlock.Core;
namespace WuWaFpsUnlock.Services;

// Uses ONLY the unmodified FPS-only native DLL extracted from the user's supplied tool.
// No database writes, FOV, UID changes, anti-cheat workarounds, or advanced plugin loading.
public sealed class FpsSession : IAsyncDisposable
{
    public const string PluginHash="844d7552692f53e8a1bfe45edf360a094597c5bf2b26dc058ff59b21d2250c3a";
    private const string PipeName="55984705-F24C-45C2-B2B7-27F047B43A56";
    private NamedPipeClientStream? _pipe;
    private readonly SemaphoreSlim _sendGate=new(1,1);
    public Process? Game {get;private set;}
    public bool Connected=>_pipe?.IsConnected==true && Game is not null && !Game.HasExited;
    public async Task ConnectAsync(Process game,string pluginPath,Action<string> log,CancellationToken token)
    {
        using var sourceLock=new FileStream(pluginPath,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(await SafePaths.HashAsync(pluginPath,token)!=PluginHash)throw new InvalidDataException("FPS 插件哈希不符，拒绝载入。");
        if(!PeInspector.IsAmd64(pluginPath))throw new BadImageFormatException("FPS 插件不是 x64。");
        Game=game;
        await Task.Run(()=>Inject(game,pluginPath,log),token);
        var pipe=new NamedPipeClientStream(".",PipeName,PipeDirection.Out,PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(20000,token);
            if(!GetNamedPipeServerProcessId(pipe.SafePipeHandle,out uint server)||server!=(uint)game.Id)
                throw new IOException("FPS 通信管道不属于本次游戏进程；没有发送设置。请关闭其他解锁器。");
            _pipe=pipe; log("FPS 基础插件已加载，IPC 已连接；实际帧率仍需在游戏中确认。");
        }
        catch {await pipe.DisposeAsync();throw;}
    }
    public async Task SetFpsAsync(int fps,CancellationToken token=default)
    {
        if(fps is <30 or >420)throw new ArgumentOutOfRangeException(nameof(fps));
        await _sendGate.WaitAsync(token);
        try
        {
            if(!Connected)throw new IOException("FPS 控制通道未连接。");
            if(!GetNamedPipeServerProcessId(_pipe!.SafePipeHandle,out uint id)||id!=(uint)Game!.Id)throw new IOException("FPS 管道进程标识不匹配。");
            // Six-field protocol used by ww_plugin_base; advanced fields are always disabled.
            byte[] msg=Encoding.ASCII.GetBytes($"0,0,45.0,0,0,{fps}");
            await _pipe.WriteAsync(msg,token);await _pipe.FlushAsync(token);
        }
        finally{_sendGate.Release();}
    }
    public async ValueTask DisposeAsync(){if(_pipe is not null)await _pipe.DisposeAsync();_pipe=null;Game=null;}
    private static void Inject(Process game,string dll,Action<string> log)
    {
        if(game.HasExited||!GameProcesses.HasUnrealWindow(game.Id))throw new IOException("目标不是仍在运行的鸣潮渲染进程。");
        var modules=game.Modules.Cast<ProcessModule>().ToList();
        if(modules.Any(m=>string.Equals(m.FileName,Path.GetFullPath(dll),StringComparison.OrdinalIgnoreCase))) {log("FPS 插件已存在，复用当前实例。");return;}
        if(modules.Any(m=>m.ModuleName.Contains("ww_ulk_plugin_base",StringComparison.OrdinalIgnoreCase)||m.ModuleName.Equals("ww_plugin_base.dll",StringComparison.OrdinalIgnoreCase)))throw new IOException("检测到其他解锁器的 FPS 插件。请退出游戏与旧解锁器后重试。");
        using var handle=OpenProcess(0x0002|0x0008|0x0010|0x0020|0x0400,false,game.Id);
        if(handle.IsInvalid)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"无法访问游戏进程；不会绕过权限或反作弊限制。");
        if(game.HasExited||!GetProcessTimes(handle,out long created,out _,out _,out _)||DateTime.FromFileTimeUtc(created)!=game.StartTime.ToUniversalTime())throw new IOException("FPS目标进程身份变化，停止加载。");
        IntPtr localModule=GetModuleHandle("kernel32.dll"),proc=GetProcAddress(localModule,"LoadLibraryW");
        if(proc==IntPtr.Zero||!GetModuleHandleEx(0x00000004|0x00000002,proc,out var owner))throw new InvalidOperationException("无法定位系统加载函数。");
        var ownerPath=new StringBuilder(32768);GetModuleFileName(owner,ownerPath,ownerPath.Capacity);
        string ownerName=Path.GetFileName(ownerPath.ToString());
        var remoteOwner=modules.FirstOrDefault(m=>m.ModuleName.Equals(ownerName,StringComparison.OrdinalIgnoreCase))??throw new IOException("目标进程缺少系统加载模块。");
        IntPtr remoteFunction=new(remoteOwner.BaseAddress.ToInt64()+proc.ToInt64()-owner.ToInt64());
        byte[] bytes=Encoding.Unicode.GetBytes(Path.GetFullPath(dll)+"\0");
        IntPtr remote=VirtualAllocEx(handle,IntPtr.Zero,(nuint)bytes.Length,0x3000,0x04);bool threadStarted=false,threadFinished=false;
        if(remote==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            if(!WriteProcessMemory(handle,remote,bytes,(nuint)bytes.Length,out nuint wrote)||wrote!=(nuint)bytes.Length)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            using var thread=CreateRemoteThread(handle,IntPtr.Zero,0,remoteFunction,remote,0,out _);
            if(thread.IsInvalid)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());threadStarted=true;
            uint wait=WaitForSingleObject(thread,15000);if(wait!=0)throw new TimeoutException("插件加载未在 15 秒内结束；已停止后续操作，请检查游戏。不会提前释放仍可能使用的远程参数。");
            threadFinished=true;
            if(!GetExitCodeThread(thread,out uint result)||result==0)throw new IOException("FPS DLL 的加载调用失败。");
            game.Refresh();
            if(!game.Modules.Cast<ProcessModule>().Any(m=>string.Equals(m.FileName,Path.GetFullPath(dll),StringComparison.OrdinalIgnoreCase)))throw new IOException("未在目标进程中确认 FPS DLL，不能报告加载成功。");
        }
        finally{if(!threadStarted||threadFinished)VirtualFreeEx(handle,remote,0,0x8000);}
    }
    [DllImport("kernel32.dll",SetLastError=true)]private static extern SafeProcessHandle OpenProcess(uint access,bool inherit,int pid);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool GetProcessTimes(SafeProcessHandle process,out long creation,out long exit,out long kernel,out long user);
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

