using System.Text.Json;
using Microsoft.Win32;

namespace WuWaFpsUnlock.Core;

public sealed record GameDiscoveryHint(string Path, string Source);
public sealed record GameDiscoveryCandidate(string GameRoot, string ShippingExePath, IReadOnlyList<string> Evidence)
{
    public string RelativeShippingExePath => Path.GetRelativePath(GameRoot, ShippingExePath);
}
public sealed record GameDirectorySearchOptions(int MaxDirectories = 100_000, int MaxDepth = 16,
    int MaxSeconds = 20, int MaxPendingDirectories = 20_000);
public sealed record GameDiscoveryResult(IReadOnlyList<GameDiscoveryCandidate> Candidates, IReadOnlyList<string> Diagnostics);

/// <summary>Read-only discovery: exact selected root, local hints, then authorized bounded directory-name search. Never executes a candidate.</summary>
public static class GameDiscoveryService
{
    public const string ShippingRelativePath = "Client/Binaries/Win64/Client-Win64-Shipping.exe";
    public static Task<GameDiscoveryResult> DiscoverAsync(string? selectedRoot, IEnumerable<string>? savedPaths = null,
        bool includeSystemHints = true, CancellationToken token = default) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        if (TryResolveGameRoot(selectedRoot, out var selected, out _))
            return new GameDiscoveryResult([selected!], ["已从当前正确游戏目录直接定位EXE，未搜索磁盘。"]);
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
        diagnostics.AddRange(result.Diagnostics);
        if (result.Candidates.Count != 0 || !includeSystemHints || !OperatingSystem.IsWindows())
            return new GameDiscoveryResult(result.Candidates, diagnostics);
        var drives = new List<string>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            token.ThrowIfCancellationRequested();
            try { if (drive.DriveType == DriveType.Fixed && drive.IsReady) drives.Add(drive.RootDirectory.FullName); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { diagnostics.Add($"无法读取本机磁盘 {drive.Name}：{ex.Message}"); }
        }
        var diskResult = SearchDirectoriesByName(drives, ["Wuthering Waves Game", "Wuthering Waves"], token: token);
        return new GameDiscoveryResult(diskResult.Candidates, diagnostics.Concat(diskResult.Diagnostics).ToArray());
    }, token);

    /// <summary>Derives only the verified canonical EXE from this exact root; never walks parents or searches a disk.</summary>
    public static bool TryResolveGameRoot(string? selectedRoot, out GameDiscoveryCandidate? candidate, out string reason)
    {
        candidate = null; reason = "请选择鸣潮游戏目录。";
        if (string.IsNullOrWhiteSpace(selectedRoot) || !Path.IsPathFullyQualified(selectedRoot)) return false;
        try { return TryValidateSelection(selectedRoot, Path.Combine(Path.GetFullPath(selectedRoot), ShippingRelativePath), out candidate, out reason); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { reason = ex.Message; return false; }
    }

    /// <summary>Searches only directories under explicit roots, by exact leaf name. All caps are cooperative and cancellation is checked between filesystem calls.</summary>
    public static GameDiscoveryResult SearchDirectoriesByName(IEnumerable<string> searchRoots, IEnumerable<string> directoryNames,
        GameDirectorySearchOptions? options = null, CancellationToken token = default)
    {
        options ??= new();
        if (options.MaxDirectories < 1 || options.MaxDepth < 0 || options.MaxSeconds < 1 || options.MaxPendingDirectories < 1)
            throw new ArgumentOutOfRangeException(nameof(options));
        var names = new HashSet<string>(directoryNames.Where(n => !string.IsNullOrWhiteSpace(n) && n != "." && n != ".." &&
            n.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) < 0), StringComparer.OrdinalIgnoreCase);
        if (names.Count == 0) return new([], ["没有有效的游戏目录名，未搜索磁盘。"]);
        var queue = new Queue<(string Path, int Depth)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var found = new Dictionary<string, GameDiscoveryCandidate>(StringComparer.OrdinalIgnoreCase);
        var diagnostics = new List<string>();
        int scanned = 0, denied = 0, links = 0, depthSkipped = 0;
        bool capped = false;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        foreach (var root in searchRoots.Take(64))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (!Path.IsPathFullyQualified(root) || !Directory.Exists(root)) continue;
                var full = Path.GetFullPath(root); AssertPlainAncestors(full);
                if (visited.Add(full)) queue.Enqueue((full, 0));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException) { denied++; }
        }
        while (queue.Count != 0 && !capped)
        {
            token.ThrowIfCancellationRequested();
            if (scanned >= options.MaxDirectories || timer.Elapsed.TotalSeconds >= options.MaxSeconds) { capped = true; break; }
            var item = queue.Dequeue(); scanned++;
            try
            {
                if ((File.GetAttributes(item.Path) & FileAttributes.ReparsePoint) != 0) { links++; continue; }
                if (names.Contains(Path.GetFileName(Path.TrimEndingDirectorySeparator(item.Path))) &&
                    TryResolveGameRoot(item.Path, out var candidate, out _))
                    found[candidate!.GameRoot] = candidate with { Evidence = [$"本机目录名查找：{item.Path}"] };
                if (item.Depth >= options.MaxDepth) { depthSkipped++; continue; }
                foreach (var child in Directory.EnumerateDirectories(item.Path, "*", SearchOption.TopDirectoryOnly))
                {
                    token.ThrowIfCancellationRequested();
                    if (timer.Elapsed.TotalSeconds >= options.MaxSeconds) { capped = true; break; }
                    try
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) { links++; continue; }
                        if (queue.Count >= options.MaxPendingDirectories || visited.Count >= options.MaxDirectories)
                        { capped = true; break; }
                        if (visited.Add(child)) queue.Enqueue((child, item.Depth + 1));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { denied++; }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { denied++; }
        }
        diagnostics.Add($"本机目录名查找已检查{scanned}个目录，找到{found.Count}项；跳过链接{links}项，访问失败{denied}项，深度限制跳过{depthSkipped}项。");
        if (capped || depthSkipped > 0) diagnostics.Add($"搜索达到范围限制，结果可能不完整：最多{options.MaxDirectories}目录、{options.MaxDepth}层、{options.MaxSeconds}秒、{options.MaxPendingDirectories}待查目录；可手动指定游戏目录。");
        return new(found.Values.OrderBy(c => c.GameRoot, StringComparer.OrdinalIgnoreCase).ToArray(), diagnostics);
    }
    /// <summary>Checks the exact selected pair without discovering or substituting another executable.</summary>
    public static bool TryValidateSelection(string? selectedRoot, string? selectedExe,
        out GameDiscoveryCandidate? candidate, out string reason)
    {
        candidate = null;
        reason = "请先选择鸣潮游戏根目录与原装Shipping。";
        try
        {
            if (string.IsNullOrWhiteSpace(selectedRoot) || string.IsNullOrWhiteSpace(selectedExe) ||
                !Path.IsPathFullyQualified(selectedRoot) || !Path.IsPathFullyQualified(selectedExe)) return false;
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(selectedRoot));
            var exe = Path.GetFullPath(selectedExe);
            var expected = Path.GetFullPath(Path.Combine(root, ShippingRelativePath));
            if (!exe.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                reason = "所选EXE不是此游戏根内Client\\Binaries\\Win64\\Client-Win64-Shipping.exe；需要重新查找或手选。";
                return false;
            }
            var entry = Path.Combine(root, "Wuthering Waves.exe");
            AssertPlainAncestors(exe); AssertPlainAncestors(entry);
            if (!File.Exists(exe) || !File.Exists(entry))
            {
                reason = "所选鸣潮路径不完整：Shipping或根入口Wuthering Waves.exe不存在。";
                return false;
            }
            if (!IsAmd64Executable(exe))
            {
                reason = "所选Shipping不是可读取的x64 PE可执行文件。";
                return false;
            }
            candidate = new(root, exe, ["已核验当前根目录与Shipping精确对应、x64 PE及无链接"]);
            reason = "当前鸣潮根目录与Shipping路径有效，可复用；未验证文件官方来源或游戏运行。";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            reason = $"所选鸣潮路径无法验证：{ex.Message}";
            return false;
        }
    }
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
                        var shipping = Path.GetFullPath(Path.Combine(root, ShippingRelativePath));
                        // Recheck each candidate; no cached candidate survives a changed file.
                        if (!TryValidateSelection(root, shipping, out var validated, out var reason))
                        {
                            if (File.Exists(shipping)) diagnostics.Add($"跳过候选 {shipping}：{reason}");
                            continue;
                        }
                        var fullRoot = validated!.GameRoot;
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
