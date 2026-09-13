using System.Windows;
using System.Windows.Controls;
using WuWaFpsUnlock.ViewModels;
namespace WuWaFpsUnlock;
public partial class SettingsWindow:Window
{
    public SettingsWindow(AppViewModel vm)
    {
        InitializeComponent();DataContext=vm;
        Width=Math.Min(Width,SystemParameters.WorkArea.Width-36);Height=Math.Min(Height,SystemParameters.WorkArea.Height-36);
        Closing+=(_,e)=>{if(vm.Busy){e.Cancel=true;vm.Log("部署操作未结束，设置窗口暂不能关闭。");}};
    }
    private void LogChanged(object sender,TextChangedEventArgs e){if(sender is TextBox text)text.ScrollToEnd();}
    private void Minimize_Click(object sender,RoutedEventArgs e)=>SystemCommands.MinimizeWindow(this);
    private void Maximize_Click(object sender,RoutedEventArgs e){if(WindowState==WindowState.Maximized)SystemCommands.RestoreWindow(this);else SystemCommands.MaximizeWindow(this);}
    private void Close_Click(object sender,RoutedEventArgs e)=>Close();
}
