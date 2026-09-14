using System.Text.Json;
using Microsoft.Win32;

namespace WuWaFpsUnlock.Core;

public sealed record GameDiscoveryHint(string Path, string Source);
public sealed record GameDiscoveryCandidate(string GameRoot, string ShippingExePath, IReadOnlyList<string> Evidence);
public sealed record GameDiscoveryResult(IReadOnlyList<GameDiscoveryCandidate> Candidates, IReadOnlyList<string> Diagnostics);

/// <summary>Read-only, bounded discovery from explicit hints. Never searches drive roots or executes a candidate.</summary>
public static class GameDiscoveryService
{
    private const string ShippingRelative = "Client/Binaries/Win64/Client-Win64-Shipping.exe";
    public static Task<GameDiscoveryResult> DiscoverAsync(string? selectedRoot, IEnumerable<string>? savedPaths = null,
        bool includeSystemHints = true, CancellationToken token = default) => Task.Run(() =>
    {
        var hints = new List<GameDiscoveryHint>();
        var diagnostics = new List<string>();
        if (!string.IsNullOrWhiteSpace(selectedRoot)) hints.Add(new(selectedRoot, "当前已选路径"));
        if (savedPaths is not null)
            foreach (var path in savedPaths.Take(128))
                if (!string.IsNullOrWhiteSpace(path)) hints.Add(new(path, "已保存路径"));
        if (includeSystemHints && OperatingSystem.IsWindows())
        {
            ReadRegistryHints(hints, diagnostics, token);
            ReadEpicHints(hints, diagnostics, token);
        }
        var result = DiscoverFromHints(hints, token);
        return new GameDiscoveryResult(result.Candidates, diagnostics.Concat(result.Diagnostics).ToArray());
    }, token);

    public static GameDiscoveryResult DiscoverFromHints(IEnumerable<GameDiscoveryHint> hints, CancellationToken token = default)
    {
        var found = new Dictionary<string, (string Shipping, HashSet<string> Sources)>(StringComparer.OrdinalIgnoreCase);
        var diagnostics = new List<string>();
        int count = 0;
        foreach (var hint in hints)
        {
            token.ThrowIfCancellationRequested();
            if (++count > 256) { diagnostics.Add("候选来源超过256项，已停止处理其余来源。"); break; }
            try
            {
                if (string.IsNullOrWhiteSpace(hint.Path) || !Path.IsPathFullyQualified(hint.Path)) continue;
                var path = Path.GetFullPath(hint.Path);
                AssertPlainAncestors(path);
                if (File.Exists(path)) path = Path.GetDirectoryName(path)!;
                else if (!Directory.Exists(path)) continue;
                // Hints may be the launcher directory, game root or a saved Shipping path.
                // Only inspect two exact layouts at each of at most four ancestor levels.
                for (int level = 0; level < 4 && path is not null; level++, path = Path.GetDirectoryName(path))
                {
                    token.ThrowIfCancellationRequested();
                    foreach (var root in new[] { path, Path.Combine(path, "Wuthering Waves Game") })
                    {
                        var shipping = Path.GetFullPath(Path.Combine(root, ShippingRelative));
                        var entry = Path.Combine(root, "Wuthering Waves.exe");
                        if (!File.Exists(shipping) || !File.Exists(entry)) continue;
                        AssertPlainAncestors(shipping); AssertPlainAncestors(entry);
                        if (!IsAmd64Executable(shipping)) { diagnostics.Add($"跳过非x64 PE Shipping：{shipping}"); continue; }
                        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
                        if (!found.TryGetValue(fullRoot, out var item)) item = (shipping, new(StringComparer.Ordinal));
                        item.Sources.Add($"{hint.Source}：{hint.Path}"); found[fullRoot] = item;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
            { diagnostics.Add($"跳过来源 {hint.Source}：{hint.Path}（{ex.Message}）"); }
        }
        return new(found.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => new GameDiscoveryCandidate(x.Key, x.Value.Shipping, x.Value.Sources.OrderBy(s => s).ToArray())).ToArray(), diagnostics);
    }

    private static bool IsAmd64Executable(string path)
    {
        using var stream = File.OpenRead(path); using var reader = new BinaryReader(stream);
        if (stream.Length < 64 || reader.ReadUInt16() != 0x5a4d) return false;
        stream.Position = 0x3c; int offset = reader.ReadInt32();
        if (offset < 64 || offset > stream.Length - 24) return false;
        stream.Position = offset;
        if (reader.ReadUInt32() != 0x4550 || reader.ReadUInt16() != 0x8664) return false;
        stream.Position = offset + 22;
        var characteristics = reader.ReadUInt16();
        return (characteristics & 0x0002) != 0 && (characteristics & 0x2000) == 0;
    }

    private static void AssertPlainAncestors(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"不跟随链接：{current}");
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void ReadRegistryHints(List<GameDiscoveryHint> hints, List<string> diagnostics, CancellationToken token)
    {
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null) continue;
                foreach (var name in uninstall.GetSubKeyNames().Take(4096))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        using var app = uninstall.OpenSubKey(name);
                        var display = app?.GetValue("DisplayName") as string;
                        if (!IsGameName(display)) continue;
                        if (app?.GetValue("InstallLocation") is string location && !string.IsNullOrWhiteSpace(location))
                            hints.Add(new(location, $"卸载注册表 {hive}/{view}/{display}"));
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
                    { diagnostics.Add($"无法读取卸载项 {name}：{ex.Message}"); }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            { diagnostics.Add($"无法读取卸载注册表 {hive}/{view}：{ex.Message}"); }
        }
    }

    private static bool IsGameName(string? name) => name is not null &&
        (name.Contains("Wuthering Waves", StringComparison.OrdinalIgnoreCase) || name.Contains("鸣潮", StringComparison.Ordinal));

    private static void ReadEpicHints(List<GameDiscoveryHint> hints, List<string> diagnostics, CancellationToken token)
    {
        // Epic's local launcher installation manifests; no username, drive or library root guessing.
        var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(common)) return;
        var directory = Path.Combine(common, "Epic", "EpicGamesLauncher", "Data", "Manifests");
        try
        {
            if (!Directory.Exists(directory)) return;
            AssertPlainAncestors(directory);
            foreach (var file in Directory.EnumerateFiles(directory, "*.item", SearchOption.TopDirectoryOnly).Take(2048))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    AssertPlainAncestors(file);
                    if (new FileInfo(file).Length > 512 * 1024) continue;
                    using var json = JsonDocument.Parse(File.ReadAllText(file));
                    var root = json.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) continue;
                    if (!root.TryGetProperty("DisplayName", out var display) || display.ValueKind != JsonValueKind.String || !IsGameName(display.GetString())) continue;
                    if (root.TryGetProperty("InstallLocation", out var location) && location.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(location.GetString()))
                        hints.Add(new(location.GetString()!, $"Epic本机安装清单 {file}"));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                { diagnostics.Add($"无法读取Epic安装清单 {file}：{ex.Message}"); }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { diagnostics.Add($"无法读取Epic安装清单目录：{ex.Message}"); }
    }
}
