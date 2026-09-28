using System.Globalization;
using System.IO.Pipes;
using System.Text;
namespace WuWaFpsUnlock.Services;

// One game session owns one retained pipe. No idle polling or heartbeat writes.
internal sealed class FpsControlChannel(TimeSpan? sendTimeout=null):IAsyncDisposable
{
    private readonly SemaphoreSlim _gate=new(1,1);
    private readonly TimeSpan _sendTimeout=sendTimeout??TimeSpan.FromSeconds(5);
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
