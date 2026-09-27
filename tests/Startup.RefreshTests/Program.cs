using System.Reflection;
using System.Windows;
using WuWaFpsUnlock.Core;
using WuWaFpsUnlock.ViewModels;
class Program
{
 [STAThread] static int Main()
 {
  var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};int exit=0,count=0;
  app.Dispatcher.BeginInvoke(new Action(async()=>{
   using var gate=new ManualResetEventSlim();int probes=0;
   var vm=new AppViewModel(probeEnvironment:_=>{Interlocked.Increment(ref probes);gate.Wait();return HardwareInfo.Unknown;});
   void Check(bool condition,string name){if(!condition)throw new Exception(name);Console.WriteLine("PASS "+name);count++;}
   void Busy(bool value)=>typeof(AppViewModel).GetProperty("Busy")!.SetValue(vm,value);
   try {
    var settingsBanner=new WuWaFpsUnlock.Controls.HeroBanner();
    Check(settingsBanner.ShowVersion,"settings banner retains version by default");
    var mainBanner=new WuWaFpsUnlock.Controls.HeroBanner {ShowVersion=false};
    Check(!mainBanner.ShowVersion,"main banner can hide version without changing settings");
    var refresh=vm.RefreshAsync();
    Check(!vm.Busy,"diagnostic does not reserve operation lock");
    Check(vm.StartLabel=="开始游戏","startup keeps normal start label");
    Check(vm.StartCommand.CanExecute(null),"start remains available during environment probe");
    Check(vm.OpenSettingsCommand.CanExecute(null),"settings remain available during probe");
    Check(!vm.RefreshCommand.CanExecute(null),"duplicate refresh command disabled");
    await vm.RefreshAsync();
    Busy(true);
    Check(!vm.StartCommand.CanExecute(null)&&!vm.DeployCommand.CanExecute(null)&&!vm.CleanCommand.CanExecute(null),"operation lock still guards start deploy clean");
    Check(vm.StartLabel!="正在启动…","nonlaunch operation never reports launching");
    typeof(AppViewModel).GetField("_starting",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(vm,true);
    Check(vm.StartLabel=="正在启动…","launch phase reports starting");
    gate.Set();await refresh;
    Check(vm.Busy,"passive refresh never clears another operation lock");
    Check(probes==1,"overlapping passive refresh coalesced");
    Busy(false);
    Check(vm.RefreshCommand.CanExecute(null),"refresh reenabled after completion");
    Console.WriteLine($"{count}/{count} passed");
   }catch(Exception e){Console.Error.WriteLine(e);exit=1;}
   finally{gate.Set();app.Shutdown();}
  }));
  app.Run();return exit;
 }
}
