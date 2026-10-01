using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using WuWaFpsUnlock.Core;

namespace WuWaFpsUnlock.Services;

/// <summary>Resolves only resource-tier arguments from the launcher bound to this installation.</summary>
public static class OfficialLaunchOptions
{
    private const string Help = "请在主页“包体档位”中手动选择与已安装资源对应的档位，或先通过官方启动器确认设置。";

    public static string ReadResourceTier(string gameRoot, Action<string>? log = null) =>
        ResolveResourceTier(gameRoot, LauncherRoots(gameRoot), log);

    public static string ResolveResourceTier(string gameRoot, IEnumerable<string> launcherRoots, Action<string>? log = null)
    {
        var root = Normalize(gameRoot);
        var matched = new List<(string Argument, string Source)>();
        bool invalid = false;
        foreach (var candidate in launcherRoots.Distinct(StringComparer.OrdinalIgnoreCase).Take(128))
        {
            try
            {
                var launcher = Normalize(candidate);
                var cache = Path.Combine(launcher, "kr_game_cache");
                var bindingPath = Path.Combine(cache, "kr_game_temp.bin");
                if (!File.Exists(bindingPath) || !File.Exists(Path.Combine(launcher, "launcher.exe"))) continue;
                var bytes = Convert.FromBase64String(Encoding.UTF8.GetString(ReadBytes(bindingPath)).TrimStart('\uFEFF').Trim());
                for (int i = 0; i < bytes.Length; i++) bytes[i] ^= 0x63;
                using var binding = Parse(bytes);
                if (!binding.RootElement.TryGetProperty("installDirPath", out var install) ||
                    install.ValueKind != JsonValueKind.String || !Path.IsPathFullyQualified(install.GetString()!) ||
                    !string.Equals(root, Normalize(install.GetString()!), StringComparison.OrdinalIgnoreCase)) continue;
                // This cache belongs to the selected game; malformed/disabled choices must not
                // fall through to another launcher or a guessed default.
                var config = Path.Combine(cache, "Extend_command_cache.json");
                try
                {
                    var uiState = Path.Combine(cache, "platform_ui_state.bin");
                    var argument = File.Exists(uiState) ? ReadNativeBundle(uiState) : ReadSelection(config, Path.Combine(cache, "Extend_command_preferences.json"));
                    matched.Add((argument, File.Exists(uiState) ? uiState : config));
                }
                catch (Exception e) when (IsMetadataError(e))
                {
                    invalid = true;
                    log?.Invoke($"包体档位读取失败：{config}（{e.GetType().Name}）。");
                }
            }
            catch (Exception e) when (IsMetadataError(e))
            {
                log?.Invoke($"无法核对官方启动器与游戏目录的关联：{candidate}（{e.GetType().Name}）。");
            }
        }
        if (invalid) throw new InvalidDataException("当前游戏的官方包体档位配置缺失、无效或未启用。" + Help);
        if (matched.Count == 0) throw new InvalidDataException("未找到与当前游戏目录关联的官方包体档位配置。" + Help);
        if (matched.Select(x => x.Argument).Distinct().Count() != 1)
            throw new InvalidDataException("多个官方启动器为当前游戏保存了不同包体档位，无法自动确定。" + Help);
        log?.Invoke($"跟随官方包体档位：{matched[0].Argument}；配置：{matched[0].Source}");
        return matched[0].Argument;
    }

    private static IEnumerable<string> LauncherRoots(string root)
    {
        var candidates = new List<string>();
        var parent = Directory.GetParent(Normalize(root))?.FullName;
        if (parent is not null) candidates.Add(parent);
        candidates.Add(Normalize(root));
        if (!OperatingSystem.IsWindows()) return candidates;
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var registry = RegistryKey.OpenBaseKey(hive, view);
                using var launchers = registry.OpenSubKey(@"Software\kurogame\KRLauncher");
                if (launchers is null) continue;
                foreach (var name in launchers.GetSubKeyNames().Take(128))
                {
                    if (!name.Contains("_G152_", StringComparison.OrdinalIgnoreCase)) continue;
                    using var entry = launchers.OpenSubKey(name);
                    if (entry?.GetValue("SingleLauncherInstallPath") is string path && Path.IsPathFullyQualified(path))
                        candidates.Add(path);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        }
        return candidates;
    }

    private static string ReadSelection(string configPath, string preferencePath)
    {
        using var config = Parse(ReadBytes(configPath));
        var data = config.RootElement;
        if (data.GetProperty("commandSwitch").GetInt32() == 0) throw new InvalidDataException("Official command selection disabled.");
        using var preferences = File.Exists(preferencePath) ? Parse(ReadBytes(preferencePath)) : null;
        string? selected = null;
        foreach (var item in data.GetProperty("commandList").EnumerateArray())
        {
            var command = item.GetProperty("cmd").GetString()?.Trim();
            if (command is null || !command.StartsWith("-krqlv=", StringComparison.OrdinalIgnoreCase)) continue;
            var enabled = item.GetProperty("default").GetInt32();
            var id = item.GetProperty("id").GetString();
            if (id is not null && preferences is not null && preferences.RootElement.TryGetProperty(id, out var value))
                enabled = value.GetInt32();
            if (enabled == 0) continue;
            command = command.ToLowerInvariant();
            if (command is not ("-krqlv=uhd" or "-krqlv=hd" or "-krqlv=sd"))
                throw new InvalidDataException("Unsupported resource tier argument.");
            if (selected is not null && selected != command)
                throw new InvalidDataException("Conflicting resource tier arguments.");
            selected = command;
        }
        return selected ?? throw new InvalidDataException("No enabled resource tier argument.");
    }

    private static string ReadNativeBundle(string path)
    {
        var bytes = Convert.FromBase64String(Encoding.UTF8.GetString(ReadBytes(path)).TrimStart('\uFEFF').Trim());
        for (int i = 0; i < bytes.Length; i++) bytes[i] ^= 0x63;
        using var state = Parse(bytes);
        var data = state.RootElement;
        string? bundle = null;
        if (data.TryGetProperty("activeGameScope", out var active) &&
            active.TryGetProperty("gameId", out var id) && id.GetString() == "G152" &&
            active.TryGetProperty("gameIdentity", out var identity) && identity.GetString() == "Aki")
            bundle = active.GetProperty("bundleName").GetString();
        else if (data.TryGetProperty("recentGames", out var recent) && recent.TryGetProperty("Aki", out var game) &&
            game.TryGetProperty("gameId", out var gameId) && gameId.GetString() == "G152")
            bundle = game.GetProperty("bundleName").GetString();
        bundle = bundle?.ToLowerInvariant();
        return bundle is "uhd" or "hd" or "sd" ? "-krqlv=" + bundle : throw new InvalidDataException("No official bundle selected.");
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    private static bool IsMetadataError(Exception e) => e is IOException or UnauthorizedAccessException or JsonException or
        FormatException or ArgumentException or InvalidOperationException or KeyNotFoundException or OverflowException;

    private static byte[] ReadBytes(string path)
    {
        SafePaths.EnsureNoLinks(Path.GetPathRoot(path)!, path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > 64 * 1024) throw new InvalidDataException("Official launch metadata too large.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }
    private static JsonDocument Parse(byte[] bytes)
    {
        var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
        try { CheckUnique(document.RootElement); return document; }
        catch { document.Dispose(); throw; }
    }
    private static void CheckUnique(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate metadata key.");
                CheckUnique(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) CheckUnique(item);
    }
}
