using System.Security.Cryptography;
using System.Text;
using WuWaFpsUnlock.Core;
namespace WuWaFpsUnlock.Services;
public static class AppPaths
{
    public static string Base=>AppContext.BaseDirectory;
    public static string Data=>Path.Combine(Base,"data");
    public static string Settings=>Path.Combine(Data,"settings.json");
    public static string FpsCore=>Path.Combine(Base,"components","fps","ww_plugin_base.dll");
    public static string Baseline(string gameExe)=>Receipt(gameExe)+".baseline.json";
    public static string Receipt(string gameExe)
    { string hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(gameExe).ToUpperInvariant())));return Path.Combine(Data,"deployments",hash[..24]+".json"); }
    public static DeploymentReceipt? LoadReceipt(UserSettings s)=>!string.IsNullOrWhiteSpace(s.GameExe)&&File.Exists(Receipt(s.GameExe))?JsonFiles.Read<DeploymentReceipt>(Receipt(s.GameExe)):null;
    public static void SaveReceipt(DeploymentReceipt r){r.Updated=DateTimeOffset.UtcNow;JsonFiles.Save(Receipt(r.GameExe),r);}
}
public sealed class NeedsElevationException(string message):IOException(message);
public static class WriteProbe
{
    public static void Check(string path)
    {
        string parent=Path.GetDirectoryName(Path.GetFullPath(path))!;
        while(!Directory.Exists(parent)) parent=Path.GetDirectoryName(parent)??throw new DirectoryNotFoundException();
        string probe=Path.Combine(parent,".wwfps-probe-"+Guid.NewGuid().ToString("N"));
        try {using(var file=new FileStream(probe,FileMode.CreateNew,FileAccess.Write,FileShare.None))file.WriteByte(0);}
        catch(UnauthorizedAccessException e){throw new NeedsElevationException("目标目录需要管理员写入权限："+parent+"；"+e.Message);}
        finally {if(File.Exists(probe))File.Delete(probe);}
    }
}

