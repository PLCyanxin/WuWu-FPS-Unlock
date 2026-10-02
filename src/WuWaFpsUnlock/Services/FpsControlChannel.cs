using System.Globalization;
using System.IO.Pipes;
using System.Text;
namespace WuWaFpsUnlock.Services;

// One game session owns one retained pipe. Status reads are serialized with writes.
internal sealed class FpsControlChannel(TimeSpan? sendTimeout=null, TimeSpan? queryTimeout=null):IAsyncDisposable
{
    private static readonly byte[] StatusRequest="status-v1"u8.ToArray();
    private readonly SemaphoreSlim _gate=new(1,1);
    private readonly TimeSpan _sendTimeout=sendTimeout??TimeSpan.FromSeconds(5);
    private readonly TimeSpan _queryTimeout=queryTimeout??TimeSpan.FromSeconds(5);
    private readonly CancellationTokenSource _stop=new();
    private readonly object _disposeLock=new();
    private Task? _disposeTask;
    private int _disposing;
    private bool _connectAttempted;
    private Stream? _stream;
    private Action<Stream>? _verify;
    internal bool Connected
    {
        get
        {
            if(Volatile.Read(ref _disposing)!=0)return false;
            var stream=Volatile.Read(ref _stream);
            try{return stream is PipeStream pipe?pipe.IsConnected:stream?.CanWrite==true;}
            catch(ObjectDisposedException){return false;}
        }
    }
    internal async Task ConnectAsync(Func<CancellationToken,Task<Stream>> connect,Action<Stream> verify,CancellationToken token)
    {
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(token,_stop.Token);
        await _gate.WaitAsync(linked.Token);
        Stream? pending=null;
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposing)!=0,this);
            if(_connectAttempted)throw new InvalidOperationException("FPS 会话已使用；重启游戏必须创建新会话。");
            _connectAttempted=true;
            pending=await connect(linked.Token);
            linked.Token.ThrowIfCancellationRequested();verify(pending);
            _verify=verify;_stream=pending;pending=null;
        }
        finally{try{if(pending is not null)await pending.DisposeAsync();}finally{_gate.Release();}}
    }
    internal async Task SendAsync(int fps,CancellationToken token)
    {
        if(fps is <30 or >420)throw new ArgumentOutOfRangeException(nameof(fps));
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(token,_stop.Token);
        await _gate.WaitAsync(linked.Token);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposing)!=0,this);
            if(!Connected||_stream is null)throw new IOException("FPS 控制通道未连接。");
            try{_verify!(_stream);}
            catch{await DropStreamAsync();throw;}
            // Exact original six-field protocol. Advanced settings remain disabled.
            byte[] message=Encoding.ASCII.GetBytes("0,0,45.0,0,0,"+fps.ToString(CultureInfo.InvariantCulture));
            linked.CancelAfter(_sendTimeout);
            try{await _stream.WriteAsync(message,linked.Token);await _stream.FlushAsync(linked.Token);}
            catch(OperationCanceledException) when(!token.IsCancellationRequested&&!_stop.IsCancellationRequested)
            {
                await DropStreamAsync();
                throw new TimeoutException("FPS 设置发送超时；已关闭此会话通道，未自动重连或重复加载。");
            }
            catch{await DropStreamAsync();throw;}
        }
        finally{_gate.Release();}
    }
    internal async Task<FpsCoreStatus> QueryStatusAsync(int expectedPid,CancellationToken token)
    {
        if(expectedPid<=0)throw new ArgumentOutOfRangeException(nameof(expectedPid));
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(token,_stop.Token);
        await _gate.WaitAsync(linked.Token);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposing)!=0,this);
            if(!Connected||_stream is null||!_stream.CanRead)throw new IOException("FPS 状态通道未连接或不支持读取。");
            try{_verify!(_stream);}
            catch{await DropStreamAsync();throw;}
            linked.CancelAfter(_queryTimeout);
            try
            {
                await _stream.WriteAsync(StatusRequest,linked.Token);
                await _stream.FlushAsync(linked.Token);
                byte[] reply=new byte[128];
                int count=0;
                while(count<reply.Length)
                {
                    int read=await _stream.ReadAsync(reply.AsMemory(count),linked.Token);
                    if(read==0)throw new EndOfStreamException("FPS 状态管道在回执前关闭。");
                    int newline=Array.IndexOf(reply,(byte)'\n',count,read);
                    count+=read;
                    if(newline>=0)
                    {
                        if(newline!=count-1)throw new InvalidDataException("FPS 状态回执包含多余数据。");
                        break;
                    }
                }
                if(count==reply.Length && reply[count-1]!=(byte)'\n')throw new InvalidDataException("FPS 状态回执超过 128 字节。");
                var status=FpsCoreStatus.Parse(reply.AsSpan(0,count));
                if(status.Pid!=expectedPid)throw new IOException($"FPS 状态回执 PID 不匹配：预期 {expectedPid}，实际 {status.Pid}。");
                _verify!(_stream);
                return status;
            }
            catch(OperationCanceledException) when(!token.IsCancellationRequested&&!_stop.IsCancellationRequested)
            {
                await DropStreamAsync();
                throw new TimeoutException("FPS 状态回执超时；已关闭会话通道。");
            }
            catch{await DropStreamAsync();throw;}
        }
        finally{_gate.Release();}
    }
    private async ValueTask DropStreamAsync()
    {
        var stream=_stream;_stream=null;_verify=null;
        if(stream is not null)await stream.DisposeAsync();
    }
    public ValueTask DisposeAsync()
    {
        lock(_disposeLock)return new(_disposeTask??=DisposeCoreAsync());
    }
    private async Task DisposeCoreAsync()
    {
        Interlocked.Exchange(ref _disposing,1);_stop.Cancel();
        await _gate.WaitAsync();
        try
        {
            await DropStreamAsync();
        }
        finally{_gate.Release();}
        // Keep cancellation/gate objects alive so concurrent late callers observe
        // cancellation rather than racing disposal of synchronization primitives.
    }
}
