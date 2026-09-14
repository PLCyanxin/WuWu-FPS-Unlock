using System.IO.Pipes;
using System.Runtime.InteropServices;
namespace WuWaFpsUnlock.Services;

// Only a restore request crosses this channel. It cannot start a game or perform maintenance.
public sealed class SingleInstanceActivation : IDisposable
{
    public const string Channel = "WuWaFPSUnlock_Activate_v1";
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _listener;
    public SingleInstanceActivation(Action restore, string channel = Channel) => _listener = ListenAsync(restore, channel);
    private async Task ListenAsync(Action restore, string channel)
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(channel, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_stop.Token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                await server.WriteAsync(BitConverter.GetBytes(Environment.ProcessId), timeout.Token);
                var request = new byte[1];
                await server.ReadExactlyAsync(request, timeout.Token);
                if (request[0] == 1) restore();
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
        }
    }
    public static async Task<bool> RequestAsync(string channel = Channel)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var client = new NamedPipeClientStream(".", channel, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
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
    public void Dispose() { _stop.Cancel(); }
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
}
