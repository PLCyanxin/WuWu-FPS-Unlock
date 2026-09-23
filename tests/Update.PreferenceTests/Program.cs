using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using WuWaFpsUnlock;
using WuWaFpsUnlock.Core;
using WuWaFpsUnlock.Services;
using WuWaFpsUnlock.ViewModels;
public static class PreferenceTests
{
 [STAThread]public static int Main()
 {
  // Own project output only. Never touches installed application data.
  if(!AppContext.BaseDirectory.Contains("Update.PreferenceTests",StringComparison.Ordinal))throw new Exception("not fixture output");
  byte[]? original=File.Exists(AppPaths.Settings)?File.ReadAllBytes(AppPaths.Settings):null;
  Directory.CreateDirectory(AppPaths.Data);JsonFiles.Save(AppPaths.Settings,new UserSettings{AutoCheckUpdates=true,SkippedUpdateTag="v2.0.0RC"});
  var app=new App();app.InitializeComponent();
  var startup=typeof(App).GetMethod("OnStartup",BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly)!;
  app.Startup-=(StartupEventHandler)Delegate.CreateDelegate(typeof(StartupEventHandler),app,startup);
  var vm=new AppViewModel();int count=0;
  void Assert(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);count++;}
  var field=typeof(AppViewModel).GetField("_settings",BindingFlags.NonPublic|BindingFlags.Instance)!;
  var persist=typeof(AppViewModel).GetMethod("PersistUpdatePreference",BindingFlags.NonPublic|BindingFlags.Instance)!;
  void Skip(bool value){try{persist.Invoke(vm,[new Action<UserSettings>(s=>s.SkippedUpdateTag=value?"v2.0.0RC":"")]);}catch(TargetInvocationException e){throw e.InnerException!;}}
  UpdateDialog? dialog=null;
  try
  {
   dialog=new UpdateDialog("2.0.0RC","fixture",true,Skip,(_,_)=>Task.FromResult(false),_=>{});
   var check=(CheckBox)dialog.FindName("SkipVersion");
   using(var held=new FileStream(AppPaths.Settings,FileMode.Open,FileAccess.Read,FileShare.Read))
   {
    vm.AutoCheckUpdates=false;
    Assert(vm.AutoCheckUpdates,"auto preference unchanged on denied replacement");
    Assert(vm.UpdateStatus.Contains("未保存"),"auto preference reports save failure");
    check.IsChecked=false;
    Assert(check.IsChecked==true,"skip checkbox restored on save failure");
    Assert(((UserSettings)field.GetValue(vm)!).SkippedUpdateTag=="v2.0.0RC","skip in-memory tag unchanged");
    Assert(((TextBlock)dialog.FindName("OperationStatus")).Text.Contains("未保存"),"skip error visible in dialog");
    Assert(JsonFiles.Read<UserSettings>(AppPaths.Settings).AutoCheckUpdates,"failed writes preserve disk state");
   }
   vm.AutoCheckUpdates=false;check.IsChecked=false;
   var saved=JsonFiles.Read<UserSettings>(AppPaths.Settings);
   Assert(!saved.AutoCheckUpdates&&saved.SkippedUpdateTag=="","successful preferences persisted");
   check.IsChecked=true;
   Assert(JsonFiles.Read<UserSettings>(AppPaths.Settings).SkippedUpdateTag=="v2.0.0RC","successful skip persisted");
   Console.WriteLine($"{count}/{count} passed; native WPF components only, no shown windows/network/update execution.");return 0;
  }
  finally
  {
   dialog?.Close();vm.CloseAsync().GetAwaiter().GetResult();
   if(original is null)File.Delete(AppPaths.Settings);else File.WriteAllBytes(AppPaths.Settings,original);
  }
 }
}

