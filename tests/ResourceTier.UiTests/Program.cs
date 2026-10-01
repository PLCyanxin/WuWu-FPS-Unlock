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
    Directory.CreateDirectory(Path.Combine(root,"launcherDownload","uhd"));
    File.WriteAllText(Path.Combine(root,"launcherDownload","uhd","startup.tmp"),"already downloading at startup");
    await vm.RefreshResourceTiersAsync();
    Check(vm.UhdTier.Status==TierDownloadStatus.Downloading,"startup detects existing recent tier download writes");
    var window=new MainWindow(vm);window.Measure(new Size(490,370));window.Arrange(new Rect(0,0,490,370));
    var combo=(ComboBox)window.FindName("ResourceTierSelector");
    combo.ApplyTemplate();
    var popup=(System.Windows.Controls.Primitives.Popup)combo.Template.FindName("PART_Popup",combo);
    var menu=(Border)popup.Child;
    Check(menu.Child is ItemsPresenter&&double.IsNaN(menu.Width),"menu fits content and has no scroll viewer");
    Check(combo.Items.Count==3,"three bundle choices; no automatic option");Check(vm.ResourceTierIndex==1,"old explicit hd stays selected");
    vm.ResourceTierIndex=2;Check(((UserSettings)typeof(AppViewModel).GetField("_settings",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(vm)!).ResourceTier=="sd","choice maps to explicit sd argument");
    vm.ObserveResourceTiers(true);Directory.CreateDirectory(Path.Combine(root,"launcherDownload","uhd"));
    File.WriteAllText(Path.Combine(root,"launcherDownload","uhd","resource.tmp"),"fixture");
    for(int i=0;i<15&&vm.UhdTier.Status!=TierDownloadStatus.Downloading;i++)await Task.Delay(50);
    Check(vm.UhdTier.Status==TierDownloadStatus.Downloading,"filesystem activity updates yellow state without waiting for periodic refresh");
    int unchanged=0;vm.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(vm.UhdTier))unchanged++;};
    for(int i=0;i<10;i++)File.AppendAllText(Path.Combine(root,"launcherDownload","uhd","resource.tmp"),"x");
    await Task.Delay(200);await vm.RefreshResourceTiersAsync();Check(unchanged==0,"unchanged activity avoids repeated UI notifications");
    vm.ObserveResourceTiers(false);
    Directory.CreateDirectory(Path.Combine(root,"launcherDownload","hd"));
    File.WriteAllText(Path.Combine(root,"launcherDownload","hd","resource.tmp"),"active while menu is closed");
    await Task.Delay(200);
    Check(vm.HdTier.Status!=TierDownloadStatus.Downloading,"hidden menu activity causes no UI refresh");
    vm.ObserveResourceTiers(true);
    Check(vm.HdTier.Status==TierDownloadStatus.Downloading,"opening menu immediately shows previously observed download activity");
    vm.ObserveResourceTiers(false);
    Check(typeof(AppViewModel).GetField("_tierTimer",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(vm) is System.Windows.Threading.DispatcherTimer timer&&!timer.IsEnabled,"closed menu stops periodic metadata polling");
    window.Close();Console.WriteLine($"{checks} passed; no windows shown, no game or official launcher started");
   }catch(Exception e){Console.Error.WriteLine(e);result=1;}finally{vm.ObserveResourceTiers(false);await vm.CloseAsync();Directory.Delete(root,true);app.Shutdown();}
  }));app.Run();return result;
 }
}
