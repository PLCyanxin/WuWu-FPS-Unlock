using WuWaFpsUnlock.Services;
using System.Text;
using System.Text.Json;
if(args.Length==1){Console.WriteLine(OfficialLaunchOptions.ReadResourceTier(args[0],Console.WriteLine));return;}
var fixture=Path.Combine(Path.GetTempPath(),"wuwa-tier-"+Guid.NewGuid().ToString("N"));
var root=Path.Combine(fixture,"game-drive","Wuthering Waves Game");Directory.CreateDirectory(root);
string Launcher(string name,string game){var path=Path.Combine(fixture,name);Directory.CreateDirectory(Path.Combine(path,"kr_game_cache"));File.WriteAllText(Path.Combine(path,"launcher.exe"),"");Bind(path,game);return path;}
void Bind(string launcher,string game){var data=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{installDirPath=game}));for(int i=0;i<data.Length;i++)data[i]^=0x63;File.WriteAllText(Path.Combine(launcher,"kr_game_cache","kr_game_temp.bin"),Convert.ToBase64String(data));}
void Config(string launcher,string cmd="-krqlv=hd",int enabled=1)=>File.WriteAllText(Path.Combine(launcher,"kr_game_cache","Extend_command_cache.json"),JsonSerializer.Serialize(new{commandSwitch=enabled,commandList=new[]{new{id="tier",cmd,@default=1}}}));
var first=Launcher("official-launcher-drive",root);var second=Launcher("other-launcher",root+"-other");Config(second,"-krqlv=uhd");
int count=0;
void Check(string expected,string name,params string[] paths){if(OfficialLaunchOptions.ResolveResourceTier(root,paths)!=expected)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
void Reject(string name,params string[] paths){try{OfficialLaunchOptions.ResolveResourceTier(root,paths);throw new Exception(name+" accepted");}catch(InvalidDataException){count++;Console.WriteLine("PASS "+name);}}
Reject("missing metadata blocks rather than launching without parameter",first);
Config(first);Check("-krqlv=hd","separate launcher and game directories",first);
Check("-krqlv=hd","other installation is excluded",second,first);
Reject("unrelated launcher not used",second);
Bind(first,root.ToUpperInvariant()+Path.DirectorySeparatorChar);Check("-krqlv=hd","normalized binding path",first);
Config(first,"-krqlv=uhd");Check("-krqlv=uhd","uhd official selection",first);
Config(first,"-krqlv=sd");Check("-krqlv=sd","sd official selection",first);
var prefs=Path.Combine(first,"kr_game_cache","Extend_command_preferences.json");
File.WriteAllText(prefs,"{\"tier\":0}");Reject("disabled preference overrides official default",first);
File.WriteAllText(prefs,"{\"tier\":1}");Check("-krqlv=sd","enabled preference read fresh",first);
File.WriteAllText(prefs,"{\"tier\":0,\"tier\":1}");Reject("duplicate preference keys",first);
File.WriteAllText(prefs,"{bad");Reject("malformed preferences do not silently use default",first);File.Delete(prefs);
Config(first,enabled:0);Reject("disabled command list",first);
Config(first,"-slno");Reject("no resource tier",first);
Config(first,"-krqlv=hd -other");Reject("arbitrary arguments rejected",first);
var config=Path.Combine(first,"kr_game_cache","Extend_command_cache.json");
File.WriteAllText(config,"{\"commandSwitch\":1,\"commandList\":[{\"id\":\"a\",\"cmd\":\"-krqlv=hd\",\"default\":1},{\"id\":\"b\",\"cmd\":\"-krqlv=sd\",\"default\":1}]}");Reject("conflicting choices",first);
File.WriteAllText(config,new string('x',65537));Reject("oversized metadata",first);
Config(first);Bind(second,root);Reject("multiple associated launchers disagree",first,second);
Config(second);Check("-krqlv=hd","multiple associated launchers agree",first,second);
File.Delete(config);Reject("matching broken config cannot fall through",first,second);
Config(first);File.WriteAllText(Path.Combine(first,"kr_game_cache","kr_game_temp.bin"),"broken");Reject("invalid association",first);
Bind(first,root+"-other");Reject("stale moved installation binding",first);
var settings=new WuWaFpsUnlock.Core.UserSettings{ResourceTier="uhd"};if(settings.Clone().ResourceTier!="uhd")throw new Exception("Persistence failed");count++;
Bind(first,root);Config(first);
void Native(string json){var bytes=System.Text.Encoding.UTF8.GetBytes(json);for(int i=0;i<bytes.Length;i++)bytes[i]^=99;File.WriteAllText(Path.Combine(first,"kr_game_cache","platform_ui_state.bin"),Convert.ToBase64String(bytes));}
Native("{\"activeGameScope\":{\"gameIdentity\":\"Aki\",\"gameId\":\"G152\",\"bundleName\":\"UHD\"}}");
Check("-krqlv=uhd","native bundle takes precedence over stale hd cache",first);
Native("{\"activeGameScope\":{\"gameIdentity\":\"Other\",\"gameId\":\"G100\"},\"recentGames\":{\"Aki\":{\"gameId\":\"G152\",\"bundleName\":\"SD\"}}}");
Check("-krqlv=sd","recent WuWa scope when another game is active",first);
Native("{\"activeGameScope\":{\"gameIdentity\":\"Aki\",\"gameId\":\"G152\",\"bundleName\":\"INVALID\"}}");Reject("invalid native bundle cannot use stale extension",first);
Directory.CreateDirectory(root);Directory.CreateDirectory(Path.Combine(root,"launcherDownloadConfig"));
void Verify(bool ok,string name){if(!ok)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
Verify(ResourceTierCatalog.Read(root).All(x=>x.Status==TierDownloadStatus.Unknown),"legacy metadata not guessed installed");
File.WriteAllText(Path.Combine(root,"launcherDownloadConfig.json"),"{\"bundles\":{\"HD\":{\"version\":\"3.7.0\",\"state\":\"\",\"resourcePacks\":[\"common\",\"hd\"]}}}");
foreach(var pack in new[]{"common","hd"})File.WriteAllText(Path.Combine(root,"launcherDownloadConfig",pack+".json"),JsonSerializer.Serialize(new{packName=pack,version="3.7.0"}));
var catalog=ResourceTierCatalog.Read(root);
Verify(catalog[1].Status==TierDownloadStatus.Installed&&catalog[0].Status==TierDownloadStatus.Missing&&catalog[2].Status==TierDownloadStatus.Missing,"only installed bundle green despite UI selecting uhd");
Verify(ResourceTierCatalog.Read(root,t=>t=="sd")[2].Status==TierDownloadStatus.Downloading,"observed tier writes indicate active download");
File.Delete(Path.Combine(root,"launcherDownloadConfig","common.json"));
Verify(ResourceTierCatalog.Read(root)[1].Status==TierDownloadStatus.Missing,"missing required pack removes green");
File.WriteAllText(Path.Combine(root,"launcherDownloadConfig.json"),"{bad");Verify(ResourceTierCatalog.Read(root).All(x=>x.Status==TierDownloadStatus.Unknown),"partial official writes do not claim installed");
Verify(ResourceTierCatalog.TierFromResourcePath(@"C:\game\launcherDownload\UHD\a.tmp")=="uhd","specific download resource path");
Verify(ResourceTierCatalog.TierFromResourcePath(@"C:\game\launcherDownload\common\a.tmp") is null,"shared download not attributed to all tiers");
Console.WriteLine($"{count} launch option tests passed; no game started or registry modified.");
Directory.Delete(fixture,true);
