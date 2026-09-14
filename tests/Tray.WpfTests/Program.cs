using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using WuWaFpsUnlock;
using WuWaFpsUnlock.ViewModels;
internal static class Program
{
    [STAThread]static int Main(string[] args)
    {
        if(args.Length==2&&args[0]=="--activate-test")return WuWaFpsUnlock.Services.SingleInstanceActivation.RequestAsync(args[1]).GetAwaiter().GetResult()?0:1;
        int passed=0;var app=new App();app.InitializeComponent();
        var startup=typeof(App).GetMethod("OnStartup",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)!;
        app.Startup-=(StartupEventHandler)Delegate.CreateDelegate(typeof(StartupEventHandler),app,startup);
        var vm=new AppViewModel();var window=new MainWindow(vm);app.MainWindow=window;
        Console.WriteLine("Native WPF/NotifyIcon lifecycle only. Game-ready event simulated; no game, FPS DLL, termination, UAC or installation.");
        var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};
        timer.Tick+=async(_,_)=>
        {
            timer.Stop();
            try
            {
                void Check(bool ok,string name){if(!ok)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
                Check(window.IsVisible,"real native window shown before readiness");
                var ready=(Action?)typeof(AppViewModel).GetField("GameReady",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(vm);
                ready!();
                var tray=(System.Windows.Forms.NotifyIcon?)typeof(MainWindow).GetField("_tray",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window);
                Check(!window.IsVisible&&tray?.Visible==true,"game-ready event hides window and exposes notification icon");
                Check(tray?.Icon is not null&&tray.Text=="鸣潮 FPS Unlock v0.1","original-color icon and version assigned");
                typeof(MainWindow).GetMethod("RestoreFromTray",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
                Check(window.IsVisible&&window.WindowState==WindowState.Normal&&tray!.Visible==false,"restore returns window without starting a process");
                bool closed=false;window.Closed+=(_,_)=>closed=true;
                window.Close();
                Check(!closed&&!window.IsVisible&&tray!.Visible,"close button hides to tray without closing application");
                var channel="WuWaActivationTest_"+Guid.NewGuid().ToString("N");
                var restored=new TaskCompletionSource<bool>();
                using var activation=new WuWaFpsUnlock.Services.SingleInstanceActivation(()=>window.Dispatcher.BeginInvoke(new Action(()=>{window.RestoreExistingInstance();restored.TrySetResult(true);})),channel);
                var childInfo=new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};
                if(string.Equals(System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath),"dotnet",StringComparison.OrdinalIgnoreCase))childInfo.ArgumentList.Add(typeof(Program).Assembly.Location);
                childInfo.ArgumentList.Add("--activate-test");childInfo.ArgumentList.Add(channel);
                using var child=System.Diagnostics.Process.Start(childInfo)!;
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
                Check(child.ExitCode==0,"separate second process delivered IPC restore request and exited");
                await restored.Task.WaitAsync(TimeSpan.FromSeconds(3));
                Check(window.IsVisible&&!tray!.Visible&&!closed,"activation channel restores same tray window without game launch");
                typeof(MainWindow).GetMethod("RestoreFromTray",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
                Check(window.IsVisible&&!closed,"window restores after close-to-tray");
                typeof(MainWindow).GetMethod("ExitFromTray",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
                Check(closed,"explicit tray exit really closes window");
                Console.WriteLine($"TOTAL passed={passed} failed=0; notification lifecycle, not real-game readiness acceptance");app.Shutdown(0);
            }
            catch(Exception e){Console.WriteLine(e);app.Shutdown(1);}
        };
        window.Show();timer.Start();return app.Run();
    }
}


