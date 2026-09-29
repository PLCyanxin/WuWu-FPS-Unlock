using System.Net;
using System.Text;
using System.Text.Json;
using System.IO.Compression;
using WuWaFpsUnlock.Services;

if (args.Length == 2)
{
 using var probe = new OfficialPredownloadService();
 Console.WriteLine(JsonSerializer.Serialize(await probe.CheckAsync(args[0], args[1])));
 return;
}

var fixture = Path.Combine(Path.GetTempPath(), "wuwa-predownload-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixture);
var root = Path.Combine(fixture,"Wuthering Waves Game");
var shipping=Path.Combine(root,"Client","Binaries","Win64","Client-Win64-Shipping.exe");
Directory.CreateDirectory(Path.GetDirectoryName(shipping)!);File.WriteAllText(shipping,"");
File.WriteAllText(Path.Combine(root,"Wuthering Waves.exe"),"");File.WriteAllText(Path.Combine(fixture,"launcher.exe"),"");
var versionDir=Path.Combine(fixture,"2.6.5.0");Directory.CreateDirectory(Path.Combine(versionDir,"Assets"));
File.WriteAllText(Path.Combine(versionDir,"launcher_main.dll"),"");File.WriteAllText(Path.Combine(versionDir,"launcher_main.exe"),"");
var endpoint="https://prod-cn-alicdn-gamestarter.kurogame.com/launcher/game/G152/channel/index.json";
void Channel(string? url=null,string app="10003") {
 var bytes=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{appId=app,gameId="G152",gameDirName="Wuthering Waves Game",gameExeName="Wuthering Waves.exe",configUrl=url??endpoint}));
 for(int i=0;i<bytes.Length;i++)bytes[i]^=99;
 File.WriteAllText(Path.Combine(versionDir,"Assets","KRApp.conf"),Convert.ToBase64String(bytes));
}
void Local(string version="3.6.1",string state="",string app="10003")=>File.WriteAllText(Path.Combine(root,"launcherDownloadConfig.json"),JsonSerializer.Serialize(new{version,state,appId=app,isPreDownload=false}));
string Server(string pre="3.7.0",int enabled=1)=>JsonSerializer.Serialize(new{predownloadSwitch=enabled,@default=new{config=new{version="3.6.1"}},predownload=new{config=new{version=pre}}});
var handler=new FakeHandler();using var service=new OfficialPredownloadService(handler);
int tests=0;
async Task Expect(PredownloadState expected,string name){var r=await service.CheckAsync(root,shipping);if(r.State!=expected)throw new Exception(name+": "+r);tests++;Console.WriteLine("PASS "+name);}
Channel();Local();handler.Body=Encoding.UTF8.GetBytes(Server());await Expect(PredownloadState.Available,"official future predownload");
var marker=Path.Combine(root,"launcherDownload","3.7.0","launcherDownloadConfig.json");Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
File.WriteAllText(marker,"{\"version\":\"3.7.0\",\"state\":\"download_completed\",\"appId\":\"10003\"}");await Expect(PredownloadState.AlreadyDownloaded,"matching completion marker");
File.WriteAllText(marker,"{\"version\":\"3.6.1\",\"state\":\"download_completed\",\"appId\":\"10003\"}");await Expect(PredownloadState.Available,"stale completion is not current predownload");File.Delete(marker);
Local("3.5.0");await Expect(PredownloadState.NotAvailable,"ordinary update never prompts");Local();
handler.Body=Encoding.UTF8.GetBytes(Server(enabled:0));await Expect(PredownloadState.NotAvailable,"official switch off");
handler.Body=Encoding.UTF8.GetBytes(Server("3.6.1"));await Expect(PredownloadState.NotAvailable,"same version not predownload");
handler.Body=Encoding.UTF8.GetBytes(Server("../3.7.0"));await Expect(PredownloadState.Unknown,"unsafe preversion rejected");
handler.Body=Encoding.UTF8.GetBytes("{broken");await Expect(PredownloadState.Unknown,"malformed response");
handler.Body=new byte[1024*1024+1];await Expect(PredownloadState.Unknown,"oversize response");
byte[] Zip(byte[] data){using var m=new MemoryStream();using(var z=new GZipStream(m,CompressionLevel.SmallestSize,true))z.Write(data);return m.ToArray();}
handler.Body=Zip(Encoding.UTF8.GetBytes(Server()));await Expect(PredownloadState.Available,"gzip magic without header");
handler.Body=Zip(new byte[1024*1024+1]);await Expect(PredownloadState.Unknown,"decompressed size bounded");
handler.Body=Encoding.UTF8.GetBytes(Server());handler.Status=HttpStatusCode.Redirect;await Expect(PredownloadState.Unknown,"redirect refused");handler.Status=HttpStatusCode.OK;
Channel("https://evil.example/launcher/game/G152/a/index.json");var calls=handler.Calls;await Expect(PredownloadState.Unknown,"untrusted host rejected");if(calls!=handler.Calls)throw new Exception("untrusted network call");
Channel();Local(app:"other");await Expect(PredownloadState.Unknown,"channel mismatch");Local(state:"repairing");await Expect(PredownloadState.Unknown,"repairing installation");Local();
handler.Body=Encoding.UTF8.GetBytes("{\"predownloadSwitch\":0,\"predownloadSwitch\":1}");await Expect(PredownloadState.Unknown,"duplicate key refused");
handler.Body=Encoding.UTF8.GetBytes(Server());
var wrong=await service.CheckAsync(root,Path.Combine(root,"Wuthering Waves.exe"));if(wrong.State!=PredownloadState.Unknown)throw new Exception("wrong exe");tests++;
using(var cts=new CancellationTokenSource()){cts.Cancel();try{await service.CheckAsync(root,shipping,cts.Token);throw new Exception("cancellation swallowed");}catch(OperationCanceledException){tests++;}}
var alias=Path.Combine(fixture,"linked-game");
var junction=new System.Diagnostics.ProcessStartInfo("cmd.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
junction.ArgumentList.Add("/c");junction.ArgumentList.Add("mklink");junction.ArgumentList.Add("/J");junction.ArgumentList.Add(alias);junction.ArgumentList.Add(root);
using(var process=System.Diagnostics.Process.Start(junction)!){await process.WaitForExitAsync();if(process.ExitCode!=0)throw new Exception("fixture junction creation failed");}
var linked=await service.CheckAsync(alias,Path.Combine(alias,"Client","Binaries","Win64","Client-Win64-Shipping.exe"));if(linked.State!=PredownloadState.Unknown)throw new Exception("linked root accepted");tests++;Directory.Delete(alias);
var newer=Path.Combine(fixture,"2.7.0.0");Directory.CreateDirectory(Path.Combine(newer,"Assets"));File.WriteAllText(Path.Combine(newer,"launcher_main.dll"),"");File.WriteAllText(Path.Combine(newer,"launcher_main.exe"),"");File.WriteAllText(Path.Combine(newer,"Assets","KRApp.conf"),"malformed");
await Expect(PredownloadState.Unknown,"highest launcher invalid config never falls back to older channel");
var equivalent=Path.Combine(fixture,"2.7.0");Directory.CreateDirectory(Path.Combine(equivalent,"Assets"));File.WriteAllText(Path.Combine(equivalent,"launcher_main.dll"),"");File.WriteAllText(Path.Combine(equivalent,"launcher_main.exe"),"");File.WriteAllText(Path.Combine(equivalent,"Assets","KRApp.conf"),"malformed");
await Expect(PredownloadState.Unknown,"equivalent highest versions are ambiguous");
var preferences = new WuWaFpsUnlock.Core.UserSettings();
var prefsPath = Path.Combine(fixture, "preferences.json");
void PreferenceCheck(bool ok,string name){if(!ok)throw new Exception(name);tests++;Console.WriteLine("PASS "+name);}
PreferenceCheck(!WuWaFpsUnlock.Core.PredownloadReminders.IsIgnored(preferences,root,"3.7.0"),"default reminder enabled");
WuWaFpsUnlock.Core.PredownloadReminders.SetIgnored(preferences,root,"3.7.0",true);
WuWaFpsUnlock.Core.JsonFiles.Save(prefsPath,preferences);
var restored=JsonSerializer.Deserialize<WuWaFpsUnlock.Core.UserSettings>(File.ReadAllText(prefsPath))!;
PreferenceCheck(WuWaFpsUnlock.Core.PredownloadReminders.IsIgnored(restored,root.ToUpperInvariant()+"\\","3.7.0"),"same version stays ignored after restart with normalized path");
PreferenceCheck(!WuWaFpsUnlock.Core.PredownloadReminders.IsIgnored(restored,root,"3.8.0"),"new predownload version reminds again");
PreferenceCheck(!WuWaFpsUnlock.Core.PredownloadReminders.IsIgnored(restored,root+"other","3.7.0"),"other installation remains independent");
WuWaFpsUnlock.Core.PredownloadReminders.SetIgnored(restored,root,"3.7.0",false);
PreferenceCheck(!WuWaFpsUnlock.Core.PredownloadReminders.IsIgnored(restored,root,"3.7.0"),"uncheck restores reminder");
Console.WriteLine($"{tests}/{tests} isolated official predownload tests passed. No official executable started.");
// Unique test root only, never an installation directory.
Directory.Delete(fixture,true);
sealed class FakeHandler:HttpMessageHandler {
 public byte[] Body=[];public HttpStatusCode Status=HttpStatusCode.OK;public int Calls;
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){token.ThrowIfCancellationRequested();Calls++;return Task.FromResult(new HttpResponseMessage(Status){Content=new ByteArrayContent(Body),RequestMessage=request});}
}
