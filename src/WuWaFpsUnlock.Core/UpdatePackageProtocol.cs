using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace WuWaFpsUnlock.Core;

public sealed record UpdatePackageFile(string Path,long Size,string Sha256);
public sealed record UpdatePackageManifest(int ProtocolVersion,string ProductId,string Version,string Updater,UpdatePackageFile[] Files);
public static class UpdatePackageProtocol
{
    public const int MaxFiles=4096;
    public const long MaxFileBytes=512L*1024*1024,MaxTotalBytes=1024L*1024*1024;
    public static readonly string[] RequiredFiles=["更新.exe","update-payload/WuWaFpsUnlock.exe","update-payload/components/fps/ww_plugin_base.dll","update-payload/components/PROVENANCE.json","update-payload/payload/files/addon/renodx-mfgunlock.addon64","update-payload/payload/addon-source.json"];
    public static UpdatePackageManifest ValidateDirectory(string directory,string? expectedVersion=null,CancellationToken token=default)
    {
        var root=Path.GetFullPath(directory);RejectLink(root);
        int entryCount=0;
        var actual=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        void Walk(string dir)
        {
            foreach(var entry in Directory.EnumerateFileSystemEntries(dir))
            {
                token.ThrowIfCancellationRequested();
                if(++entryCount>MaxFiles)throw new InvalidDataException("更新包目录项过多。");
                RejectLink(entry);
                if(Directory.Exists(entry)){Walk(entry);continue;}
                string name=Path.GetRelativePath(root,entry).Replace('\\','/');ValidatePath(name);
                if(actual.Count>=MaxFiles||!actual.TryAdd(name,entry))throw new InvalidDataException("更新包文件过多或路径重复。");
            }
        }
        Walk(root);
        actual.Remove("回退.cmd"); // Local worker-generated retry entry; ZIP intake never permits it.
        if(!actual.Remove("update-manifest.json",out var manifestPath))throw new InvalidDataException("更新包缺少协议清单。");
        if(new FileInfo(manifestPath).Length>2*1024*1024)throw new InvalidDataException("更新清单过大。");
        var manifest=JsonSerializer.Deserialize<UpdatePackageManifest>(File.ReadAllText(manifestPath),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidDataException("更新清单为空。");
        if(manifest.ProtocolVersion!=1||manifest.ProductId!="WuWaFpsUnlock"||manifest.Updater!="更新.exe"||!IsVersion(manifest.Version)||manifest.Files is null)
            throw new InvalidDataException("不支持的更新协议、产品或版本。");
        if(expectedVersion is not null&&!string.Equals(manifest.Version,expectedVersion,StringComparison.Ordinal))throw new InvalidDataException("更新包版本与发布版本不匹配。");
        var declared=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;
        foreach(var file in manifest.Files)
        {
            if(file is null)throw new InvalidDataException("空更新文件条目。");
            ValidatePath(file.Path);
            if(!Allowed(file.Path)||!declared.Add(file.Path)||file.Size<0||file.Size>MaxFileBytes||file.Sha256 is null||!Regex.IsMatch(file.Sha256,"^[0-9a-fA-F]{64}$"))throw new InvalidDataException("非法更新文件条目："+file.Path);
            total=checked(total+file.Size);if(total>MaxTotalBytes)throw new InvalidDataException("更新包过大。");
            if(!actual.Remove(file.Path,out var path)||new FileInfo(path).Length!=file.Size)throw new InvalidDataException("更新文件缺失或大小不符："+file.Path);
            using var stream=File.OpenRead(path);
            using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer=new byte[81920];int read;
            while((read=stream.Read(buffer))!=0){token.ThrowIfCancellationRequested();hash.AppendData(buffer,0,read);}
            if(!Convert.ToHexString(hash.GetHashAndReset()).Equals(file.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("更新文件哈希不符："+file.Path);
        }
        if(actual.Count!=0||RequiredFiles.Any(x=>!declared.Contains(x)))throw new InvalidDataException("更新包存在未声明文件或缺少必需文件。");
        return manifest;
    }
    public static bool IsVersion(string? value)=>value is not null&&Regex.IsMatch(value,@"^\d+\.\d+\.\d+(?:RC\d*)?$");
    public static void RejectLink(string path){if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("更新路径禁止链接："+path);}
    public static void ValidatePath(string path)
    {
        if(string.IsNullOrEmpty(path)||path.Length>240||path.Contains('\\')||path.StartsWith('/')||path.Contains(':'))throw new InvalidDataException("更新路径无效。");
        foreach(var part in path.Split('/'))
            if(part.Length==0||part is "." or ".."||part.EndsWith('.')||part.EndsWith(' ')||part.Any(c=>c<32||"<>\"|?*".Contains(c))||Regex.IsMatch(part,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)",RegexOptions.IgnoreCase))throw new InvalidDataException("更新路径无效："+path);
    }
    public static bool Allowed(string name)
    {
        if(name is "更新.exe" or "更新说明.txt")return true;
        const string prefix="update-payload/";
        if(!name.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))return false;
        string path=name[prefix.Length..];
        return RequiredFiles.Skip(1).Any(x=>x.Equals(name,StringComparison.OrdinalIgnoreCase))||path.StartsWith("licenses/",StringComparison.OrdinalIgnoreCase);
    }
}
