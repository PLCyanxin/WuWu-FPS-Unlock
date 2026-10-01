using System.Text.Json;
using System.Text.RegularExpressions;
using WuWaFpsUnlock.Core;

namespace WuWaFpsUnlock.Services;

public enum TierDownloadStatus { Unknown, Missing, Downloading, Installed }
public sealed record ResourceTierInfo(string Tier, TierDownloadStatus Status)
{
    public string Name => Tier switch { "uhd" => "极致", "hd" => "高清", _ => "流畅" };
    public string StatusText => Status switch { TierDownloadStatus.Installed => "已下载", TierDownloadStatus.Downloading => "下载中", TierDownloadStatus.Missing => "未下载", _ => "未捕获" };
    public string Color => Status switch { TierDownloadStatus.Installed => "#20A66A", TierDownloadStatus.Downloading => "#E2AD28", TierDownloadStatus.Missing => "#DF6464", _ => "#8994A6" };
}

/// <summary>Read-only view of official bundle installation records. Never hashes game payloads.</summary>
public static class ResourceTierCatalog
{
    public static readonly string[] Tiers = ["uhd", "hd", "sd"];
    public static ResourceTierInfo[] Read(string root, Func<string, bool>? hasRecentWrite = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return Unknown();
            var path = Path.Combine(root, "launcherDownloadConfig.json");
            if (!File.Exists(path)) return Tiers.Select(t => new ResourceTierInfo(t,
                hasRecentWrite?.Invoke(t) == true ? TierDownloadStatus.Downloading : TierDownloadStatus.Unknown)).ToArray();
            using var installed = ReadJson(path);
            if (!installed.RootElement.TryGetProperty("bundles", out var bundles)) return Unknown();
            return Tiers.Select(tier => new ResourceTierInfo(tier, Status(root, bundles, tier, hasRecentWrite))).ToArray();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        { return Unknown(); }
    }
    private static TierDownloadStatus Status(string root, JsonElement bundles, string tier, Func<string, bool>? activity)
    {
        if (activity?.Invoke(tier) == true) return TierDownloadStatus.Downloading;
        JsonElement bundle = default;
        foreach (var p in bundles.EnumerateObject()) if (p.Name.Equals(tier, StringComparison.OrdinalIgnoreCase)) { bundle = p.Value; break; }
        if (bundle.ValueKind != JsonValueKind.Object || !bundle.TryGetProperty("version", out var version) ||
            string.IsNullOrWhiteSpace(version.GetString()) ||
            (bundle.TryGetProperty("state", out var state) && !string.IsNullOrEmpty(state.GetString()))) return TierDownloadStatus.Missing;
        var downloadPath = Path.Combine(root, "launcherDownload", "launcherDownloadConfig.json");
        if (File.Exists(downloadPath))
        {
            using var pending = ReadJson(downloadPath);
            if (pending.RootElement.TryGetProperty("bundleStates", out var states))
            foreach (var p in states.EnumerateObject())
                if (p.Name.Equals(tier, StringComparison.OrdinalIgnoreCase) &&
                    !(p.Value.TryGetProperty("isPreDownload", out var pre) && pre.GetBoolean()) &&
                    pending.RootElement.TryGetProperty("version", out var target) && target.GetString() != version.GetString())
                    return TierDownloadStatus.Missing;
        }
        if (!bundle.TryGetProperty("resourcePacks", out var packs) || packs.GetArrayLength() == 0) return TierDownloadStatus.Unknown;
        foreach (var pack in packs.EnumerateArray())
        {
            var name = pack.GetString();
            if (name is null || !Regex.IsMatch(name, "^[A-Za-z0-9_-]{1,64}$")) return TierDownloadStatus.Unknown;
            var packPath = Path.Combine(root, "launcherDownloadConfig", name + ".json");
            if (!File.Exists(packPath)) return TierDownloadStatus.Missing;
            using var record = ReadJson(packPath);
            if (!record.RootElement.TryGetProperty("packName", out var packName) || packName.GetString() != name ||
                !record.RootElement.TryGetProperty("version", out var packVersion) || string.IsNullOrWhiteSpace(packVersion.GetString())) return TierDownloadStatus.Missing;
        }
        return TierDownloadStatus.Installed;
    }
    public static JsonDocument ReadJson(string path)
    {
        SafePaths.EnsureNoLinks(Path.GetPathRoot(path)!, path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > 128 * 1024) throw new InvalidDataException("Bundle metadata too large.");
        return JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 12 });
    }
    public static string? TierFromResourcePath(string path)
    {
        // Only tier-specific resource writes are evidence; common packs cannot identify a tier.
        var match = Regex.Match(path, @"(?:^|[\\/_.-])(uhd|hd|sd)(?:[\\/_.-]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
    }
    private static ResourceTierInfo[] Unknown() => Tiers.Select(t => new ResourceTierInfo(t, TierDownloadStatus.Unknown)).ToArray();
}
