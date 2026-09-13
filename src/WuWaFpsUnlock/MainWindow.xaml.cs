using System.ComponentModel;
using System.Windows;
using WuWaFpsUnlock.ViewModels;
namespace WuWaFpsUnlock;
public partial class MainWindow:Window
{
    private readonly AppViewModel _vm;private SettingsWindow? _settings;
    public MainWindow(AppViewModel vm)
    {
        InitializeComponent();_vm=vm;DataContext=vm;vm.SettingsRequested+=OpenSettings;
        Width=Math.Min(Width,SystemParameters.WorkArea.Width-36);Height=Math.Min(Height,SystemParameters.WorkArea.Height-36);
        Closing+=OnClosing;Closed+=async(_,_)=>await vm.CloseAsync();
        Loaded+=async(_,_)=>{try{await vm.RefreshAsync();}catch(Exception e){vm.ReportError(e);}};
    }
    private void OpenSettings()
    {
        if(_settings is not null){if(_settings.WindowState==WindowState.Minimized)_settings.WindowState=WindowState.Normal;_settings.Activate();return;}
        _settings=new SettingsWindow(_vm){Owner=this};_settings.Closed+=(_,_)=>_settings=null;_settings.Show();
    }
    private void OnClosing(object? sender,CancelEventArgs e)
    {
        if(_vm.Busy){e.Cancel=true;_vm.Log("当前操作仍在进行，不能在文件写入/启动过程中关闭窗口。");return;}
        if(_vm.IsGameRunning && MessageBox.Show(this,"游戏将继续运行，FPS 插件可能保持最后的上限设置，但关闭本工具后无法继续调节。是否关闭窗口？","关闭控制器",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)e.Cancel=true;
    }
    private void Minimize_Click(object sender,RoutedEventArgs e)=>SystemCommands.MinimizeWindow(this);
    private void Maximize_Click(object sender,RoutedEventArgs e){if(WindowState==WindowState.Maximized)SystemCommands.RestoreWindow(this);else SystemCommands.MaximizeWindow(this);}
    private void Close_Click(object sender,RoutedEventArgs e)=>Close();
}
