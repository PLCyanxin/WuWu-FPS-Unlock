using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WuWaFpsUnlock;
using WuWaFpsUnlock.ViewModels;

namespace UiRendering;
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        string output=Path.GetFullPath(args.Length==1?args[0]:"artifacts/native-offscreen");Directory.CreateDirectory(output);
        using var log=new StreamWriter(Path.Combine(output,"offscreen-results.log")){AutoFlush=true};
        void Write(string line){Console.WriteLine(line);log.WriteLine(line);}
        int passed=0,failed=0;
        void Test(string name,Action action){try{action();passed++;Write("PASS "+name);}catch(Exception e){failed++;Write("FAIL "+name+": "+e);}}
        void Check(bool condition,string reason){if(!condition)throw new InvalidOperationException(reason);}
        Write("NATIVE WPF OFFSCREEN COMPONENT TEST. No Show, Application.Run, desktop automation, game, unlocker or installer execution.");
        Write("Raster scales 100/150/200% are RenderTargetBitmap DPI, not a claim of actual monitor DPI or desktop interaction.");
        try
        {
            // Load exact production resources, then detach the source-inspected Startup handler before any dispatcher flush.
            var app=new App();app.InitializeComponent();
            var startup=typeof(App).GetMethod("OnStartup",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.DeclaredOnly)!;
            app.Startup-=(StartupEventHandler)Delegate.CreateDelegate(typeof(StartupEventHandler),app,startup);
            RenderOptions.ProcessRenderMode=System.Windows.Interop.RenderMode.SoftwareOnly;
            var vm=new AppViewModel();vm.FpsEnabled=true;vm.TargetFps=240;
            var main=new MainWindow(vm);var settings=new SettingsWindow(vm);
            // Use approved design DIPs, independently from the locked desktop's current work area.
            main.Width=490;main.Height=370;settings.Width=1160;settings.Height=806;
            FrameworkElement MainRoot()=> (FrameworkElement)main.Content;
            FrameworkElement SettingsRoot()=> (FrameworkElement)settings.Content;
            void Layout(Window window)
            {
                var content=(FrameworkElement)window.Content;
                content.Measure(new Size(window.Width,window.Height));content.Arrange(new Rect(0,0,window.Width,window.Height));content.UpdateLayout();
                foreach(var element in Tree(content).OfType<FrameworkElement>())
                {
                    if(element is TextBox box){box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();box.GetBindingExpression(UIElement.IsEnabledProperty)?.UpdateTarget();}
                    if(element is Button button){button.GetBindingExpression(Button.CommandProperty)?.UpdateTarget();button.GetBindingExpression(UIElement.IsEnabledProperty)?.UpdateTarget();}
                    if(element is Slider slider)slider.GetBindingExpression(Slider.ValueProperty)?.UpdateTarget();
                    if(element is TextBlock block)block.GetBindingExpression(TextBlock.TextProperty)?.UpdateTarget();
                }
                content.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ContextIdle);
                content.UpdateLayout();
            }
            Layout(main);Layout(settings);
            TextBox Fps(FrameworkElement root)=>Tree(root).OfType<TextBox>().Single(x=>AutomationProperties.GetName(x)=="目标帧率");
            Button Command(FrameworkElement root,object command)=>Tree(root).OfType<Button>().Single(x=>ReferenceEquals(x.Command,command));
            Test("real MainWindow and SettingsWindow share production VM",()=>Check(ReferenceEquals(main.DataContext,settings.DataContext)&&ReferenceEquals(main.DataContext,vm),"VM mismatch"));
            Test("FPS binding changes from main input to settings input",()=>{
                var input=Fps(MainRoot());input.SetCurrentValue(TextBox.TextProperty,"321");input.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();Layout(main);Layout(settings);
                Check(vm.TargetFps==321&&Fps(SettingsRoot()).Text=="321","main->settings binding did not propagate");
            });
            Test("FPS binding changes from settings input to main input",()=>{
                var input=Fps(SettingsRoot());input.SetCurrentValue(TextBox.TextProperty,"288");input.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();Layout(settings);Layout(main);
                Check(vm.TargetFps==288&&Fps(MainRoot()).Text=="288","settings->main binding did not propagate");
            });
            Test("invalid FPS text disables actual start button and preserves target",()=>{
                int prior=vm.TargetFps;var input=Fps(MainRoot());input.SetCurrentValue(TextBox.TextProperty,"invalid");input.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();Layout(main);Layout(settings);
                Check(vm.TargetFps==prior&&!vm.StartCommand.CanExecute(null)&&!Command(MainRoot(),vm.StartCommand).IsEnabled,"invalid FPS did not disable start");
                Check(Fps(SettingsRoot()).Text=="invalid","invalid shared input not visible in settings");
                input.SetCurrentValue(TextBox.TextProperty,"240");input.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();Layout(main);Layout(settings);
                Check(vm.StartCommand.CanExecute(null)&&Command(MainRoot(),vm.StartCommand).IsEnabled,"valid FPS did not recover start command");
            });
            Test("running game keeps restart enabled and discovery disabled",()=>{
                typeof(AppViewModel).GetField("_running",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(vm,true);
                Check(vm.StartCommand.CanExecute(null),"running game disabled restart");
                Check(!vm.FindGameCommand.CanExecute(null),"running game enabled discovery");
                typeof(AppViewModel).GetField("_busy",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(vm,true);
                Check(!vm.StartCommand.CanExecute(null),"busy restart was enabled");
                typeof(AppViewModel).GetField("_busy",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(vm,false);
                typeof(AppViewModel).GetField("_running",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(vm,false);
            });
            Test("auto discovery button exists without removing manual choices",()=>{
                Check(Command(SettingsRoot(),vm.FindGameCommand)!=null&&Command(SettingsRoot(),vm.BrowseDirectoryCommand)!=null&&Command(SettingsRoot(),vm.BrowseExeCommand)!=null,"path controls missing");
            });
            Test("external unlocker runtime route is absent from built assemblies",()=>{
                Check(typeof(App).Assembly.GetType("WuWaFpsUnlock.Services.ExternalUnlockerService")==null,"external service compiled");
                Check(typeof(App).Assembly.GetType("WuWaFpsUnlock.Services.ExternalLaunchWorker")==null,"external worker compiled");
                Check(typeof(WuWaFpsUnlock.Core.UserSettings).Assembly.GetType("WuWaFpsUnlock.Core.LaunchPlanBuilder")==null,"legacy launch plan compiled");
                Check(typeof(App).Assembly.GetType("WuWaFpsUnlock.Services.FpsSession")!=null,"builtin core session missing");
            });
            Test("all own assemblies use 0.1.0.0",()=>{
                Check(typeof(App).Assembly.GetName().Version==new Version(0,1,0,0),"WPF version");
                Check(typeof(WuWaFpsUnlock.Core.UserSettings).Assembly.GetName().Version==new Version(0,1,0,0),"Core version");
            });
            Test("FPS disabled disables both native FPS editors",()=>{
                vm.FpsEnabled=false;Layout(main);Layout(settings);Check(!Fps(MainRoot()).IsEnabled&&!Fps(SettingsRoot()).IsEnabled,"disabled FPS editors remain enabled");
                vm.FpsEnabled=true;Layout(main);Layout(settings);
            });
            Test("deployment and clean buttons are equal and fill same grid row",()=>{
                var deploy=Command(SettingsRoot(),vm.DeployCommand);var clean=Command(SettingsRoot(),vm.CleanCommand);
                Check(deploy.Parent is Grid&&ReferenceEquals(deploy.Parent,clean.Parent),"buttons do not share grid");var parent=(Grid)deploy.Parent;
                Check(Math.Abs(deploy.ActualWidth-clean.ActualWidth)<0.6,"unequal widths: "+deploy.ActualWidth+" / "+clean.ActualWidth);
                Check(Math.Abs(deploy.ActualWidth+clean.ActualWidth+12-parent.ActualWidth)<1,"buttons do not fill grid width");
                Write($"METRIC deploy={deploy.ActualWidth:F2} clean={clean.ActualWidth:F2} row={parent.ActualWidth:F2}");
            });
            string longPath=@"E:\离屏测试（不是实际游戏目录）\"+string.Join("\\",Enumerable.Repeat("鸣潮 很长的路径",14));
            Test("long Chinese paths remain bounded with complete tooltip",()=>{
                vm.GameRoot=longPath;vm.GameExe=longPath+@"\Client\Binaries\Win64\Client-Win64-Shipping.exe";Layout(settings);
                var fields=Tree(SettingsRoot()).OfType<TextBox>().Where(x=>x.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path?.Path is "GameRoot" or "GameExe").ToArray();
                Check(fields.Length==2,"missing path controls");foreach(var field in fields){Check(field.ActualWidth>40&&field.ActualWidth<settings.Width/2,"long path expanded grid");Check(field.ToolTip?.ToString()==field.Text,"full path tooltip missing");}
            });
            Test("native text viewports retain a full line, no double padding clipping",()=>{
                foreach(var root in new[]{MainRoot(),SettingsRoot()})
                foreach(var field in Tree(root).OfType<TextBox>().Where(x=>!x.IsReadOnly))
                {
                    var view=Tree(field).OfType<FrameworkElement>().Single(x=>x.GetType().Name=="TextBoxView");
                    Check(view.ActualHeight>=field.FontSize*1.15,$"text viewport {view.ActualHeight:F2} below one line for {field.FontSize:F2}pt font");
                    Write($"TEXT VIEWPORT font={field.FontSize:F2} height={view.ActualHeight:F2} width={view.ActualWidth:F2}");
                }
            });
            Render(settings,1,"settings-long-path-fixture");
            vm.GameRoot="";vm.GameExe="";vm.TargetFps=240;vm.FpsEnabled=true;
            vm.CloseAsync().GetAwaiter().GetResult();
            vm=new AppViewModel();main=new MainWindow(vm);settings=new SettingsWindow(vm);
            main.Width=490;main.Height=370;settings.Width=1160;settings.Height=806;
            Test("default render reads actual local environment with empty game paths",()=>{
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                Task refresh=vm.RefreshAsync();var watch=System.Diagnostics.Stopwatch.StartNew();
                while(!refresh.IsCompleted)
                {
                    if(watch.Elapsed>TimeSpan.FromSeconds(20))throw new TimeoutException("actual environment read timed out");
                    Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ContextIdle);Thread.Sleep(10);
                }
                refresh.GetAwaiter().GetResult();Layout(main);Layout(settings);
                Check(vm.GameRoot==""&&vm.GameExe==""&&vm.TargetFps==240,"default screenshot contains fixture path or FPS");
                Write($"ACTUAL ENV GPU={vm.Gpu}; DRIVER={vm.Driver}; OS={vm.Os}; HAGS={vm.Hags}; DYNAMIC={vm.DynamicStatus}");
            });
            foreach(double scale in new[]{1.0,1.5,2.0})
            {
                Test($"main native offscreen {scale*100:0}%",()=>Render(main,scale,"main"));
                Test($"settings native offscreen {scale*100:0}%",()=>Render(settings,scale,"settings"));
            }
            void Render(Window window,double scale,string name)
            {
                Layout(window);var root=(FrameworkElement)window.Content;
                int width=(int)Math.Round(window.Width*scale),height=(int)Math.Round(window.Height*scale);
                var bitmap=new RenderTargetBitmap(width,height,96*scale,96*scale,PixelFormats.Pbgra32);
                var background=new DrawingVisual();using(var drawing=background.RenderOpen())drawing.DrawRectangle(window.Background,null,new Rect(0,0,window.Width,window.Height));
                bitmap.Render(background);bitmap.Render(root);
                string file=Path.Combine(output,$"{name}-offscreen-{scale*100:0}pct.png");var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(file))encoder.Save(stream);
                Check(new FileInfo(file).Length>10000,"render unexpectedly blank/tiny");Check(bitmap.PixelWidth==width&&bitmap.PixelHeight==height,"wrong pixel size");
                Write($"RENDER {file} {width}x{height}, {96*scale:0}dpi");
            }
            vm.CloseAsync().GetAwaiter().GetResult();
            Write("Window instances were never shown. No desktop screenshot/monitor-DPI/single-instance activation acceptance claimed.");
        }
        catch(Exception e){failed++;Write("FATAL "+e);}
        Write($"TOTAL passed={passed} failed={failed}");return failed==0?0:1;
    }
    private static IEnumerable<DependencyObject> Tree(DependencyObject root)
    {
        yield return root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Tree(VisualTreeHelper.GetChild(root,i)))yield return child;
    }
}








