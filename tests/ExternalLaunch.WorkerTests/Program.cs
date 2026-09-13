using System.Diagnostics;
using System.IO;
using WuWaFpsUnlock.Core;
using WuWaFpsUnlock.Services;

string root=Path.GetFullPath(Path.Combine("artifacts","worker-protocol-测试",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);
int passed=0,failed=0;
async Task Test(string name,Func<Task> action){try{await action();Console.WriteLine("PASS "+name);passed++;}catch(Exception e){Console.WriteLine("FAIL "+name+": "+e);failed++;}}
void Check(bool condition){if(!condition)throw new Exception("assertion failed");}
Task Sync(Action action){action();return Task.CompletedTask;}
async Task<Process> FailStart(Exception error){await Task.Yield();throw error;}
async Task Reject(Func<Task> action){try{await action();}catch(Exception e) when(e is IOException or InvalidDataException or OperationCanceledException){return;}throw new Exception("expected rejection");}
string jobs=Path.Combine(root,"jobs");Directory.CreateDirectory(jobs);
var plan=new LaunchPlan(true,Path.Combine(root,"fixed unlock.exe"),root,Path.Combine(root,"Shipping.exe"),Path.Combine(root,"ww_fps_config.ini"),Path.Combine(root,"Wuthering Waves.exe"),240);
async Task<(string path,string nonce,string digest,ExternalLaunchRequest request)> Request(DateTimeOffset? created=null)
{
    string nonce=Guid.NewGuid().ToString("N"),path=ExternalLaunchProtocol.RequestPath(jobs,nonce);
    var request=new ExternalLaunchRequest{Nonce=nonce,CreatedUtc=created??DateTimeOffset.UtcNow,Settings=new(){FpsEnabled=true},PlanFingerprint=ExternalLaunchProtocol.Fingerprint(plan)};
    JsonFiles.Save(path,request);return(path,nonce,await SafePaths.HashAsync(path),request);
}
await Test("protocol roundtrip and immutable plan fingerprint",async()=>{var x=await Request();var value=ExternalLaunchProtocol.Read(jobs,x.path,x.nonce,x.digest,DateTimeOffset.UtcNow);ExternalLaunchProtocol.ValidatePlan(value,plan);Check(value.Nonce==x.nonce);});
await Test("request tampering after approval rejected",async()=>{var x=await Request();File.AppendAllText(x.path," ");await Reject(()=>Sync(()=>ExternalLaunchProtocol.Read(jobs,x.path,x.nonce,x.digest,DateTimeOffset.UtcNow)));});
await Test("request outside fixed jobs rejected",async()=>{var x=await Request();string outside=Path.Combine(root,Path.GetFileName(x.path));File.Copy(x.path,outside);await Reject(()=>Sync(()=>ExternalLaunchProtocol.Read(jobs,outside,x.nonce,x.digest,DateTimeOffset.UtcNow)));});
await Test("nonce traversal and mismatched request names rejected",async()=>{await Reject(()=>Sync(()=>ExternalLaunchProtocol.RequestPath(jobs,"../escape")));var x=await Request();await Reject(()=>Sync(()=>ExternalLaunchProtocol.Read(jobs,x.path,Guid.NewGuid().ToString("N"),x.digest,DateTimeOffset.UtcNow)));});
await Test("expired request rejected",async()=>{var x=await Request(DateTimeOffset.UtcNow.AddMinutes(-6));await Reject(()=>Sync(()=>ExternalLaunchProtocol.Read(jobs,x.path,x.nonce,x.digest,DateTimeOffset.UtcNow)));});
await Test("future request rejected",async()=>{var x=await Request(DateTimeOffset.UtcNow.AddMinutes(1));await Reject(()=>Sync(()=>ExternalLaunchProtocol.Read(jobs,x.path,x.nonce,x.digest,DateTimeOffset.UtcNow)));});
await Test("FPS OFF cannot enter elevated external route",async()=>{var x=await Request();x.request.Settings.FpsEnabled=false;JsonFiles.Save(x.path,x.request);string digest=await SafePaths.HashAsync(x.path);await Reject(()=>Sync(()=>ExternalLaunchProtocol.Read(jobs,x.path,x.nonce,digest,DateTimeOffset.UtcNow)));});
await Test("post-approval Shipping change rejected",async()=>{var x=await Request();await Reject(()=>Sync(()=>ExternalLaunchProtocol.ValidatePlan(x.request,plan with{ShippingExePath=Path.Combine(root,"different.exe")})));});
await Test("request replay claim rejected",async()=>{var x=await Request();using(var first=ExternalLaunchProtocol.Claim(jobs,x.nonce)){}await Reject(()=>Sync(()=>{using var duplicate=ExternalLaunchProtocol.Claim(jobs,x.nonce);}));});
await Test("response nonce and failed phase cannot report success",async()=>{string nonce=Guid.NewGuid().ToString("N");var response=new ExternalLaunchResult{Nonce=nonce,Success=true,ChildPid=123,ChildStartedUtc=DateTime.UtcNow,Phase="ChildStarted"};ExternalLaunchProtocol.ValidateResult(response,nonce,0);await Reject(()=>Sync(()=>ExternalLaunchProtocol.ValidateResult(response,Guid.NewGuid().ToString("N"),0)));response.Success=false;response.Phase="PrepareConfig";await Reject(()=>Sync(()=>ExternalLaunchProtocol.ValidateResult(response,nonce,1)));});
await Test("fixed UAC start info has no arbitrary executable or CLI",()=>Sync(()=>{
    string nonce=Guid.NewGuid().ToString("N"),path=ExternalLaunchProtocol.RequestPath(Path.Combine(AppPaths.Data,"jobs"),nonce);
    var info=ExternalLaunchWorker.BuildElevationStartInfo(path,nonce,new string('a',64));
    Check(info.UseShellExecute&&info.Verb=="runas"&&info.FileName==Path.Combine(AppPaths.Base,"WuWaFpsUnlock.exe")&&info.ArgumentList.Count==4&&info.ArgumentList[0]=="--external-launch-worker");
}));
await Test("read-only PID identity uses limited process query",()=>Sync(()=>Check(string.Equals(GameProcesses.ImagePath(Environment.ProcessId),Environment.ProcessPath,StringComparison.OrdinalIgnoreCase))));
await Test("async UAC cancellation has no renderer wait or fallback start",async()=>{
    int starts=0,waits=0;var states=new List<string>();
    await Reject(()=>new LaunchExecution().RunAsync(plan,()=>Task.CompletedTask,()=>{starts++;return FailStart(new OperationCanceledException("fake UAC1223"));},
        (_,_)=>{waits++;return Task.FromResult(Process.GetCurrentProcess());},states.Add,TimeSpan.FromSeconds(1),default));
    Check(starts==1&&waits==0&&states[^1]=="Cancelled");
});
await Test("async worker error never falls back to Shipping",async()=>{
    int starts=0,waits=0;var states=new List<string>();
    await Reject(()=>new LaunchExecution().RunAsync(plan,()=>Task.CompletedTask,()=>{starts++;return FailStart(new IOException("fake worker failure"));},
        (_,_)=>{waits++;return Task.FromResult(Process.GetCurrentProcess());},states.Add,TimeSpan.FromSeconds(1),default));
    Check(starts==1&&waits==0&&states[^1]=="Failed");
});
Console.WriteLine($"TOTAL passed={passed} failed={failed}; protocol/fake boundary only, no UAC, unlocker, game or plugin executed");Environment.ExitCode=failed==0?0:1;
