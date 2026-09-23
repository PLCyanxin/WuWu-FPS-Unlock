using System.Security.Cryptography;
using System.Text.Json;

internal static partial class Program
{
    const string RollbackCompleted = "rollback.completed";
    static FileStream AcquireInstallationLock(string root)
    {
        string path=Scoped(root,".update-operation.lock");
        return new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
    }
    static void RequireUnusedBackup(string backup,Snapshot snapshot)
    {
        string marker=Scoped(backup,RollbackCompleted);
        if(File.Exists(marker)||Directory.Exists(marker))throw new InvalidDataException("此备份已经完成回退，不能再次使用。");
        if(!snapshot.Complete)throw new InvalidDataException("此备份对应的更新未完成，不能回退。");
    }
    static void MarkRollbackCompleted(string backup)
    {
        string marker=Scoped(backup,RollbackCompleted),temp=Scoped(backup,"rollback-completed-"+Guid.NewGuid().ToString("N")+".tmp");
        string metadata=Scoped(backup,"snapshot.json"),checksum=Scoped(backup,"snapshot.sha256");
        byte[] originalMetadata=File.ReadAllBytes(metadata),originalChecksum=File.ReadAllBytes(checksum);
        var snapshot=ReadSnapshot(backup);RequireUnusedBackup(backup,snapshot);
        bool metadataChanged=false;
        try
        {
            var bytes=JsonSerializer.SerializeToUtf8Bytes(new{schema=1,completedAt=DateTimeOffset.UtcNow});
            using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(bytes);file.Flush(true);}
            // Complete=false also blocks the original Schema-1 rollback executables.
            metadataChanged=true;WriteSnapshot(backup,snapshot with{Complete=false});
            File.Move(temp,marker,false);
        }
        catch
        {
            if(metadataChanged){File.WriteAllBytes(metadata,originalMetadata);File.WriteAllBytes(checksum,originalChecksum);}
            throw;
        }
        finally{try{if(File.Exists(temp))File.Delete(temp);}catch{}}
    }

    static void PrunePreviousBackups(string root,string currentBackup)
    {
        // Revalidate the new recoverable snapshot before touching an older one.
        var current=ReadSnapshot(currentBackup);RequireUnusedBackup(currentBackup,current);
        if(!Path.GetFullPath(root).Equals(Path.GetFullPath(current.Root),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("新备份与安装目录不一致。");
        foreach(string candidate in Directory.EnumerateDirectories(root,"update-backup-*",SearchOption.TopDirectoryOnly))
        {
            if(Path.GetFullPath(candidate).Equals(Path.GetFullPath(currentBackup),StringComparison.OrdinalIgnoreCase))continue;
            try
            {
                var previous=ReadSnapshot(candidate);
                if((!previous.Complete && !File.Exists(Scoped(candidate,RollbackCompleted))) || !Path.GetFullPath(previous.Root).Equals(Path.GetFullPath(root),StringComparison.OrdinalIgnoreCase))continue;
                DeleteVerifiedBackup(candidate,previous);
                Console.WriteLine("已清理上一次更新备份："+candidate);
            }
            catch(Exception error){Console.WriteLine("旧备份保留，未能安全清理："+candidate+"；"+error.Message);}
        }
    }
    static void DeleteVerifiedBackup(string backup,Snapshot snapshot)
    {
        ValidateSnapshot(backup,snapshot);
        if(!snapshot.Files.Any(f=>f.Relative=="WuWaFpsUnlock.exe")||!snapshot.Updated.Any(f=>f.Relative=="WuWaFpsUnlock.exe"))throw new InvalidDataException("目录不含完整程序备份身份。");
        var allowed=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"snapshot.json","snapshot.sha256","files.txt","rollback.cmd","WuWaUpdater.exe",RollbackCompleted};
        foreach(var file in snapshot.Files)allowed.Add("snapshot/"+file.Relative);
        foreach(var file in snapshot.Updated){allowed.Add("original/"+file.Relative);allowed.Add("staged/"+file.Relative);}
        var allowedDirectories=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"snapshot","original","staged"};
        foreach(string path in allowed)
            for(string? dir=Path.GetDirectoryName(path)?.Replace('\\','/');!string.IsNullOrEmpty(dir);dir=Path.GetDirectoryName(dir)?.Replace('\\','/'))allowedDirectories.Add(dir);
        foreach(string directory in snapshot.Directories)allowedDirectories.Add("snapshot/"+directory);
        var files=new List<string>();var dirs=new List<string>();
        void Walk(string directory)
        {
            NoLinks(directory);
            foreach(string path in Directory.EnumerateFileSystemEntries(directory))
            {
                NoLinks(path);string relative=Path.GetRelativePath(backup,path).Replace('\\','/');
                if(Directory.Exists(path))
                {
                    if(!allowedDirectories.Contains(relative))throw new IOException("备份中存在未登记目录，已保留。");
                    Walk(path);dirs.Add(path);
                }
                else
                {
                    if(!allowed.Contains(relative))throw new IOException("备份中存在未登记文件，已保留。");
                    files.Add(path);
                }
            }
        }
        Walk(backup);
        foreach(string file in files){NoLinks(file);File.Delete(file);}
        foreach(string dir in dirs){NoLinks(dir);Directory.Delete(dir,false);}
        NoLinks(backup);Directory.Delete(backup,false);
    }
    static int WaitThenRollback(string pidText,string expectedLauncher,string backupDirectory)
    {
        if(!int.TryParse(pidText,out int pid)||pid<=0)throw new ArgumentException("启动器 PID 无效。");
        string backup=Path.TrimEndingDirectorySeparator(Path.GetFullPath(backupDirectory));
        var snapshot=ReadSnapshot(backup);RequireUnusedBackup(backup,snapshot);
        string exe=Scoped(snapshot.Root,"WuWaFpsUnlock.exe");MatchExpectedLauncher(exe,expectedLauncher);ProductVersion(exe);
        ProductVersion(Scoped(backup,"snapshot/WuWaFpsUnlock.exe"));
        WaitForLauncherExit(pid,exe,OpenLauncherProcess);
        return Rollback(backup);
    }
}
