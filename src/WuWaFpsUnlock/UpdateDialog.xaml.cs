using System.ComponentModel;
using System.Windows;

namespace WuWaFpsUnlock;

public partial class UpdateDialog : Window
{
    private readonly Func<CancellationToken, IProgress<string>, Task<bool>> _install;
    private readonly Action<string> _log;
    private CancellationTokenSource? _download;
    private bool _installing, _closeAfterCancel, _closed, _gameRunning;
    public bool DeferredRequested { get; private set; }
    public bool UpdateStarted { get; private set; }

    public UpdateDialog(string version, string notes, bool skipped, Action<bool> skipChanged,
        Func<CancellationToken, IProgress<string>, Task<bool>> install, Action<string> log,
        bool gameRunning = false, bool allowDefer = true, bool autoStart = false)
    {
        InitializeComponent();
        _install = install; _log = log; _gameRunning = gameRunning;
        AfterGameButton.Visibility = allowDefer ? Visibility.Visible : Visibility.Collapsed;
        InstallButton.IsEnabled = !gameRunning;
        MinHeight = allowDefer ? 400 : 320;
        ReleaseTitle.Text = "发现新版本 " + version;
        ReleaseNotes.Text = string.IsNullOrWhiteSpace(notes) ? "此版本未提供更新说明。" : notes;
        SkipVersion.IsChecked = skipped;
        bool committedSkip = skipped, restoringSkip = false;
        void SaveSkip(bool requested)
        {
            if (restoringSkip) return;
            try { skipChanged(requested); committedSkip = requested; }
            catch (Exception error)
            {
                restoringSkip = true;
                try { SkipVersion.IsChecked = committedSkip; }
                finally { restoringSkip = false; }
                OperationStatus.Text = "跳过设置未保存：" + error.Message;
                _log(OperationStatus.Text);
            }
        }
        SkipVersion.Checked += (_, _) => SaveSkip(true);
        SkipVersion.Unchecked += (_, _) => SaveSkip(false);
        OperationStatus.Text = gameRunning ? "游戏正在运行，可预约本次游玩结束后更新。" : "可立即更新，或预约下一次游玩结束后更新。";
        double availableWidth = Math.Max(320, SystemParameters.WorkArea.Width - 36);
        MinWidth = Math.Min(MinWidth, availableWidth);
        Width = Math.Min(Width, availableWidth);
        MaxHeight = Math.Min(560, Math.Max(MinHeight, SystemParameters.WorkArea.Height - 36));
        MinHeight = Math.Min(MinHeight, MaxHeight);
        // Measure once for a compact opening size. During resize, only the star
        // row changes height; the actions remain outside the scrolling notes.
        ReleaseNotes.MaxHeight = Math.Max(80, MaxHeight - (allowDefer ? 302 : 260));
        DialogLayout.Measure(new Size(Width - 2 * SystemParameters.ResizeFrameVerticalBorderWidth, double.PositiveInfinity));
        double chrome = SystemParameters.WindowCaptionHeight + 2 * SystemParameters.ResizeFrameHorizontalBorderHeight;
        Height = Math.Clamp(DialogLayout.DesiredSize.Height + chrome, MinHeight, MaxHeight);
        ReleaseNotes.ClearValue(MaxHeightProperty);
        bool autoStartAttempted = false;
        if (autoStart) Loaded += (_, _) => { if (!autoStartAttempted && !_closed && !_gameRunning && !_installing) { autoStartAttempted = true; Install_Click(InstallButton, new RoutedEventArgs()); } };
        Closing += OnClosing;
        Closed += (_, _) => { _closed = true; _download?.Cancel(); };
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; CancelOrClose(); } };
    }

    public void CancelDownload() => _download?.Cancel();
    public void CancelForShutdown(){ if (!_closed) Close(); }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_installing || _gameRunning || _closed) return;
        _installing = true; _closeAfterCancel = false;
        using var cancellation = new CancellationTokenSource(); _download = cancellation;
        InstallButton.IsEnabled = false; SkipVersion.IsEnabled = false; AfterGameButton.IsEnabled = false;
        LaterButton.Content = "取消下载"; DownloadProgress.Visibility = Visibility.Visible;
        OperationStatus.Text = "请勿关闭窗口与游戏启动器。\n正在准备下载…";
        try
        {
            var progress = new Progress<string>(message => { if (!_closed) OperationStatus.Text = "请勿关闭窗口与游戏启动器。\n" + message; });
            UpdateStarted = await _install(cancellation.Token, progress);
            if (UpdateStarted) { _installing = false; DialogResult = true; return; }
        }
        catch (OperationCanceledException)
        {
            OperationStatus.Text = "下载已取消，启动器仍可使用。";
            _log("更新下载已取消；没有退出启动器。");
        }
        catch (Exception ex)
        {
            OperationStatus.Text = "更新未开始：" + ex.Message;
            _log(OperationStatus.Text);
        }
        finally
        {
            _installing = false; _download = null;
            if (!_closed)
            {
                InstallButton.IsEnabled = !_gameRunning; SkipVersion.IsEnabled = true; AfterGameButton.IsEnabled = true;
                LaterButton.Content = "暂不更新"; DownloadProgress.Visibility = Visibility.Collapsed;
            }
        }
        if (_closeAfterCancel && !_closed) Close();
    }

    public void SetGameRunning(bool running)
    {
        _gameRunning = running;
        if (!_installing) InstallButton.IsEnabled = !running;
    }
    private void AfterGame_Click(object sender, RoutedEventArgs e)
    {
        if (_installing || _closed) return;
        DeferredRequested = true;
        Close();
    }
    private void Later_Click(object sender, RoutedEventArgs e) => CancelOrClose();
    private void CancelOrClose()
    {
        if (_installing) { _download?.Cancel(); OperationStatus.Text = "正在取消下载…"; }
        else Close();
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_installing) return;
        e.Cancel = true; _closeAfterCancel = true; _download?.Cancel();
        OperationStatus.Text = "正在取消下载…";
    }
}
