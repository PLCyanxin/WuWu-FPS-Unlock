using WuWaFpsUnlock.Core;
using WuWaFpsUnlock.Services;

string root=Path.GetFullPath(args.Length>0?args[0]:".");
string work=Path.Combine(root,"artifacts","windows-integration",DateTime.Now.ToString("yyyyMMdd-HHmmss"));
Directory.CreateDirectory(work);
string fixture=Path.Combine(work,"中文 测试游戏","Client","Binaries","Win64");Directory.CreateDirectory(fixture);
string exe=Path.Combine(fixture,"Client-Win64-Shipping.exe");
// Copy a Microsoft dotnet host for PE inspection only. This EXE is NEVER executed.
File.Copy(Path.Combine(root,".tools","dotnet","dotnet.exe"),exe);
var settings=new UserSettings{GameRoot=Path.Combine(work,"中文 测试游戏"),GameExe=exe,PackageManifest=Path.Combine(root,"payload","manifest.json"),MfgSelected=true};
var manifest=PackageReader.Load(settings.PackageManifest);
var receipt=new DeploymentReceipt{GameRoot=settings.GameRoot,GameExe=exe};
var service=new ReShadeService(Console.WriteLine);
int passed=0,failed=0;
void Check(bool condition,string message){if(!condition)throw new Exception(message);}
async Task Test(string name,Func<Task> run){try{await run();Console.WriteLine("PASS "+name);passed++;}catch(Exception ex){Console.WriteLine("FAIL "+name+" "+ex);failed++;}}
async Task Throws<T>(Func<Task> run)where T:Exception{try{await run();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
await Test("local setup exact SHA and version",async()=>{Check(File.Exists(await service.ResolveLocalSetupAsync(settings.PackageManifest,manifest.ReShade)),"missing setup");});
await Test("corrupt setup manifest hash blocked before execution",async()=>{var bad=new ReShadeSpec{LocalSetupPath=manifest.ReShade.LocalSetupPath,SetupSha256=new string('0',64)};await Throws<InvalidDataException>(()=>service.ResolveLocalSetupAsync(settings.PackageManifest,bad));});
await Test("missing local setup blocked without network fallback",async()=>{var bad=new ReShadeSpec{LocalSetupPath="setup/missing.exe",SetupSha256=manifest.ReShade.SetupSha256};await Throws<FileNotFoundException>(()=>service.ResolveLocalSetupAsync(settings.PackageManifest,bad));});
await Test("fresh fixture identified missing",()=>{Check(service.Inspect(settings).State=="Missing","expected missing");return Task.CompletedTask;});
await Test("actual user Setup headless installs verified x64 Full Add-on",async()=>{
    var installed=await service.EnsureAsync(settings,manifest.ReShade,receipt,false,CancellationToken.None);
    Check(installed.State=="Reusable"&&installed.Proxy==Path.Combine(fixture,"dxgi.dll"),"unexpected runtime");
    Console.WriteLine("RUNTIME_SHA256 "+await SafePaths.HashAsync(installed.Proxy!));
    Check(!Directory.Exists(Path.Combine(fixture,"reshade-shaders")),"unexpected shaders");
});
if(File.Exists(Path.Combine(fixture,"dxgi.dll")))
{
    await Test("reuse preserves custom INI and add-on directory",async()=>{
        string text="[ADDON]\nAddonPath=addons\nLoadFromDllMain=other.addon64\n[GENERAL]\nPresetPath=MyPreset.ini\n[User]\nKeep=untouched\n";
        File.WriteAllText(Path.Combine(fixture,"ReShade.ini"),text);
        var installed=await service.EnsureAsync(settings,manifest.ReShade,receipt,false,CancellationToken.None);
        Check(installed.State=="Reusable"&&installed.AddonDirectory==Path.Combine(fixture,"addons"),"custom path not respected");
        Check(File.ReadAllText(installed.Ini)==text,"user INI changed on reuse");
    });
    await Test("multiple ReShade proxies blocked",()=>{File.Copy(Path.Combine(fixture,"dxgi.dll"),Path.Combine(fixture,"d3d12.dll"));try{Check(service.Inspect(settings).State=="Conflict","multiple proxy accepted");}finally{File.Delete(Path.Combine(fixture,"d3d12.dll"));}return Task.CompletedTask;});
    await Test("shared external AddonPath blocked",()=>{File.WriteAllText(Path.Combine(fixture,"ReShade.ini"),"[ADDON]\nAddonPath="+work);Check(service.Inspect(settings).State=="Conflict","external path accepted");return Task.CompletedTask;});
    await Test("redirected installation blocked",()=>{File.WriteAllText(Path.Combine(fixture,"ReShade.ini"),"[INSTALL]\nBasePath=elsewhere");Check(service.Inspect(settings).State=="Conflict","redirect accepted");return Task.CompletedTask;});
    await Test("ambiguous INI files blocked",()=>{File.WriteAllText(Path.Combine(fixture,"ReShade.ini"),"[User]\nKeep=1");File.WriteAllText(Path.Combine(fixture,"dxgi.ini"),"[User]\nKeep=2");try{Check(service.Inspect(settings).State=="Conflict","ambiguous ini accepted");}finally{File.Delete(Path.Combine(fixture,"dxgi.ini"));}return Task.CompletedTask;});
    await Test("unknown graphics proxy blocked",()=>{File.WriteAllText(Path.Combine(fixture,"d3d11.dll"),"unrelated hook");try{Check(service.Inspect(settings).State=="Conflict","unknown proxy accepted");}finally{File.Delete(Path.Combine(fixture,"d3d11.dll"));}return Task.CompletedTask;});
    await Test("actual 18 user DLLs map, replace, verify and remove in fixture",async()=>{
        string vendorDir=Path.Combine(settings.GameRoot,"Engine","Plugins","NVIDIA","TestOnly");Directory.CreateDirectory(vendorDir);
        foreach(var file in manifest.Files.Where(f=>f.Kind==PayloadKind.Vendor))File.WriteAllText(Path.Combine(vendorDir,Path.GetFileName(file.Target)),"test original "+file.Target);
        var plan=await PackageReader.PlanAsync(manifest,settings.PackageManifest,settings.GameRoot,fixture,fixture);
        Check(plan.Count(f=>f.Kind==PayloadKind.Vendor)==18,"not all user DLLs mapped");
        Check(plan.Where(f=>f.Kind==PayloadKind.Vendor).All(f=>PeInspector.IsAmd64(f.Source)),"input not x64");
        JsonFiles.Save(Path.Combine(work,"actual-material-file-map.json"),plan);
        var r=new DeploymentReceipt{GameRoot=settings.GameRoot,GameExe=exe};
        await DeploymentFiles.ApplyAsync(plan,r,()=>JsonFiles.Save(Path.Combine(work,"actual-material-receipt.json"),r),Console.WriteLine);
        foreach(var file in plan)Check(await SafePaths.HashAsync(file.Target)==file.Sha256,"deployed hash mismatch");
        var repeat=await PackageReader.PlanAsync(manifest,settings.PackageManifest,settings.GameRoot,fixture,fixture);
        await DeploymentFiles.ApplyAsync(repeat,r,()=>{},Console.WriteLine);
        Check(r.Files.Count(f=>f.ReplacedByTool)==18,"ownership lost on repeat");
        await DeploymentFiles.CleanOwnedAddonsAsync(r,()=>JsonFiles.Save(Path.Combine(work,"actual-material-receipt.json"),r),Console.WriteLine);
        Check(plan.All(f=>!File.Exists(f.Target)),"owned material not removed");
        Check(File.Exists(Path.Combine(fixture,"dxgi.dll")),"user ReShade deleted");
        Check(File.ReadAllText(Path.Combine(fixture,"ReShade.ini")).Contains("Keep=1"),"user INI lost");
    });
}
Console.WriteLine($"RESULT: {passed} passed, {failed} failed. User Setup really executed in a project fixture; no game/unlocker/plugin executed. Work={work}");
return failed==0?0:1;
