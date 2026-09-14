using System.ComponentModel;
using System.Windows;
using WuWaFpsUnlock.ViewModels;
namespace WuWaFpsUnlock;
public partial class MainWindow:Window
{
    private readonly AppViewModel _vm;private SettingsWindow? _settings;
    private System.Windows.Forms.NotifyIcon? _tray;
    private System.Drawing.Icon? _trayIcon;
    private bool _exitRequested;
    public MainWindow(AppViewModel vm)
    {
        InitializeComponent();_vm=vm;DataContext=vm;vm.SettingsRequested+=OpenSettings;
        vm.GameReady+=MinimizeToTray;
        Width=Math.Min(Width,SystemParameters.WorkArea.Width-36);Height=Math.Min(Height,SystemParameters.WorkArea.Height-36);
        Closing+=OnClosing;Closed+=async(_,_)=>{_tray?.Dispose();_trayIcon?.Dispose();await vm.CloseAsync();};
        Loaded+=async(_,_)=>{try{await vm.RefreshAsync();}catch(Exception e){vm.ReportError(e);}};
    }
    private void OpenSettings()
    {
        if(_settings is not null){_settings.Show();if(_settings.WindowState==WindowState.Minimized)_settings.WindowState=WindowState.Normal;_settings.Activate();return;}
        _settings=new SettingsWindow(_vm){Owner=this};_settings.Closed+=(_,_)=>_settings=null;_settings.Show();
    }
    private void OnClosing(object? sender,CancelEventArgs e)
    {
        if(!_exitRequested&&_vm.IsGameRunning){e.Cancel=true;MinimizeToTray();return;}
        if(_vm.Busy){e.Cancel=true;_vm.Log("当前操作仍在进行，不能在文件写入/启动过程中关闭窗口。");return;}
    }
    private void ExitFromTray(){_exitRequested=true;try{Close();}finally{_exitRequested=false;}}
    private void MinimizeToTray()
    {
        try
        {
            if(_tray is null)
            {
                using var stream=Application.GetResourceStream(new Uri("pack://application:,,,/WuWaFpsUnlock;component/Assets/App.ico")).Stream;
                _trayIcon=new System.Drawing.Icon(stream);
                _tray=new System.Windows.Forms.NotifyIcon{Icon=_trayIcon,Text="鸣潮 FPS Unlock v0.1"};
                _tray.DoubleClick+=(_,_)=>Dispatcher.Invoke(RestoreFromTray);
                var menu=new System.Windows.Forms.ContextMenuStrip();
                menu.Items.Add("显示启动器",null,(_,_)=>Dispatcher.Invoke(RestoreFromTray));
                menu.Items.Add("退出启动器",null,(_,_)=>Dispatcher.Invoke(ExitFromTray));
                _tray.ContextMenuStrip=menu;
            }
            _tray.Visible=true;_settings?.Hide();WindowState=WindowState.Minimized;Hide();
            _vm.Log("启动器已收起到通知区；双击头像可恢复，右键菜单可退出。折叠位置由Windows控制。");
        }
        catch(Exception e){_vm.Log("收起到通知区失败，保留窗口："+e.Message);Show();WindowState=WindowState.Normal;}
    }
    private void RestoreFromTray(){Show();WindowState=WindowState.Normal;Activate();if(_tray is not null)_tray.Visible=false;}
    public void RestoreExistingInstance()=>RestoreFromTray();
    private void Minimize_Click(object sender,RoutedEventArgs e)=>SystemCommands.MinimizeWindow(this);
    private void Maximize_Click(object sender,RoutedEventArgs e){if(WindowState==WindowState.Maximized)SystemCommands.RestoreWindow(this);else SystemCommands.MaximizeWindow(this);}
    private void Close_Click(object sender,RoutedEventArgs e)=>Close();
}

