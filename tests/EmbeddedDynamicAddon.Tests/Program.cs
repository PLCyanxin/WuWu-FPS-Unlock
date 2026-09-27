using System.Reflection;
using System.Security.Cryptography;
using WuWaFpsUnlock.Core;
using WuWaFpsUnlock.Services;

int passed=0,failed=0;
async Task Test(string name,Func<string,Task> run)
{
    string root=Path.Combine(Path.GetTempPath(),"WuWaEmbeddedAddonTests-"+Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);AppPaths.Base=Path.Combine(root,"launcher");Directory.CreateDirectory(AppPaths.Base);
    try{await run(root);passed++;Console.WriteLine("PASS "+name);}
    catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e);}
    finally{Directory.Delete(root,true);}
}
void Check(bool value,string reason){if(!value)throw new Exception(reason);}
async Task Reject<T>(Func<Task> action) where T:Exception
{try{await action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
UserSettings Settings(string root){string game=Path.Combine(root,"game");Directory.CreateDirectory(game);return new(){GameRoot=game};}
Task Plan(List<PlannedFile> plan,UserSettings settings,CancellationToken token=default)=>EmbeddedDynamicAddon.AppendToPlanAsync(plan,settings,Path.Combine(settings.GameRoot,"addons"),token);

await Test("preview fingerprints inert resource and writes only launcher cache",async root=>
{
    var settings=Settings(root);File.WriteAllText(Path.Combine(settings.GameRoot,"untouched.txt"),"original");var plan=new List<PlannedFile>();await Plan(plan,settings);
    using var resource=Assembly.GetExecutingAssembly().GetManifestResourceStream("WuWaFpsUnlock.DynamicMax.addon64")!;
    using var memory=new MemoryStream();await resource.CopyToAsync(memory);byte[] bytes=memory.ToArray();
    var entry=plan.Single();string hash=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    Check(entry.Sha256==hash&&entry.Size==bytes.Length&&entry.Kind==PayloadKind.Addon,"resource metadata");
    Check(entry.Source==Path.Combine(AppPaths.Data,"components",hash,EmbeddedDynamicAddon.FileName),"hash cache location");
    Check(entry.Target==Path.Combine(settings.GameRoot,"addons",EmbeddedDynamicAddon.FileName)&&entry.ExpectedTargetHash is null,"destination plan");
    Check(File.ReadAllBytes(entry.Source).SequenceEqual(bytes),"cache bytes");
    Check(Directory.GetFileSystemEntries(settings.GameRoot).Length==1&&File.ReadAllText(Path.Combine(settings.GameRoot,"untouched.txt"))=="original","preview must not write game");
});
await Test("valid cache reuse preserves bytes and timestamp",async root=>
{
    var settings=Settings(root);var first=new List<PlannedFile>();await Plan(first,settings);string source=first.Single().Source;
    var timestamp=new DateTime(2020,1,1,0,0,0,DateTimeKind.Utc);File.SetLastWriteTimeUtc(source,timestamp);
    var again=new List<PlannedFile>();await Plan(again,settings);
    Check(first.Single()==again.Single()&&File.GetLastWriteTimeUtc(source)==timestamp,"unchanged cache was rewritten");
});
await Test("corrupt cache reextracts resource without temp residue",async root=>
{
    var settings=Settings(root);var plan=new List<PlannedFile>();await Plan(plan,settings);var expected=plan.Single();
    File.WriteAllText(expected.Source,"corrupted");plan.Clear();await Plan(plan,settings);
    Check(await SafePaths.HashAsync(expected.Source)==expected.Sha256,"corrupt cache not repaired");
    Check(!Directory.EnumerateFiles(AppPaths.Data,"*.tmp",SearchOption.AllDirectories).Any(),"temp files remain");
});
await Test("pre-cancelled extraction changes neither plan nor game nor cache",async root=>
{
    var settings=Settings(root);var plan=new List<PlannedFile>();using var cancel=new CancellationTokenSource();cancel.Cancel();
    await Reject<OperationCanceledException>(()=>Plan(plan,settings,cancel.Token));
    Check(plan.Count==0&&!Directory.Exists(AppPaths.Data)&&!Directory.EnumerateFileSystemEntries(settings.GameRoot).Any(),"cancelled extraction caused mutation");
});
await Test("cancelled cached preview does not append",async root=>
{
    var settings=Settings(root);var plan=new List<PlannedFile>();await Plan(plan,settings);plan.Clear();using var cancel=new CancellationTokenSource();cancel.Cancel();
    await Reject<OperationCanceledException>(()=>Plan(plan,settings,cancel.Token));Check(plan.Count==0,"cancelled append");
});
await Test("outside-game addon directory rejected without target write",async root=>
{
    var settings=Settings(root);var plan=new List<PlannedFile>();string outside=Path.Combine(root,"elsewhere");
    await Reject<InvalidDataException>(()=>EmbeddedDynamicAddon.AppendToPlanAsync(plan,settings,outside,default));
    Check(plan.Count==0&&!Directory.Exists(outside)&&!Directory.EnumerateFileSystemEntries(settings.GameRoot).Any(),"outside path modified");
});
await Test("duplicate addon filename rejected case-insensitively before extraction",async root=>
{
    var settings=Settings(root);var entry=new PlannedFile("unused",Path.Combine(settings.GameRoot,"other",EmbeddedDynamicAddon.FileName.ToUpperInvariant()),"unused",1,PayloadKind.Addon);
    var plan=new List<PlannedFile>{entry};await Reject<InvalidDataException>(()=>Plan(plan,settings));
    Check(plan.Count==1&&plan[0]==entry&&!Directory.Exists(AppPaths.Data),"duplicate rejection mutated plan or cache");
});
await Test("existing game addon hash captured and original bytes preserved",async root=>
{
    var settings=Settings(root);string addon=Path.Combine(settings.GameRoot,"addons",EmbeddedDynamicAddon.FileName);Directory.CreateDirectory(Path.GetDirectoryName(addon)!);File.WriteAllText(addon,"user existing version");
    string before=await SafePaths.HashAsync(addon);var plan=new List<PlannedFile>();await Plan(plan,settings);
    Check(plan.Single().ExpectedTargetHash==before&&File.ReadAllText(addon)=="user existing version","preview overwrite or wrong prior hash");
});
await Test("target directory rejected before cache extraction",async root=>
{
    var settings=Settings(root);string target=Path.Combine(settings.GameRoot,"addons",EmbeddedDynamicAddon.FileName);Directory.CreateDirectory(target);
    var plan=new List<PlannedFile>();await Reject<IOException>(()=>Plan(plan,settings));
    Check(plan.Count==0&&!Directory.Exists(AppPaths.Data)&&Directory.Exists(target),"directory conflict mutated cache or target");
});
Console.WriteLine($"RESULT: {passed} passed, {failed} failed. Production helper with inert embedded bytes and temporary filesystem only; no addon execution or game writes.");
return failed==0?0:1;

namespace WuWaFpsUnlock.Services
{
    internal static class AppPaths
    {
        internal static string Base {get;set;}="";
        internal static string Data=>Path.Combine(Base,"data");
    }
}
