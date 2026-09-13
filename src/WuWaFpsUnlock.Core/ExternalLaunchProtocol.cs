using System.Security.Cryptography;
using System.Text.Json;
namespace WuWaFpsUnlock.Core;

public sealed class ExternalLaunchRequest
{
    public string Nonce {get;set;}="";
    public DateTimeOffset CreatedUtc {get;set;}
    public UserSettings Settings {get;set;}=new();
    public string PlanFingerprint {get;set;}="";
}
public sealed class ExternalLaunchResult
{
    public string Nonce {get;set;}="";
    public bool Success {get;set;}
    public int ChildPid {get;set;}
    public DateTime ChildStartedUtc {get;set;}
    public string Phase {get;set;}="ValidateRequest";
    public string Error {get;set;}="";
    public List<string> Log {get;set;}=[];
}
public static class ExternalLaunchProtocol
{
    public static string Fingerprint(LaunchPlan plan)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(plan,JsonFiles.Options)));
    public static string RequestPath(string jobs,string nonce)=>PathFor(jobs,nonce,".external-request.json");
    public static string ResultPath(string jobs,string nonce)=>PathFor(jobs,nonce,".external-result.json");
    public static string ClaimPath(string jobs,string nonce)=>PathFor(jobs,nonce,".external-claim");
    private static string PathFor(string jobs,string nonce,string suffix)
    {
        if(!Guid.TryParseExact(nonce,"N",out _))throw new InvalidDataException("启动请求 nonce 无效。");
        return SafePaths.Under(jobs,nonce+suffix);
    }
    public static FileStream Claim(string jobs,string nonce)=>new(ClaimPath(jobs,nonce),FileMode.CreateNew,FileAccess.Write,FileShare.None);
    public static ExternalLaunchRequest Read(string jobs,string requestPath,string nonce,string expectedDigest,DateTimeOffset now)
    {
        string expected=RequestPath(jobs,nonce);
        if(!string.Equals(Path.GetFullPath(requestPath),expected,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("启动请求必须位于本工具 data/jobs 下的固定文件名。");
        SafePaths.EnsureNoLinks(jobs,expected);
        using var stream=new FileStream(expected,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(stream.Length is <=0 or >65536)throw new InvalidDataException("启动请求大小无效。");
        byte[] bytes=new byte[(int)stream.Length];stream.ReadExactly(bytes);
        if(expectedDigest.Length!=64||!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)),expectedDigest,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("启动请求在确认后被修改，未启动。");
        var request=JsonSerializer.Deserialize<ExternalLaunchRequest>(bytes,JsonFiles.Options)??throw new InvalidDataException("启动请求为空。");
        if(request.Nonce!=nonce||!request.Settings.FpsEnabled||request.CreatedUtc>now.AddSeconds(10)||request.CreatedUtc<now.AddMinutes(-5))throw new InvalidDataException("启动请求不匹配、过期或不是 FPS 外部启动。");
        return request;
    }
    public static void ValidatePlan(ExternalLaunchRequest request,LaunchPlan rebuilt)
    {
        if(!rebuilt.FpsEnabled||!string.Equals(request.PlanFingerprint,Fingerprint(rebuilt),StringComparison.Ordinal))
            throw new InvalidDataException("提升后启动路径/配置与已确认计划不一致；未启动。");
    }
    public static void ValidateResult(ExternalLaunchResult result,string nonce,int exitCode)
    {
        if(result.Nonce!=nonce)throw new InvalidDataException("启动工作进程响应 nonce 不匹配。");
        if(!result.Success||exitCode!=0||result.ChildPid<=0||result.ChildStartedUtc==default||result.Phase!="ChildStarted")
            throw new IOException("外部启动工作进程失败 ["+result.Phase+"]："+result.Error);
    }
}
