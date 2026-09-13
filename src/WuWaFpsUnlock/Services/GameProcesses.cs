using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using WuWaFpsUnlock.Core;
namespace WuWaFpsUnlock.Services;
public static class GameProcesses
{
    private static readonly string[] Names=["Wuthering Waves","WutheringWaves","Client-Win64-Shipping"];
    public static string ValidateExe(UserSettings settings)
    {
        string root=SafePaths.GameRoot(settings.GameRoot); string exe=Path.GetFullPath(settings.GameExe);
        SafePaths.EnsureInside(root,exe); SafePaths.EnsureNoLinks(root,exe);
        if(!File.Exists(exe)||!Path.GetExtension(exe).Equals(".exe",StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(exe).Equals("Client-Win64-Shipping.exe",StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("请手动选择鸣潮真正的游戏 EXE，而不是启动器、崩溃报告程序或本工具。");
        if(!PeInspector.IsAmd64(exe)) throw new InvalidDataException("所选游戏 EXE 不是 Windows x64。");
        return exe;
    }
    public static IReadOnlyList<Process> Find(string root)
    {
        var result=new List<Process>();
        foreach(string name in Names)
        foreach(var p in Process.GetProcessesByName(name))
        {
            try { var path=ImagePath(p.Id); if(path is not null && SafePaths.IsInside(root,path)) { result.Add(p);continue; } }
            catch { p.Dispose(); throw new UnauthorizedAccessException("存在无法检查的鸣潮进程；请先关闭游戏再部署，不会忽略该占用。"); }
            p.Dispose();
        }
        return result;
    }
    public static void RequireStopped(string root)
    { var all=Find(root); bool running=all.Count>0; foreach(var p in all)p.Dispose();if(running)throw new IOException("鸣潮正在运行。请完全退出游戏后再部署或清除插件。"); }
    [DllImport("kernel32.dll",SetLastError=true)] private static extern Microsoft.Win32.SafeHandles.SafeProcessHandle OpenProcess(uint access,bool inherit,int pid);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool QueryFullProcessImageName(Microsoft.Win32.SafeHandles.SafeProcessHandle process,uint flags,StringBuilder path,ref int size);
    public static string ImagePath(int pid)
    {
        using var handle=OpenProcess(0x1000,false,pid); // PROCESS_QUERY_LIMITED_INFORMATION: no process memory access.
        if(handle.IsInvalid)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"无法只读核对进程路径。");
        var path=new StringBuilder(32768);int size=path.Capacity;
        if(!QueryFullProcessImageName(handle,0,path,ref size))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"无法只读核对进程路径。");
        return path.ToString();
    }
    private delegate bool EnumProc(IntPtr hwnd,IntPtr param);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb,IntPtr param);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd,StringBuilder value,int max);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd,StringBuilder value,int max);
    public static bool HasWindowTitle(string title)
    { bool found=false;EnumWindows((h,_)=>{var s=new StringBuilder(512);GetWindowText(h,s,s.Capacity);if(s.ToString()==title){found=true;return false;}return true;},IntPtr.Zero);return found; }
    public static bool HasUnrealWindow(int pid)
    { bool found=false;EnumWindows((h,_)=>{GetWindowThreadProcessId(h,out uint id);if(id==(uint)pid){var s=new StringBuilder(256);GetClassName(h,s,s.Capacity);if(s.ToString()=="UnrealWindow"){found=true;return false;}}return true;},IntPtr.Zero);return found; }
    public static async Task<Process> WaitForRenderer(string shippingExe,DateTime launchTime,CancellationToken token)
    {
        var watch=Stopwatch.StartNew();
        while(watch.Elapsed<TimeSpan.FromMinutes(2))
        {
            token.ThrowIfCancellationRequested();
            var all=Find(Path.GetDirectoryName(shippingExe)!);
            Process? chosen=null;
            foreach(var p in all)
            {
                try { if(string.Equals(ImagePath(p.Id),shippingExe,StringComparison.OrdinalIgnoreCase) && !p.HasExited && p.StartTime.ToUniversalTime()>=launchTime.AddSeconds(-2) && HasUnrealWindow(p.Id)) {chosen=p;break;} }
                catch { }
            }
            foreach(var p in all)if(!ReferenceEquals(p,chosen))p.Dispose();
            if(chosen is not null)return chosen;
            await Task.Delay(400,token);
        }
        throw new TimeoutException("两分钟内未找到本次启动的鸣潮渲染窗口。未确认游戏启动；未声称 FPS 生效。");
    }
}
