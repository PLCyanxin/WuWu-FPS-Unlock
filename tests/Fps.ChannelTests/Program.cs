using System.IO.Pipes;
using System.Text;
using WuWaFpsUnlock.Services;
int checks=0;
void Check(bool v,string name){if(!v)throw new Exception(name);Console.WriteLine("PASS "+name);checks++;}
async Task Reject<T>(Func<Task> act) where T:Exception{try{await act();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
var bytes=new Observed();var channel=new FpsControlChannel();int identities=0;
await channel.ConnectAsync(_=>Task.FromResult<Stream>(bytes),_=>identities++,default);await channel.SendAsync(333,default);
Check(Encoding.ASCII.GetString(bytes.ToArray())=="0,0,45.0,0,0,333","exact original protocol and disabled advanced values");
Check(identities==2,"identity verified on connect and every send");
await Task.Delay(100);Check(bytes.Writes==1&&bytes.Disposals==0&&channel.Connected,"idle keeps pipe mounted without heartbeat writes");
int attempts=0;await Reject<InvalidOperationException>(()=>channel.ConnectAsync(_=>{attempts++;return Task.FromResult<Stream>(new Observed());},_=>{},default));Check(attempts==0,"same session cannot reconnect or replace PID");
await Reject<ArgumentOutOfRangeException>(()=>channel.SendAsync(421,default));Check(bytes.Writes==1,"invalid FPS does not write");
await Task.WhenAll(channel.DisposeAsync().AsTask(),channel.DisposeAsync().AsTask());Check(bytes.Disposals==1&&!channel.Connected,"concurrent dispose closes retained pipe exactly once");
await Reject<OperationCanceledException>(()=>channel.SendAsync(240,default));Check(bytes.Writes==1,"late send after disposal cannot write");
var blocked=new Blocking();var sending=new FpsControlChannel();await sending.ConnectAsync(_=>Task.FromResult<Stream>(blocked),_=>{},default);
var send=sending.SendAsync(240,default);await blocked.Entered.Task;var dispose=sending.DisposeAsync().AsTask();await Reject<OperationCanceledException>(()=>send);await dispose.WaitAsync(TimeSpan.FromSeconds(3));Check(blocked.Disposals==1,"dispose cancels in-flight write before closing stream");
var delayed=new FpsControlChannel();var connected=new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);var pending=new Observed();var connecting=delayed.ConnectAsync(_=>connected.Task,_=>{},default);var closed=delayed.DisposeAsync().AsTask();connected.SetResult(pending);await Reject<OperationCanceledException>(()=>connecting);await closed.WaitAsync(TimeSpan.FromSeconds(3));Check(pending.Disposals==1&&!delayed.Connected,"late connection after cancellation is disposed and never published");
var wrong=new FpsControlChannel();var wrongPipe=new Observed();await Reject<IOException>(()=>wrong.ConnectAsync(_=>Task.FromResult<Stream>(wrongPipe),_=>throw new IOException("wrong fixture PID"),default));await wrong.DisposeAsync();Check(wrongPipe.Disposals==1&&wrongPipe.Writes==0,"wrong identity pipe closed before any settings");
var changedIdentity=new FpsControlChannel();var stalePipe=new Observed();int identityChecks=0;await changedIdentity.ConnectAsync(_=>Task.FromResult<Stream>(stalePipe),_=>{if(++identityChecks>1)throw new IOException("fixture identity changed");},default);await Reject<IOException>(()=>changedIdentity.SendAsync(240,default));Check(stalePipe.Disposals==1&&stalePipe.Writes==0&&!changedIdentity.Connected,"identity rejection during send drops stale channel immediately");await changedIdentity.DisposeAsync();Check(stalePipe.Disposals==1,"rejected channel disposal remains idempotent");
var slow=new Blocking();var timed=new FpsControlChannel(TimeSpan.FromMilliseconds(30));await timed.ConnectAsync(_=>Task.FromResult<Stream>(slow),_=>{},default);await Reject<TimeoutException>(()=>timed.SendAsync(240,default));Check(slow.Disposals==1&&!timed.Connected,"bounded write timeout drops partial-message channel without retry");await timed.DisposeAsync();
var freshPipe=new Observed();var fresh=new FpsControlChannel();await fresh.ConnectAsync(_=>Task.FromResult<Stream>(freshPipe),_=>{},default);await channel.DisposeAsync();await fresh.SendAsync(360,default);Check(freshPipe.Writes==1&&fresh.Connected,"old session disposal cannot disconnect replacement session");await fresh.DisposeAsync();
string name="WuWaFpsChannelFixture-"+Guid.NewGuid().ToString("N");await using var server=new NamedPipeServerStream(name,PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous);var accept=server.WaitForConnectionAsync();var live=new FpsControlChannel();await live.ConnectAsync(async token=>{var client=new NamedPipeClientStream(".",name,PipeDirection.Out,PipeOptions.Asynchronous);try{await client.ConnectAsync(2000,token);return client;}catch{client.Dispose();throw;}},_=>{},default);await accept;byte[] buffer=new byte[64];var reading=server.ReadAsync(buffer).AsTask();await live.SendAsync(144,default).WaitAsync(TimeSpan.FromSeconds(3));int read=await reading.WaitAsync(TimeSpan.FromSeconds(3));Check(Encoding.ASCII.GetString(buffer,0,read)=="0,0,45.0,0,0,144","actual isolated named pipe receives original message");await live.DisposeAsync();Check(await server.ReadAsync(buffer)==0,"actual pipe disconnect after disposal");
Console.WriteLine($"{checks} checks passed; no injection or game process operations");
class Observed:MemoryStream{
 public int Writes,Disposals;
 public override ValueTask WriteAsync(ReadOnlyMemory<byte> b,CancellationToken token=default){Writes++;return base.WriteAsync(b,token);}
 public override ValueTask DisposeAsync(){Disposals++;return base.DisposeAsync();}
}
sealed class Blocking:Observed{
 public TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);
 public override async ValueTask WriteAsync(ReadOnlyMemory<byte> b,CancellationToken token=default){Entered.SetResult();await Task.Delay(Timeout.Infinite,token);}
}
