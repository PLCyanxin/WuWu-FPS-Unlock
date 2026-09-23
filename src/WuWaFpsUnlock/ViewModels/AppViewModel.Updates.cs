using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using WuWaFpsUnlock.Core;
using WuWaFpsUnlock.Services;

namespace WuWaFpsUnlock.ViewModels;

public sealed partial class AppViewModel
{
    private static readonly HttpClient UpdateHttp = new() { Timeout = TimeSpan.FromMinutes(20) };
    private readonly GitHubUpdateService _updates = new(UpdateHttp);
    private CancellationTokenSource? _updateCheckCancellation;
    private UpdateDialog? _updateDialog;
    private bool _checkingUpdates, _startupUpdateChecked, _automaticCheckInProgress;
    private string _updateStatus = "";
    public event Action? UpdateExitRequested;
    public AsyncCommand CheckUpdatesCommand { get; private set; } = null!;
    public bool AutoCheckUpdates
    {
        get => _settings.AutoCheckUpdates;
        set
        {
            if (_settings.AutoCheckUpdates == value) return;
            try
            {
                PersistUpdatePreference(settings => settings.AutoCheckUpdates = value);
                if (!value && _automaticCheckInProgress) _updateCheckCancellation?.Cancel();
            }
            catch (Exception error)
            {
                UpdateStatus = "更新设置未保存：" + error.Message; Log(UpdateStatus);
                // WPF may still be completing a source update; refresh the checkbox afterward.
                Application.Current?.Dispatcher.BeginInvoke(new Action(() => Notify(nameof(AutoCheckUpdates))));
            }
            Notify();
        }
    }
    public string UpdateStatus { get => _updateStatus; private set { _updateStatus = value; Notify(); } }
    public string UpdateCheckLabel => _checkingUpdates ? "正在检查…" : "检查更新";
    private void InitializeUpdates() => CheckUpdatesCommand = new(() => CheckForUpdatesAsync(true), () => !Busy && !_checkingUpdates && !_closing, ReportError);
    public async Task CheckForUpdatesOnStartupAsync()
    {
        if (_startupUpdateChecked || _closing) return;
        _startupUpdateChecked = true;
        if (AutoCheckUpdates) await CheckForUpdatesAsync(false);
    }
    private static string CurrentUpdateVersion => typeof(AppViewModel).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(AppViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (_closing || _checkingUpdates) return;
        _checkingUpdates = true; _automaticCheckInProgress = !manual;
        Notify(nameof(UpdateCheckLabel)); CheckUpdatesCommand.Refresh();
        UpdateStatus = "正在检查更新…";
        using var check = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _updateCheckCancellation = check;
        check.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var release = await _updates.CheckAsync(CurrentUpdateVersion, check.Token);
            check.Token.ThrowIfCancellationRequested();
            // Only the highest candidate is considered: skipping it never offers an older release.
            if (release is null) { UpdateStatus = "当前已是最新版本"; if (manual) Log(UpdateStatus); return; }
            if (!manual && string.Equals(_settings.SkippedUpdateTag, release.Tag, StringComparison.OrdinalIgnoreCase))
            { UpdateStatus = "已跳过版本 " + release.Tag; Log(UpdateStatus); return; }
            UpdateStatus = "发现新版本 " + release.Tag;
            if (Busy) { UpdateStatus += "，当前操作结束后可手动检查"; Log(UpdateStatus); return; }
            try { RequireGameStoppedForUpdate(); }
            catch (Exception ex) { UpdateStatus += "；" + ex.Message; Log(UpdateStatus); return; }
            bool started = false;
            Busy = true;
            try
            {
                var dialog = new UpdateDialog(release.Version, release.Notes,
                    string.Equals(_settings.SkippedUpdateTag, release.Tag, StringComparison.OrdinalIgnoreCase),
                    skipped =>
                    {
                        PersistUpdatePreference(settings =>
                        {
                            if (skipped) settings.SkippedUpdateTag = release.Tag;
                            else if (string.Equals(settings.SkippedUpdateTag, release.Tag, StringComparison.OrdinalIgnoreCase)) settings.SkippedUpdateTag = "";
                        });
                    }, (token, progress) => InstallUpdateAsync(release, token, progress), Log);
                dialog.Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                    ?? Application.Current.MainWindow;
                _updateDialog = dialog;
                dialog.ShowDialog();
                started = dialog.UpdateStarted;
                if (!started) UpdateStatus = string.Equals(_settings.SkippedUpdateTag, release.Tag, StringComparison.OrdinalIgnoreCase)
                    ? "已跳过版本 " + release.Tag : "暂未安装 " + release.Tag;
            }
            finally { _updateDialog = null; Busy = false; }
            if (started) UpdateExitRequested?.Invoke();
        }
        catch (OperationCanceledException)
        {
            if (!_closing) { UpdateStatus = check.IsCancellationRequested && (manual || AutoCheckUpdates) ? "检查更新超时或已取消" : "已关闭自动检查更新"; Log(UpdateStatus); }
        }
        catch (Exception ex) { UpdateStatus = "检查更新失败：" + ex.Message; Log(UpdateStatus); }
        finally
        {
            _updateCheckCancellation = null; _checkingUpdates = false; _automaticCheckInProgress = false;
            Notify(nameof(UpdateCheckLabel)); CheckUpdatesCommand.Refresh();
        }
    }
    private async Task<bool> InstallUpdateAsync(UpdateRelease release, CancellationToken cancellation, IProgress<string> progress)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _lifetime.Token);
        linked.CancelAfter(TimeSpan.FromMinutes(20));
        RequireGameStoppedForUpdate();
        progress.Report("正在下载并校验更新包…");
        var updater = await _updates.StageAsync(release, AppPaths.Base, linked.Token);
        linked.Token.ThrowIfCancellationRequested();
        RequireGameStoppedForUpdate();
        var appExe = Environment.ProcessPath ?? throw new IOException("无法确定当前启动器路径。");
        progress.Report("正在启动更新程序…");
        var start = new ProcessStartInfo(updater) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(updater)! };
        start.ArgumentList.Add("--wait-for-exit");
        start.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add(Path.GetFullPath(appExe));
        using var process = Process.Start(start) ?? throw new IOException("更新程序未能启动。");
        Log("已启动更新程序；启动器退出后才会替换文件。");
        return true;
    }
    private void RequireGameStoppedForUpdate()
    {
        if (IsGameRunning) throw new InvalidOperationException("请先退出游戏，再安装更新。");
        if (!string.IsNullOrWhiteSpace(GameRoot) && Directory.Exists(GameRoot)) GameProcesses.RequireStopped(GameRoot);
        // An externally started game must also block updates even with an empty or stale saved path.
        var processes = Process.GetProcessesByName("Client-Win64-Shipping");
        try
        {
            if (processes.Any(process => !process.HasExited)) throw new InvalidOperationException("检测到游戏仍在运行，请退出游戏后再安装更新。");
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }
    private void PersistUpdatePreference(Action<UserSettings> change)
    {
        var candidate = _settings.Clone();
        change(candidate);
        JsonFiles.Save(AppPaths.Settings, candidate);
        _settings = candidate; // Commit in-memory state only after durable write succeeds.
    }
    private void CancelUpdateWork()
    {
        _updateCheckCancellation?.Cancel();
        _updateDialog?.CancelForShutdown();
    }
}
