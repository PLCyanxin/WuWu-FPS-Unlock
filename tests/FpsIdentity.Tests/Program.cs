using System.IO;
using System.ComponentModel;
using WuWaFpsUnlock.Services;
int passed=0,failed=0;
void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e);}}
void Check(bool value){if(!value)throw new Exception("assertion failed");}
void Reject(Action action){try{action();}catch(IOException){return;}throw new Exception("expected rejection");}
var snapshot=new FpsTargetSnapshot(1234,133900001234567891,@"E:\test\Client-Win64-Shipping.exe");
Test("raw FILETIME preserves all precision",()=>FpsTargetIdentity.VerifySnapshot(snapshot,snapshot with{}));
Test("one tick difference rejected",()=>Reject(()=>FpsTargetIdentity.VerifySnapshot(snapshot,snapshot with{CreationFileTime=snapshot.CreationFileTime+1})));
Test("same PID with new birth time rejected",()=>Reject(()=>FpsTargetIdentity.VerifySnapshot(snapshot,snapshot with{CreationFileTime=snapshot.CreationFileTime+10000000})));
Test("different PID rejected",()=>Reject(()=>FpsTargetIdentity.VerifySnapshot(snapshot,snapshot with{Pid=5678})));
Test("same filename at other path rejected",()=>Reject(()=>FpsTargetIdentity.VerifySnapshot(snapshot,snapshot with{ExecutablePath=@"E:\other\Client-Win64-Shipping.exe"})));
Test("Windows path casing accepted",()=>FpsTargetIdentity.VerifySnapshot(snapshot,snapshot with{ExecutablePath=snapshot.ExecutablePath.ToUpperInvariant()}));
Test("access denied includes exact phase PID code",()=>{
var cause=new Win32Exception(5,"fake access denied");var error=BuiltinFpsService.ExplainFailure("FPS/module-list",1234,cause);
Check(error.Message.Contains("FPS/module-list")&&error.Message.Contains("PID=1234")&&error.Message.Contains("Win32=5")&&ReferenceEquals(error.InnerException,cause));});
Test("renderer and FPS failure stages distinguished",()=>{var a=BuiltinFpsService.ExplainFailure("renderer/identity",1234,new Win32Exception(5));var b=BuiltinFpsService.ExplainFailure("FPS/module-list",1234,new Win32Exception(5));Check(a.Message!=b.Message);});
Test("timeout preserves inner cause",()=>{var cause=new TimeoutException("fixture timeout");var error=BuiltinFpsService.ExplainFailure("renderer/window",1234,cause);Check(ReferenceEquals(error.InnerException,cause));});
Console.WriteLine($"TOTAL passed={passed} failed={failed}; pure records and exception formatting only, no process API calls");Environment.ExitCode=failed==0?0:1;
