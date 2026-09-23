using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using WuWaFpsUnlock;
using WuWaFpsUnlock.Core;
using WuWaFpsUnlock.Services;
using WuWaFpsUnlock.ViewModels;

internal static class SettingsLayoutTests
{
    public static void Run(Action<bool, string> check, Action pump)
    {
        if (!AppContext.BaseDirectory.Contains("Update.DialogLayoutTests", StringComparison.Ordinal))
            throw new Exception("Settings fixture must run in its isolated test output.");
        Directory.CreateDirectory(AppPaths.Data);
        byte[]? saved = File.Exists(AppPaths.Settings) ? File.ReadAllBytes(AppPaths.Settings) : null;
        File.WriteAllText(AppPaths.Settings, "{}");
        AppViewModel? vm = null;
        SettingsWindow? window = null;
        try
        {
            vm = new AppViewModel();
            typeof(AppViewModel).GetField("_hardware", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm,
                new HardwareInfo("NVIDIA GeForce RTX 4090", 59541, true, "Windows 11 fixture", "已开启", true));
            window = new SettingsWindow(vm);
            var root = (FrameworkElement)window.Content;
            var combo = (TextBlock)window.FindName("DynamicRuntimeHint");
            var mfg = Parents(combo).OfType<Border>().First();
            var right = (Grid)mfg.Parent;
            var columns = (Grid)right.Parent;
            var left = columns.Children.OfType<Grid>().Single(x => Grid.GetColumn(x) == 0);
            var environment = left.Children.OfType<Border>().Single(x => Grid.GetRow(x) == 2);
            var maintenance = right.Children.OfType<Border>().Single(x => Grid.GetRow(x) == 4);
            foreach (double width in new[] { 1160d, 850d })
            {
                root.Measure(new Size(width, 784));
                root.Arrange(new Rect(0, 0, width, 784));
                root.UpdateLayout(); pump(); root.UpdateLayout();
                check(!window.IsVisible, "settings window remains unshown");
                var leftBounds = Bounds(left, columns);
                var rightBounds = Bounds(right, columns);
                check(Math.Abs(leftBounds.Bottom - rightBounds.Bottom) <= 1.1,
                    $"settings columns align at width {width}: {leftBounds.Bottom} / {rightBounds.Bottom}");
                check(Math.Abs(Bounds(environment, columns).Bottom - Bounds(maintenance, columns).Bottom) <= 1.1,
                    "environment and maintenance card bottom edges align");
                check(Math.Abs(left.ActualHeight - 474) <= 1.1 && Math.Abs(right.ActualHeight - 474) <= 1.1,
                    "settings columns preserve total height 474");
                foreach (var card in new[] { mfg, maintenance })
                {
                    Inside(card, right, check, "right settings card");
                    foreach (var child in Descendants(card).OfType<FrameworkElement>().Where(x => x.Visibility == Visibility.Visible))
                        Inside(child, card, check, "card content " + child.GetType().Name);
                }
                var buttons = Descendants(maintenance).OfType<Button>()
                    .Where(x => x.Content is TextBlock text && new[] { "开始部署", "回退版本", "清除插件" }.Contains(text.Text)).ToArray();
                check(buttons.Length == 3, "deploy, rollback and clean buttons all present");
                foreach (var button in buttons)
                {
                    Inside(button, maintenance, check, "maintenance action button");
                    Inside(button, root, check, "maintenance action visible in window content");
                    check(button.ActualHeight >= button.MinHeight && button.ActualWidth > 0,
                        "maintenance action has full button dimensions");
                }
                check(buttons.Max(x => x.ActualWidth) - buttons.Min(x => x.ActualWidth) <= 1.1,
                    "three maintenance buttons have equal widths");
            }
        }
        finally
        {
            window?.Close(); vm?.CloseAsync().GetAwaiter().GetResult();
            if (saved is null) File.Delete(AppPaths.Settings); else File.WriteAllBytes(AppPaths.Settings, saved);
        }
    }
    private static Rect Bounds(FrameworkElement child, FrameworkElement parent) =>
        child.TransformToAncestor(parent).TransformBounds(new Rect(child.RenderSize));
    private static void Inside(FrameworkElement child, FrameworkElement parent, Action<bool, string> check, string label)
    {
        var b = Bounds(child, parent);
        check(b.Left >= -1.1 && b.Top >= -1.1 && b.Right <= parent.ActualWidth + 1.1 && b.Bottom <= parent.ActualHeight + 1.1,
            $"{label} {b} inside {parent.ActualWidth}x{parent.ActualHeight}");
    }
    private static IEnumerable<DependencyObject> Parents(DependencyObject child)
    {
        while (LogicalTreeHelper.GetParent(child) is { } parent) { yield return parent; child = parent; }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
