using WuWaFpsUnlock.Services;
int n=0;void Check(bool v,string name){if(!v)throw new Exception(name);Console.WriteLine("PASS "+name);n++;}
SchedulingCpu[] hybrid=[new(10,0,0,8,0),new(11,0,1,8,0),new(12,0,2,0,0),new(13,0,3,0,0)];
Check(LauncherSchedulingPolicy.Select(hybrid,[],15).SequenceEqual(new uint[]{12,13}),"lowest efficiency class uses actual IDs");
Check(LauncherSchedulingPolicy.Select(hybrid,[13],15).SequenceEqual(new uint[]{13}),"original CPU sets respected");
Check(LauncherSchedulingPolicy.Select(hybrid,[],7).SequenceEqual(new uint[]{12}),"hard affinity respected");
Check(LauncherSchedulingPolicy.Select([new(1,0,0,0,0)],[],1).Length==0,"homogeneous topology untouched");
Check(LauncherSchedulingPolicy.Select([hybrid[0],hybrid[2] with{Group=1}],[],15).Length==0,"multiple groups conservatively unchanged");
Check(LauncherSchedulingPolicy.Select([hybrid[0],hybrid[2] with{Flags=2}],[],15).Length==0,"other process reservation excluded");
Check(LauncherSchedulingPolicy.Select([hybrid[0],hybrid[2] with{Flags=1}],[],15).SequenceEqual(new uint[]{12}),"parked efficiency core remains identifiable");
Check(LauncherSchedulingPolicy.Select([hybrid[0],hybrid[2] with{Flags=3}],[],15).Length==0,"parked core reserved by another process still excluded");
var parkedApi=new Fake(hybrid.Select(c=>c with{Flags=1}).ToArray());var parkedPolicy=new LauncherSchedulingPolicy(parkedApi);parkedPolicy.Initialize();parkedPolicy.SetBackground(true);Check(parkedApi.Mask==12,"all parked topology still applies E core policy");parkedPolicy.SetBackground(false);Check(parkedApi.Mask==15,"parked topology foreground restores original mask");
var api=new Fake(hybrid);var policy=new LauncherSchedulingPolicy(api);policy.Initialize();policy.SetBackground(true);Check(api.Mask==12&&api.Current.SequenceEqual(new uint[]{12,13}),"background selects efficient sets");
policy.SetBusy(true);Check(api.Mask==15&&api.Current.Length==0,"busy restores unrestricted original");policy.SetBusy(false);Check(api.Mask==12&&api.Current.Length==2,"idle background reapplies");
policy.CreateChild(()=>{policy.SetBackground(true);Check(api.Mask==15&&api.Current.Length==0,"child creation runs with restored policy");return 1;});Check(api.Mask==12&&api.Current.Length==2,"parent background restored after child create");
try{policy.CreateChild<int>(()=>throw new IOException("fixture"));}catch(IOException){}Check(api.Mask==12&&api.Current.Length==2,"failed child creation still restores parent background policy");
api.FailRestore=true;bool called=false;try{policy.CreateChild(()=>called=true);}catch(IOException){}Check(!called,"restore failure forbids child creation");api.FailRestore=false;policy.SetBackground(false);Check(api.Mask==15&&api.Current.Length==0,"foreground restores original sets");
var original=new Fake(hybrid){Current=[13]};var second=new LauncherSchedulingPolicy(original);second.Initialize();second.SetBackground(true);second.SetBackground(false);Check(original.Current.SequenceEqual(new uint[]{13}),"nonempty inherited CPU set policy restored");
var ignored=new Fake(hybrid){IgnoreWrites=true};var failedVerify=new LauncherSchedulingPolicy(ignored);failedVerify.Initialize();failedVerify.SetBackground(true);Check(failedVerify.Status.Contains("回读不一致"),"silent scheduling mismatch is rejected by readback");
var partial=new Fake(hybrid){FailBackgroundDefaults=true};var rollback=new LauncherSchedulingPolicy(partial);rollback.Initialize();rollback.SetBackground(true);Check(partial.Mask==15&&partial.Current.Length==0,"second-stage failure rolls back both original policies");
var failure=new Fake(hybrid);var blocked=new LauncherSchedulingPolicy(failure);blocked.Initialize();blocked.SetBackground(true);failure.FailMaskRestore=true;bool spawned=false;try{blocked.CreateChild(()=>spawned=true);}catch(IOException){}Check(!spawned&&failure.Current.Length==0,"affinity restore failure blocks child even when defaults restored");failure.FailMaskRestore=false;blocked.SetBackground(false);Check(failure.Mask==15,"failed affinity restore can recover later");
var pOnly=new Fake(hybrid){Mask=3};var noE=new LauncherSchedulingPolicy(pOnly);noE.Initialize();noE.SetBackground(true);Check(pOnly.Writes==0,"original mask without E cores unchanged");
var nonIntel=new Fake(hybrid){Intel=false};var nonIntelPolicy=new LauncherSchedulingPolicy(nonIntel);nonIntelPolicy.Initialize();nonIntelPolicy.SetBackground(true);Check(nonIntel.Writes==0,"non-Intel topology unchanged");
var broken=new Fake(hybrid){FailRead=true};var safe=new LauncherSchedulingPolicy(broken);safe.Initialize();safe.SetBackground(true);Check(broken.Writes==0,"failed detection performs no scheduling write");
byte[] truncated=new byte[8];BitConverter.GetBytes(32u).CopyTo(truncated,0);bool rejects=false;try{WindowsLauncherSchedulingApi.Parse(truncated);}catch(InvalidDataException){rejects=true;}Check(rejects,"truncated topology rejected");
var steadyApi=new Fake(hybrid);var steady=new LauncherSchedulingPolicy(steadyApi);steady.Initialize();steady.SetBackground(true);
for(int i=0;i<1000;i++){steady.SetBackground(true);steady.SetBusy(false);}
int priorWrites=steadyApi.Writes;long allocatedBefore=GC.GetAllocatedBytesForCurrentThread();
for(int i=0;i<10000;i++){steady.SetBackground(true);steady.SetBusy(false);}
long allocated=GC.GetAllocatedBytesForCurrentThread()-allocatedBefore;
Check(steadyApi.Writes==priorWrites,"20000 unchanged scheduling events perform zero policy writes");
Check(allocated==0,"20000 unchanged scheduling events allocate zero managed bytes");
Console.WriteLine($"EFFICIENCY unchanged events=20000 additional writes={steadyApi.Writes-priorWrites} allocated bytes={allocated}; fake API only");
Console.WriteLine($"{n} fake API assertions passed; no native scheduling writes");
if(args.Contains("--read-topology")){
 var native=new WindowsLauncherSchedulingApi();var topology=native.ReadTopology();var defaults=native.ReadDefaults();var affinity=native.ReadAffinity();
 Console.WriteLine("READONLY Intel="+native.IsIntel());
 Console.WriteLine("READONLY original="+string.Join(',',defaults)+" affinity="+affinity.ToString("X"));
 foreach(var cpu in topology)Console.WriteLine($"CPU id={cpu.Id} group={cpu.Group} logical={cpu.Logical} efficiency={cpu.Efficiency} flags={cpu.Flags}");
 Console.WriteLine("READONLY selected="+string.Join(',',LauncherSchedulingPolicy.Select(topology,defaults,affinity)));
}
if(args.Contains("--exercise-current-process")){
 var native=new WindowsLauncherSchedulingApi();var originalMask=native.ReadAffinity();var originalSets=native.ReadDefaults();
 var selected=LauncherSchedulingPolicy.Select(native.ReadTopology(),originalSets,originalMask);
 Check(selected.Length>0,"native process has identifiable E cores including parked cores");
 var current=new LauncherSchedulingPolicy(native);current.Initialize();
 try{
  current.SetBackground(true);
  Check(native.ReadDefaults().Order().SequenceEqual(selected.Order()),"native background CPU sets readback");
  Check(native.ReadAffinity()!=originalMask,"native background E core affinity applied");
  current.SetBusy(true);Check(native.ReadAffinity()==originalMask,"native busy restores original affinity");
  current.SetBusy(false);Check(native.ReadDefaults().Order().SequenceEqual(selected.Order()),"native background idle reapplies E cores");
  current.CreateChild(()=>{Check(native.ReadAffinity()==originalMask,"native child creation restores original affinity");return 0;});
 }finally{current.SetBackground(false);}
 Check(native.ReadAffinity()==originalMask&&native.ReadDefaults().Order().SequenceEqual(originalSets.Order()),"native foreground restores both original policies");
 Console.WriteLine("NATIVE VERIFIED: only this disposable test process was changed; original policy restored.");
}
sealed class Fake(SchedulingCpu[] cpus):ILauncherSchedulingApi{
 public uint[] Current=[];public bool Intel=true;public bool IsIntel()=>Intel;public ulong Mask=15;public bool FailRestore,FailRead,IgnoreWrites,FailBackgroundDefaults,FailMaskRestore;public int Writes;
 public uint[] ReadDefaults()=>FailRead?throw new IOException("fake query failure"):Current.ToArray();
 public SchedulingCpu[] ReadTopology()=>cpus;public ulong ReadAffinity()=>Mask;
 public void SetAffinity(ulong mask){Writes++;if(FailMaskRestore&&mask==15)throw new IOException("fake affinity restore failure");if(!IgnoreWrites)Mask=mask;}
 public void SetDefaults(uint[] ids){if(FailBackgroundDefaults&&ids.Length>0)throw new IOException("fake second phase failure");if(FailRestore&&ids.Length==0)throw new IOException("fake restore failure");Writes++;if(!IgnoreWrites)Current=ids.ToArray();}
}
