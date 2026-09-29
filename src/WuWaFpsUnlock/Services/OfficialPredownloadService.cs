using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WuWaFpsUnlock.Services;

public enum PredownloadState { Unknown, NotAvailable, AlreadyDownloaded, Available }
public sealed record PredownloadResult(PredownloadState State, string? Version = null, string? LauncherPath = null, string Detail = "");

/// <summary>Read-only, anonymous official predownload metadata check. Never starts a process.</summary>
public sealed class OfficialPredownloadService : IDisposable
{
    private const int Limit = 1024 * 1024;
    private readonly HttpClient http;
    private static readonly HashSet<string> Hosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "prod-cn-alicdn-gamestarter.kurogame.com", "prod-volcdn-gamestarter.kurogame.xyz",
        "prod-alicdn-gamestarter.kurogame.com", "prod-alicdn-gamestarter.kurogame.xyz"
    };
    public OfficialPredownloadService(HttpMessageHandler? handler = null)
    {
        http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None });
        http.Timeout = Timeout.InfiniteTimeSpan;
    }
    public void Dispose() => http.Dispose();

    public async Task<PredownloadResult> CheckAsync(string gameRoot, string gameExe, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameRoot));
            EnsurePath(root, true);
            var shipping = Path.Combine(root, "Client", "Binaries", "Win64", "Client-Win64-Shipping.exe");
            if (!string.Equals(Path.GetFullPath(gameExe), shipping, StringComparison.OrdinalIgnoreCase))
                return Unknown("Selected executable does not match the Shipping layout.");
            EnsurePath(shipping, false);
            var parent = Directory.GetParent(root)?.FullName ?? throw new InvalidDataException("No launcher parent.");
            var launcher = Path.Combine(parent, "launcher.exe");
            EnsurePath(launcher, false);
            var candidates = new List<(Version Version, string Config)>();
            var entries = Directory.EnumerateDirectories(parent).Take(129).ToArray();
            if (entries.Length > 128) return Unknown("Too many launcher directory candidates.");
            foreach (var dir in entries)
            {
                if (!TryVersion(Path.GetFileName(dir), out var version)) continue;
                EnsurePath(dir, true);
                var config = Path.Combine(dir, "Assets", "KRApp.conf");
                if (!File.Exists(config)) continue;
                EnsurePath(Path.Combine(dir, "launcher_main.dll"), false);
                EnsurePath(Path.Combine(dir, "launcher_main.exe"), false);
                EnsurePath(config, false);
                candidates.Add((version!, config));
            }
            if (candidates.Count == 0) return Unknown("Official launcher channel configuration not found.");
            candidates.Sort((a, b) => b.Version.CompareTo(a.Version));
            if (candidates.Count > 1 && candidates[0].Version == candidates[1].Version)
                return Unknown("Ambiguous launcher versions.");
            var encoded = await ReadLocal(candidates[0].Config, timeout.Token);
            var decoded = Convert.FromBase64String(Encoding.UTF8.GetString(encoded).TrimStart('\uFEFF').Trim());
            for (var i = 0; i < decoded.Length; i++) decoded[i] ^= 99;
            using var channel = Parse(decoded);
            var configRoot = channel.RootElement;
            var appId = Text(configRoot, "appId");
            if (string.IsNullOrWhiteSpace(appId) || Text(configRoot, "gameId") != "G152" ||
                !string.Equals(Text(configRoot, "gameDirName"), Path.GetFileName(root), StringComparison.OrdinalIgnoreCase))
                return Unknown("Selected game does not match the official channel.");
            var originalExe = Text(configRoot, "gameExeName");
            if (string.IsNullOrEmpty(originalExe) || Path.GetFileName(originalExe) != originalExe)
                return Unknown("Invalid official executable name.");
            EnsurePath(Path.Combine(root, originalExe), false);
            using var local = Parse(await ReadLocal(Path.Combine(root, "launcherDownloadConfig.json"), timeout.Token));
            if (Text(local.RootElement, "appId") != appId || !TryVersion(Text(local.RootElement, "version"), out var installed))
                return Unknown("Local game version/channel is unknown.");
            if (Text(local.RootElement, "state") is "repairing" or "moving")
                return Unknown("Official installation is not ready.");
            var endpoint = Endpoint(Text(configRoot, "configUrl"));
            using var response = await http.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if ((int)response.StatusCode is >= 300 and <= 399) return Unknown("Official redirect was refused.");
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri is { } effective && effective != endpoint)
                return Unknown("Official endpoint changed during request.");
            if (response.Content.Headers.ContentLength > Limit) return Unknown("Official response exceeds size limit.");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var body = await ReadBounded(stream, timeout.Token);
            if (body.Length >= 2 && body[0] == 0x1f && body[1] == 0x8b)
            {
                using var compressed = new MemoryStream(body);
                using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
                body = await ReadBounded(gzip, timeout.Token);
            }
            using var server = Parse(body);
            return await Evaluate(server.RootElement, installed!, root, launcher, appId, timeout.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException or JsonException or FormatException or ArgumentException or HttpRequestException or OperationCanceledException or InvalidOperationException)
        {
            return Unknown("Official predownload check unavailable: " + e.GetType().Name);
        }
    }

    private static async Task<PredownloadResult> Evaluate(JsonElement server, Version installed, string root, string launcher, string appId, CancellationToken token)
    {
        if (!server.TryGetProperty("predownloadSwitch", out var flag) || !flag.TryGetInt32(out var enabled))
            return Unknown("Official predownload switch is unknown.");
        if (enabled == 0) return new(PredownloadState.NotAvailable, Detail: "Official predownload is disabled.");
        if (enabled != 1) return Unknown("Unsupported official predownload switch.");
        if (!server.TryGetProperty("default", out var normal) || !normal.TryGetProperty("config", out var current) ||
            !TryVersion(Text(current, "version"), out var currentVersion)) return Unknown("Official current version is unknown.");
        if (!server.TryGetProperty("predownload", out var pre) || pre.ValueKind == JsonValueKind.Null)
            return new(PredownloadState.NotAvailable);
        if (!pre.TryGetProperty("config", out var preConfig) || !TryVersion(Text(preConfig, "version"), out var preVersion))
            return Unknown("Official predownload version is invalid.");
        if (preVersion <= currentVersion || installed >= preVersion) return new(PredownloadState.NotAvailable);
        // Ordinary game updates are deliberately not surfaced by this service.
        if (installed < currentVersion) return new(PredownloadState.NotAvailable, Detail: "Installed game is not eligible for this predownload.");
        var version = Text(preConfig, "version")!;
        var marker = Path.Combine(root, "launcherDownload", version, "launcherDownloadConfig.json");
        if (File.Exists(marker))
        {
            EnsurePath(marker, false);
            using var completed = Parse(await ReadLocal(marker, token));
            var data = completed.RootElement;
            if (Text(data, "version") == version && Text(data, "state") == "download_completed" && Text(data, "appId") == appId)
                return new(PredownloadState.AlreadyDownloaded, version, launcher, "Official completion record found; archive integrity not rechecked.");
        }
        return new(PredownloadState.Available, version, launcher, "Official predownload is available.");
    }
    private static PredownloadResult Unknown(string detail) => new(PredownloadState.Unknown, Detail: detail);
    private static string? Text(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool TryVersion(string? text, out Version? version)
    {
        version = null;
        if (text is null || !Regex.IsMatch(text, @"^(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})(\.(0|[1-9][0-9]{0,8}))?$", RegexOptions.CultureInvariant)) return false;
        if (!Version.TryParse(text, out var parsed)) return false;
        version = new Version(parsed.Major, parsed.Minor, parsed.Build, Math.Max(0, parsed.Revision));
        return true;
    }
    private static Uri Endpoint(string? text)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort ||
            !Hosts.Contains(uri.IdnHost) || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || uri.Query.Length != 0 ||
            !uri.AbsolutePath.StartsWith("/launcher/game/G152/", StringComparison.Ordinal) || !uri.AbsolutePath.EndsWith("/index.json", StringComparison.Ordinal))
            throw new InvalidDataException("Unrecognized official endpoint.");
        return uri;
    }
    private static JsonDocument Parse(byte[] bytes)
    {
        using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
        ValidateUnique(doc.RootElement);
        return JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
    }
    private static void ValidateUnique(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject()) { if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate JSON field."); ValidateUnique(property.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var child in element.EnumerateArray()) ValidateUnique(child);
    }
    private static void EnsurePath(string path, bool directory)
    {
        var full = Path.GetFullPath(path);
        for (var cursor = full; cursor is not null; cursor = Path.GetDirectoryName(cursor))
        {
            if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked paths are not allowed.");
        }
        if (directory ? !Directory.Exists(full) : !File.Exists(full)) throw new IOException("Required path is missing.");
    }
    private static async Task<byte[]> ReadLocal(string path, CancellationToken token)
    {
        EnsurePath(path, false);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
        return await ReadBounded(stream, token);
    }
    private static async Task<byte[]> ReadBounded(Stream stream, CancellationToken token)
    {
        using var result = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, token)) != 0)
        {
            if (result.Length + count > Limit) throw new InvalidDataException("Metadata exceeds size limit.");
            result.Write(buffer, 0, count);
        }
        return result.ToArray();
    }
}
