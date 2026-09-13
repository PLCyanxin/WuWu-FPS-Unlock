using System.Text.Json;
using System.Text.Json.Serialization;

namespace WuWaFpsUnlock.Core;

public sealed class UserSettings
{
    public string GameRoot { get; set; } = "";
    public string GameExe { get; set; } = "";
    public int TargetFps { get; set; } = 240;
    public bool FpsEnabled { get; set; } = true;
    public bool MfgSelected { get; set; }
    public string PackageManifest { get; set; } = "";
    public bool RiskAccepted { get; set; }
    public UserSettings Clone() => JsonSerializer.Deserialize<UserSettings>(JsonSerializer.Serialize(this))!;
}

public sealed record HardwareInfo(string Gpu, int? Driver, bool IsAdaGeForce, string Os, string Hags, bool? HagsConfigured)
{
    public string DriverText => Driver is int d ? $"{d / 100}.{d % 100:00}" : "检测未知";
    public string DynamicText => !IsAdaGeForce ? "未确认 RTX 40 系显卡" : Driver is null ? "驱动未检测，Dynamic 条件未知"
        : Driver < 59541 ? "Dynamic 驱动不足；Fixed 按文件包条件判断"
        : "Dynamic 驱动条件已满足；游戏内能力待确认";
    public static HardwareInfo Unknown => new("检测中…", null, false, "检测中…", "检测中…", null);
}

public enum TargetAnchor { GameRoot, ExeDir, AddonDir }
public enum PayloadKind { Vendor, Addon }
public sealed class PayloadFile
{
    public string Source { get; set; } = "";
    public TargetAnchor Anchor { get; set; }
    public string Target { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
    public PayloadKind Kind { get; set; }
}
public sealed class ReShadeSpec
{
    public string LocalSetupPath { get; set; } = ""; // Relative to the payload manifest directory.
    public string Version { get; set; } = "6.8.0";
    public string ProxyApi { get; set; } = ""; // Required: based on the known-working layout, never guessed from D3D12.
    public string SetupSha256 { get; set; } = ""; // A publisher-pinned hash is strongly recommended.
    public string FullRuntimeSha256 { get; set; } = "";
}
public sealed class PayloadManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string PackageId { get; set; } = "";
    public string AddonVersion { get; set; } = "0.9";
    public int DynamicMinimumDriver { get; set; } = 59541;
    public int? FixedMinimumDriver { get; set; }
    public int FixedMultiplier { get; set; } = 4;
    public bool PreferDynamic { get; set; } = true;
    public ReShadeSpec ReShade { get; set; } = new();
    public List<PayloadFile> Files { get; set; } = [];
    public Dictionary<string, string> MfgConfig { get; set; } = new();
}
public sealed record PlannedFile(string Source, string Target, string Sha256, long Size, PayloadKind Kind, string? ExpectedTargetHash = null);
public sealed class FileReceipt
{
    public string Path { get; set; } = "";
    public string InstalledHash { get; set; } = "";
    public bool CreatedByTool { get; set; }
    public bool ReplacedByTool { get; set; }
    public string Kind { get; set; } = "";
    public bool Completed { get; set; }
}
public sealed class IniReceipt
{
    public string Section { get; set; } = "";
    public string Key { get; set; } = "";
    public string? Previous { get; set; }
    public string Written { get; set; } = "";
}
public sealed class DeploymentReceipt
{
    public int SchemaVersion { get; set; } = 1;
    public string GameRoot { get; set; } = "";
    public string GameExe { get; set; } = "";
    public string PackageId { get; set; } = "";
    public string Status { get; set; } = "NotInstalled";
    public string IniPath { get; set; } = "";
    public string ProxyPath { get; set; } = "";
    public DateTimeOffset Updated { get; set; } = DateTimeOffset.UtcNow;
    public List<FileReceipt> Files { get; set; } = [];
    public List<string> SkippedVendorNames { get; set; } = [];
    public List<IniReceipt> IniEdits { get; set; } = [];
}
public static class JsonFiles
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };
    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? throw new InvalidDataException($"JSON 为空：{path}");
    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(value, Options), new System.Text.UTF8Encoding(false)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
