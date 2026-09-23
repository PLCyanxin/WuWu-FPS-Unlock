using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WuWaFpsUnlock;

public static class DialogLayoutTests
{
    private static int _checks;
    private static readonly string LongNotes = string.Join("\n", Enumerable.Range(1, 600)
        .Select(i => $"{i}. 更新说明：部署完成不代表游戏内已经生效，请核对环境和操作步骤。"));
    private static readonly string LongError = string.Concat(Enumerable.Repeat("下载失败：连接中断，请稍后重试。", 100));

    [STAThread]
    public static int Main()
    {
        try
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            var app = new App();
            app.InitializeComponent();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var startup = typeof(App).GetMethod("OnStartup", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;
            app.Startup -= (StartupEventHandler)Delegate.CreateDelegate(typeof(StartupEventHandler), app, startup);
            // Never run Application or show a Window: measure the real XAML content and templates only.
            foreach (var notes in new[] { "修复更新窗口布局。", LongNotes })
            {
                var dialog = NewDialog(notes);
                Check(dialog.SizeToContent == SizeToContent.Manual, "window permits manual vertical resize");
                Check(double.IsFinite(dialog.MaxHeight) && dialog.Height <= dialog.MaxHeight,
                    "initial window height is bounded");
                Check(dialog.MinHeight > 0 && dialog.MinHeight <= dialog.MaxHeight, "minimum height is usable");
                foreach (double width in new[] { 600d, 480d })
                foreach (double height in new[] { dialog.Height, dialog.MinHeight, (dialog.Height + dialog.MinHeight) / 2, dialog.Height })
                {
                    Layout(dialog, height, width);
                    CheckControls(dialog);
                    if (notes == LongNotes) CheckScrolling(dialog);
                }
                dialog.Close();
            }
            ErrorLayout();
            DownloadLayout();
            SettingsLayoutTests.Run(Check, Pump);
            Console.WriteLine($"{_checks}/{_checks} native WPF layout checks passed; no shown windows, network, updater or game execution.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    private static UpdateDialog NewDialog(string notes,
        Func<CancellationToken, IProgress<string>, Task<bool>>? install = null) =>
        new("1.2RC", notes, false, _ => { }, install ?? ((_, _) => Task.FromResult(false)), _ => { });

    private static T Named<T>(UpdateDialog dialog, string name) where T : FrameworkElement => (T)dialog.FindName(name);
    private static FrameworkElement Root(UpdateDialog dialog) => (FrameworkElement)dialog.Content;
    private static void Layout(UpdateDialog dialog, double outerHeight, double width = 480)
    {
        // Reserve native caption/borders without creating an HWND. Use minimum supported width as well.
        double chrome = SystemParameters.CaptionHeight + 2 * SystemParameters.ResizeFrameVerticalBorderWidth;
        double clientHeight = Math.Max(1, outerHeight - chrome);
        var root = Root(dialog);
        root.Measure(new Size(width, clientHeight));
        root.Arrange(new Rect(0, 0, width, clientHeight));
        root.UpdateLayout();
        Pump();
        root.UpdateLayout();
        Check(root.ActualWidth <= width + 1.1 && root.ActualHeight <= clientHeight + 1.1,
            "layout stays within the allocated client size");
    }
    private static void CheckControls(UpdateDialog dialog)
    {
        Check(!dialog.IsVisible, "dialog remains unshown");
        foreach (string name in new[] { "InstallButton", "LaterButton", "SkipVersion", "ReleaseNotes" })
            Inside(Named<FrameworkElement>(dialog, name), Root(dialog), name);
        var install = Named<Button>(dialog, "InstallButton");
        var later = Named<Button>(dialog, "LaterButton");
        Check(install.ActualHeight >= install.MinHeight && later.ActualHeight >= later.MinHeight,
            "action buttons retain their full minimum height");
        var progress = Named<ProgressBar>(dialog, "DownloadProgress");
        if (progress.Visibility == Visibility.Visible) Inside(progress, Root(dialog), "download progress");
    }
    private static void Inside(FrameworkElement child, FrameworkElement root, string label)
    {
        var bounds = child.TransformToAncestor(root).TransformBounds(new Rect(child.RenderSize));
        const double tolerance = 1.1; // Layout rounding can consume a device pixel.
        Check(bounds.Width > 0 && bounds.Height > 0 && bounds.Left >= -tolerance && bounds.Top >= -tolerance &&
              bounds.Right <= root.ActualWidth + tolerance && bounds.Bottom <= root.ActualHeight + tolerance,
            $"{label} visible bounds {bounds} inside {root.ActualWidth}x{root.ActualHeight}");
    }
    private static void CheckScrolling(UpdateDialog dialog)
    {
        var notes = Named<TextBox>(dialog, "ReleaseNotes");
        notes.ApplyTemplate();
        var scroll = (ScrollViewer)notes.Template.FindName("PART_ContentHost", notes);
        Check(scroll.ViewportHeight > 0 && scroll.ScrollableHeight > 0, "long release notes have a finite scrollable viewport");
        notes.ScrollToEnd();
        Pump(); Root(dialog).UpdateLayout();
        Check(scroll.VerticalOffset > 0, "long release notes can scroll to later content");
        CheckControls(dialog);
    }
    private static void CheckStatusScrolling(UpdateDialog dialog)
    {
        var status = Named<TextBlock>(dialog, "OperationStatus");
        var scroll = status.Parent as ScrollViewer;
        Check(scroll is not null && scroll.ScrollableHeight > 0, "long status text has its own scrollable viewport");
        Inside(scroll!, Root(dialog), "error/status viewport");
        scroll!.ScrollToEnd(); Pump(); Root(dialog).UpdateLayout();
        Check(scroll.VerticalOffset > 0, "full error remains reachable by scrolling");
    }
    private static void ErrorLayout()
    {
        var dialog = NewDialog(LongNotes, (_, _) => Task.FromException<bool>(new IOException(LongError)));
        Named<Button>(dialog, "InstallButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump();
        Check(Named<TextBlock>(dialog, "OperationStatus").Text.Contains(LongError), "real install failure retains full error text");
        foreach (double width in new[] { 600d, 480d })
        {
            Layout(dialog, dialog.MinHeight, width);
            CheckControls(dialog); CheckScrolling(dialog); CheckStatusScrolling(dialog);
        }
        Check(Named<Button>(dialog, "InstallButton").IsEnabled, "failed download restores install button");
        dialog.Close();
    }
    private static void DownloadLayout()
    {
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken received = default;
        using var registrationHolder = new CancellationHolder();
        var dialog = NewDialog(LongNotes, (token, progress) =>
        {
            received = token;
            registrationHolder.Registration = token.Register(() => pending.TrySetCanceled(token));
            progress.Report(LongError);
            return pending.Task;
        });
        Named<Button>(dialog, "InstallButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump();
        foreach (double width in new[] { 600d, 480d })
        {
            Layout(dialog, dialog.MinHeight, width);
            CheckControls(dialog); CheckScrolling(dialog); CheckStatusScrolling(dialog);
        }
        var cancel = Named<Button>(dialog, "LaterButton");
        Check(cancel.Content?.ToString() == "取消下载" && cancel.IsEnabled, "cancel download remains available");
        Check(!Named<Button>(dialog, "InstallButton").IsEnabled, "install button disabled during download");
        cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(received.IsCancellationRequested, "visible cancel action cancels the download token");
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!Named<Button>(dialog, "InstallButton").IsEnabled && DateTime.UtcNow < deadline) { Pump(); Thread.Sleep(1); }
        Check(Named<Button>(dialog, "InstallButton").IsEnabled, "cancellation completes without starting updater");
        Layout(dialog, dialog.MinHeight); CheckControls(dialog);
        dialog.Close();
    }
    private sealed class CancellationHolder : IDisposable
    {
        public CancellationTokenRegistration Registration;
        public void Dispose() => Registration.Dispose();
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static void Check(bool pass, string name)
    {
        if (!pass) throw new Exception("FAIL " + name);
        ++_checks; Console.WriteLine("PASS " + name);
    }
}
