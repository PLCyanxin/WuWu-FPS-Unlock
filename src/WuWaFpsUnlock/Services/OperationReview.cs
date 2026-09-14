using System.Windows;
using System.Windows.Controls;
namespace WuWaFpsUnlock.Services;

/// <summary>A scrollable confirmation for the exact file plan, shared by deploy and clean.</summary>
public static class OperationReview
{
    public static bool Show(string title, string details, bool fitContent=false)
    {
        return CreateWindow(title,details,fitContent).ShowDialog()==true;
    }
    public static Window CreateWindow(string title,string details,bool fitContent=false)
    {
        var owner=Application.Current.Windows.OfType<Window>().FirstOrDefault(w=>w.IsActive)??Application.Current.MainWindow;
        var window=new Window{Title=title,Width=850,Height=540,MinWidth=500,MinHeight=300,Owner=owner,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        if(fitContent){window.Width=Math.Min(620,SystemParameters.WorkArea.Width-32);window.MinWidth=0;window.MinHeight=0;window.SizeToContent=SizeToContent.Height;window.ResizeMode=ResizeMode.NoResize;window.MaxHeight=Math.Max(200,SystemParameters.WorkArea.Height-32);}
        var grid=new Grid{Margin=new Thickness(16)};
        grid.RowDefinitions.Add(new RowDefinition{Height=fitContent?GridLength.Auto:new GridLength(1,GridUnitType.Star)});
        grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        grid.Children.Add(new TextBox{Text=details,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalContentAlignment=VerticalAlignment.Top,Padding=new Thickness(12),MaxHeight=fitContent?Math.Max(100,SystemParameters.WorkArea.Height-150):double.PositiveInfinity});
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};
        var cancel=new Button{Content="取消",MinWidth=100,IsCancel=true,Margin=new Thickness(0,0,12,0)};
        var confirm=new Button{Content="确认执行",MinWidth=120};confirm.Click+=(_,_)=>window.DialogResult=true;
        buttons.Children.Add(cancel);buttons.Children.Add(confirm);Grid.SetRow(buttons,1);grid.Children.Add(buttons);
        window.Content=grid;return window;
    }
}
