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
var valid=FpsCoreStatus.Parse("WUWA-FPS/1,123,4,0,240,240,17\n"u8);
Check(valid.Pid==123&&valid.State==FpsCoreState.Maintaining&&valid.Target==240&&valid.Applied==240&&valid.Repairs==17&&valid.ToDisplayText().Contains("实际帧率以游戏为准"),"strict status fields and display distinction");
foreach(string bad in new[]{"WUWA-FPS/2,123,4,0,240,240,17\n","WUWA-FPS/1,123,7,0,240,240,17\n","WUWA-FPS/1,123,4,13,240,240,17\n","WUWA-FPS/1,123,4,0,240,1,17\n","WUWA-FPS/1,123,4,0,240,240,-1\n","WUWA-FPS/1,123,4,0,240,240,17","WUWA-FPS/1,123,4,0,240,240,17\nX"})
    await Reject<InvalidDataException>(()=>Task.Run(()=>FpsCoreStatus.Parse(Encoding.ASCII.GetBytes(bad))));
Check(true,"status rejects version, ranges, negative, missing or trailing data");
var duplex=new ScriptedDuplex("WUWA-FPS/1,123,4,0,240,240,17\n");var queried=new FpsControlChannel();int verified=0;
await queried.ConnectAsync(_=>Task.FromResult<Stream>(duplex),_=>verified++,default);
await queried.SendAsync(240,default);var reply=await queried.QueryStatusAsync(123,default);
Check(reply.Applied==240&&duplex.Messages.SequenceEqual(new[]{"0,0,45.0,0,0,240","status-v1"})&&verified==4,"query follows unchanged command and verifies identity before and after reply");
await queried.DisposeAsync();
var postIdentity=new ScriptedDuplex("WUWA-FPS/1,123,4,0,240,240,17\n");var postQuery=new FpsControlChannel();int postChecks=0;await postQuery.ConnectAsync(_=>Task.FromResult<Stream>(postIdentity),_=>{if(++postChecks==3)throw new IOException("server changed after reply");},default);await Reject<IOException>(()=>postQuery.QueryStatusAsync(123,default));Check(!postQuery.Connected&&postIdentity.Disposals==1,"post-reply identity rejection drops channel");await postQuery.DisposeAsync();
var trailing=new ScriptedDuplex("WUWA-FPS/1,123,4,0,240,240,17\nX",bulkRead:true);var trailingQuery=new FpsControlChannel();await trailingQuery.ConnectAsync(_=>Task.FromResult<Stream>(trailing),_=>{},default);await Reject<InvalidDataException>(()=>trailingQuery.QueryStatusAsync(123,default));Check(!trailingQuery.Connected,"same read trailing data rejected");await trailingQuery.DisposeAsync();
var wrongStatus=new ScriptedDuplex("WUWA-FPS/1,456,4,0,240,240,1\n");var wrongQuery=new FpsControlChannel();await wrongQuery.ConnectAsync(_=>Task.FromResult<Stream>(wrongStatus),_=>{},default);await Reject<IOException>(()=>wrongQuery.QueryStatusAsync(123,default));Check(!wrongQuery.Connected&&wrongStatus.Disposals==1,"wrong reply PID drops channel");await wrongQuery.DisposeAsync();
var invalidStatus=new ScriptedDuplex("WUWA-FPS/1,123,4,0,240,1,1\n");var invalidQuery=new FpsControlChannel();await invalidQuery.ConnectAsync(_=>Task.FromResult<Stream>(invalidStatus),_=>{},default);await Reject<InvalidDataException>(()=>invalidQuery.QueryStatusAsync(123,default));Check(!invalidQuery.Connected,"malformed status drops channel");await invalidQuery.DisposeAsync();
var eofStatus=new ScriptedDuplex(null);var eofQuery=new FpsControlChannel();await eofQuery.ConnectAsync(_=>Task.FromResult<Stream>(eofStatus),_=>{},default);await Reject<EndOfStreamException>(()=>eofQuery.QueryStatusAsync(123,default));Check(!eofQuery.Connected,"status EOF drops channel");await eofQuery.DisposeAsync();
var oversizedStatus=new ScriptedDuplex(new string('9',129));var oversizedQuery=new FpsControlChannel();await oversizedQuery.ConnectAsync(_=>Task.FromResult<Stream>(oversizedStatus),_=>{},default);await Reject<InvalidDataException>(()=>oversizedQuery.QueryStatusAsync(123,default));Check(!oversizedQuery.Connected,"oversize response drops channel");await oversizedQuery.DisposeAsync();
var slowStatus=new ScriptedDuplex("",holdRead:true);var slowQuery=new FpsControlChannel(queryTimeout:TimeSpan.FromMilliseconds(30));await slowQuery.ConnectAsync(_=>Task.FromResult<Stream>(slowStatus),_=>{},default);await Reject<TimeoutException>(()=>slowQuery.QueryStatusAsync(123,default));Check(!slowQuery.Connected,"status timeout drops channel");await slowQuery.DisposeAsync();
var disposedStatus=new ScriptedDuplex("",holdRead:true);var disposeQuery=new FpsControlChannel();await disposeQuery.ConnectAsync(_=>Task.FromResult<Stream>(disposedStatus),_=>{},default);var pendingQuery=disposeQuery.QueryStatusAsync(123,default);await disposedStatus.ReadEntered.Task;var disposePending=disposeQuery.DisposeAsync().AsTask();await Reject<OperationCanceledException>(()=>pendingQuery);await disposePending.WaitAsync(TimeSpan.FromSeconds(3));Check(disposedStatus.Disposals==1,"dispose cancels and waits for pending status query");
string duplexName="WuWaFpsStatusFixture-"+Guid.NewGuid().ToString("N");await using var duplexServer=new NamedPipeServerStream(duplexName,PipeDirection.InOut,1,PipeTransmissionMode.Message,PipeOptions.Asynchronous);var duplexAccept=duplexServer.WaitForConnectionAsync();var realDuplex=new FpsControlChannel();await realDuplex.ConnectAsync(async token=>{var client=new NamedPipeClientStream(".",duplexName,PipeDirection.InOut,PipeOptions.Asynchronous);await client.ConnectAsync(2000,token);return client;},_=>{},default);await duplexAccept;
var serverTask=Task.Run(async()=>{byte[] data=new byte[128];int n=await duplexServer.ReadAsync(data);Check(Encoding.ASCII.GetString(data,0,n)=="0,0,45.0,0,0,160","private duplex receives legacy command");n=await duplexServer.ReadAsync(data);Check(Encoding.ASCII.GetString(data,0,n)=="status-v1","private duplex receives exact query");await duplexServer.WriteAsync("WUWA-FPS/1,123,4,0,160,160,2\n"u8.ToArray());await duplexServer.FlushAsync();});
await realDuplex.SendAsync(160,default);var actualReply=await realDuplex.QueryStatusAsync(123,default).WaitAsync(TimeSpan.FromSeconds(3));await serverTask.WaitAsync(TimeSpan.FromSeconds(3));Check(actualReply.Applied==160,"private real duplex pipe returns maintenance state");await realDuplex.DisposeAsync();
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
sealed class ScriptedDuplex(string? response,bool holdRead=false,bool bulkRead=false):Stream
{
 readonly Queue<byte> _reply=new(response is null?[]:Encoding.ASCII.GetBytes(response));
 public List<string> Messages=new();public int Disposals;public TaskCompletionSource ReadEntered=new(TaskCreationOptions.RunContinuationsAsynchronously);
 public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>true;
 public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
 public override ValueTask WriteAsync(ReadOnlyMemory<byte> bytes,CancellationToken token=default){Messages.Add(Encoding.ASCII.GetString(bytes.Span));return ValueTask.CompletedTask;}
 public override ValueTask<int> ReadAsync(Memory<byte> bytes,CancellationToken token=default){ReadEntered.TrySetResult();return holdRead?new ValueTask<int>(Wait(token)):new ValueTask<int>(ReadOne(bytes));}
 async Task<int> Wait(CancellationToken token){await Task.Delay(Timeout.Infinite,token);return 0;}
 int ReadOne(Memory<byte> bytes){if(_reply.Count==0)return 0;int count=0;do{bytes.Span[count++]=_reply.Dequeue();}while(bulkRead&&_reply.Count>0&&count<bytes.Length);return count;}
 public override void Flush(){}public override Task FlushAsync(CancellationToken token)=>Task.CompletedTask;
 public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
 public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long value)=>throw new NotSupportedException();
 public override ValueTask DisposeAsync(){Disposals++;return ValueTask.CompletedTask;}
}
