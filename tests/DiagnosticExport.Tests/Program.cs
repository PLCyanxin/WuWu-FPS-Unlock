using System.IO;
using System.Linq;
using System.Text;
using WuWaFpsUnlock.Services;

string workspace = Path.Combine(Path.GetTempPath(), "WuWa-DiagnosticExport-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(workspace);
try
{
    string logs = Path.Combine(workspace, "launcher", "logs");
    string selected = Path.Combine(workspace, "selected", "Client", "Binaries", "Win64");
    string other = Path.Combine(workspace, "other", "Client", "Binaries", "Win64");
    Directory.CreateDirectory(logs); Directory.CreateDirectory(selected); Directory.CreateDirectory(other);
    string current = Path.Combine(logs, "current.log");
    string previous = Path.Combine(logs, "previous.log");
    File.WriteAllText(current, "InvalidOperationException: failure\n   at Launch.Stage()\ntoken=private-value");
    File.WriteAllText(previous, "previous stage result");
    File.WriteAllText(Path.Combine(workspace, "launcher", "startup-error.log"), "startup exception stack");
    File.WriteAllText(Path.Combine(other, "ReShade.log"), "OTHER_INSTALL_MARKER");
    var recent = DiagnosticExport.RecentLauncherLogs(current);
    Require(recent.Count == 3 && recent.Contains(current) && recent.Contains(previous) && recent.Any(p => p.EndsWith("startup-error.log")), "current, recent and startup selection");
    string result = Path.Combine(workspace, "diagnostic.txt");
    using (new FileStream(current, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        await DiagnosticExport.WriteAsync(result, ["版本=1.2.4RC", "api_key=hidden-value"], recent, selected, "selected proxy directory");
    string exported = File.ReadAllText(result, Encoding.UTF8);
    Require(exported.Contains("InvalidOperationException: failure") && exported.Contains("at Launch.Stage()"), "full exception retained");
    Require(exported.Contains("previous stage result") && exported.Contains("startup exception stack") && exported.Contains("暂无 ReShade 日志"), "previous, startup and missing ReShade logs");
    Require(!exported.Contains("private-value") && !exported.Contains("hidden-value"), "secrets redacted");
    Require(!exported.Contains("OTHER_INSTALL_MARKER"), "other game log excluded");
    Require(!File.ReadAllBytes(result).Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }), "UTF-8 without BOM");
    File.WriteAllText(Path.Combine(selected, "ReShade.log"), "SELECTED_INSTALL_MARKER");
    await DiagnosticExport.WriteAsync(result, [], recent, selected, "selected proxy directory");
    Require(File.ReadAllText(result).Contains("SELECTED_INSTALL_MARKER"), "selected game ReShade included");
    string large = Path.Combine(logs, "large.log");
    File.WriteAllBytes(large, Enumerable.Repeat((byte)'x', 16 * 1024 * 1024 + 128).ToArray());
    string bounded = await DiagnosticExport.ReadBoundedAsync(large, "missing");
    Require(bounded.Contains("已省略前 128 字节") && bounded.Length < 16 * 1024 * 1024 + 200, "strict 16 MiB read bound");
    Console.WriteLine("PASS DiagnosticExport: current/recent, shared read, missing/selected ReShade, isolation, stack, redaction, UTF-8");
}
finally { Directory.Delete(workspace, true); }

static void Require(bool condition, string reason) { if (!condition) throw new Exception(reason); }
