using System.Diagnostics;
using System.IO;
using WuWaFpsUnlock.Core;
using WuWaFpsUnlock.Services;

if(args.Length==3&&args[0]=="--owned-fixture")
{
    File.WriteAllText(args[1],Environment.ProcessId.ToString());await Task.Delay(int.Parse(args[2]));return;
}
string root=Path.GetFullPath(Path.Combine("artifacts","restart-process-tests 中文 空格",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);
string CopyFixture(string name)
{
    string target=Path.Combine(root,name);Directory.CreateDirectory(target);
    foreach(string file in Directory.GetFiles(AppContext.BaseDirectory))File.Copy(file,Path.Combine(target,Path.GetFileName(file)),true);
    return Path.Combine(target,"WuWaRestartFixtureTests.exe");
}
string targetExe=CopyFixture("选定安装"),otherExe=CopyFixture("其他安装");
var owned=new List<Process>();var catalog=new WindowsRestartProcessCatalog();int passed=0,failed=0;
void Check(bool condition,string message="assertion failed"){if(!condition)throw new Exception(message);}
async Task Test(string name,Func<Task> action){try{await action();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e);}finally{foreach(var p in owned.ToArray()){try{if(!p.HasExited){p.Kill();await p.WaitForExitAsync();}}catch{}p.Dispose();}owned.Clear();}}
async Task<Process> StartOwned(string path,int life=60000)
{
    string marker=Path.Combine(root,Guid.NewGuid()+".pid");var info=new ProcessStartInfo(path){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(path)!};
    string host=Environment.ProcessPath!;if(Path.GetFileNameWithoutExtension(host).Equals("dotnet",StringComparison.OrdinalIgnoreCase))info.Environment["DOTNET_ROOT_X64"]=Path.GetDirectoryName(host)!;
    info.ArgumentList.Add("--owned-fixture");info.ArgumentList.Add(marker);info.ArgumentList.Add(life.ToString());
    var child=Process.Start(info)!;owned.Add(child);var watch=Stopwatch.StartNew();while(!File.Exists(marker)){if(watch.Elapsed>TimeSpan.FromSeconds(10))throw new TimeoutException("fixture startup");await Task.Delay(10);}return child;
}
async Task<RestartFailureException> Failure(Func<Task> action)
{try{await action();}catch(RestartFailureException e){return e;}throw new Exception("expected restart refusal");}
RestartActions Actions(Func<CancellationToken,Task>? preflight=null,Func<IReadOnlyList<RestartProcessIdentity>,CancellationToken,Task<bool>>? notice=null,Func<CancellationToken,Task>? release=null,Func<string,CancellationToken,Task<Process>>? start=null,Func<Process,CancellationToken,Task>? attach=null)
=>new(){Preflight=preflight??(_=>Task.CompletedTask),ConfirmNotice=notice??((_,_)=>Task.FromResult(true)),ReleaseOldSession=release??(_=>Task.CompletedTask),Start=start??((path,_)=>StartOwned(path)),AttachFps=attach};
Task<Process?> Run(RestartActions actions,bool fps=false,IRestartProcessCatalog? processCatalog=null,RestartController? controller=null)
=>(controller??new()).RunAsync(new(targetExe,fps),processCatalog??catalog,actions,_=>{},TimeSpan.FromSeconds(2),default);
await Test("actual no-running FPS OFF creates one fixture, no FPS attach",async()=>{
    int starts=0,attaches=0;var result=await Run(Actions(start:async(path,_)=>{starts++;return await StartOwned(path);},attach:(_,_)=>{attaches++;return Task.CompletedTask;}));Check(result is not null&&starts==1&&attaches==0);
});
await Test("actual exact-path restart kills old fixture but preserves same-name other installation",async()=>{
    var old=await StartOwned(targetExe);var foreign=await StartOwned(otherExe);int starts=0;
    var result=await Run(Actions(notice:(identities,_)=>{Check(identities.Count==1&&identities[0].Pid==old.Id&&!old.HasExited);return Task.FromResult(true);},start:async(path,_)=>{starts++;Check(old.HasExited&&!foreign.HasExited);return await StartOwned(path);}));
    Check(result is not null&&old.HasExited&&!foreign.HasExited&&starts==1);
});
await Test("actual FPS ON releases old session then restarts and attaches once",async()=>{
    var old=await StartOwned(targetExe);var order=new List<string>();
    var result=await Run(Actions(notice:(_,_)=>{order.Add("notice");Check(!old.HasExited);return Task.FromResult(true);},release:_=>{order.Add("release");Check(!old.HasExited);return Task.CompletedTask;},start:async(path,_)=>{order.Add("start");Check(old.HasExited);return await StartOwned(path);},attach:(_,_)=>{order.Add("attach");return Task.CompletedTask;}),true);
    Check(result is not null&&order.SequenceEqual(new[]{"notice","release","start","attach"}));
});
await Test("actual notice cancellation leaves old process alive, no release or start",async()=>{
    var old=await StartOwned(targetExe);int touched=0;
    var result=await Run(Actions(notice:(_,_)=>Task.FromResult(false),release:_=>{touched++;return Task.CompletedTask;},start:async(path,_)=>{touched++;return await StartOwned(path);}));Check(result is null&&touched==0&&!old.HasExited);
});
await Test("actual preflight failure preserves old process",async()=>{
    var old=await StartOwned(targetExe);int starts=0;await Failure(()=>Run(Actions(preflight:_=>throw new IOException("fake missing component"),start:async(path,_)=>{starts++;return await StartOwned(path);})));Check(!old.HasExited&&starts==0);
});
await Test("same-name unreadable identity refuses before any termination",async()=>{
    int starts=0;var fake=new FakeCatalog(){Capture=_=>throw new UnauthorizedAccessException("fake unreadable path")};
    await Failure(()=>Run(Actions(start:async(path,_)=>{starts++;return await StartOwned(path);}),processCatalog:fake));Check(starts==0);
});
await Test("termination failure stops with zero new launches",async()=>{
    var old=new FakeHeld(targetExe){StopError=new UnauthorizedAccessException("fake access denied")};var fake=new FakeCatalog(){Capture=_=>new[]{old}};int starts=0;
    var error=await Failure(()=>Run(Actions(start:async(path,_)=>{starts++;return await StartOwned(path);}),processCatalog:fake));Check(starts==0&&old.StopCalls==1&&error.Phase=="Stopping");
});
await Test("termination timeout stops with zero new launches",async()=>{
    var old=new FakeHeld(targetExe){StopError=new TimeoutException("fake process would not exit")};var fake=new FakeCatalog(){Capture=_=>new[]{old}};int starts=0;
    var error=await Failure(()=>Run(Actions(start:async(path,_)=>{starts++;return await StartOwned(path);}),processCatalog:fake));Check(starts==0&&error.InnerException is TimeoutException);
});
await Test("PID identity race after notice stops before termination",async()=>{
    var old=new FakeHeld(targetExe);var fake=new FakeCatalog(){Capture=_=>new[]{old}};int starts=0;
    await Failure(()=>Run(Actions(notice:(_,_)=>{old.Changed=true;return Task.FromResult(true);},start:async(path,_)=>{starts++;return await StartOwned(path);}),processCatalog:fake));Check(old.StopCalls==0&&starts==0);
});
await Test("new matching process after stop is not terminated and blocks launch",async()=>{
    var old=new FakeHeld(targetExe);var appeared=new FakeHeld(targetExe);int captures=0,starts=0;var fake=new FakeCatalog(){Capture=_=>++captures==1?new[]{old}:new[]{appeared}};
    await Failure(()=>Run(Actions(start:async(path,_)=>{starts++;return await StartOwned(path);}),processCatalog:fake));Check(old.StopCalls==1&&appeared.StopCalls==0&&starts==0);
});
await Test("single controller rejects double click while notice is pending",async()=>{
    var controller=new RestartController();var entered=new TaskCompletionSource();var choice=new TaskCompletionSource<bool>();int starts=0;
    var first=Run(Actions(notice:(_,_)=>{entered.SetResult();return choice.Task;},start:async(path,_)=>{starts++;return await StartOwned(path);}),controller:controller);await entered.Task;
    try{await Run(Actions(),controller:controller);throw new Exception("double click accepted");}catch(IOException){}choice.SetResult(false);Check(await first is null&&starts==0);
});
await Test("actual held process natural exit is already stopped, never PID-reopened for kill",async()=>{
    var old=await StartOwned(targetExe,300);var held=catalog.CaptureMatching(catalog.NormalizeExecutablePath(targetExe));Check(held.Count==1);await old.WaitForExitAsync();
    try{held[0].VerifyStillSameProcess();await held[0].TerminateAndWaitAsync(TimeSpan.FromSeconds(1),default);Check(old.HasExited);}finally{foreach(var item in held)item.Dispose();}
});
await Test("actual FPS attach failure reports started process without restarting or killing it",async()=>{
    int starts=0;var failure=await Failure(()=>Run(Actions(start:async(path,_)=>{starts++;return await StartOwned(path);},attach:(_,_)=>throw new IOException("fake attach error")),true));
    Check(starts==1&&failure.Phase=="AttachingFps"&&failure.StartedProcess is not null&&!failure.StartedProcess.HasExited);
});
await Test("canonical Chinese path resolves dot segments and same image identity",()=>{
    string dotted=Path.Combine(Path.GetDirectoryName(targetExe)!,".",Path.GetFileName(targetExe));Check(catalog.NormalizeExecutablePath(dotted).Equals(catalog.NormalizeExecutablePath(targetExe),StringComparison.OrdinalIgnoreCase));return Task.CompletedTask;
});
await Test("all captured identities verified before any old process is terminated",async()=>{
    var first=new FakeHeld(targetExe);var second=new FakeHeld(targetExe){Changed=true};var fake=new FakeCatalog(){Capture=_=>new[]{first,second}};
    await Failure(()=>Run(Actions(),processCatalog:fake));Check(first.StopCalls==0&&second.StopCalls==0);
});
await Test("actual two matching instances both exit before a single replacement starts",async()=>{
    var first=await StartOwned(targetExe);var second=await StartOwned(targetExe);int starts=0;
    var result=await Run(Actions(notice:(identities,_)=>{Check(identities.Count==2);return Task.FromResult(true);},start:async(path,_)=>{Check(first.HasExited&&second.HasExited);starts++;return await StartOwned(path);}));
    Check(result is not null&&starts==1);
});
await Test("preflight invalidated during notice preserves actual old process",async()=>{
    var old=await StartOwned(targetExe);bool invalid=false;int starts=0;
    await Failure(()=>Run(Actions(preflight:_=>invalid?throw new IOException("fake component removed after notice"):Task.CompletedTask,notice:(_,_)=>{invalid=true;return Task.FromResult(true);},start:async(path,_)=>{starts++;return await StartOwned(path);})));Check(!old.HasExited&&starts==0);
});
await Test("FPS ON missing attach step is rejected before terminating actual process",async()=>{
    var old=await StartOwned(targetExe);await Failure(()=>Run(Actions(),true));Check(!old.HasExited);
});
await Test("cancellation before workflow never confirms, releases, terminates or starts",async()=>{
    var old=await StartOwned(targetExe);int touched=0;using var cts=new CancellationTokenSource();cts.Cancel();
    try{await new RestartController().RunAsync(new(targetExe,false),catalog,Actions(preflight:_=>{touched++;return Task.CompletedTask;}),_=>{},TimeSpan.FromSeconds(1),cts.Token);throw new Exception("cancel accepted");}catch(OperationCanceledException){}Check(touched==0&&!old.HasExited);
});
await Test("controller recovers after a failed preflight and performs one later start",async()=>{
    var controller=new RestartController();await Failure(()=>Run(Actions(preflight:_=>throw new IOException("first preflight fails")),controller:controller));int starts=0;
    var result=await Run(Actions(start:async(path,_)=>{starts++;return await StartOwned(path);}),controller:controller);Check(result is not null&&starts==1);
});
await Test("actual old process exits naturally during notice and replacement still starts once",async()=>{
    var old=await StartOwned(targetExe,500);int starts=0;
    var result=await Run(Actions(notice:async(_,_)=>{await old.WaitForExitAsync();return true;},start:async(path,_)=>{starts++;return await StartOwned(path);}));Check(result is not null&&starts==1&&old.HasExited);
});
await Test("actual newly started process reference survives cancellation after Start",async()=>{
    using var cts=new CancellationTokenSource();int starts=0;
    var error=await Failure(()=>new RestartController().RunAsync(new(targetExe,false),catalog,Actions(start:async(path,_)=>{starts++;var child=await StartOwned(path);cts.Cancel();return child;}),_=>{},TimeSpan.FromSeconds(1),cts.Token));
    Check(error.StartedProcess is not null&&!error.StartedProcess.HasExited&&error.InnerException is OperationCanceledException&&starts==1);
});
await Test("actual newly started process reference survives FPS attachment cancellation",async()=>{
    var error=await Failure(()=>Run(Actions(attach:(_,_)=>throw new OperationCanceledException("fake attachment cancellation")),true));
    Check(error.StartedProcess is not null&&!error.StartedProcess.HasExited&&error.Phase=="AttachingFps"&&error.InnerException is OperationCanceledException);
});
Console.WriteLine($"TOTAL passed={passed} failed={failed}; only self-built owned fixture processes started/terminated. No game, FPS DLL or UAC executed.");Environment.ExitCode=failed==0?0:1;

sealed class FakeCatalog:IRestartProcessCatalog
{
    public required Func<string,IReadOnlyList<IRestartProcess>> Capture {get;init;}
    public string NormalizeExecutablePath(string value)=>Path.GetFullPath(value);
    public IReadOnlyList<IRestartProcess> CaptureMatching(string normalized)=>Capture(normalized);
}
sealed class FakeHeld(string path):IRestartProcess
{
    public RestartProcessIdentity Identity{get;}=new(100,path,DateTime.UtcNow);
    public bool Changed{get;set;}
    public Exception? StopError{get;set;}
    public int StopCalls{get;private set;}
    public void VerifyStillSameProcess(){if(Changed)throw new IOException("fake changed/exited identity");}
    public Task TerminateAndWaitAsync(TimeSpan timeout,CancellationToken token){StopCalls++;if(StopError is not null)throw StopError;return Task.CompletedTask;}
    public void Dispose(){}
}
