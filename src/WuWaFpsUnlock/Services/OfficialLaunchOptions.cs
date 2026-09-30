using System.Text.Json;
using WuWaFpsUnlock.Core;

namespace WuWaFpsUnlock.Services;

/// <summary>Reads only the official resource-tier argument, never arbitrary cached commands.</summary>
public static class OfficialLaunchOptions
{
    public static string? ReadResourceTier(string gameRoot)
    {
        var root = Path.GetFullPath(gameRoot);
        var parent = Directory.GetParent(root)?.FullName;
        if (parent is null) return null;
        var cache = Path.Combine(parent, "kr_game_cache");
        var configPath = Path.Combine(cache, "Extend_command_cache.json");
        if (!File.Exists(configPath)) return null;
        using var config = Read(configPath);
        var data = config.RootElement;
        if (data.GetProperty("commandSwitch").GetInt32() == 0) return null;
        var preferencePath = Path.Combine(cache, "Extend_command_preferences.json");
        using var preferences = File.Exists(preferencePath) ? Read(preferencePath) : null;
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
                throw new InvalidDataException("官方包体启动参数无法识别，请先在官方启动器中确认包体设置。");
            if (selected is not null && selected != command)
                throw new InvalidDataException("官方包体启动选项存在冲突，请先在官方启动器中确认包体设置。");
            selected = command;
        }
        return selected;
    }

    private static JsonDocument Read(string path)
    {
        SafePaths.EnsureNoLinks(Path.GetPathRoot(path)!, path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > 64 * 1024) throw new InvalidDataException("官方启动选项文件过大。");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
    }
}
