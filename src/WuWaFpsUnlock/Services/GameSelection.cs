using System.Windows;
using System.Windows.Controls;
using WuWaFpsUnlock.Core;
namespace WuWaFpsUnlock.Services;
public static class GameSelection
{
    public static GameDiscoveryCandidate? Show(IReadOnlyList<GameDiscoveryCandidate> candidates)
    {
        var list=new ListBox{ItemsSource=candidates,DisplayMemberPath=nameof(GameDiscoveryCandidate.GameRoot),Margin=new Thickness(12)};
        var choose=new Button{Content="使用所选安装",Margin=new Thickness(12),MinHeight=36,IsDefault=true};
        var panel=new DockPanel();DockPanel.SetDock(choose,Dock.Bottom);panel.Children.Add(choose);panel.Children.Add(list);
        var dialog=new Window{Title="选择找到的鸣潮安装",Width=720,Height=340,Owner=Application.Current.MainWindow,Content=panel,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        choose.Click+=(_,_)=>{if(list.SelectedItem is not null)dialog.DialogResult=true;};
        return dialog.ShowDialog()==true?list.SelectedItem as GameDiscoveryCandidate:null;
    }
}
