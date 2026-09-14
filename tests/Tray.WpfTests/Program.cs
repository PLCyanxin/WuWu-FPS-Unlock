using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using WuWaFpsUnlock;
using WuWaFpsUnlock.ViewModels;
internal static class Program
{
    [STAThread]static int Main()
    {
        int passed=0;var app=new App();app.InitializeComponent();
        var startup=typeof(App).GetMethod("OnStartup",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)!;
        app.Startup-=(StartupEventHandler)Delegate.CreateDelegate(typeof(StartupEventHandler),app,startup);
        var vm=new AppViewModel();var window=new MainWindow(vm);app.MainWindow=window;
        Console.WriteLine("Native WPF/NotifyIcon lifecycle only. Game-ready event simulated; no game, FPS DLL, termination, UAC or installation.");
        var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};
        timer.Tick+=(_,_)=>
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

