using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using WuWaFpsUnlock.Services;

if(args.Length!=1)throw new ArgumentException("Provide the original published updater EXE.");
var reference=Path.GetFullPath(args[0]);
var assembly=typeof(AppPaths).Assembly;
var method=assembly.GetType("WuWaFpsUnlock.Services.RollbackWorkerResource",true)!.GetMethod("Extract",BindingFlags.Public|BindingFlags.Static)!;
var watch=Stopwatch.StartNew();long before=GC.GetTotalAllocatedBytes(true);
string path=(string)method.Invoke(null,null)!;
long allocated=GC.GetTotalAllocatedBytes(true)-before;
Console.WriteLine($"Streaming extraction: {watch.Elapsed.TotalMilliseconds:F1} ms, {allocated:N0} allocated bytes");
try
{
    using(var expected=File.OpenRead(reference))using(var actual=File.OpenRead(path))
        if(expected.Length!=actual.Length || !SHA256.HashData(expected).SequenceEqual(SHA256.HashData(actual)))
            throw new InvalidDataException("Extracted updater differs from its published source.");
    if(allocated>8*1024*1024)throw new Exception("Extraction unexpectedly buffers the whole updater.");
    Console.WriteLine("PASS exact updater bytes and bounded streaming allocation");
    using var child=Process.Start(new ProcessStartInfo(path,"--self-test"){UseShellExecute=false,CreateNoWindow=true})!;
    if(!child.WaitForExit(60000)){child.Kill();throw new TimeoutException("Isolated updater self-test timed out.");}
    if(child.ExitCode!=0)throw new Exception("Extracted updater self-test failed.");
    Console.WriteLine("PASS extracted updater executes its isolated regression suite");
}
finally { File.Delete(path);Directory.Delete(Path.GetDirectoryName(path)!); }
