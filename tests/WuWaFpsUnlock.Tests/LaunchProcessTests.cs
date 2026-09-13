using System.Diagnostics;
using WuWaFpsUnlock.Core;

public static class LaunchProcessTests
{
    // Child fixture deliberately has no reference to unlocker binaries or real game paths.
    public static async Task<bool> HandleFixtureAsync(string[] arguments)
    {
        if(arguments.Length!=3||arguments[0]!="--launch-fixture")return false;
        File.AppendAllText(arguments[1],Environment.ProcessId+Environment.NewLine);
        await Task.Delay(int.Parse(arguments[2]));return true;
    }
    public static async Task RunAsync(Func<string,Func<Task>,Task> test)
    {
        string root=Path.GetFullPath(Path.Combine("artifacts","test-work","真实子进程 空格-"+Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        string fixtureDirectory=Path.Combine(root,"子程序 中文");Directory.CreateDirectory(fixtureDirectory);
        string assemblyDirectory=Path.GetDirectoryName(typeof(LaunchProcessTests).Assembly.Location)!;
        foreach(string source in Directory.GetFiles(assemblyDirectory))File.Copy(source,Path.Combine(fixtureDirectory,Path.GetFileName(source)),true);
        string fixtureHost=Path.Combine(fixtureDirectory,Path.GetFileNameWithoutExtension(typeof(LaunchProcessTests).Assembly.Location)+".exe");
        void Check(bool value,string why){if(!value)throw new Exception(why);}
        ProcessStartInfo Fixture(string marker,int duration=900)
        {
            string executable=Environment.ProcessPath??throw new Exception("process path missing");
            var info=new ProcessStartInfo(File.Exists(fixtureHost)?fixtureHost:executable){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true};
            if(Path.GetFileNameWithoutExtension(executable).Equals("dotnet",StringComparison.OrdinalIgnoreCase))
            {
                info.Environment["DOTNET_ROOT_X64"]=Path.GetDirectoryName(executable)!;
                if(!File.Exists(fixtureHost))info.ArgumentList.Add(Path.Combine(fixtureDirectory,Path.GetFileName(typeof(LaunchProcessTests).Assembly.Location)));
            }
            info.ArgumentList.Add("--launch-fixture");info.ArgumentList.Add(marker);info.ArgumentList.Add(duration.ToString());
            return info;
        }
        LaunchPlan Plan(bool fps)=>new(fps,"fixture only",root,"synthetic renderer",null,null,240);
        async Task WaitMarker(string marker)
        {
            var watch=Stopwatch.StartNew();while(!File.Exists(marker)){if(watch.Elapsed>TimeSpan.FromSeconds(10))throw new TimeoutException("fixture did not start");await Task.Delay(20);}
        }
        async Task Expect<T>(Func<Task> action) where T:Exception
        {try{await action();}catch(T){return;}throw new Exception("expected "+typeof(T).Name);}
        foreach(bool fps in new[]{false,true})
            await test("actual fixture single process "+(fps?"FPS ON":"FPS OFF")+" and Chinese path",async()=>{
                string marker=Path.Combine(root,Guid.NewGuid()+" 中文 marker.txt");int starts=0;Process? child=null;var states=new List<string>();
                using var observed=await new LaunchExecution().RunAsync(Plan(fps),()=>Task.CompletedTask,()=>{starts++;child=Process.Start(Fixture(marker))!;return child;},
                    async(_,ct)=>{await WaitMarker(marker);return Process.GetProcessById(int.Parse(File.ReadAllLines(marker)[0]));},states.Add,TimeSpan.FromSeconds(10),default);
                await observed.WaitForExitAsync();
                Check(starts==1 && File.ReadAllLines(marker).Length==1,"more than one fixture launch");
                Check(states.SequenceEqual(new[]{fps?"StartingUnlocker":"StartingShipping","WaitingForRenderer","GameRunning"}),"wrong branch/state transition");
            });
        await test("actual fixture starts but no renderer: timeout is failure, no second launch",async()=>{
            string marker=Path.Combine(root,Guid.NewGuid()+".txt");var states=new List<string>();int starts=0;int pid=0;
            await Expect<TimeoutException>(()=>new LaunchExecution().RunAsync(Plan(true),()=>Task.CompletedTask,()=>{starts++;var p=Process.Start(Fixture(marker,1200))!;pid=p.Id;return p;},
                async(_,ct)=>{await Task.Delay(Timeout.Infinite,ct);throw new Exception("unreachable");},states.Add,TimeSpan.FromMilliseconds(500),default));
            await WaitMarker(marker);using var child=Process.GetProcessById(pid);Check(!child.HasExited,"controller killed fixture on timeout");await child.WaitForExitAsync();
            Check(starts==1 && !states.Contains("GameRunning") && states[^1]=="Failed","timeout reported as game success");
        });
        await test("actual fixture exits before renderer: exit is not game success",async()=>{
            string marker=Path.Combine(root,Guid.NewGuid()+".txt");var states=new List<string>();int starts=0;
            await Expect<TimeoutException>(()=>new LaunchExecution().RunAsync(Plan(true),()=>Task.CompletedTask,()=>{starts++;return Process.Start(Fixture(marker,100))!;},
                async(_,ct)=>{await Task.Delay(Timeout.Infinite,ct);throw new Exception("unreachable");},states.Add,TimeSpan.FromMilliseconds(900),default));
            await WaitMarker(marker);Check(starts==1 && !states.Contains("GameRunning") && states[^1]=="Failed","external exit incorrectly reported game success");
        });
        await test("missing configuration stops before actual process creation",async()=>{
            string marker=Path.Combine(root,Guid.NewGuid()+".txt");int starts=0;
            var plan=Plan(true) with {ConfigPath=Path.Combine(root,"missing.ini")};
            await Expect<InvalidDataException>(()=>new LaunchExecution().RunAsync(plan,()=>{UnlockerConfigAdapter.Prepare(plan);return Task.CompletedTask;},
                ()=>{starts++;return Process.Start(Fixture(marker))!;},(_,_)=>throw new Exception("must not wait"),_=>{},TimeSpan.FromSeconds(1),default));
            Check(starts==0&&!File.Exists(marker),"missing INI launched a process");
        });
        await test("actual running same-name fixture blocks unsafe external route",async()=>{
            string marker=Path.Combine(root,Guid.NewGuid()+".txt");using var child=Process.Start(Fixture(marker))!;await WaitMarker(marker);
            await Expect<IOException>(()=>{LaunchProcessGuard.RequireNamesStopped(new[]{child.ProcessName});return Task.CompletedTask;});
            Check(!child.HasExited,"guard terminated existing process");await child.WaitForExitAsync();
        });
        await test("actual fixture concurrent double start blocked",async()=>{
            string marker=Path.Combine(root,Guid.NewGuid()+".txt");var execution=new LaunchExecution();int starts=0;
            var first=execution.RunAsync(Plan(true),()=>Task.CompletedTask,()=>{starts++;return Process.Start(Fixture(marker,1100))!;},
                async(_,ct)=>{await WaitMarker(marker);await Task.Delay(350,ct);return Process.GetProcessById(int.Parse(File.ReadAllLines(marker)[0]));},_=>{},TimeSpan.FromSeconds(5),default);
            await WaitMarker(marker);
            await Expect<IOException>(()=>execution.RunAsync(Plan(true),()=>Task.CompletedTask,()=>{starts++;return Process.Start(Fixture(marker))!;},(_,_)=>throw new Exception("second wait"),_=>{},TimeSpan.FromSeconds(5),default));
            using var observed=await first;await observed.WaitForExitAsync();Check(starts==1 && File.ReadAllLines(marker).Length==1,"duplicate process created");
        });
        await test("actual fixture monitoring cancellation leaves child alive",async()=>{
            string marker=Path.Combine(root,Guid.NewGuid()+".txt");using var cts=new CancellationTokenSource();int pid=0;var states=new List<string>();
            var running=new LaunchExecution().RunAsync(Plan(false),()=>Task.CompletedTask,()=>{var p=Process.Start(Fixture(marker,1100))!;pid=p.Id;return p;},
                async(_,ct)=>{await Task.Delay(Timeout.Infinite,ct);throw new Exception("unreachable");},states.Add,TimeSpan.FromSeconds(10),cts.Token);
            await WaitMarker(marker);cts.Cancel();await Expect<OperationCanceledException>(async()=>{await running;});
            using var child=Process.GetProcessById(pid);Check(!child.HasExited,"cancel killed child");await child.WaitForExitAsync();Check(states[^1]=="Cancelled","cancel state missing");
        });
    }
}
