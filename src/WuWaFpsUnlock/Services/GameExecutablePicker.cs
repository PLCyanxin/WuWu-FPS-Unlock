using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WuWaFpsUnlock.Services;

/// <summary>A managed WPF file picker. It never asks the shell for icons, menus, thumbnails or execution.</summary>
public static class GameExecutablePicker
{
    public static string? Show(string initialDirectory)
    {
        var dialog = new PickerWindow(initialDirectory);
        return dialog.ShowDialog() == true ? dialog.SelectedExecutable : null;
    }

    private sealed record Entry(string FullPath, string Name, bool IsDirectory)
    {
        public string Label => IsDirectory ? "文件夹   " + Name : Name;
    }

    private sealed class PickerWindow : Window
    {
        private readonly TextBox _address = new() { MinWidth = 100 };
        private readonly TextBox _file = new() { MinWidth = 100 };
        private readonly ListBox _entries = new() { DisplayMemberPath = nameof(Entry.Label), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
        private readonly Button _up = new() { Content = "上级", MinWidth = 66, IsEnabled = false };
        private readonly Button _open = new() { Content = "前往", MinWidth = 66 };
        private readonly Button _choose = new() { Content = "使用所选 EXE", MinWidth = 132, IsDefault = true, IsEnabled = false };
        private string _directory = "";
        private CancellationTokenSource? _navigation;
        private bool _closed;
        public string? SelectedExecutable { get; private set; }

        public PickerWindow(string initialDirectory)
        {
            Title = "选择鸣潮游戏 EXE"; Width = 820; Height = 570; MinWidth = 640; MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;
            if (Application.Current?.TryFindResource("WindowFrame") is Style frame) Style = frame;
            else { FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"); FontSize = 13; }
            var layout = new Grid { Margin = new Thickness(18) };
            foreach (var height in new[] { GridLength.Auto, new GridLength(12), new GridLength(1, GridUnitType.Star), new GridLength(12), GridLength.Auto, new GridLength(12), GridLength.Auto })
                layout.RowDefinitions.Add(new RowDefinition { Height = height });
            var addressRow = new Grid();
            addressRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            addressRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); addressRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            _address.ToolTip = "输入文件夹或 EXE 完整路径后按 Enter";
            _up.Margin = new Thickness(8, 0, 0, 0); _open.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(_open, 1); Grid.SetColumn(_up, 2); addressRow.Children.Add(_address); addressRow.Children.Add(_open); addressRow.Children.Add(_up);
            layout.Children.Add(addressRow);
            Grid.SetRow(_entries, 2); layout.Children.Add(_entries);
            var fileRow = new Grid(); fileRow.ColumnDefinitions.Add(new() { Width = new GridLength(76) }); fileRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            fileRow.Children.Add(new TextBlock { Text = "EXE 路径", VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(_file, 1); fileRow.Children.Add(_file); Grid.SetRow(fileRow, 4); layout.Children.Add(fileRow);
            var footer = new Grid(); footer.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            _status.Margin = new Thickness(0, 0, 12, 0); footer.Children.Add(_status);
            var cancel = new Button { Content = "取消", MinWidth = 82, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
            Grid.SetColumn(_choose, 1); Grid.SetColumn(cancel, 2); footer.Children.Add(_choose); footer.Children.Add(cancel); Grid.SetRow(footer, 6); layout.Children.Add(footer); Content = layout;

            // Handle right-click and keyboard context-menu requests before child controls or any shell integration.
            PreviewMouseRightButtonDown += (_, e) => e.Handled = true;
            PreviewMouseRightButtonUp += (_, e) => e.Handled = true;
            ContextMenuOpening += (_, e) => e.Handled = true;
            PreviewKeyDown += (_, e) => { if (e.Key == Key.Apps || (e.Key == Key.F10 && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))) e.Handled = true; };
            _address.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await NavigateAsync(_address.Text); } };
            _open.Click += async (_, _) => await NavigateAsync(_address.Text);
            _up.Click += async (_, _) => { if (Path.GetDirectoryName(_directory) is string parent) await NavigateAsync(parent); };
            _entries.SelectionChanged += (_, _) => { if (_entries.SelectedItem is Entry entry && !entry.IsDirectory) _file.Text = entry.FullPath; };
            _entries.MouseDoubleClick += async (_, e) =>
            {
                if (e.ChangedButton == MouseButton.Left && ItemsControl.ContainerFromElement(_entries, e.OriginalSource as DependencyObject) is ListBoxItem && _entries.SelectedItem is Entry entry)
                { e.Handled = true; if (entry.IsDirectory) await NavigateAsync(entry.FullPath); else _file.Text = entry.FullPath; }
            };
            _entries.KeyDown += async (_, e) =>
            {
                if (e.Key == Key.Enter && _entries.SelectedItem is Entry entry) { e.Handled = true; if (entry.IsDirectory) await NavigateAsync(entry.FullPath); else _file.Text = entry.FullPath; }
            };
            _file.TextChanged += (_, _) => _choose.IsEnabled = !_closed && _navigation is null && !string.IsNullOrWhiteSpace(_file.Text);
            _choose.Click += (_, _) => Confirm();
            Closed += (_, _) => { _closed = true; _navigation?.Cancel(); };
            Loaded += async (_, _) => await NavigateAsync(string.IsNullOrWhiteSpace(initialDirectory) ? AppContext.BaseDirectory : initialDirectory);
        }

        private async Task NavigateAsync(string input)
        {
            _navigation?.Cancel(); var navigation = new CancellationTokenSource(); _navigation = navigation;
            var token = navigation.Token;
            _status.Text = "正在读取目录…"; _entries.IsEnabled = false; _choose.IsEnabled = false;
            try
            {
                string path = Normalize(input); string? selected = null;
                EnsurePlain(path);
                if (File.Exists(path)) { if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new IOException("请选择 EXE 文件或文件夹。"); selected = path; path = Path.GetDirectoryName(path)!; }
                if (!Directory.Exists(path)) throw new DirectoryNotFoundException("目录不存在或无法访问，请输入正确路径。");
                string directory = path;
                var result = await Task.Run(() =>
                {
                    var list = new List<Entry>(); int skipped = 0, examined = 0; bool capped = false;
                    foreach (var item in Directory.EnumerateFileSystemEntries(directory))
                    {
                        token.ThrowIfCancellationRequested(); if (++examined > 20_000) { capped = true; break; }
                        try
                        {
                            var attributes = File.GetAttributes(item);
                            if ((attributes & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                            bool folder = (attributes & FileAttributes.Directory) != 0;
                            if (folder || item.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) list.Add(new(item, Path.GetFileName(item), folder));
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; }
                    }
                    return (Items: list.OrderByDescending(e => e.IsDirectory).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(), Skipped: skipped, Capped: capped);
                }, token);
                if (_closed || token.IsCancellationRequested) return;
                _directory = directory; _address.Text = directory; _file.Text = selected ?? "";
                _entries.ItemsSource = result.Items; _up.IsEnabled = Path.GetDirectoryName(directory) is not null;
                if (selected is not null) _entries.SelectedItem = result.Items.FirstOrDefault(e => e.FullPath.Equals(selected, StringComparison.OrdinalIgnoreCase));
                _status.Text = $"{result.Items.Count(e => e.IsDirectory)} 个文件夹，{result.Items.Count(e => !e.IsDirectory)} 个 EXE" +
                    (result.Skipped > 0 ? $"；跳过 {result.Skipped} 个链接或不可读项目" : "") + (result.Capped ? "；目录过大，请输入更精确路径" : "");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
            { if (!_closed && !token.IsCancellationRequested) _status.Text = ex.Message; }
            finally
            {
                if (ReferenceEquals(_navigation, navigation)) { _navigation = null; if (!_closed) { _entries.IsEnabled = true; _choose.IsEnabled = !string.IsNullOrWhiteSpace(_file.Text); } }
                navigation.Dispose();
            }
        }

        private string Normalize(string value)
        {
            value = value.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("请输入文件夹或 EXE 路径。");
            if (!Path.IsPathFullyQualified(value))
            {
                if (string.IsNullOrWhiteSpace(_directory)) throw new ArgumentException("请输入完整路径。");
                value = Path.Combine(_directory, value);
            }
            return Path.GetFullPath(value);
        }

        private void Confirm()
        {
            try
            {
                var path = Normalize(_file.Text); EnsurePlain(path);
                if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) throw new IOException("所选 EXE 不存在，请重新选择。");
                SelectedExecutable = path; DialogResult = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException) { _status.Text = ex.Message; }
        }

        private static void EnsurePlain(string path)
        {
            for (string? item = path; item is not null; item = Path.GetDirectoryName(item))
                if ((File.Exists(item) || Directory.Exists(item)) && (File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("此选择器不跟随目录或文件链接，请直接输入真实路径。");
        }
    }
}
