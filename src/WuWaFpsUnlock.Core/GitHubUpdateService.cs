using System.IO.Compression;
using System.Net.Http;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace WuWaFpsUnlock.Core;

public sealed record UpdateRelease(string Tag,string Version,string Notes,Uri PackageUrl,Uri ChecksumsUrl,string PackageFileName);
public sealed class GitHubUpdateService(HttpClient http)
{
    private const string Repository="PLCyanxin/WuWu-FPS-Unlock";
    public async Task<UpdateRelease?> CheckAsync(string currentVersion,CancellationToken token=default)
    {
        _=ParseVersion(currentVersion);UpdateRelease? best=null;
        for(int page=1;page<=10;page++)
        {
            using var response=await SendAsync(new Uri($"https://api.github.com/repos/{Repository}/releases?per_page=100&page={page}"),token);
            using var data=JsonDocument.Parse(await ReadBoundedAsync(response,8*1024*1024,token));
            var releases=data.RootElement;
            if(releases.ValueKind!=JsonValueKind.Array)throw new InvalidDataException("发布列表格式不符。");
            foreach(var release in releases.EnumerateArray())
            {
                if(release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean())continue;
                string tag=release.GetProperty("tag_name").GetString()??"";
                string version=tag.StartsWith('v')?tag[1..]:tag;
                if(!UpdatePackageProtocol.IsVersion(version)||CompareVersions(version,currentVersion)<=0||(best is not null&&CompareVersions(version,best.Version)<=0))continue;
                string filename=$"WuWaFPSUnlock-{version}-update.zip";
                Uri? package=null,checksums=null;
                foreach(var asset in release.GetProperty("assets").EnumerateArray())
                {
                    string? name=asset.GetProperty("name").GetString();
                    if(name!=filename&&name!="SHA256SUMS.txt")continue;
                    var url=new Uri(asset.GetProperty("browser_download_url").GetString()!);
                    ValidateAsset(url,tag,name);
                    if(name==filename){if(package is not null)throw new InvalidDataException("重复更新资产。");package=url;}
                    else{if(checksums is not null)throw new InvalidDataException("重复校验资产。");checksums=url;}
                }
                if(package is not null&&checksums is not null)best=new(tag,version,release.TryGetProperty("body",out var body)?body.GetString()??"":"",package,checksums,filename);
            }
            if(releases.GetArrayLength()<100)return best;
        }
        throw new InvalidDataException("发布列表超过安全分页上限。");
    }
    public async Task<string> StageAsync(UpdateRelease release,string appRoot,CancellationToken token=default)
    {
        if(!UpdatePackageProtocol.IsVersion(release.Version)||release.Tag!=(release.Tag.StartsWith('v')?"v":"")+release.Version||release.PackageFileName!=$"WuWaFPSUnlock-{release.Version}-update.zip")throw new InvalidDataException("发布版本无效。");
        ValidateAsset(release.PackageUrl,release.Tag,release.PackageFileName);ValidateAsset(release.ChecksumsUrl,release.Tag,"SHA256SUMS.txt");
        string root=Path.GetFullPath(appRoot);UpdatePackageProtocol.RejectLink(root);
        string cache=Path.Combine(root,".updates");Directory.CreateDirectory(cache);UpdatePackageProtocol.RejectLink(cache);
        string nonce=Guid.NewGuid().ToString("N"),directory=Path.Combine(cache,nonce),zipPath=Path.Combine(cache,nonce+".zip");
        Directory.CreateDirectory(directory);
        try
        {
            string expected;
            using(var sums=await SendAsync(release.ChecksumsUrl,token))
            {
                string content=System.Text.Encoding.UTF8.GetString(await ReadBoundedAsync(sums,1024*1024,token));
                var matches=content.Split('\n').Select(x=>Regex.Match(x.TrimEnd('\r'),@"^([0-9a-fA-F]{64})\s+\*?(.+)$")).Where(x=>x.Success&&x.Groups[2].Value==release.PackageFileName).ToArray();
                if(matches.Length!=1)throw new InvalidDataException("SHA256SUMS缺少唯一更新包哈希。");expected=matches[0].Groups[1].Value;
            }
            using(var response=await SendAsync(release.PackageUrl,token))
            await using(var stream=await response.Content.ReadAsStreamAsync(token))
            await using(var file=new FileStream(zipPath,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true))
            {
                var buffer=new byte[81920];long total=0;int n;
                while((n=await stream.ReadAsync(buffer,token))!=0){total+=n;if(total>UpdatePackageProtocol.MaxTotalBytes)throw new InvalidDataException("下载更新包过大。");await file.WriteAsync(buffer.AsMemory(0,n),token);}
            }
            await using(var file=File.OpenRead(zipPath))
                if(!Convert.ToHexString(await SHA256.HashDataAsync(file,token)).Equals(expected,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("下载更新包SHA256不匹配。");
            using(var archive=ZipFile.OpenRead(zipPath))
            {
                if(archive.Entries.Count>UpdatePackageProtocol.MaxFiles)throw new InvalidDataException("ZIP文件过多。");
                var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;
                foreach(var entry in archive.Entries)
                {
                    token.ThrowIfCancellationRequested();string name=entry.FullName;
                    bool folder=name.EndsWith('/');string normalized=folder?name[..^1]:name;
                    UpdatePackageProtocol.ValidatePath(normalized);
                    if(!names.Add(normalized)||(entry.ExternalAttributes&0x400)!=0||((entry.ExternalAttributes>>16)&0xf000)==0xa000)throw new InvalidDataException("ZIP路径重复或包含链接。");
                    if(entry.Length>UpdatePackageProtocol.MaxFileBytes||(total=checked(total+entry.Length))>UpdatePackageProtocol.MaxTotalBytes)throw new InvalidDataException("ZIP解压大小超过限制。");
                    string target=Path.Combine(directory,normalized.Replace('/',Path.DirectorySeparatorChar));
                    if(folder){if(entry.Length!=0)throw new InvalidDataException("非法ZIP目录。");Directory.CreateDirectory(target);continue;}
                    if(name!="update-manifest.json"&&!UpdatePackageProtocol.Allowed(name))throw new InvalidDataException("ZIP含未允许的更新路径。");
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    await using var input=entry.Open();await using var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true);
                    var buffer=new byte[81920];long copied=0;int n;
                    while((n=await input.ReadAsync(buffer,token))!=0){copied+=n;if(copied>entry.Length)throw new InvalidDataException("ZIP声明大小不符。");await output.WriteAsync(buffer.AsMemory(0,n),token);}
                    if(copied!=entry.Length)throw new InvalidDataException("ZIP文件截断。");
                }
            }
            token.ThrowIfCancellationRequested();
            await Task.Run(()=>UpdatePackageProtocol.ValidateDirectory(directory,release.Version,token),token);
            token.ThrowIfCancellationRequested();return Path.Combine(directory,"更新.exe");
        }
        catch{try{Directory.Delete(directory,true);}catch{}throw;}
        finally{try{File.Delete(zipPath);}catch{}}
    }
    private async Task<HttpResponseMessage> SendAsync(Uri uri,CancellationToken token)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,uri);request.Headers.UserAgent.ParseAdd("WuWaFpsUnlock-Updater/1.0");
        var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
        if(!response.IsSuccessStatusCode){response.Dispose();throw new HttpRequestException("更新服务器返回HTTP错误。");}
        return response;
    }
    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response,int limit,CancellationToken token)
    {
        await using var stream=await response.Content.ReadAsStreamAsync(token);using var output=new MemoryStream();var buffer=new byte[81920];int n;
        while((n=await stream.ReadAsync(buffer,token))!=0){if(output.Length+n>limit)throw new InvalidDataException("更新元数据过大。");output.Write(buffer,0,n);}return output.ToArray();
    }
    private static void ValidateAsset(Uri uri,string tag,string name)
    {
        string expected=$"https://github.com/{Repository}/releases/download/{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(name)}";
        if(!uri.IsAbsoluteUri||uri.AbsoluteUri!=expected)throw new InvalidDataException("更新资产不属于固定仓库的发布。");
    }
    private static (BigInteger A,BigInteger B,BigInteger C,BigInteger? Rc) ParseVersion(string value)
    {
        if(!UpdatePackageProtocol.IsVersion(value))throw new InvalidDataException("版本格式无效。");
        var parts=value.Split("RC");var numbers=parts[0].Split('.');return(BigInteger.Parse(numbers[0]),BigInteger.Parse(numbers[1]),BigInteger.Parse(numbers[2]),parts.Length==1?null:parts[1].Length==0?BigInteger.One:BigInteger.Parse(parts[1]));
    }
    public static int CompareVersions(string left,string right)
    {
        var a=ParseVersion(left);var b=ParseVersion(right);int n=a.A.CompareTo(b.A);if(n!=0)return n;n=a.B.CompareTo(b.B);if(n!=0)return n;n=a.C.CompareTo(b.C);if(n!=0)return n;
        if(a.Rc is null)return b.Rc is null?0:1;if(b.Rc is null)return -1;return a.Rc.Value.CompareTo(b.Rc.Value);
    }
}
