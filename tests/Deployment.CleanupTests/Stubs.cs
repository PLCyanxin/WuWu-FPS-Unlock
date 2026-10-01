using WuWaFpsUnlock.Core;
namespace WuWaFpsUnlock.Services;
// Only the real cleanup code is exercised. Runtime, game and installer operations are prohibited.
public static class GameProcesses
{
    public static bool Running;
    public static string ValidateExe(UserSettings s) { SafePaths.EnsureInside(s.GameRoot,s.GameExe); return s.GameExe; }
    public static void RequireStopped(string root) { if(Running)throw new IOException("Fixture game running"); }
}
public static class AppPaths
{
    public static string Root="";
    public static string Base=>Root;
    public static string Baseline(string exe)=>Path.Combine(Root,"baseline.json");
    public static DeploymentReceipt? LoadReceipt(UserSettings s)=>File.Exists(Path.Combine(Root,"receipt.json"))?JsonFiles.Read<DeploymentReceipt>(Path.Combine(Root,"receipt.json")):null;
    public static void SaveReceipt(DeploymentReceipt r){r.Updated=DateTimeOffset.UtcNow;JsonFiles.Save(Path.Combine(Root,"receipt.json"),r);}
}
public static class WriteProbe { public static Action? OnCheck; public static void Check(string path) { OnCheck?.Invoke(); } }
public static class EnvironmentProbe { public static HardwareInfo ReadBasic(Action<string> log)=>throw new Exception("Forbidden environment probe"); }
public sealed record ReShadeInfo(string State,string? Proxy,string Ini,string AddonDirectory,string Description);
public sealed class ReShadeService
{
    public ReShadeService(Action<string> log) { }
    public ReShadeInfo Inspect(UserSettings s,ReShadeSpec spec)=>throw new Exception("Forbidden installer operation");
    public Task<ReShadeInfo> EnsureAsync(UserSettings s,ReShadeSpec spec,DeploymentReceipt r,bool approved,CancellationToken token)=>throw new Exception("Forbidden installer operation");
}
public static class EmbeddedDynamicAddon
{
    public static Task AppendToPlanAsync(List<PlannedFile> plan,UserSettings s,string directory,CancellationToken token)=>throw new Exception("Forbidden deployment operation");
}
