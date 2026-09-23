using System.Text;
using System.Text.Json.Nodes;

internal static partial class Program
{
    static readonly string[] InventoryPaths = ["payload/manifest.json", "payload/source-manifest.json", "payload/payload-map.json"];

    // Inventory is derived locally, never accepted as an extra protocol-1 ZIP target.
    static void AddInventoryEntries(string root, List<Entry> entries, List<FileStream> held, string scratch)
    {
        var addon = entries.Single(e => e.Relative.Equals("payload/files/addon/renodx-mfgunlock.addon64", StringComparison.OrdinalIgnoreCase));
        var provenanceEntry = entries.Single(e => e.Relative.Equals("payload/addon-source.json", StringComparison.OrdinalIgnoreCase));
        var provenance = ReadInventoryJson(provenanceEntry.SourceHandle!) as JsonObject ?? throw new InvalidDataException("Addon 来源记录格式无效。");
        if(!string.Equals(provenance["sha256"]?.GetValue<string>(),addon.NewHash,StringComparison.OrdinalIgnoreCase)
            || provenance["length"]?.GetValue<long>()!=addon.SourceHandle!.Length)
            throw new InvalidDataException("Addon 来源记录与更新文件不一致。");
        foreach(string relative in InventoryPaths)
        {
            string target=Scoped(root,relative);NoLinks(target);
            if(!File.Exists(target))continue;
            var current=new FileStream(target,FileMode.Open,FileAccess.ReadWrite,FileShare.None);held.Add(current);
            string oldHash=Hash(current);
            var document=ReadInventoryJson(current);
            RefreshAddonInventory(document,relative,provenance);
            NoLinks(scratch);Directory.CreateDirectory(scratch);
            string generated=Path.Combine(scratch,Path.GetFileName(relative));
            File.WriteAllText(generated,document.ToJsonString(JsonOptions),new UTF8Encoding(false));
            var source=new FileStream(generated,FileMode.Open,FileAccess.Read,FileShare.Read);held.Add(source);
            entries.Add(new(relative,generated,target,Hash(source)){OldHash=oldHash,SourceHandle=source,TargetHandle=current});
        }
    }
    static JsonNode ReadInventoryJson(FileStream stream)
    {
        if(stream.Length>8*1024*1024)throw new InvalidDataException("材料清单过大。");
        stream.Position=0;
        return JsonNode.Parse(stream)??throw new InvalidDataException("材料清单为空。");
    }
    static void RefreshAddonInventory(JsonNode document,string relative,JsonObject provenance)
    {
        const string name="renodx-mfgunlock.addon64";
        bool manifest=relative=="payload/manifest.json";
        var files=(relative=="payload/payload-map.json"?document:document["files"]) as JsonArray
            ??throw new InvalidDataException("材料清单缺少文件列表："+relative);
        var matches=files.OfType<JsonObject>().Where(item=>string.Equals(
            item[manifest?"source":"name"]?.GetValue<string>(),manifest?"files/addon/"+name:name,StringComparison.OrdinalIgnoreCase)).ToArray();
        if(matches.Length!=1)throw new InvalidDataException("材料清单缺少唯一 addon 条目："+relative);
        var entry=matches[0];entry["sha256"]=provenance["sha256"]!.DeepClone();
        string? version=provenance["addonVersion"]?.GetValue<string>();
        if(manifest)
        {
            entry["size"]=provenance["length"]!.DeepClone();
            if(!string.IsNullOrWhiteSpace(version))document["addonVersion"]=version;
        }
        else
        {
            if(!string.IsNullOrWhiteSpace(version))entry["addonVersion"]=version;
            if(relative=="payload/source-manifest.json")
            {
                entry["length"]=provenance["length"]!.DeepClone();entry["copySha256"]=provenance["sha256"]!.DeepClone();
                entry["copyVerified"]=true;entry["sourcePath"]="payload/addon-source.json";
                entry["copiedPath"]="payload/files/addon/"+name;
                entry["signatureStatus"]="NotChecked";entry["signatureMessage"]="Signature not revalidated for this build.";
                entry["signer"]=null;entry["certificateThumbprint"]=null;
                if(provenance["fileVersion"] is JsonValue fileVersion)entry["fileVersion"]=fileVersion.DeepClone();
            }
            else entry["source"]="payload/files/addon/"+name;
        }
    }
}
