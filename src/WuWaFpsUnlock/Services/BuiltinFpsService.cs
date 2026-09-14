using System.Diagnostics;
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
    public static async Task WaitForRendererAsync(Process process,string expected,CancellationToken token)
    {
        var paths=new WindowsRestartProcessCatalog();string canonical=paths.NormalizeExecutablePath(expected);var timer=Stopwatch.StartNew();
        while(timer.Elapsed<TimeSpan.FromMinutes(2))
        {
            token.ThrowIfCancellationRequested();
            if(process.HasExited)throw new IOException($"游戏PID {process.Id} 已退出，未确认渲染就绪。");
            if(!paths.NormalizeExecutablePath(GameProcesses.ImagePath(process.Id)).Equals(canonical,StringComparison.OrdinalIgnoreCase))throw new IOException("新游戏进程路径变化，停止连接。");
            if(GameProcesses.HasUnrealWindow(process.Id))return;
            await Task.Delay(400,token);
        }
        throw new TimeoutException($"已启动PID {process.Id}，但两分钟内未确认游戏渲染窗口；没有再次启动。");
    }
}
