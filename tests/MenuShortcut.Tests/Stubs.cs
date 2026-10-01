using WuWaFpsUnlock.Core;
namespace WuWaFpsUnlock.Services;
internal static class Fixture
{
    public static string Root="";
    public static string State="Reusable";
    public static bool Running;
    public static Action? BeforeWrite;
    public static int Writes;
}
internal static class GameProcesses
{
    public static void RequireStopped(string root){if(Fixture.Running)throw new IOException("Fixture game is running");}
}
internal sealed record ReShadeInfo(string State,string? Proxy,string Ini,string Description);
internal sealed class ReShadeService
{
    public ReShadeService(Action<string> log) { }
    public ReShadeInfo Inspect(UserSettings settings)=>new(Fixture.State,Path.Combine(Fixture.Root,"proxy.dll"),Path.Combine(Fixture.Root,"ReShade.ini"),"fixture runtime");
}
internal static class DeploymentService
{
    public static FileStream AcquireMaintenanceLock(UserSettings settings)=>new(Path.Combine(Fixture.Root,"maintenance.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
}
internal static class WriteProbe
{
    public static void Check(string path){Fixture.Writes++;Fixture.BeforeWrite?.Invoke();}
}
