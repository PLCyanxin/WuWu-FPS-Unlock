using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace WuWaFpsUnlock.Core;

public static partial class SafePaths
{
    public static string Under(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':') || relative.StartsWith('/') || relative.StartsWith('\\'))
            throw new InvalidDataException("必须使用非空相对路径：" + relative);
        var parts = relative.Replace('\\', '/').Split('/');
        if (parts.Any(p => p.Length == 0 || p.Any(c => c < 32) || p is "." or ".." || p.EndsWith(' ') || p.EndsWith('.') || p.IndexOfAny(['<', '>', '"', '|', '?', '*', '\0']) >= 0 || DeviceName().IsMatch(p)))
            throw new InvalidDataException("路径包含越界/保留/无效名称：" + relative);
        string result = Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
        EnsureInside(root, result); EnsureNoLinks(root, result);
        return result;
    }
    public static bool IsInside(string root, string path)
    {
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
    public static void EnsureInside(string root, string path)
    { if (!IsInside(root, path)) throw new InvalidDataException("目标路径不在游戏目录内：" + path); }
    public static void EnsureNoLinks(string root, string target)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        for (string? p = Path.GetFullPath(target); p is not null; p = Path.GetDirectoryName(p))
        {
            if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("为避免写入错误位置，拒绝目录链接/重解析点：" + p);
            if (string.Equals(p.TrimEnd(Path.DirectorySeparatorChar), fullRoot, StringComparison.OrdinalIgnoreCase)) break;
        }
    }
    public static string GameRoot(string input)
    {
        string root = Path.GetFullPath(input.Trim());
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("请先选择存在的鸣潮目录。");
        if (string.Equals(Path.GetPathRoot(root)?.TrimEnd('\\','/'), root.TrimEnd('\\','/'), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("不能把整个磁盘根目录作为游戏目录。");
        EnsureNoLinks(Path.GetPathRoot(root)!, root);
        return root;
    }
    public static async Task<string> HashAsync(string path, CancellationToken token = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).ToLowerInvariant();
    }
    [GeneratedRegex(@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase)]
    private static partial Regex DeviceName();
}

