using WuWaFpsUnlock.Core;

public static class DualAddonTests
{
    public static async Task RunAsync(Func<string,Func<Task>,Task> test)
    {
        static void Check(bool ok){if(!ok)throw new Exception("dual addon assertion failed");}
        string root=Path.Combine(Path.GetTempPath(),"WuWa-DualAddon-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            foreach(bool preexisting in new[]{false,true})
            await test("main early loading preserves companion user tokens and runtime selection " + preexisting,()=>{
                string path=Path.Combine(root,Guid.NewGuid()+".ini");
                File.WriteAllText(path,";preserve\n[WuWa.DynamicMax]\nEnabled=0\nDynamicLiveMaxMultiplier=6\n[ADDON]\nLoadFromDllMain=user.addon64"+(preexisting?",WUWA-DYNAMICMAX.ADDON64":"")+"\n");
                var ini=IniDocument.Load(path);var receipt=new DeploymentReceipt();
                ini.ApplyManagedAddonLoading(receipt);ini.ApplyManagedAddonLoading(receipt);
                Check(ini.Get("ADDON","LoadFromDllMain")!.Split(',').Length==(preexisting?3:2));
                ini.Set("ADDON","LoadFromDllMain",ini.Get("ADDON","LoadFromDllMain")+",later.addon64,wuwa-dynamicmax.addon64.other");
                ini.RemoveOwnedEdits(receipt,_=>{});ini.Save(path);
                string actual=ini.Get("ADDON","LoadFromDllMain")!;
                Check(actual.Contains("user.addon64")&&actual.Contains("later.addon64")&&actual.Contains("wuwa-dynamicmax.addon64.other"));
                Check(!actual.Contains(ManagedAddons.Main));
                Check(actual.Contains("WUWA-DYNAMICMAX.ADDON64")==preexisting);
                Check(ini.Get("WuWa.DynamicMax","Enabled")=="0"&&ini.Get("WuWa.DynamicMax","DynamicLiveMaxMultiplier")=="6");
                Check(File.ReadAllText(path).StartsWith(";preserve"));return Task.CompletedTask;
            });
            await test("legacy receipt does not claim independently added companion early-load entry",()=>{
                var ini=IniDocument.Load(Path.Combine(root,"legacy.ini"));var receipt=new DeploymentReceipt();
                ini.ApplyOwned("ADDON","LoadFromDllMain",ManagedAddons.Main,receipt);
                ini.Set("ADDON","LoadFromDllMain",ManagedAddons.Main+","+ManagedAddons.Dynamic);
                ini.ApplyManagedAddonLoading(receipt);ini.RemoveOwnedEdits(receipt,_=>{});
                Check(ini.Get("ADDON","LoadFromDllMain")==ManagedAddons.Dynamic);return Task.CompletedTask;
            });
            await test("historical receipt removes only its two explicitly added addon tokens",()=>{
                var ini=IniDocument.Load(Path.Combine(root,"historical.ini"));var receipt=new DeploymentReceipt();
                ini.Set("ADDON","LoadFromDllMain","user.addon64");
                ini.ApplyOwned("ADDON","LoadFromDllMain","user.addon64,"+ManagedAddons.Main+","+ManagedAddons.Dynamic,receipt);
                ini.Set("ADDON","LoadFromDllMain",ini.Get("ADDON","LoadFromDllMain")+",later.addon64");
                ini.RemoveOwnedEdits(receipt,_=>{});
                Check(ini.Get("ADDON","LoadFromDllMain")=="user.addon64,later.addon64");return Task.CompletedTask;
            });
            foreach(string mode in new[]{"owned","modified","manual","incomplete","unknown"})
            await test("companion cleanup requires owned completed matching receipt " + mode,async()=>{
                string dir=Path.Combine(root,mode);Directory.CreateDirectory(dir);
                string file=Path.Combine(dir,mode=="unknown"?"unrelated.addon64":ManagedAddons.Dynamic);File.WriteAllText(file,"inert addon");
                var receipt=new DeploymentReceipt{GameRoot=dir,Files=[new(){Path=file,Kind="Addon",CreatedByTool=mode!="manual",Completed=mode!="incomplete",InstalledHash=await SafePaths.HashAsync(file)}]};
                if(mode=="modified")File.AppendAllText(file,"modified");
                await DeploymentFiles.CleanOwnedAddonsAsync(receipt,()=>{},_=>{});
                Check(File.Exists(file)!=(mode=="owned"));
            });
            await test("companion remains excluded from material-discovery cleanup",()=>{
                try{PackageReader.FindExistingMaterialTargets(root,ManagedAddons.Dynamic);throw new Exception("must reject receipt-free discovery");}
                catch(InvalidDataException){}return Task.CompletedTask;
            });
        }
        finally{Directory.Delete(root,true);}
    }
}
