using System.Text.Json.Nodes;
using WuWaFpsUnlock.Core;

internal static partial class Program
{
    static void InventoryTests(Action<string,Action> test)
    {
        const string addon="renodx-mfgunlock.addon64";
        JsonObject Provenance()=>new(){["sha256"]=new string('a',64),["length"]=99L,["addonVersion"]="1.1+WuWu.DynamicMax.1",["fileVersion"]="1.1.0.0"};
        JsonNode Manifest()=>JsonNode.Parse("""{"addonVersion":"0.9","unknown":{"keep":true},"files":[{"source":"files/addon/renodx-mfgunlock.addon64","size":1,"sha256":"old"},{"source":"files/game/vendor.dll","sha256":"unchanged"}]}""")!;
        void Check(bool value){if(!value)throw new Exception("inventory assertion failed");}
        test("addon inventory updates version and hash without changing other materials",()=>
        {
            var manifest=Manifest();RefreshAddonInventory(manifest,InventoryPaths[0],Provenance());
            Check(manifest["addonVersion"]!.GetValue<string>()=="1.1+WuWu.DynamicMax.1");
            Check(manifest["files"]![0]!["size"]!.GetValue<long>()==99&&manifest["files"]![1]!["sha256"]!.GetValue<string>()=="unchanged");
            Check(manifest["unknown"]!["keep"]!.GetValue<bool>());
        });
        test("legacy addon source without version preserves recorded version",()=>
        {
            var manifest=Manifest();var source=Provenance();source.Remove("addonVersion");RefreshAddonInventory(manifest,InventoryPaths[0],source);
            Check(manifest["addonVersion"]!.GetValue<string>()=="0.9");
        });
        test("inventory remains forbidden in protocol one remote payload",()=>
        {
            foreach(var path in InventoryPaths)Check(!UpdatePackageProtocol.Allowed("update-payload/"+path)&&!AllowedPackageFile(path)&&Allowed(path));
        });
        test("derived inventory entries participate in snapshot rollback",()=>
        {
            string root=Path.Combine(Path.GetTempPath(),"WuWaInventory-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            var held=new List<FileStream>();var entries=new List<Entry>();
            try
            {
                Write(root,"WuWaFpsUnlock.exe","inert app");Write(root,"data/settings.json","private settings");
                Write(root,InventoryPaths[0],Manifest().ToJsonString());
                Write(root,InventoryPaths[1],"""{"sourceRoot":"private local path","files":[{"name":"renodx-mfgunlock.addon64","sha256":"old","length":1,"unknown":17}]}""");
                Write(root,InventoryPaths[2],"""[{"name":"renodx-mfgunlock.addon64","sha256":"old","actualGameTargets":["private game path"]}]""");
                string old=File.ReadAllText(Scoped(root,InventoryPaths[0]));
                Write(root,"incoming/"+addon,"inert new addon");var provenance=Provenance();
                provenance["sha256"]=FileHash(Scoped(root,"incoming/"+addon));provenance["length"]=new FileInfo(Scoped(root,"incoming/"+addon)).Length;
                Write(root,"incoming/addon-source.json",provenance.ToJsonString());
                foreach(var pair in new[]{("payload/files/addon/"+addon,"incoming/"+addon),("payload/addon-source.json","incoming/addon-source.json")})
                {
                    string source=Scoped(root,pair.Item2),target=Scoped(root,pair.Item1);Write(root,pair.Item1,"old bytes");
                    var input=File.OpenRead(source);held.Add(input);var current=new FileStream(target,FileMode.Open,FileAccess.ReadWrite,FileShare.None);held.Add(current);
                    entries.Add(new(pair.Item1,source,target,Hash(input)){SourceHandle=input,TargetHandle=current,OldHash=Hash(current)});
                }
                AddInventoryEntries(root,entries,held,Scoped(root,"derived"));Check(entries.Count==5);
                var generated=JsonNode.Parse(File.ReadAllText(entries.Single(e=>e.Relative==InventoryPaths[1]).Source))!;
                Check(generated["sourceRoot"]!.GetValue<string>()=="private local path"&&generated["files"]![0]!["unknown"]!.GetValue<int>()==17);
                var map=JsonNode.Parse(File.ReadAllText(entries.Single(e=>e.Relative==InventoryPaths[2]).Source))!;
                Check(map[0]!["actualGameTargets"]![0]!.GetValue<string>()=="private game path");
                string backup=Scoped(root,"update-backup-fixture");Directory.CreateDirectory(backup);
                var snapshot=CaptureSnapshot(root,backup,Scoped(root,"incoming"),entries) with{Complete=true};WriteSnapshot(backup,snapshot);
                foreach(var handle in held)handle.Dispose();
                foreach(var entry in entries)File.Copy(entry.Source,entry.Target,true);
                Check(JsonNode.Parse(File.ReadAllText(Scoped(root,InventoryPaths[0])))!["addonVersion"]!.GetValue<string>()=="1.1+WuWu.DynamicMax.1");
                RestoreSnapshot(backup,snapshot,()=>{});
                Check(File.ReadAllText(Scoped(root,InventoryPaths[0]))==old&&File.ReadAllText(Scoped(root,"data/settings.json"))=="private settings");
            }
            finally{foreach(var handle in held)handle.Dispose();NoLinks(root);Directory.Delete(root,true);}
        });
    }
}
