using WuWaFpsUnlock.Services;
using System.Text.Json;
if(args.Length==1){Console.WriteLine(OfficialLaunchOptions.ReadResourceTier(args[0])??"No official resource tier");return;}
var fixture=Path.Combine(Path.GetTempPath(),"wuwa-tier-"+Guid.NewGuid().ToString("N"));
var root=Path.Combine(fixture,"Wuthering Waves Game"); Directory.CreateDirectory(root);
var cache=Path.Combine(fixture,"kr_game_cache");Directory.CreateDirectory(cache);
var config=Path.Combine(cache,"Extend_command_cache.json");var prefs=Path.Combine(cache,"Extend_command_preferences.json");
int count=0;
void Check(string? expected){if(OfficialLaunchOptions.ReadResourceTier(root)!=expected)throw new Exception("Unexpected tier");count++;}
void Config(string command="-krqlv=hd",int enabled=1)=>File.WriteAllText(config,JsonSerializer.Serialize(new{commandSwitch=enabled,commandList=new[]{new{id="tier",cmd=command,@default=1}}}));
Check(null);Config();Check("-krqlv=hd");Config("-krqlv=uhd");Check("-krqlv=uhd");Config("-krqlv=sd");Check("-krqlv=sd");
File.WriteAllText(prefs,"{\"tier\":0}");Check(null);File.WriteAllText(prefs,"{\"tier\":1}");Check("-krqlv=sd");
Config(enabled:0);Check(null);Config("-slno");Check(null);
Config("-krqlv=hd -other");try{OfficialLaunchOptions.ReadResourceTier(root);throw new Exception("Unsafe value accepted");}catch(InvalidDataException){count++;}
File.Delete(prefs);File.WriteAllText(config,"{\"commandSwitch\":1,\"commandList\":[{\"id\":\"a\",\"cmd\":\"-krqlv=hd\",\"default\":1},{\"id\":\"b\",\"cmd\":\"-krqlv=sd\",\"default\":1}]}");
try{OfficialLaunchOptions.ReadResourceTier(root);throw new Exception("Conflict accepted");}catch(InvalidDataException){count++;}
Console.WriteLine($"{count} launch option tests passed; no game started.");
Directory.Delete(fixture,true);
