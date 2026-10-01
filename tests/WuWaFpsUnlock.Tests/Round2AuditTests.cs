using WuWaFpsUnlock.Core;
public static class Round2AuditTests
{
    public static async Task RunAsync(Func<string,Func<Task>,Task> test)
    {
        string root=Path.Combine(Path.GetTempPath(),"ww-round2-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        void Check(bool ok){if(!ok)throw new Exception("round-two regression failed");}
        async Task Reject(Func<Task> run){try{await run();}catch(Exception e) when(e is IOException or InvalidDataException){return;}throw new Exception("Expected rejection");}
        try
        {
            await test("ReShade comma escapes survive managed addon deployment and cleanup",async()=>{
                string path=Path.Combine(root,"ReShade.ini");string text="; untouched\n[ADDON] ; comment\nLoadFromDllMain=vendor,,extras.addon64,another.addon64\nAddonPath=addons,,shared\n[renodx.mfgunlock]\nEnabled=7\n[USER]\nKeep=a,,b\n";File.WriteAllText(path,text);
                var receipt=new DeploymentReceipt{GameRoot=root,IniPath=path};var ini=IniDocument.Load(path);
                Check(ReShadeValues.Scalar(ini.Get("ADDON","AddonPath"))=="addons,shared");
                ini.ApplyManagedAddonLoading(receipt);ini.ApplyOwned("RenoDX.MFGUnlock","Enabled","1",receipt);ini.Save(path);
                var parsed=IniDocument.Load(path);Check(ReShadeValues.Decode(parsed.Get("ADDON","LoadFromDllMain")).SequenceEqual(new[]{"vendor,extras.addon64","another.addon64",ManagedAddons.Main}));
                Check(parsed.Get("renodx.mfgunlock","Enabled")=="7"&&parsed.Get("RenoDX.MFGUnlock","Enabled")=="1"&&parsed.Get("USER","Keep")=="a,,b");
                parsed.RemoveOwnedEdits(receipt,_=>{});parsed.Save(path);
                Check(File.ReadAllText(path).Contains("[ADDON] ; comment\nLoadFromDllMain=vendor,,extras.addon64,another.addon64")&&File.ReadAllText(path).Contains("[USER]\nKeep=a,,b"));
                await Reject(()=>{ReShadeValues.Scalar("one,two");return Task.CompletedTask;});
                Check(ReShadeValues.Decode("a,,,b").SequenceEqual(new[]{"a,","b"}));
                string utf16=Path.Combine(root,"utf16.ini");File.WriteAllText(utf16,"[ADDON]\nLoadFromDllMain=user.addon64",System.Text.Encoding.Unicode);
                byte[] before=File.ReadAllBytes(utf16);await Reject(()=>{IniDocument.Load(utf16);return Task.CompletedTask;});Check(File.ReadAllBytes(utf16).SequenceEqual(before));
            });
            foreach(int mode in new[]{0,1,2})await test("redeployment preserves saved frame-generation mode "+mode,()=>{
                string path=Path.Combine(root,"mode"+mode+".ini");File.WriteAllText(path,$"[RenoDX.MFGUnlock]\nWuWaFrameGenerationModeV1={mode}\nWuWaLastFixedMultiplier=6\nDynamicMFG=1\nForceMultiplier=4\nDynamicLiveMaxMultiplier=5\n");
                var ini=IniDocument.Load(path);var settings=new UserSettings();var manifest=new PayloadManifest{PreferDynamic=true,FixedMultiplier=3};
                var cfg=MfgDeploymentConfiguration.Create(settings,manifest,new HardwareInfo("fixture",59540,true,"fixture","fixture",true),ini);var receipt=new DeploymentReceipt();cfg.ApplyOwned(ini,receipt);
                Check(cfg.Mode==mode&&cfg.SavedMode&&cfg.PreviewText.Contains("保留游戏内"));
                Check(ini.Get("RenoDX.MFGUnlock","DynamicMFG")== (mode==2?"1":"0")&&ini.Get("RenoDX.MFGUnlock","ForceMultiplier")== (mode==1?"6":"0"));
                Check(ini.Get("RenoDX.MFGUnlock","WuWaFrameGenerationModeV1")==mode.ToString()&&ini.Get("RenoDX.MFGUnlock","DynamicLiveMaxMultiplier")=="5"&&!receipt.IniEdits.Any(e=>e.Key=="WuWaFrameGenerationModeV1"));return Task.CompletedTask;
            });
            await test("launch checks actual deployed files even with MFG selection disabled",async()=>{
                string addon=Path.Combine(root,ManagedAddons.Main),iniPath=Path.Combine(root,"launch.ini");File.WriteAllText(addon,"inert addon");File.WriteAllText(iniPath,"[RenoDX.MFGUnlock]\nEnabled=0\nDynamicMFG=0\n");
                var receipt=new DeploymentReceipt{GameRoot=root,Status="Deployed",IniPath=iniPath,IniEdits=[new(){Section="RenoDX.MFGUnlock",Key="DynamicMFG",Written="1"}],Files=[new(){Path=addon,Kind="Addon",Completed=true,InstalledHash=await SafePaths.HashAsync(addon)}]};
                await DeploymentLaunchGuard.RequireReadyAsync(receipt,false,_=>{}); // Runtime menu changes are warning-only.
                File.WriteAllText(addon,"external change");await Reject(()=>DeploymentLaunchGuard.RequireReadyAsync(receipt,false,_=>{}));
                File.Delete(addon);await Reject(()=>DeploymentLaunchGuard.RequireReadyAsync(receipt,true,_=>{}));
                foreach(string state in new[]{"InstallingReShade","Installing","PartialClean","PartialFailure","unknown"}){receipt.Status=state;await Reject(()=>DeploymentLaunchGuard.RequireReadyAsync(receipt,false,_=>{}));}
                await DeploymentLaunchGuard.RequireReadyAsync(null,false,_=>{});await Reject(()=>DeploymentLaunchGuard.RequireReadyAsync(null,true,_=>{}));
                receipt.Status="Cleaned";receipt.Files.Clear();await DeploymentLaunchGuard.RequireReadyAsync(receipt,false,_=>{});
            });
        }
        finally{Directory.Delete(root,true);}
    }
}