public static class PackageReader
{
    public static readonly IReadOnlySet<string> VendorNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { "NvLowLatencyVk.dll", "nvngx_deepdvc.dll", "nvngx_dlssnr.dll", "sl.dlss_nr.dll", "sl.nvperf.dll", "nvngx_dlss.dll", "nvngx_dlssg.dll", "nvngx_dlssd.dll", "sl.interposer.dll", "sl.common.dll", "sl.dlss_g.dll", "sl.reflex.dll", "sl.pcl.dll", "sl.dlss.dll", "sl.dlss_d.dll", "sl.deepdvc.dll", "sl.directsr.dll", "sl.nis.dll" };
    public static PayloadManifest Load(string manifestPath)
    {
        var m = JsonFiles.Read<PayloadManifest>(manifestPath);
        if (m.SchemaVersion != 1 || string.IsNullOrWhiteSpace(m.PackageId) || m.Files.Count == 0 || m.Files.Count > 80) throw new InvalidDataException("文件包清单为空或版本不受支持。");
        if (m.DynamicMinimumDriver < 59541) throw new InvalidDataException("0.9 的 Dynamic 驱动门槛不能低于 595.41。");
        if (m.FixedMultiplier is < 2 or > 6) throw new InvalidDataException("Fixed 倍率须为 2–6。");
        if (m.ReShade.ProxyApi is not ("dxgi" or "d3d12")) throw new InvalidDataException("清单必须注明已验证的 ReShade 代理：dxgi 或 d3d12。");
        if (!Regex.IsMatch(m.ReShade.Version, @"^\d+\.\d+\.\d+$")) throw new InvalidDataException("ReShade 版本格式无效。");
        if (m.ReShade.Version != "6.8.0") throw new InvalidDataException("该实现的自动安装流程仅按 ReShade 6.8.0 源码适配；更换版本需验证后更新代码。");
        var vendorInputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in m.Files)
        {
            if (!Enum.IsDefined(f.Anchor) || !Enum.IsDefined(f.Kind)) throw new InvalidDataException("文件锚点或类型无效。");
            if (f.Size <= 0 || !Regex.IsMatch(f.Sha256, "^[a-fA-F0-9]{64}$")) throw new InvalidDataException("文件大小或哈希缺失：" + f.Source);
            var name = f.Target.Replace('\\','/').Split('/').Last();
            if (f.Kind == PayloadKind.Vendor && !vendorInputs.Add(name)) throw new InvalidDataException("重复的运行库输入：" + name);
            if (f.Kind == PayloadKind.Vendor && (!VendorNames.Contains(name) || f.Anchor == TargetAnchor.AddonDir)) throw new InvalidDataException("拒绝未知运行库：" + name);
            if (f.Kind == PayloadKind.Addon && (name != "renodx-mfgunlock.addon64" || f.Anchor != TargetAnchor.AddonDir || name != f.Target)) throw new InvalidDataException("MFG addon 必须使用 AddonDir 锚点和标准文件名。");
        }
        if (m.Files.Count(x => x.Kind == PayloadKind.Addon) != 1) throw new InvalidDataException("清单必须包含且只包含一个 MFG addon。");
        if (m.MfgConfig.Keys.Any(k => k.Contains('\n') || k.Contains('\r') || k.Contains('=') || k.Contains('[')) || m.MfgConfig.Values.Any(v => v.Contains('\n') || v.Contains('\r')))
            throw new InvalidDataException("配置键值不能包含换行或章节注入。");
        return m;
    }
    public static async Task<List<PlannedFile>> PlanAsync(PayloadManifest m, string manifestPath, string gameRoot, string exeDir, string addonDir, CancellationToken token = default)
    {
        string sourceRoot = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        var list = new List<PlannedFile>(); var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in m.Files)
        {
            token.ThrowIfCancellationRequested();
            var source = SafePaths.Under(sourceRoot, f.Source);
            var anchor = f.Anchor switch { TargetAnchor.GameRoot => gameRoot, TargetAnchor.ExeDir => exeDir, _ => addonDir };
            if (!File.Exists(source) || new FileInfo(source).Length != f.Size || !string.Equals(await SafePaths.HashAsync(source, token), f.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("源文件缺失或 SHA-256 不匹配：" + f.Source);
            // Latest user rule: flat vendor materials replace every existing same-name file
            // strictly under the selected root. Missing names are never newly deployed.
            var targets = f.Kind == PayloadKind.Vendor
                ? FindExistingVendorTargets(gameRoot, Path.GetFileName(f.Target.Replace('\\', '/')))
                : [SafePaths.Under(anchor, f.Target)];
            foreach (var target in targets)
            {
                SafePaths.EnsureInside(gameRoot, target); SafePaths.EnsureNoLinks(gameRoot, target);
                if (!destinations.Add(target)) throw new InvalidDataException("重复写入目标：" + target);
                list.Add(new(source, target, f.Sha256.ToLowerInvariant(), f.Size, f.Kind,
                    File.Exists(target) ? await SafePaths.HashAsync(target, token) : null));
            }
        }
        return list;
    }
    public static List<string> FindExistingVendorTargets(string gameRoot, string name)
    {
        if (!VendorNames.Contains(name)) throw new InvalidDataException("拒绝未知运行库：" + name);
        gameRoot = SafePaths.GameRoot(gameRoot);
        var found = new List<string>(); var pending = new Stack<string>(); pending.Push(gameRoot);
        while (pending.Count > 0)
        {
            var directory = pending.Pop(); SafePaths.EnsureNoLinks(gameRoot, directory);
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                else if (Path.GetFileName(entry).Equals(name, StringComparison.OrdinalIgnoreCase)) found.Add(entry);
            }
        }
        found.Sort(StringComparer.OrdinalIgnoreCase); return found;
    }
}
