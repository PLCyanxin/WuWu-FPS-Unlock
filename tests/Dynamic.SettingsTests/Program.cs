using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WuWaFpsUnlock;
using WuWaFpsUnlock.Core;
using WuWaFpsUnlock.Services;
using WuWaFpsUnlock.ViewModels;

public static class DynamicSettingsTests
{
    [STAThread] public static int Main()
    {
        if(!AppContext.BaseDirectory.Contains("Dynamic.SettingsTests",StringComparison.Ordinal))throw new Exception("Not isolated test output.");
        byte[]? original=File.Exists(AppPaths.Settings)?File.ReadAllBytes(AppPaths.Settings):null;
        Directory.CreateDirectory(AppPaths.Data);
        File.WriteAllText(AppPaths.Settings,"{\"MfgSelected\":false,\"FutureSetting\":{\"keep\":true}}");
        var app=new App();app.InitializeComponent();
        var startup=typeof(App).GetMethod("OnStartup",BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly)!;
        app.Startup-=(StartupEventHandler)Delegate.CreateDelegate(typeof(StartupEventHandler),app,startup);
        var vm=new AppViewModel();SettingsWindow? window=null;int passed=0;
        void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);passed++;}
        void Pump()=>app.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        void Flag(string name,bool value){typeof(AppViewModel).GetProperty(name)!.SetValue(vm,value);Pump();}
        try
        {
            window=new SettingsWindow(vm);
            var combo=(ComboBox)window.FindName("DynamicMaxMultiplierBox");Pump();
            Check(vm.DynamicMaxMultiplier==4 && (int)combo.SelectedValue==4,"legacy settings select default 4x");
            Check(vm.DynamicMaxMultiplierOptions.Select(x=>x.Value).SequenceEqual(new[]{0,2,3,4,5,6}),"only supported choices offered");
            Check(combo.IsEnabled&&!vm.MfgSelected,"cap can be prepared before MFG selection");
            combo.SelectedValue=6;Pump();
            Check(vm.DynamicMaxMultiplier==6&&JsonFiles.Read<UserSettings>(AppPaths.Settings).DynamicMaxMultiplier==6,"selection persists through WPF binding");
            Check(vm.Status.Contains("重新部署")&&vm.Status.Contains("完全重启"),"saved setting requests deployment and full restart");
            Check(JsonFiles.Read<UserSettings>(AppPaths.Settings).ExtensionData!["FutureSetting"].GetProperty("keep").GetBoolean(),"unknown settings survive persistence");
            combo.SelectedValue=0;Pump();
            Check(JsonFiles.Read<UserSettings>(AppPaths.Settings).DynamicMaxMultiplier==0,"NVIDIA default persists as zero");
            vm.DynamicMaxMultiplier=4;Pump();
            using(var held=new FileStream(AppPaths.Settings,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                combo.SelectedValue=5;Pump();
                Check(vm.DynamicMaxMultiplier==4&&(int)combo.SelectedValue==4,"failed save restores selected value");
                Check(vm.Status.Contains("未保存")&&JsonFiles.Read<UserSettings>(AppPaths.Settings).DynamicMaxMultiplier==4,"failed save preserves disk and reports failure");
            }
            Flag(nameof(AppViewModel.Busy),true);vm.DynamicMaxMultiplier=6;
            Check(!combo.IsEnabled&&vm.DynamicMaxMultiplier==4,"busy state prevents preference change");
            Flag(nameof(AppViewModel.Busy),false);Flag(nameof(AppViewModel.IsGameRunning),true);vm.DynamicMaxMultiplier=2;
            Check(!combo.IsEnabled&&vm.DynamicMaxMultiplier==4,"running game prevents preference change");
            Flag(nameof(AppViewModel.IsGameRunning),false);vm.DynamicMaxMultiplier=7;Pump();
            Check(vm.DynamicMaxMultiplier==0&&(int)combo.SelectedValue==0,"invalid programmatic input disables override");
            Console.WriteLine($"{passed}/{passed} passed. Native WPF components only; no visible windows, game access or deployment.");
            return 0;
        }
        finally
        {
            Flag(nameof(AppViewModel.Busy),false);Flag(nameof(AppViewModel.IsGameRunning),false);
            window?.Close();vm.CloseAsync().GetAwaiter().GetResult();
            if(original is null)File.Delete(AppPaths.Settings);else File.WriteAllBytes(AppPaths.Settings,original);
        }
    }
}
