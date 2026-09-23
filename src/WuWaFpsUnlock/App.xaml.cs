using System.Windows;
using WuWaFpsUnlock.Services;
using WuWaFpsUnlock.ViewModels;
namespace WuWaFpsUnlock;
public partial class App:Application
{
    private Mutex? _singleInstance;
    private SingleInstanceActivation? _activation;
    private async void OnStartup(object sender,StartupEventArgs e)
    {
        DispatcherUnhandledException+=(_,args)=>{MessageBox.Show(args.Exception.Message,"鸣潮 FPS Unlock：未处理错误",MessageBoxButton.OK,MessageBoxImage.Error);args.Handled=true;};
        if(e.Args.Any(a=>a=="--external-launch-worker")){Shutdown(1);return;}
        if(e.Args.Length==2&&e.Args[0]=="--worker")
        {
            try{Shutdown(await ElevatedWorker.Execute(e.Args[1]));}catch{Shutdown(1);}return;
        }
        _singleInstance=new Mutex(true,@"Local\WuWaFPSUnlock_0_9",out bool created);
        if(!created){await SingleInstanceActivation.RequestAsync();if(e.Args.Contains("--update-completed"))MessageBox.Show("更新完成，请点击重新部署。\n已有启动器窗口正在运行，请打开更新后安装目录的启动器设置。","更新完成",MessageBoxButton.OK,MessageBoxImage.Information,MessageBoxResult.OK,MessageBoxOptions.DefaultDesktopOnly);Shutdown();return;}
        try{
            var vm=new AppViewModel();MainWindow=new MainWindow(vm);
            if(e.Args.Contains("--update-completed")) ((MainWindow)MainWindow).StartupCompleted=()=>
            {
                MainWindow.Activate();vm.OpenSettingsCommand.Execute(null);
                vm.Log("更新完成，请点击重新部署。");
                MessageBox.Show(MainWindow.OwnedWindows.OfType<SettingsWindow>().FirstOrDefault() ?? MainWindow,"更新完成，请点击重新部署。","更新完成",MessageBoxButton.OK,MessageBoxImage.Information);
            };
            _activation=new SingleInstanceActivation(()=>Dispatcher.BeginInvoke(new Action(()=>((MainWindow)MainWindow).RestoreExistingInstance())));
            MainWindow.Show();
        }
        catch(Exception ex){try{Directory.CreateDirectory(AppPaths.Data);File.WriteAllText(Path.Combine(AppPaths.Data,"startup-error.log"),ex.ToString());}catch{} MessageBox.Show("无法启动："+ex.GetBaseException().Message+"\n详细记录：data/startup-error.log","启动失败",MessageBoxButton.OK,MessageBoxImage.Error);Shutdown(1);}
    }
    protected override void OnExit(ExitEventArgs e){_activation?.Dispose();_singleInstance?.Dispose();base.OnExit(e);}
}
