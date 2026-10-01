using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using WuWaFpsUnlock;
using WuWaFpsUnlock.Core;
using WuWaFpsUnlock.ViewModels;
class Program
{
 [STAThread] static int Main()
 {
  var app=new App();app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
  var startup=typeof(App).GetMethod("OnStartup",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)!;
  app.Startup-=(StartupEventHandler)Delegate.CreateDelegate(typeof(StartupEventHandler),app,startup);
  int checks=0,result=0;
  app.Dispatcher.BeginInvoke(new Action(async()=>{
   void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
   void Field(AppViewModel vm,string name,object? value)=>typeof(AppViewModel).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(vm,value);
   void Property(AppViewModel vm,string name,bool value)=>typeof(AppViewModel).GetProperty(name)!.SetValue(vm,value);
   object? Invoke(AppViewModel vm,string name)=>typeof(AppViewModel).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(vm,null);
   AppViewModel Create()=>new(probeEnvironment:_=>HardwareInfo.Unknown,probeHags:(basic,_)=>basic);
   try{
    var vm=Create();int exits=0;vm.GameExited+=()=>exits++;
    var dialog=new UpdateDialog("9.0","fixture",false,_=>{},(_,_)=>Task.FromResult(false),_=>{},gameRunning:true);
    Field(vm,"_updateDialog",dialog);Field(vm,"_sessionReady",true);Property(vm,"IsGameRunning",true);Property(vm,"Busy",true);
    var button=(Button)dialog.FindName("InstallButton");Check(!button.IsEnabled,"running game disables immediate install");
    await (Task)Invoke(vm,"CompleteObservedGameExitAsync")!;
    Check(!vm.IsGameRunning,"confirmed exit updates shared running state during offer");
    Check(button.IsEnabled,"existing offer enables install after observed exit");
    Check(vm.Busy&&exits==0,"offer retains operation lock and suppresses exit event");
    Invoke(vm,"DispatchPendingGameExit");Check(exits==0,"duplicate dispatch while dialog open does nothing");
    Field(vm,"_updateDialog",null);Property(vm,"Busy",false);Invoke(vm,"DispatchPendingGameExit");Invoke(vm,"DispatchPendingGameExit");
    Check(exits==1,"closing offer delivers one deferred exit only");await vm.CloseAsync();
    var idle=Create();int idleExits=0;idle.GameExited+=()=>idleExits++;Invoke(idle,"DispatchPendingGameExit");Check(idleExits==0,"no game session creates no exit event");await idle.CloseAsync();
    var pending=Create();int later=0;pending.GameExited+=()=>later++;Field(pending,"_sessionReady",true);Property(pending,"Busy",true);Field(pending,"_updateDialog",dialog);
    var cleanup=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);Field(pending,"_sessionCleanup",cleanup.Task);
    var completing=(Task)Invoke(pending,"CompleteObservedGameExitAsync")!;
    Field(pending,"_updateDialog",null);Property(pending,"Busy",false);Invoke(pending,"DispatchPendingGameExit");Check(later==0,"dialog close cannot dispatch before session cleanup finishes");
    cleanup.SetResult();await completing;Check(later==1,"cleanup completion delivers retained exit");await pending.CloseAsync();
    var closing=Create();Field(closing,"_gameExitPending",true);await closing.CloseAsync();int closed=0;closing.GameExited+=()=>closed++;Invoke(closing,"DispatchPendingGameExit");Check(closed==0,"shutdown/update handoff suppresses queued exit");
    var failed=Create();int failureExits=0;failed.GameExited+=()=>failureExits++;Property(failed,"IsGameRunning",true);
    await (Task)Invoke(failed,"CompleteObservedGameExitAsync")!;
    Check(failureExits==0&&!failed.IsGameRunning&&!failed.Busy,"failed startup exit leaves launcher open and ready for retry");await failed.CloseAsync();
    Console.WriteLine($"{checks} passed; native WPF components only, no shown windows or process operations");
   }catch(Exception error){Console.Error.WriteLine(error);result=1;}finally{app.Shutdown();}
  }));app.Run();return result;
 }
}
