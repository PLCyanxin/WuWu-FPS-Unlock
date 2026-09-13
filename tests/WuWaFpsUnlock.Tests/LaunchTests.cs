using WuWaFpsUnlock.Core;

public static class LaunchTests
{
    public static async Task RunAsync(Func<string,Func<Task>,Task> test)
    {
        string root=Path.GetFullPath(Path.Combine("artifacts","test-work","launch-中文 空格-"+Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        string game=Path.Combine(root,"游戏"),shipping=Path.Combine(game,"Client","Binaries","Win64","Client-Win64-Shipping.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(shipping)!);File.WriteAllText(shipping,"fake: not executed");
        string entry=Path.Combine(game,"Wuthering Waves.exe");File.WriteAllText(entry,"fake root entry: not executed");
        string unlock=Path.Combine(root,"unlocker","unlock.exe"),iniPath=Path.Combine(root,"unlocker","ww_fps_config.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(unlock)!);File.WriteAllText(unlock,"fake: not executed");
        string config="[Settings]\nFpsValue=240\nPathValue=original\nAutoStartEnabled=False\nDX11Enabled=False\nPowerSavingEnabled=False\nAdvanEnabled=False\nFovEnabled=False\nHideUidEnabled=False\nRemoveBlurEnabled=False\nGameServerArea=2\nGameLaunchExe=0\nProcessPriorityMode=0\nFovValue=45.0\nGameParam=\nUnknownFutureKey=保留\n";
        UserSettings Settings(bool enabled=true)=>new(){GameRoot=game,GameExe=shipping,FpsEnabled=enabled,TargetFps=360};
        void Check(bool value){if(!value)throw new Exception("launch assertion failed");}
        Task Sync(Action action){action();return Task.CompletedTask;}
        void Reject(Action action){try{action();}catch(IOException){return;}throw new Exception("expected refusal");}
        await test("FPS OFF plan only Shipping, no unlocker or config required",()=>Sync(()=>{
            var plan=LaunchPlanBuilder.Build(Settings(false),Path.Combine(root,"missing.exe"));
            Check(plan.Executable==shipping && plan.ConfigPath is null && plan.Arguments.Count==0);
            UnlockerConfigAdapter.Prepare(plan);Check(!File.Exists(iniPath));
        }));
        await test("FPS ON plan only unlocker, verified working directory and empty arguments",()=>Sync(()=>{
            var plan=LaunchPlanBuilder.Build(Settings(),unlock);Check(plan.Executable==unlock && plan.WorkingDirectory==Path.GetDirectoryName(unlock));
            Check(plan.ShippingExePath==shipping && plan.UnlockerGamePath==entry && plan.Arguments.Count==0);
        }));
        await test("INI adapter preserves unknown/server and enables audited automatic Shipping mode",()=>Sync(()=>{
            File.WriteAllText(iniPath,config);UnlockerConfigAdapter.Prepare(LaunchPlanBuilder.Build(Settings(),unlock));
            var ini=IniDocument.Load(iniPath);Check(ini.Get("Settings","FpsValue")=="360" && ini.Get("Settings","GameLaunchExe")=="1");
            Check(ini.Get("Settings","PathValue")==entry && ini.Get("Settings","AutoStartEnabled")=="True");
            Check(ini.Get("Settings","GameServerArea")=="2" && ini.Get("Settings","UnknownFutureKey")=="保留");
        }));
        await test("FPS OFF does not modify existing unlocker INI",()=>Sync(()=>{
            var before=File.ReadAllBytes(iniPath);UnlockerConfigAdapter.Prepare(LaunchPlanBuilder.Build(Settings(false),unlock));Check(before.SequenceEqual(File.ReadAllBytes(iniPath)));
        }));
        await test("missing unlocker rejected",()=>Sync(()=>Reject(()=>LaunchPlanBuilder.Build(Settings(),Path.Combine(root,"missing.exe")))));
        await test("official root EXE rejected as Shipping",()=>Sync(()=>{var s=Settings(false);s.GameExe=entry;Reject(()=>LaunchPlanBuilder.Build(s,unlock));}));
        await test("FPS ON noncanonical Shipping layout refused",()=>Sync(()=>{
            string wrong=Path.Combine(game,"Client-Win64-Shipping.exe");File.WriteAllText(wrong,"fake");var s=Settings();s.GameExe=wrong;Reject(()=>LaunchPlanBuilder.Build(s,unlock));
        }));
        await test("missing configuration rejected without generating defaults",()=>Sync(()=>{File.Delete(iniPath);Reject(()=>UnlockerConfigAdapter.Prepare(LaunchPlanBuilder.Build(Settings(),unlock)));Check(!File.Exists(iniPath));}));
        await test("invalid server enum preserves bad INI and refuses",()=>Sync(()=>{File.WriteAllText(iniPath,config.Replace("GameServerArea=2","GameServerArea=99"));var before=File.ReadAllText(iniPath);Reject(()=>UnlockerConfigAdapter.Prepare(LaunchPlanBuilder.Build(Settings(),unlock)));Check(File.ReadAllText(iniPath)==before);}));
        await test("duplicate controlled INI key refuses without overwrite",()=>Sync(()=>{File.WriteAllText(iniPath,config+"AutoStartEnabled=True\n");var before=File.ReadAllText(iniPath);Reject(()=>UnlockerConfigAdapter.Prepare(LaunchPlanBuilder.Build(Settings(),unlock)));Check(File.ReadAllText(iniPath)==before);}));
        await test("invalid bool configuration refuses",()=>Sync(()=>{File.WriteAllText(iniPath,config.Replace("DX11Enabled=False","DX11Enabled=bogus"));Reject(()=>UnlockerConfigAdapter.Prepare(LaunchPlanBuilder.Build(Settings(),unlock)));}));
        await test("FPS range validation",()=>Sync(()=>{var s=Settings();s.TargetFps=421;Reject(()=>LaunchPlanBuilder.Build(s,unlock));}));
    }
}
