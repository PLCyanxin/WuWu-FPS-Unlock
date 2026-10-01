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
            await test("manifest relocation preserves existing custom even when content is invalid",()=>Run(()=>
            {
                File.WriteAllText(custom,"invalid custom content must not silently select other material");
                Check(PackageManifestLocation.Resolve(custom,root)==custom);
            }));
            await test("manifest relocation rechecks disappearance before deployment",()=>Run(()=>
            {
                File.Delete(custom);
                Check(PackageManifestLocation.Resolve(custom,root)==bundled);
                File.Delete(bundled);
                Check(PackageManifestLocation.Resolve(stale,root)==stale);
                Check(PackageManifestLocation.Resolve("",root)=="");
                Check(!File.Exists(bundled));
            }));
        }
        finally { Directory.Delete(root,true); }
    }
}
