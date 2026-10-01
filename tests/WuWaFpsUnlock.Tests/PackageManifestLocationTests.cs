using WuWaFpsUnlock.Core;

public static class PackageManifestLocationTests
{
    public static async Task RunAsync(Func<string,Func<Task>,Task> test)
    {
        var root=Path.GetFullPath(Path.Combine("artifacts","tests","test-work","material-path-"+Guid.NewGuid().ToString("N")));
        var bundled=Path.Combine(root,"payload","manifest.json");
        var custom=Path.Combine(root,"custom.json");
        var stale=Path.Combine(root,"old-installation","payload","manifest.json");
        Directory.CreateDirectory(Path.GetDirectoryName(bundled)!);
        void Check(bool result){if(!result)throw new Exception("manifest location assertion failed");}
        Task Run(Action action){action();return Task.CompletedTask;}
        try
        {
            File.WriteAllText(bundled,"bundled fixture");
            await test("manifest relocation repairs absent absolute selection and first use",()=>Run(()=>
            {
                Check(PackageManifestLocation.Resolve(stale,root)==bundled);
                Check(PackageManifestLocation.Resolve("",root)==bundled);
                Check(!Directory.Exists(Path.GetDirectoryName(stale)));
            }));
            await test("current installation wins over an existing obsolete custom selection",()=>Run(()=>
            {
                File.WriteAllText(custom,"invalid custom content must not silently select other material");
                Check(PackageManifestLocation.Resolve(custom,root)==bundled);
            }));
            await test("manifest relocation rechecks disappearance before deployment",()=>Run(()=>
            {
                File.Delete(custom);
                Check(PackageManifestLocation.Resolve(custom,root)==bundled);
                File.Delete(bundled);
                Check(PackageManifestLocation.Resolve(stale,root)==bundled);
                Check(PackageManifestLocation.Resolve("",root)==bundled);
                Check(!File.Exists(bundled));
            }));
            await test("legacy relative material path belongs to installation",()=>Run(()=>
            {
                File.WriteAllText(custom,"legacy");
                Check(PackageManifestLocation.Resolve("custom.json",root)==custom);
                Check(PackageManifestLocation.Resolve(custom,root)==custom);
                Check(PackageManifestLocation.Resolve("bad\0path",root)==bundled);
            }));
            await test("missing sources reported together without file writes",()=>Run(()=>
            {
                var manifest=new PayloadManifest { Files=[new(){Source="files/a.dll"},new(){Source="files/b.dll"}] };
                try { PackageManifestLocation.RequireSources(manifest,bundled);throw new Exception("accepted missing materials"); }
                catch(FileNotFoundException e) { Check(e.Message.Contains("a.dll")&&e.Message.Contains("b.dll")); }
                Check(!Directory.Exists(Path.Combine(root,"payload","files")));
            }));

        }
        finally { Directory.Delete(root,true); }
    }
}
