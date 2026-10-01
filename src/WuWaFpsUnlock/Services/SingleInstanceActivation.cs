using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
namespace WuWaFpsUnlock.Services;

// Only a restore request crosses this channel. It cannot start a game or perform maintenance.
public sealed class SingleInstanceActivation : IDisposable
{
    public static string Channel {get;} = "WuWaFPSUnlock_Activate_v2_"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        WindowsIdentity.GetCurrent().User!.Value+":"+System.Diagnostics.Process.GetCurrentProcess().SessionId)))[..24];
    public static string MutexName => @"Local\"+Channel;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _listener;
    private int _disposed;
    private static void Report(Action<string>? report,string message){try{report?.Invoke(message);}catch{/* Shutdown may have disposed the UI dispatcher. */}}
    public Task Completion => _listener;
    public SingleInstanceActivation(Action restore, string? channel = null, Action<string>? report = null)
        : this(restore,channel??Channel,report,()=>new NamedPipeServerStream(channel??Channel,PipeDirection.InOut,1,
            PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly)) { }
    internal SingleInstanceActivation(Action restore,string channel,Action<string>? report,Func<NamedPipeServerStream> create)
        => _listener = Task.Run(()=>ListenAsync(restore,report,create));
    private async Task ListenAsync(Action restore, Action<string>? report, Func<NamedPipeServerStream> create)
    {
        int failures=0;
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var server = create();
                await server.WaitForConnectionAsync(_stop.Token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                await server.WriteAsync(BitConverter.GetBytes(Environment.ProcessId), timeout.Token);
                var request = new byte[1];
                await server.ReadExactlyAsync(request, timeout.Token);
                if (request[0] == 1) restore();
                if(failures>0)Report(report,"启动器唤醒通道已恢复。");
                failures=0;
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
            catch (Exception error) when(error is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                if(failures++==0)Report(report,"启动器唤醒通道暂不可用，将重试；可从托盘打开窗口："+error.Message);
                try{await Task.Delay(Math.Min(5000,250*(1<<Math.Min(failures,4))),_stop.Token);}
                catch(OperationCanceledException){break;}
            }
            catch(Exception error){Report(report,"启动器唤醒通道已停止，请通过托盘打开窗口："+error.Message);break;}
        }
    }
    public static async Task<bool> RequestAsync(string? channel = null)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var client = new NamedPipeClientStream(".", channel??Channel, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await client.ConnectAsync(timeout.Token);
            var pid = new byte[4];
            await client.ReadExactlyAsync(pid, timeout.Token);
            AllowSetForegroundWindow(BitConverter.ToInt32(pid));
            await client.WriteAsync(new byte[] { 1 }, timeout.Token);
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
    public void Dispose() { if(Interlocked.Exchange(ref _disposed,1)!=0)return;_stop.Cancel();_=_listener.ContinueWith(_=>_stop.Dispose(),TaskScheduler.Default); }
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
}
