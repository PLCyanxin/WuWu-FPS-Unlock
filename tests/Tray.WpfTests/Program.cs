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
                var fixture=System.IO.Path.Combine(AppContext.BaseDirectory,"path-fixture",Guid.NewGuid().ToString("N"));
                var shipping=System.IO.Path.Combine(fixture,"Client","Binaries","Win64","Client-Win64-Shipping.exe");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(shipping)!);
                var pe=new byte[128];BitConverter.GetBytes((ushort)0x5a4d).CopyTo(pe,0);BitConverter.GetBytes(64).CopyTo(pe,0x3c);BitConverter.GetBytes(0x4550).CopyTo(pe,64);BitConverter.GetBytes((ushort)0x8664).CopyTo(pe,68);BitConverter.GetBytes((ushort)0x0022).CopyTo(pe,86);
                System.IO.File.WriteAllBytes(shipping,pe);System.IO.File.WriteAllText(System.IO.Path.Combine(fixture,"Wuthering Waves.exe"),"fixture only");
                int searches=0;
                var pathVm=new AppViewModel((root,hints,token)=>{searches++;return Task.FromResult(new WuWaFpsUnlock.Core.GameDiscoveryResult([new(fixture,shipping,["fixture"])],[]));});
                pathVm.GameRoot=fixture;pathVm.GameExe=shipping;
                await Task.Delay(1000);
                Check(searches==0,"valid current pair skips discovery after edit debounce");
                pathVm.GameRoot="C:\\invalid-cinebench";pathVm.GameExe="C:\\invalid-cinebench\\Other.exe";
                await Task.Delay(1200);
                Check(searches==1&&pathVm.GameRoot==fixture&&pathVm.GameExe==shipping,"invalid edited pair automatically searches and replaces both fields");
                pathVm.GameRoot="";pathVm.GameExe="";
                await Task.Delay(1200);
                Check(searches==2&&pathVm.GameRoot==fixture&&pathVm.GameExe==shipping,"cleared pair automatically searches and restores both fields");
                await Task.Delay(1000);
                Check(searches==2,"applying discovery does not cause another search loop");
                var pathWindow=new SettingsWindow(pathVm);pathWindow.Show();pathWindow.UpdateLayout();
                System.Windows.Controls.Button? FindButton(System.Windows.DependencyObject node){
                    if(node is System.Windows.Controls.Button button&&ReferenceEquals(button.Command,pathVm.FindGameCommand))return button;
                    for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);i++){var match=FindButton(System.Windows.Media.VisualTreeHelper.GetChild(node,i));if(match is not null)return match;}return null;
                }
                var findButton=FindButton(pathWindow)!;
                var peer=new System.Windows.Automation.Peers.ButtonAutomationPeer(findButton);
                ((System.Windows.Automation.Provider.IInvokeProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)).Invoke();
                await Task.Delay(200);
                Check(searches==2&&pathVm.Logs.Contains("当前鸣潮路径有效，已复用")&&!pathVm.Busy,"native find button reports valid reuse immediately without searching");
                pathWindow.Close();
                await pathVm.CloseAsync();
                Check(window.IsVisible,"real native window shown before readiness");
                typeof(AppViewModel).GetProperty("IsGameRunning")!.SetValue(vm,true);
                Check(vm.StartLabel=="游戏中","running game changes start label");
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
                typeof(AppViewModel).GetProperty("IsGameRunning")!.SetValue(vm,false);
                Check(vm.StartLabel=="开始游戏","game exit restores start label");
                var pickerTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(500)};
                pickerTimer.Tick+=(_,_)=>{
                    pickerTimer.Stop();
                    var picker=app.Windows.OfType<Window>().First(w=>w.Title=="选择鸣潮游戏 EXE");
                    var rightClick=new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount,System.Windows.Input.MouseButton.Right){RoutedEvent=System.Windows.Input.Mouse.PreviewMouseDownEvent};
                    picker.RaiseEvent(rightClick);
                    Check(rightClick.Handled&&picker.IsVisible,"managed EXE picker handles right click without shell menu or crash");
                    picker.Close();
                };
                pickerTimer.Start();
                Check(WuWaFpsUnlock.Services.GameExecutablePicker.Show(fixture)==null,"cancel managed EXE picker leaves selection unchanged");
                var notice=WuWaFpsUnlock.Services.OperationReview.CreateWindow("首次启动风险与须知","第三方组件可能存在兼容性问题、崩溃及账号风险；无法保证所有游戏版本兼容。\n\n开始游戏会直接结束同一安装的现有游戏并重新启动，可能中断当前操作或丢失尚未保存的状态。\n\n目标FPS不是实际帧率保证。\n\n清除DLL后可能需要官方文件校验；普通启动自动补齐尚未取得独立成功证据。\n\n确认后保存本次部署须知；后续日常启动不再提示。",true);
                notice.Show();notice.UpdateLayout();
                Check(notice.ActualHeight<500&&notice.SizeToContent==SizeToContent.Height,"first notice sizes to actual text without fixed large blank area");
                notice.Close();window.Close();
                Check(closed,"close without running game really exits window");
                Console.WriteLine($"TOTAL passed={passed} failed=0; notification lifecycle, not real-game readiness acceptance");app.Shutdown(0);
            }
            catch(Exception e){Console.WriteLine(e);app.Shutdown(1);}
        };
        window.Show();timer.Start();return app.Run();
    }
}


