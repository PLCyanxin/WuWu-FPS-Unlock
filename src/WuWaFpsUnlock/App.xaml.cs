using System.Windows;
using WuWaFpsUnlock.Services;
using WuWaFpsUnlock.ViewModels;
namespace WuWaFpsUnlock;
public partial class App:Application
{
    private Mutex? _singleInstance;
    private async void OnStartup(object sender,StartupEventArgs e)
    {
        DispatcherUnhandledException+=(_,args)=>{MessageBox.Show(args.Exception.Message,"鸣潮 FPS Unlock：未处理错误",MessageBoxButton.OK,MessageBoxImage.Error);args.Handled=true;};
        if(e.Args.Length==2&&e.Args[0]=="--worker")
        {
            try{Shutdown(await ElevatedWorker.Execute(e.Args[1]));}catch{Shutdown(1);}return;
        }
        _singleInstance=new Mutex(true,@"Local\WuWaFPSUnlock_0_9",out bool created);
        if(!created){MessageBox.Show("鸣潮 FPS Unlock 已经运行，请使用现有窗口。","提示");Shutdown();return;}
        try{var vm=new AppViewModel();MainWindow=new MainWindow(vm);MainWindow.Show();}
        catch(Exception ex){MessageBox.Show("无法启动："+ex.Message+"\n请将整个工具解压到可写目录，例如 E:\\yanxin_ws\\wuwa-fps-unlock。","启动失败",MessageBoxButton.OK,MessageBoxImage.Error);Shutdown(1);}
    }
    protected override void OnExit(ExitEventArgs e){_singleInstance?.Dispose();base.OnExit(e);}
}
