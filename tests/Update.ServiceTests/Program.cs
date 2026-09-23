using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WuWaFpsUnlock.Core;
int passed=0;string root=Path.Combine(Path.GetTempPath(),"更新服务测试-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
async Task Test(string name,Func<Task> action){await action();Console.WriteLine("PASS "+name);passed++;}
void Require(bool value){if(!value)throw new Exception("assertion failed");}
async Task Reject(Func<Task> action){try{await action();}catch(Exception e)when(e is InvalidDataException or IOException or OperationCanceledException){return;}throw new Exception("Expected rejection");}
string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
Dictionary<string,byte[]> Files()=>UpdatePackageProtocol.RequiredFiles.ToDictionary(x=>x,x=>Encoding.UTF8.GetBytes("fixture-only: "+x));
byte[] Package(Dictionary<string,byte[]> files,string version="1.1.1",string product="WuWaFpsUnlock",int protocol=1,bool manifest=true,Action<ZipArchive>? extra=null)
{
 using var stream=new MemoryStream();using(var zip=new ZipArchive(stream,ZipArchiveMode.Create,true))
 {
  foreach(var (path,bytes) in files){using var target=zip.CreateEntry(path).Open();target.Write(bytes);}
  if(manifest){using var target=zip.CreateEntry("update-manifest.json").Open();JsonSerializer.Serialize(target,new{protocolVersion=protocol,productId=product,version,updater="更新.exe",files=files.Select(x=>new{path=x.Key,size=x.Value.Length,sha256=Hash(x.Value)})});}
  extra?.Invoke(zip);
 }return stream.ToArray();
}
string Asset(string tag,string file)=>$"https://github.com/PLCyanxin/WuWu-FPS-Unlock/releases/download/{tag}/{file}";
string filename="WuWaFPSUnlock-1.1.1-update.zip";
UpdateRelease release=new("v1.1.1","1.1.1","notes",new(Asset("v1.1.1",filename)),new(Asset("v1.1.1","SHA256SUMS.txt")),filename);
GitHubUpdateService Service(byte[] zip,string? sums=null,string? list=null)=>new(new HttpClient(new FixtureHandler(uri=>uri.Host=="api.github.com"?Encoding.UTF8.GetBytes(list??"[]"):uri.AbsolutePath.EndsWith("SHA256SUMS.txt")?Encoding.UTF8.GetBytes(sums??$"{Hash(zip)}  {filename}\n"):zip)));
object Release(string tag,bool draft=false,bool prerelease=false)=>new{tag_name=tag,draft,prerelease,body="plain notes",assets=new[]{new{name=$"WuWaFPSUnlock-{tag[1..]}-update.zip",browser_download_url=Asset(tag,$"WuWaFPSUnlock-{tag[1..]}-update.zip")},new{name="SHA256SUMS.txt",browser_download_url=Asset(tag,"SHA256SUMS.txt")}}};
try
{
 await Test("formal outranks RC, RC2 outranks RC1 and numbers outrank text",()=>{Require(GitHubUpdateService.CompareVersions("1.1.1","1.1.1RC")>0);Require(GitHubUpdateService.CompareVersions("1.1.1RC2","1.1.1RC1")>0);Require(GitHubUpdateService.CompareVersions("1.10.0RC","1.9.9")>0);return Task.CompletedTask;});
 await Test("release list includes prerelease and ignores drafts, chooses greatest",async()=>{var list=JsonSerializer.Serialize(new[]{Release("v1.2.0RC",prerelease:true),Release("v9.0.0",draft:true),Release("v1.1.1")});Require((await Service([],list:list).CheckAsync("1.0.0"))?.Version=="1.2.0RC");});
 await Test("current formal ignores older RC",async()=>Require(await Service([],list:JsonSerializer.Serialize(new[]{Release("v1.1.1RC")})).CheckAsync("1.1.1") is null));
 await Test("fake HTTP check and stage full pipeline under Chinese path",async()=>{var svc=Service(Package(Files()),list:JsonSerializer.Serialize(new[]{Release("v1.1.1")}));var found=await svc.CheckAsync("1.1.1RC");string exe=await svc.StageAsync(found!,root);Require(File.Exists(exe)&&Path.GetRelativePath(root,exe).StartsWith(".updates"));UpdatePackageProtocol.ValidateDirectory(Path.GetDirectoryName(exe)!,"1.1.1");File.WriteAllText(Path.Combine(Path.GetDirectoryName(exe)!,"回退.cmd"),"local-only");UpdatePackageProtocol.ValidateDirectory(Path.GetDirectoryName(exe)!,"1.1.1");});
 await Test("bad outer hash rejected",()=>Reject(()=>Service(Package(Files()),new string('0',64)+"  "+filename).StageAsync(release,root)));
 await Test("duplicate checksum rejected",()=>Reject(()=>Service(Package(Files()),$"{new string('0',64)}  {filename}\n{new string('0',64)}  {filename}").StageAsync(release,root)));
 await Test("old no-manifest package rejected",()=>Reject(()=>Service(Package(Files(),manifest:false)).StageAsync(release,root)));
 await Test("wrong product rejected",()=>Reject(()=>Service(Package(Files(),product:"Other")).StageAsync(release,root)));
 await Test("unknown protocol rejected",()=>Reject(()=>Service(Package(Files(),protocol:2)).StageAsync(release,root)));
 await Test("release package version mismatch rejected",()=>Reject(()=>Service(Package(Files(),version:"1.1.2RC")).StageAsync(release,root)));
 await Test("undeclared root rollback command rejected at zip intake",()=>Reject(()=>Service(Package(Files(),extra:z=>z.CreateEntry("回退.cmd"))).StageAsync(release,root)));
 await Test("traversal rejected",()=>Reject(()=>Service(Package(Files(),extra:z=>z.CreateEntry("../escape.exe"))).StageAsync(release,root)));
 await Test("case duplicate rejected",()=>Reject(()=>Service(Package(Files(),extra:z=>z.CreateEntry("update-payload/WUWAFPSUNLOCK.exe"))).StageAsync(release,root)));
 await Test("zip symlink rejected",()=>Reject(()=>Service(Package(Files(),extra:z=>z.CreateEntry("update-payload/licenses/link").ExternalAttributes=unchecked((int)0xa1ff0000))).StageAsync(release,root)));
 await Test("missing required payload rejected",()=>{var files=Files();files.Remove("update-payload/WuWaFpsUnlock.exe");return Reject(()=>Service(Package(files)).StageAsync(release,root));});
 await Test("user data rejected",()=>Reject(()=>Service(Package(Files(),extra:z=>z.CreateEntry("update-payload/data/settings.json"))).StageAsync(release,root)));
 await Test("nonrepository asset rejected",()=>Reject(()=>Service(Package(Files())).StageAsync(release with{PackageUrl=new("https://example.com/evil.zip")},root)));
 await Test("cancelled download rejected",()=>Reject(()=>Service(Package(Files())).StageAsync(release,root,new CancellationToken(true))));
 await Test("safe path rules",()=>{foreach(var path in new[]{"a/../b","/abs","C:/x","a\\b","licenses/NUL.txt","licenses/a.","licenses/a ","a::b"}){try{UpdatePackageProtocol.ValidatePath(path);throw new Exception(path);}catch(InvalidDataException){}}return Task.CompletedTask;});
 await Test("tamper and undeclared files rejected by worker shared validator",async()=>{string exe=await Service(Package(Files())).StageAsync(release,root);string dir=Path.GetDirectoryName(exe)!;File.AppendAllText(exe,"tamper");await Reject(()=>{UpdatePackageProtocol.ValidateDirectory(dir);return Task.CompletedTask;});File.WriteAllBytes(exe,Files()["更新.exe"]);File.WriteAllText(Path.Combine(dir,"unlisted.txt"),"x");await Reject(()=>{UpdatePackageProtocol.ValidateDirectory(dir);return Task.CompletedTask;});});
 if(args.Length==2&&args[0]=="--package")
 {
  await Test("locally built release zip stages through launcher download pipeline",async()=>
  {
   byte[] zip=await File.ReadAllBytesAsync(args[1]);
   string exe=await Service(zip).StageAsync(release,root);
   Require(File.Exists(exe));UpdatePackageProtocol.ValidateDirectory(Path.GetDirectoryName(exe)!,release.Version);
  });
 }
 Console.WriteLine($"{passed}/{passed} passed. Fake HTTP only; no updater or game execution.");
}
finally{Directory.Delete(root,true);}
sealed class FixtureHandler(Func<Uri,byte[]> fixture):HttpMessageHandler
{
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){token.ThrowIfCancellationRequested();return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(fixture(request.RequestUri!))});}
}
