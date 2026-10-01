using WuWaFpsUnlock.Core;
using WuWaFpsUnlock.Services;
// Supply an existing Full ReShade x64 DLL as read-only data; never load it.
if(args.Length!=1)throw new ArgumentException("Pass the path of a Full ReShade 6.8+ x64 DLL (data only).");
string source=Path.GetFullPath(args[0]);
if(PeInspector.AddonBuild(source)!="FullCandidate"||!PeInspector.IsAmd64(source))throw new InvalidDataException("Expected real Full x64 ReShade fixture.");
var root=Path.Combine(Path.GetTempPath(),"ww-reshade-reuse-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
int passed=0;void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);passed++;}
try{
 var settings=new UserSettings{GameRoot=root,GameExe=Path.Combine(root,"Client-Win64-Shipping.exe"),PackageManifest=Path.Combine(root,"manifest.json")};
 var service=new ReShadeService(_=>{});var spec=new ReShadeSpec{ProxyApi="dxgi"};
 string ini=Path.Combine(root,"ReShade.ini"),filter=Path.Combine(root,"user.fx");
 File.WriteAllText(ini,"[ADDON]\nAddonPath=addons\n[USER]\nKeep=1\n");File.WriteAllText(filter,"inert user filter");
 var originalIni=File.ReadAllBytes(ini);var originalFilter=File.ReadAllBytes(filter);
 foreach(string name in new[]{"d3d9.dll","d3d10.dll","d3d11.dll","opengl32.dll","d3d12.dll"}){
  string path=Path.Combine(root,name);File.Copy(source,path);var hash=await SafePaths.HashAsync(path);
  var info=service.Inspect(settings,spec);Check(info.State=="Conflict"&&info.Description.Contains(name)&&info.Description.Contains("dxgi.dll"),"different proxy API refused: "+name);
  bool rejected=false;try{await service.EnsureAsync(settings,spec,new DeploymentReceipt(),true,CancellationToken.None);}catch(IOException){rejected=true;}
  Check(rejected&&await SafePaths.HashAsync(path)==hash,"upgrade approval cannot override API conflict: "+name);File.Delete(path);
 }
 string correct=Path.Combine(root,"dxgi.dll");File.Copy(source,correct);var expectedHash=await SafePaths.HashAsync(correct);
 var result=await service.EnsureAsync(settings,spec,new DeploymentReceipt(),false,CancellationToken.None);
 Check(result.State=="Reusable"&&result.AddonDirectory==Path.Combine(root,"addons"),"correct proxy reused with existing addon directory");
 Check(await SafePaths.HashAsync(correct)==expectedHash&&File.ReadAllBytes(ini).SequenceEqual(originalIni)&&File.ReadAllBytes(filter).SequenceEqual(originalFilter),"reuse preserves proxy bytes INI and filters");
 Check(service.Inspect(settings,new ReShadeSpec{ProxyApi=""}).State=="Conflict","missing API is not guessed");
 var manifest=new PayloadManifest{PackageId="fixture",ReShade=spec,Files=[new(){Source="unused",Target="renodx-mfgunlock.addon64",Kind=PayloadKind.Addon,Anchor=TargetAnchor.AddonDir}]};JsonFiles.Save(settings.PackageManifest,manifest);
 Check(service.Inspect(settings).State=="Reusable","overview obtains API from selected manifest");
 File.WriteAllText(ini,"[ADDON] ; inline comment\nAddonPath=addons,,shared\n");
 Check(service.Inspect(settings,spec).AddonDirectory==Path.Combine(root,"addons,shared"),"escaped comma addon path matches ReShade value semantics");
 string alternate=Path.Combine(root,"dxgi.ini");File.WriteAllText(alternate,"[USER]\nLegacy=1\n");
 Check(service.Inspect(settings,spec).Ini==ini&&File.ReadAllText(alternate).Contains("Legacy=1"),"coexisting legacy proxy INI is preserved and global ReShade.ini selected");
 File.Delete(ini);Check(service.Inspect(settings,spec).State=="Conflict","legacy proxy INI alone is refused before automatic writes");
 File.WriteAllBytes(ini,originalIni);
 string? originalOverride=Environment.GetEnvironmentVariable("RESHADE_BASE_PATH_OVERRIDE");
 try{Environment.SetEnvironmentVariable("RESHADE_BASE_PATH_OVERRIDE",Path.Combine(root,"external"));Check(service.Inspect(settings,spec).State=="Conflict","environment global config redirection stops automatic writes");}
 finally{Environment.SetEnvironmentVariable("RESHADE_BASE_PATH_OVERRIDE",originalOverride);}
 File.Delete(alternate);
 File.Move(correct,Path.Combine(root,"d3d12.dll"));Check(service.Inspect(settings).State=="Conflict","overview rejects different proxy via manifest");
 Check(service.Inspect(settings,new ReShadeSpec{ProxyApi="d3d12"}).State=="Reusable","explicit matching d3d12 specification remains supported");
 File.WriteAllText(Path.Combine(root,"dxgi.dll"),"unknown proxy");Check(service.Inspect(settings,spec).State=="Conflict","unknown proxy still blocks");
 Console.WriteLine($"RESULT: {passed} passed. Actual ReShade DLL inspected as data; no installer, addon, game or launcher executed.");return 0;
}finally{Directory.Delete(root,true);}
namespace WuWaFpsUnlock.Services {
 // Inspector-only harness. All execution/mutation dependencies fail closed.
 public static class GameProcesses{public static string ValidateExe(UserSettings s){SafePaths.EnsureInside(s.GameRoot,s.GameExe);return s.GameExe;}public static void RequireStopped(string root){}}
 public static class AppPaths{public static void SaveReceipt(DeploymentReceipt r)=>throw new Exception("Unexpected receipt write");}
 public static class WriteProbe{public static void Check(string path)=>throw new Exception("Unexpected write probe");}
 public static class LauncherScheduling{public static System.Diagnostics.Process? StartProcess(System.Diagnostics.ProcessStartInfo p)=>throw new Exception("Unexpected installer launch");}
}
