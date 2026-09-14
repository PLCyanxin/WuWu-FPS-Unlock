using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using WuWaFpsUnlock.Core;
namespace WuWaFpsUnlock.Services;
public static class BuiltinFpsService
{
    public sealed class Notice {public string CoreHash {get;set;}="";public DateTimeOffset ConfirmedAt {get;set;}}
    private static string NoticePath(string exe)=>AppPaths.Receipt(exe)+".fps-notice.json";
    public static bool RequiresNotice(string exe)=>!File.Exists(NoticePath(exe))||JsonFiles.Read<Notice>(NoticePath(exe)).CoreHash!=FpsSession.PluginHash;
    public static void Acknowledge(string exe)=>JsonFiles.Save(NoticePath(exe),new Notice{CoreHash=FpsSession.PluginHash,ConfirmedAt=DateTimeOffset.UtcNow});
    public static async Task PreflightAsync(CancellationToken token)
    {
        if(!File.Exists(AppPaths.FpsCore)||!PeInspector.IsAmd64(AppPaths.FpsCore)||await SafePaths.HashAsync(AppPaths.FpsCore,token)!=FpsSession.PluginHash)
            throw new InvalidDataException("内置FPS核心缺失或哈希不符，未结束或启动游戏。");
        SafePaths.EnsureNoLinks(AppPaths.Base,AppPaths.FpsCore);
        // Old controllers are never executed or terminated. Refuse a competing controller that could relaunch a game.
        foreach(var p in Process.GetProcessesByName("unlock"))using(p)throw new IOException("旧unlock控制器仍在运行，请先自行退出；本版本不再使用它。");
    }
    public static async Task WaitForRendererAsync(Process process,string expected,CancellationToken token,Action<string>? log=null)
    {
        string phase="渲染检测/读取进程身份";
        try
        {
            using var identity=FpsTargetIdentity.Capture(process);
            string canonical=new WindowsRestartProcessCatalog().NormalizeExecutablePath(expected);
            if(!identity.Snapshot.ExecutablePath.Equals(canonical,StringComparison.OrdinalIgnoreCase))throw new IOException("已启动进程完整路径与所选 Shipping 不一致。");
            log?.Invoke($"渲染检测：PID={identity.Snapshot.Pid}；创建FILETIME={identity.Snapshot.CreationFileTime}；路径={identity.Snapshot.ExecutablePath}");
            var timer=Stopwatch.StartNew();phase="渲染检测/等待UnrealWindow";
            while(timer.Elapsed<TimeSpan.FromMinutes(2))
            {
                token.ThrowIfCancellationRequested();identity.VerifyLive();
                if(GameProcesses.HasUnrealWindow(process.Id)){log?.Invoke($"已确认本次PID {process.Id} 的 UnrealWindow；尚未连接FPS。");return;}
                await Task.Delay(400,token);
            }
            throw new TimeoutException($"已启动PID {process.Id}，但两分钟内未确认游戏渲染窗口；没有再次启动。");
        }
        catch(OperationCanceledException){throw;}
        catch(Exception error){throw ExplainFailure(phase,process.Id,error);}
    }
    public static IOException ExplainFailure(string phase,int pid,Exception error)
    {
        var native=error as System.ComponentModel.Win32Exception;
        string code=native is null?"":$"；Win32={native.NativeErrorCode}";
        string denied=native?.NativeErrorCode==5?"；系统拒绝了所需进程访问，未绕过权限或反作弊。游戏进程可能仍在运行，FPS未连接。":"";
        return new IOException($"[{phase}] PID={pid}{code}：{error.Message}{denied}",error);
    }
}

public sealed record FpsTargetSnapshot(int Pid,long CreationFileTime,string ExecutablePath);
// Raw FILETIME comes from held process handles on both sides; never round or compare local-time Process.StartTime.
public sealed class FpsTargetIdentity:IDisposable
{
    private readonly SafeProcessHandle _handle;
    public FpsTargetSnapshot Snapshot {get;}
    private FpsTargetIdentity(SafeProcessHandle handle,FpsTargetSnapshot snapshot){_handle=handle;Snapshot=snapshot;}
    public static FpsTargetIdentity Capture(Process process)
    {
        var handle=OpenProcess(0x1000|0x100000,false,process.Id);
        if(handle.IsInvalid){handle.Dispose();throw Native("无法读取FPS目标身份");}
        try{return new(handle,ReadSnapshot(handle));}catch{handle.Dispose();throw;}
    }
    public void VerifyLive()
    {
        uint state=WaitForSingleObject(_handle,0);
        if(state==0)throw new IOException($"游戏PID {Snapshot.Pid} 已退出，未确认FPS连接。");
        if(state!=258)throw Native("读取FPS目标退出状态失败");
        VerifySnapshot(Snapshot,ReadSnapshot(_handle));
    }
    public void VerifyHandle(SafeProcessHandle other)=>VerifySnapshot(Snapshot,ReadSnapshot(other));
    public static void VerifySnapshot(FpsTargetSnapshot expected,FpsTargetSnapshot actual)
    {
        if(expected.Pid!=actual.Pid||expected.CreationFileTime!=actual.CreationFileTime||!expected.ExecutablePath.Equals(actual.ExecutablePath,StringComparison.OrdinalIgnoreCase))
            throw new IOException($"FPS目标身份不匹配：预期PID={expected.Pid}, FILETIME={expected.CreationFileTime}；实际PID={actual.Pid}, FILETIME={actual.CreationFileTime}。停止加载。");
    }
    private static FpsTargetSnapshot ReadSnapshot(SafeProcessHandle handle)
    {
        uint pid=GetProcessId(handle);if(pid==0)throw Native("无法读取FPS目标PID");
        if(!GetProcessTimes(handle,out long created,out _,out _,out _))throw Native("无法读取FPS目标创建时间");
        var path=new StringBuilder(32768);int length=path.Capacity;
        if(!QueryFullProcessImageName(handle,0,path,ref length))throw Native("无法读取FPS目标路径");
        return new((int)pid,created,new WindowsRestartProcessCatalog().NormalizeExecutablePath(path.ToString()));
    }
    public void Dispose()=>_handle.Dispose();
    private static System.ComponentModel.Win32Exception Native(string message)=>new(Marshal.GetLastWin32Error(),message);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern SafeProcessHandle OpenProcess(uint access,bool inherit,int pid);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern uint GetProcessId(SafeProcessHandle process);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool GetProcessTimes(SafeProcessHandle process,out long creation,out long exit,out long kernel,out long user);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool QueryFullProcessImageName(SafeProcessHandle process,uint flags,StringBuilder path,ref int size);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern uint WaitForSingleObject(SafeProcessHandle process,uint timeout);
}
