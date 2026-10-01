using WuWaFpsUnlock.Core;

internal static partial class Program
{
    sealed record AttemptFile(string Relative,string? Before,string? Desired);
    sealed record AttemptCompletion(int Schema,string Root,string OperationsHash,List<AttemptFile> Files);
    static void MarkAttemptComplete(string root,string directory,List<RestoreFile> operations) =>
        DurableJson.Save(Scoped(directory,"completed.state.json"),new AttemptCompletion(1,Path.GetFullPath(root),FileHash(Scoped(directory,"operations.json")),
            operations.Select(o=>new AttemptFile(o.Relative,o.Before,o.Desired)).ToList()));
    static void PruneCompletedRollbackAttempts(string root)
    {
        bool keepNewest=true;
        foreach(string directory in Directory.EnumerateDirectories(root,"rollback-attempt-*",SearchOption.TopDirectoryOnly).OrderByDescending(d=>File.GetLastWriteTimeUtc(Path.Combine(d,"completed.state.json"))))
        {
            try
            {
                if(!Guid.TryParseExact(Path.GetFileName(directory)["rollback-attempt-".Length..],"N",out _)||!File.Exists(Scoped(directory,"completed.state.json")))continue;
                var completed=DurableJson.Read<AttemptCompletion>(Scoped(directory,"completed.state.json"));
                if(completed.Schema!=1||!Path.GetFullPath(completed.Root).Equals(Path.GetFullPath(root),StringComparison.OrdinalIgnoreCase)
                    ||FileHash(Scoped(directory,"operations.json"))!=completed.OperationsHash)throw new InvalidDataException("回退事务归属或清单不符。");
                var allowed=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){["operations.json"]=completed.OperationsHash,["completed.state.json"]=FileHash(Scoped(directory,"completed.state.json"))};
                foreach(var file in completed.Files)
                {
                    if(!SnapshotPath(file.Relative)||file.Relative.StartsWith("data/",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("回退事务文件范围无效。");
                    if(file.Before is not null)allowed.Add("before/"+file.Relative,file.Before);
                    if(file.Desired is not null)allowed.Add("staged/"+file.Relative,file.Desired);
                }
                var allowedDirectories=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(string file in allowed.Keys)
                    for(string? parent=Path.GetDirectoryName(file.Replace('/',Path.DirectorySeparatorChar));!string.IsNullOrEmpty(parent);parent=Path.GetDirectoryName(parent))allowedDirectories.Add(parent);
                foreach(string child in Directory.EnumerateDirectories(directory,"*",SearchOption.AllDirectories))
                    if(!allowedDirectories.Contains(Path.GetRelativePath(directory,child)))throw new InvalidDataException("回退事务包含未登记目录。");
                var actual=Enumerate(directory).ToList();
                foreach(string file in actual)
                {
                    string relative=Path.GetRelativePath(directory,file).Replace('\\','/');
                    if(!allowed.TryGetValue(relative,out string? hash)||FileHash(file)!=hash)throw new InvalidDataException("回退事务包含已变化或未登记文件。");
                }
                if(keepNewest){keepNewest=false;continue;}
                var held=new List<FileStream>();
                try{foreach(string file in actual)held.Add(new FileStream(file,FileMode.Open,FileAccess.ReadWrite,FileShare.None));}
                finally{foreach(var handle in held)handle.Dispose();}
                foreach(string file in actual){NoLinks(file);File.Delete(file);}
                foreach(string child in Directory.EnumerateDirectories(directory,"*",SearchOption.AllDirectories).OrderByDescending(p=>p.Length)){NoLinks(child);Directory.Delete(child,false);}
                NoLinks(directory);Directory.Delete(directory,false);
            }
            catch(Exception error){Console.WriteLine("回退事务证据保留："+error.Message);}
        }
    }
}
