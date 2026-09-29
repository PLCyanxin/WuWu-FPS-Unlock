using System.Diagnostics;
using System.Windows;
using WuWaFpsUnlock.Core;
using WuWaFpsUnlock.Services;

namespace WuWaFpsUnlock.ViewModels;

public sealed partial class AppViewModel
{
    private bool _predownloadChecked;

    public async Task CheckOfficialPredownloadOnStartupAsync()
    {
        if (_predownloadChecked || _closing) return;
        _predownloadChecked = true;
        string root = GameRoot, exe = GameExe;
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(exe)) return;
        try
        {
            using var service = new OfficialPredownloadService();
            // Metadata I/O must not block the WPF dispatcher or reserve the game-start lock.
            var result = await Task.Run(() => service.CheckAsync(root, exe, _lifetime.Token));
            if (_closing || root != GameRoot || exe != GameExe) return;
            if (result.State != PredownloadState.Available)
            {
                Log(result.State switch
                {
                    PredownloadState.AlreadyDownloaded => $"官方预下载：{result.Version} 已有下载完成记录。",
                    PredownloadState.NotAvailable => "官方预下载：当前没有待处理的预下载。",
                    _ => "官方预下载：未能确认状态，不影响游戏启动。" 
                });
                return;
            }
            // A late network response must not interrupt a game launch or deployment.
            if (Busy || IsGameRunning) { Log($"鸣潮 {result.Version} 预下载已开放，请在方便时打开官方启动器下载。"); return; }
            try { GameProcesses.RequireStopped(root); }
            catch { Log($"鸣潮 {result.Version} 预下载已开放；当前无法确认游戏已退出，未自动打开官方启动器。"); return; }
            if (result.LauncherPath is not { } launcher || !File.Exists(launcher)) return;
            SafePaths.EnsureNoLinks(Path.GetPathRoot(launcher)!, launcher);
            Busy = true;
            try
            {
                using var process = LauncherScheduling.StartProcess(new ProcessStartInfo(launcher)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(launcher)!
                });
                Status = $"鸣潮 {result.Version} 预下载已开放，请在官方启动器中下载。";
                Log(Status);
                MessageBox.Show(Application.Current.MainWindow,
                    $"鸣潮 {result.Version} 大版本预下载已开放。\n已打开官方启动器，请在其中完成预下载。\n\n本工具不会下载或安装游戏更新。",
                    "鸣潮预下载", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally { Busy = false; }
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!_closing) Log("官方预下载检查或启动未完成：" + error.Message);
        }
    }
}
