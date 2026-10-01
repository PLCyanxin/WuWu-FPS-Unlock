using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using WuWaFpsUnlock;
using WuWaFpsUnlock.Core;
using WuWaFpsUnlock.Services;
using WuWaFpsUnlock.ViewModels;
class Program
{
 [STAThread] static int Main()
 {
  var app=new App();app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
  var startup=typeof(App).GetMethod("OnStartup",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)!;
  app.Startup-=(StartupEventHandler)Delegate.CreateDelegate(typeof(StartupEventHandler),app,startup);
  int result=0,checks=0;
  app.Dispatcher.BeginInvoke(new Action(async()=>{
   var root=Path.Combine(Path.GetTempPath(),"ww-tier-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
   var vm=new AppViewModel(probeEnvironment:_=>HardwareInfo.Unknown,probeHags:(h,_)=>h);
   try{
    void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
    typeof(AppViewModel).GetField("_settings",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(vm,new UserSettings{GameRoot=root,ResourceTier="hd"});
    var window=new MainWindow(vm);window.Measure(new Size(490,370));window.Arrange(new Rect(0,0,490,370));
    var combo=(ComboBox)window.FindName("ResourceTierSelector");
    Check(combo.Items.Count==3,"three bundle choices; no automatic option");Check(vm.ResourceTierIndex==1,"old explicit hd stays selected");
    vm.ResourceTierIndex=2;Check(((UserSettings)typeof(AppViewModel).GetField("_settings",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(vm)!).ResourceTier=="sd","choice maps to explicit sd argument");
    vm.ObserveResourceTiers(true);Directory.CreateDirectory(Path.Combine(root,"launcherDownload","uhd"));
    File.WriteAllText(Path.Combine(root,"launcherDownload","uhd","resource.tmp"),"fixture");await Task.Delay(500);await vm.RefreshResourceTiersAsync();
    Check(vm.UhdTier.Status==TierDownloadStatus.Downloading,"native filesystem activity produces yellow state");
    vm.ObserveResourceTiers(false);await vm.RefreshResourceTiersAsync();Check(vm.UhdTier.Status==TierDownloadStatus.Unknown,"closing menu releases observer and activity");
    window.Close();Console.WriteLine($"{checks} passed; no windows shown, no game or official launcher started");
   }catch(Exception e){Console.Error.WriteLine(e);result=1;}finally{vm.ObserveResourceTiers(false);await vm.CloseAsync();Directory.Delete(root,true);app.Shutdown();}
  }));app.Run();return result;
 }
}
