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
   var vm=new AppViewModel(probeEnvironment:_=>{Interlocked.Increment(ref probes);gate.Wait();return HardwareInfo.Unknown;},probeHags:(basic,_)=>basic);
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
    using var hagsGate=new ManualResetEventSlim();
    var hagsEntered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var fast=new AppViewModel(probeEnvironment:_=>new HardwareInfo("fixture RTX",60000,true,"fixture OS","HAGS pending",true),
      probeHags:(basic,_)=>{hagsEntered.TrySetResult();hagsGate.Wait();return basic with {Hags="fixture runtime enabled",HagsConfigured=false};});
    try
    {
      var fastRefresh=fast.RefreshAsync();
      Check(await Task.WhenAny(fastRefresh,Task.Delay(3000))==fastRefresh,"refresh completes while HAGS is blocked");
      await fastRefresh;await hagsEntered.Task;
      Check(fast.Gpu=="fixture RTX"&&fast.Os=="fixture OS","basic hardware published before HAGS");
      Check(fast.Hags=="HAGS pending"&&!fast.Busy,"pending HAGS does not own operation lock");
      Check(fast.ReShadeStatus=="未设置游戏路径","ReShade inspection finishes before HAGS");
      hagsGate.Set();
      for(int i=0;i<100&&fast.Hags!="fixture runtime enabled";i++)await Task.Delay(10);
      Check(fast.Hags=="fixture runtime enabled","HAGS fills independently on completion");
      var hw=(HardwareInfo)typeof(AppViewModel).GetField("_hardware",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(fast)!;
      Check(hw.HagsConfigured==true,"runtime result cannot change configured safety input");
    }
    finally{hagsGate.Set();}
    Console.WriteLine($"{count}/{count} passed");
   }catch(Exception e){Console.Error.WriteLine(e);exit=1;}
   finally{gate.Set();app.Shutdown();}
  }));
  app.Run();return exit;
 }
}
