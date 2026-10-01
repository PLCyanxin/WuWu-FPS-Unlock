using System.IO.Pipes;
using WuWaFpsUnlock.Services;
int passed=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
string Channel()=>"ww-activation-fixture-"+Guid.NewGuid().ToString("N");
foreach(bool accessDenied in new[]{false,true})
{
    string name=Channel();int creates=0;var notices=new System.Collections.Concurrent.ConcurrentQueue<string>();
    using var listener=new SingleInstanceActivation(()=>{},name,notices.Enqueue,()=>{
        Interlocked.Increment(ref creates);if(accessDenied)throw new UnauthorizedAccessException("injected access denial");throw new IOException("injected synchronous creation failure");
    });
    await Task.Delay(1200);
    Check(creates is >=1 and <=3&&notices.Count==1,"listener reports and backs off repeated "+(accessDenied?"access denial":"synchronous I/O failure"));
    listener.Dispose();await listener.Completion.WaitAsync(TimeSpan.FromSeconds(5));
}
{
    string name=Channel();int restored=0;
    using var listener=new SingleInstanceActivation(()=>Interlocked.Increment(ref restored),name);
    Check(await SingleInstanceActivation.RequestAsync(name),"private real Windows pipe accepts restore request");
    for(int i=0;i<20&&restored!=1;i++)await Task.Delay(25);
    Check(await SingleInstanceActivation.RequestAsync(name),"listener reconnects after client disconnect");
    for(int i=0;i<20&&restored!=2;i++)await Task.Delay(25);
    Check(restored==2,"two requests restore existing window without other actions");
    listener.Dispose();await listener.Completion.WaitAsync(TimeSpan.FromSeconds(5));
}
{
    string name=Channel();using var occupied=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
    int notices=0;using var listener=new SingleInstanceActivation(()=>{},name,_=>Interlocked.Increment(ref notices));
    await Task.Delay(100);Check(notices==1&&!listener.Completion.IsCompleted,"occupied first-instance pipe is observed without listener termination");
    occupied.Dispose();Check(await SingleInstanceActivation.RequestAsync(name),"listener recovers after occupied pipe is released");
    listener.Dispose();await listener.Completion.WaitAsync(TimeSpan.FromSeconds(5));
}
Check(SingleInstanceActivation.MutexName==@"Local\"+SingleInstanceActivation.Channel,"mutex and pipe use same user and session scope");
Console.WriteLine($"RESULT: {passed} passed; private named pipes, no launcher window or game started.");
