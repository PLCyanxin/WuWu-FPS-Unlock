using System.Security.Cryptography;

namespace WuWaFpsUnlock.Core;

public static class UpdateStageMaintenance
{
    private sealed record Completion(int Schema,string Root,string Directory,string Version,string ManifestHash);
    public static void MarkCompleted(string root,string directory)
    {
        root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));directory=Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        string cache=Path.Combine(root,".updates"),name=Path.GetFileName(directory);
        if(Path.GetDirectoryName(directory)!=cache||!Guid.TryParseExact(name,"N",out _))return; // Manual packages remain user-managed.
        CheckPath(directory);var manifest=UpdatePackageProtocol.ValidateDirectory(directory);
        DurableJson.Save(Path.Combine(cache,name+".completed.json"),new Completion(1,root,name,manifest.Version,Hash(Path.Combine(directory,"update-manifest.json"))));
    }
    public static void PruneCompleted(string root,Action<string>? log=null)
    {
        root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));string cache=Path.Combine(root,".updates");
        if(!Directory.Exists(cache))return;CheckPath(cache);
        foreach(string marker in Directory.EnumerateFiles(cache,"*.completed.json",SearchOption.TopDirectoryOnly))
        {
            try
            {
                CheckPath(marker);var completion=DurableJson.Read<Completion>(marker);
                if(completion.Schema!=1||completion.Root!=root||!Guid.TryParseExact(completion.Directory,"N",out _)
                    ||Path.GetFileName(marker)!=completion.Directory+".completed.json")throw new InvalidDataException("更新缓存归属不符。");
                string directory=Path.Combine(cache,completion.Directory);CheckPath(directory);
                if(!Directory.Exists(directory)){File.Delete(marker);continue;}
                if(Hash(Path.Combine(directory,"update-manifest.json"))!=completion.ManifestHash)throw new InvalidDataException("完成后更新清单已变化。");
                var manifest=UpdatePackageProtocol.ValidateDirectory(directory,completion.Version);
                var allowedDirectories=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(var file in manifest.Files)
                    for(string? parent=Path.GetDirectoryName(file.Path.Replace('/',Path.DirectorySeparatorChar));!string.IsNullOrEmpty(parent);parent=Path.GetDirectoryName(parent))allowedDirectories.Add(parent);
                foreach(string child in Directory.EnumerateDirectories(directory,"*",SearchOption.AllDirectories))
                    if(!allowedDirectories.Contains(Path.GetRelativePath(directory,child)))throw new InvalidDataException("更新缓存包含未登记目录。");
                var files=Directory.EnumerateFiles(directory,"*",SearchOption.AllDirectories).ToArray();
                var held=new List<FileStream>();
                try{foreach(string file in files){CheckPath(file);held.Add(new FileStream(file,FileMode.Open,FileAccess.ReadWrite,FileShare.None));}}
                finally{foreach(var handle in held)handle.Dispose();}
                // Every file was declared and validated, and none is loaded or held
                // by an updater. Unknown entries stop validation before deletion.
                foreach(string file in files){CheckPath(file);File.Delete(file);}
                foreach(string child in Directory.EnumerateDirectories(directory,"*",SearchOption.AllDirectories).OrderByDescending(p=>p.Length)){CheckPath(child);Directory.Delete(child,false);}
                CheckPath(directory);Directory.Delete(directory,false);File.Delete(marker);
            }
            catch(Exception error) when(error is IOException or UnauthorizedAccessException or InvalidDataException){log?.Invoke("更新缓存保留："+error.Message);}
        }
    }
    private static string Hash(string path){using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream));}
    private static void CheckPath(string path)
    {
        for(string? current=Path.GetFullPath(path);current is not null;current=Path.GetDirectoryName(current))
            if(File.Exists(current)||Directory.Exists(current))UpdatePackageProtocol.RejectLink(current);
    }
}
