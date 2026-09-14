using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using WuWaFpsUnlock.Core;
namespace WuWaFpsUnlock.Services;

// Only handles acquired for a canonical exact-image match can reach TerminateProcess.
public sealed class WindowsRestartProcessCatalog:IRestartProcessCatalog
{
    private const uint QueryLimited=0x1000,Terminate=1,Synchronize=0x100000;
    public string NormalizeExecutablePath(string path)
    {
        string full=Path.GetFullPath(path);
        if(!Path.GetExtension(full).Equals(".exe",StringComparison.OrdinalIgnoreCase)||!File.Exists(full))throw new FileNotFoundException("重启目标必须是存在的完整 EXE 路径。",full);
        using var file=CreateFile(full,0x80,7,IntPtr.Zero,3,0,IntPtr.Zero);
        if(file.IsInvalid)throw Error("无法规范化 EXE 路径");
        var buffer=new StringBuilder(32768);uint length=GetFinalPathNameByHandle(file,buffer,(uint)buffer.Capacity,0);
        if(length==0||length>=buffer.Capacity)throw Error("无法读取 EXE 最终路径");
        string result=buffer.ToString();
        if(result.StartsWith(@"\\?\UNC\",StringComparison.OrdinalIgnoreCase))return @"\\"+result[8..];
        if(result.StartsWith(@"\\?\",StringComparison.OrdinalIgnoreCase))return result[4..];
        return result;
    }
    public IReadOnlyList<IRestartProcess> CaptureMatching(string normalizedExecutablePath)
    {
        var result=new List<IRestartProcess>();
        try
        {
            foreach(var candidate in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(normalizedExecutablePath)))
            using(candidate)
            {
                using var query=OpenProcess(QueryLimited|Synchronize,false,candidate.Id);
                if(query.IsInvalid)
                {
                    int error=Marshal.GetLastWin32Error();
                    if(HasProvablyExited(candidate.Id))continue;
                    throw new Win32Exception(error,$"同名候选 PID {candidate.Id} 无法读取身份；未终止任何未确认对象");
                }
                if(WaitForSingleObject(query,0)==0)continue;
                string path;long created;
                try{path=NormalizeExecutablePath(ImagePath(query));created=CreationTime(query);}
                catch(Exception error){if(WaitForSingleObject(query,0)==0)continue;throw new IOException($"同名候选 PID {candidate.Id} 路径不可确认；停止重启。",error);}
                if(!path.Equals(normalizedExecutablePath,StringComparison.OrdinalIgnoreCase))continue;
                uint queryState=WaitForSingleObject(query,0);if(queryState==0)continue;
                if(queryState!=258)throw new IOException("目标进程等待状态无法确认；停止重启。");
                // Query-only handle stays held while acquiring the handle used for query + terminate + wait.
                var held=OpenProcess(QueryLimited|Terminate|Synchronize,false,candidate.Id);
                if(held.IsInvalid)
                {
                    int error=Marshal.GetLastWin32Error();held.Dispose();
                    if(WaitForSingleObject(query,0)==0)continue;
                    throw new Win32Exception(error,$"目标 PID {candidate.Id} 无终止权限；不请求 UAC 或绕过权限");
                }
                try
                {
                    if(WaitForSingleObject(held,0)==0){held.Dispose();continue;}
                    if(CreationTime(held)!=created||!NormalizeExecutablePath(ImagePath(held)).Equals(path,StringComparison.OrdinalIgnoreCase))throw new IOException("候选进程身份发生变化；未终止。");
                    result.Add(new HeldProcess(this,held,new(candidate.Id,path,DateTime.FromFileTimeUtc(created)),created));
                }
                catch{bool exited=WaitForSingleObject(held,0)==0;held.Dispose();if(exited)continue;throw;}
            }
            return result;
        }
        catch{foreach(var held in result)held.Dispose();throw;}
    }
    private sealed class HeldProcess(WindowsRestartProcessCatalog owner,SafeProcessHandle handle,RestartProcessIdentity identity,long created):IRestartProcess
    {
        public RestartProcessIdentity Identity=>identity;
        public void VerifyStillSameProcess()
        {
            uint wait=WaitForSingleObject(handle,0);
            if(wait==0)return; // The held kernel object has exited; this cannot refer to a reused PID.
            if(wait!=258)throw new IOException($"PID {identity.Pid} 等待状态异常；停止重启。");
            try
            {
                if(CreationTime(handle)!=created||!owner.NormalizeExecutablePath(ImagePath(handle)).Equals(identity.ExecutablePath,StringComparison.OrdinalIgnoreCase))
                    throw new IOException("持有句柄的身份与确认对象不一致；未终止。");
            }
            catch{if(WaitForSingleObject(handle,0)==0)return;throw;}
        }
        public async Task TerminateAndWaitAsync(TimeSpan timeout,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if(WaitForSingleObject(handle,0)==0)return;
            VerifyStillSameProcess();
            if(WaitForSingleObject(handle,0)==0)return;
            if(!TerminateProcess(handle,1))
            {
                int error=Marshal.GetLastWin32Error();
                if(WaitForSingleObject(handle,0)==0)return;
                throw new Win32Exception(error,$"终止已确认 PID {identity.Pid} 失败；停止重启");
            }
            var watch=Stopwatch.StartNew();
            while(true)
            {
                token.ThrowIfCancellationRequested();uint status=WaitForSingleObject(handle,0);
                if(status==0)return;
                if(status!=258)throw Error("终止后等待同一进程句柄失败");
                if(watch.Elapsed>=timeout)throw new TimeoutException($"PID {identity.Pid} 未在 {timeout.TotalSeconds:0.##} 秒内退出；未启动新游戏。");
                await Task.Delay(20,token);
            }
        }
        public void Dispose()=>handle.Dispose();
    }
    private static bool HasProvablyExited(int pid)
    {
        using var waitOnly=OpenProcess(Synchronize,false,pid);
        if(!waitOnly.IsInvalid)return WaitForSingleObject(waitOnly,0)==0;
        // Invalid PID plus an absent fresh process snapshot proves disappearance; access denial is not proof.
        if(Marshal.GetLastWin32Error()!=87)return false;
        var processes=Process.GetProcesses();
        try{return !processes.Any(process=>process.Id==pid);}
        finally{foreach(var process in processes)process.Dispose();}
    }
    private static string ImagePath(SafeProcessHandle handle)
    {
        var value=new StringBuilder(32768);int size=value.Capacity;
        if(!QueryFullProcessImageName(handle,0,value,ref size))throw Error("无法读取进程规范路径");return value.ToString();
    }
    private static long CreationTime(SafeProcessHandle handle)
    {if(!GetProcessTimes(handle,out long creation,out _,out _,out _))throw Error("无法读取进程创建时间");return creation;}
    private static Win32Exception Error(string message)=>new(Marshal.GetLastWin32Error(),message);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern SafeProcessHandle OpenProcess(uint access,bool inherit,int pid);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool QueryFullProcessImageName(SafeProcessHandle handle,uint flags,StringBuilder value,ref int size);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool GetProcessTimes(SafeProcessHandle handle,out long creation,out long exit,out long kernel,out long user);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool TerminateProcess(SafeProcessHandle handle,uint code);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern uint WaitForSingleObject(SafeProcessHandle handle,uint milliseconds);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern SafeFileHandle CreateFile(string name,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern uint GetFinalPathNameByHandle(SafeFileHandle file,StringBuilder path,uint size,uint flags);
}
