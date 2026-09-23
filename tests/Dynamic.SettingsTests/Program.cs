using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
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
        File.WriteAllText(AppPaths.Settings,"{\"DynamicMaxMultiplier\":4,\"FutureSetting\":{\"keep\":true}}");
        var app=new App();app.InitializeComponent();
        var startup=typeof(App).GetMethod("OnStartup",BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly)!;
        app.Startup-=(StartupEventHandler)Delegate.CreateDelegate(typeof(StartupEventHandler),app,startup);
        var vm=new AppViewModel();SettingsWindow? window=null;int passed=0;
        void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);passed++;}
        try
        {
            window=new SettingsWindow(vm);
            Check(window.FindName("DynamicMaxMultiplierBox") is null,"static deployment selector removed");
            Check(((TextBlock)window.FindName("DynamicRuntimeHint")).Text.Contains("游戏内"),"runtime control location shown");
            vm.TargetFps=160;
            var saved=JsonFiles.Read<UserSettings>(AppPaths.Settings);
            Check(saved.ExtensionData!["FutureSetting"].GetProperty("keep").GetBoolean(),"unrelated settings preserved");
            Check(saved.TargetFps==160,"FPS settings remain editable");
            Check(!window.IsVisible,"no window shown");
            Console.WriteLine($"{passed}/{passed} passed. Native WPF only; no game or deployment.");
            return 0;
        }
        finally
        {
            window?.Close();vm.CloseAsync().GetAwaiter().GetResult();
            if(original is null)File.Delete(AppPaths.Settings);else File.WriteAllBytes(AppPaths.Settings,original);
        }
    }
}
