using System.Reflection;
using System.Security.Cryptography;
using WuWaFpsUnlock.Core;

namespace WuWaFpsUnlock.Services;

/// <summary>Ships the independent controller inside the launcher so protocol-1 clients can update it.</summary>
internal static class EmbeddedDynamicAddon
{
    internal const string FileName = "wuwa-dynamicmax.addon64";
    internal static async Task AppendToPlanAsync(List<PlannedFile> plan, UserSettings settings, string addonDirectory, CancellationToken token)
    {
        if (plan.Any(p => Path.GetFileName(p.Target).Equals(FileName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("文件包包含重复的 Dynamic 倍率控制组件。");
        string target = Path.Combine(addonDirectory, FileName);
        SafePaths.EnsureInside(settings.GameRoot, target);
        SafePaths.EnsureNoLinks(settings.GameRoot, target);
        if (Directory.Exists(target)) throw new IOException("Dynamic 倍率控制组件的目标路径被目录占用。");
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("WuWaFpsUnlock.DynamicMax.addon64")
            ?? throw new FileNotFoundException("启动器缺少内置 Dynamic 倍率控制组件。");
        using var memory = new MemoryStream();
        await resource.CopyToAsync(memory, token);
        byte[] bytes = memory.ToArray();
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        string source = Path.Combine(AppPaths.Data, "components", hash, FileName);
        SafePaths.EnsureInside(AppPaths.Data, source);
        SafePaths.EnsureNoLinks(AppPaths.Base, source);
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        if (!File.Exists(source) || await SafePaths.HashAsync(source, token) != hash)
        {
            string temporary = source + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes, token);
                SafePaths.EnsureNoLinks(AppPaths.Base, source);
                File.Move(temporary, source, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        string? priorHash = File.Exists(target) ? await SafePaths.HashAsync(target, token) : null;
        plan.Add(new(source, target, hash, bytes.LongLength, PayloadKind.Addon, priorHash));
    }
}
