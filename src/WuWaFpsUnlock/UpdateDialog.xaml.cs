using System.ComponentModel;
using System.Windows;

namespace WuWaFpsUnlock;

public partial class UpdateDialog : Window
{
    private readonly Func<CancellationToken, IProgress<string>, Task<bool>> _install;
    private readonly Action<string> _log;
    private CancellationTokenSource? _download;
    private bool _installing, _closeAfterCancel, _closed;
    public bool UpdateStarted { get; private set; }

    public UpdateDialog(string version, string notes, bool skipped, Action<bool> skipChanged,
        Func<CancellationToken, IProgress<string>, Task<bool>> install, Action<string> log)
    {
        InitializeComponent();
        _install = install; _log = log;
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
        OperationStatus.Text = "点击“立即更新”后才会下载安装包；安装前启动器会退出。";
        Width = Math.Min(Width, SystemParameters.WorkArea.Width - 36);
        MaxHeight = Math.Max(240, SystemParameters.WorkArea.Height - 36);
        ReleaseNotes.MaxHeight = Math.Max(80, MaxHeight - 260);
        Closing += OnClosing;
        Closed += (_, _) => { _closed = true; _download?.Cancel(); };
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; CancelOrClose(); } };
    }

    public void CancelDownload() => _download?.Cancel();
    public void CancelForShutdown(){ if (!_closed) Close(); }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_installing) return;
        _installing = true; _closeAfterCancel = false;
        using var cancellation = new CancellationTokenSource(); _download = cancellation;
        InstallButton.IsEnabled = false; SkipVersion.IsEnabled = false;
        LaterButton.Content = "取消下载"; DownloadProgress.Visibility = Visibility.Visible;
        OperationStatus.Text = "正在准备下载…";
        try
        {
            var progress = new Progress<string>(message => { if (!_closed) OperationStatus.Text = message; });
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
                InstallButton.IsEnabled = true; SkipVersion.IsEnabled = true;
                LaterButton.Content = "暂不更新"; DownloadProgress.Visibility = Visibility.Collapsed;
            }
        }
        if (_closeAfterCancel && !_closed) Close();
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
