using System.Text;

internal static partial class Program
{
    // This is cleanup-only compatibility, not a migration into rollback ownership.
    static void DeleteLegacyBackup(string root,string backup,Action<string>? verifyProduct=null)
    {
        NoLinks(root);NoLinks(backup);
        if(!string.Equals(Path.GetDirectoryName(Path.GetFullPath(backup)),Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)),StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(backup).StartsWith("update-backup-",StringComparison.Ordinal))
            throw new InvalidDataException("旧备份不在本安装目录内。");
        string journal=Scoped(backup,"files.txt");
        using var journalLock=new FileStream(journal,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(journalLock.Length>1024*1024)throw new InvalidDataException("旧备份登记文件过大。");
        using var reader=new StreamReader(journalLock,new UTF8Encoding(false,true),true,1024,leaveOpen:true);
        var expected=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var registered=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(string line in reader.ReadToEnd().Split('\n',StringSplitOptions.RemoveEmptyEntries))
        {
            var fields=line.TrimEnd('\r').Split('\t');
            if(fields.Length!=3 || !fields[1].StartsWith("old=",StringComparison.Ordinal) || !fields[2].StartsWith("new=",StringComparison.Ordinal))
                throw new InvalidDataException("旧备份登记格式无效。");
            string relative=fields[0],old=fields[1][4..],updated=fields[2][4..];
            if(!Allowed(relative)||relative.Contains('\\')||!registered.Add(relative))throw new InvalidDataException("旧备份登记路径无效或重复。");
            Scoped(root,relative);
            static bool IsHash(string value)=>value.Length==64&&value.All(Uri.IsHexDigit);
            if(!IsHash(updated)||(old!="(absent)"&&!IsHash(old)))throw new InvalidDataException("旧备份登记哈希无效。");
            if(old!="(absent)")expected.Add("original/"+relative,old);
            string staged=Scoped(backup,"staged/"+relative);
            if(File.Exists(staged))expected.Add("staged/"+relative,updated);
        }
        if(!expected.ContainsKey("original/WuWaFpsUnlock.exe"))throw new InvalidDataException("旧备份缺少程序身份。");
        (verifyProduct??(path=>{ProductVersion(path);}))(Scoped(backup,"original/WuWaFpsUnlock.exe"));
        var allowedDirs=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"original","staged"};
        // Empty staging directories are normal after atomic moves consume their files.
        foreach(string relative in registered)
            foreach(string prefix in new[]{"original/","staged/"})
                for(string? dir=Path.GetDirectoryName(prefix+relative)?.Replace('\\','/');!string.IsNullOrEmpty(dir);dir=Path.GetDirectoryName(dir)?.Replace('\\','/'))allowedDirs.Add(dir);
        var files=new List<string>();var dirs=new List<string>();var held=new List<FileStream>();
        try
        {
            foreach(var pair in expected)
            {
                var stream=new FileStream(Scoped(backup,pair.Key),FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);
                held.Add(stream);
                if(!Hash(stream).Equals(pair.Value,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("旧备份文件哈希已改变："+pair.Key);
            }
            void Walk(string directory)
            {
                NoLinks(directory);
                foreach(string path in Directory.EnumerateFileSystemEntries(directory))
                {
                    NoLinks(path);string relative=Path.GetRelativePath(backup,path).Replace('\\','/');
                    if(Directory.Exists(path))
                    {
                        if(!allowedDirs.Contains(relative))throw new InvalidDataException("旧备份中存在未登记目录。");
                        Walk(path);dirs.Add(path);
                    }
                    else
                    {
                        if(relative!="files.txt"&&!expected.ContainsKey(relative))throw new InvalidDataException("旧备份中存在未登记文件。");
                        files.Add(path);
                    }
                }
            }
            Walk(backup);
            // Validation finishes before any deletion. The journal remains until the end.
            foreach(string file in files.Where(f=>!f.Equals(journal,StringComparison.OrdinalIgnoreCase)))
            {NoLinks(file);File.Delete(file);}
            foreach(var stream in held)stream.Dispose();
            foreach(string directory in dirs){NoLinks(directory);Directory.Delete(directory,false);}
            journalLock.Dispose();NoLinks(journal);File.Delete(journal);NoLinks(backup);Directory.Delete(backup,false);
        }
        finally{foreach(var stream in held)stream.Dispose();}
    }
}
