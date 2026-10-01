using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WuWaFpsUnlock.Core;
using WuWaFpsUnlock.ViewModels;
namespace WuWaFpsUnlock;
public partial class SettingsWindow:Window
{
    private bool _capturingMenuKey;
    public SettingsWindow(AppViewModel vm)
    {
        InitializeComponent();DataContext=vm;
        Width=Math.Min(Width,SystemParameters.WorkArea.Width-36);Height=Math.Min(Height,SystemParameters.WorkArea.Height-36);
        Closing+=(_,e)=>{if(vm.Busy){e.Cancel=true;vm.Log("当前操作未结束，设置窗口暂不能关闭。");}};
    }
    private void LogChanged(object sender,TextChangedEventArgs e){if(sender is TextBox text)text.ScrollToEnd();}
    private void Minimize_Click(object sender,RoutedEventArgs e)=>SystemCommands.MinimizeWindow(this);
    private void Maximize_Click(object sender,RoutedEventArgs e){if(WindowState==WindowState.Maximized)SystemCommands.RestoreWindow(this);else SystemCommands.MaximizeWindow(this);}
    private void Close_Click(object sender,RoutedEventArgs e)=>Close();
    private void MenuKey_Click(object sender,RoutedEventArgs e)
    {
        if(DataContext is not AppViewModel {CanEditSettings:true})return;
        _capturingMenuKey=true;
        MenuKeyButton.SetCurrentValue(Button.ContentProperty,"请按按键…");
        MenuKeyButton.Focus();
    }
    private void EndMenuKeyCapture()
    {
        _capturingMenuKey=false;
        MenuKeyButton.GetBindingExpression(Button.ContentProperty)?.UpdateTarget();
    }
    private void MenuKey_LostKeyboardFocus(object sender,KeyboardFocusChangedEventArgs e)=>EndMenuKeyCapture();
    private void ClearMenuKey_Click(object sender,RoutedEventArgs e)
    {
        EndMenuKeyCapture();
        if(DataContext is AppViewModel vm)vm.SetMenuShortcut(MenuShortcut.None);
    }
    private void MenuKey_PreviewKeyDown(object sender,KeyEventArgs e)
    {
        if(!_capturingMenuKey)return;
        e.Handled=true;
        if(e.IsRepeat)return;
        var key=e.Key==Key.System?e.SystemKey:e.Key;
        var modifiers=Keyboard.Modifiers;
        if(key==Key.Escape&&modifiers==ModifierKeys.None){EndMenuKeyCapture();return;}
        if(key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift)return;
        var shortcut=new MenuShortcut(KeyInterop.VirtualKeyFromKey(key),modifiers.HasFlag(ModifierKeys.Control),modifiers.HasFlag(ModifierKeys.Shift),modifiers.HasFlag(ModifierKeys.Alt));
        if(modifiers.HasFlag(ModifierKeys.Windows)||!shortcut.IsValid){MenuKeyButton.SetCurrentValue(Button.ContentProperty,"请换一个按键…");return;}
        if(DataContext is AppViewModel vm)vm.SetMenuShortcut(shortcut);
        EndMenuKeyCapture();
    }
}
