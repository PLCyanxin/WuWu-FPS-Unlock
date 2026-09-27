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
        token.ThrowIfCancellationRequested();
        long length=resource.Length;
        if(length<=0)throw new InvalidDataException("内置 Dynamic 倍率控制组件为空。");
        string hash = Convert.ToHexString(await SHA256.HashDataAsync(resource,token)).ToLowerInvariant();
        string source = Path.Combine(AppPaths.Data, "components", hash, FileName);
        SafePaths.EnsureInside(AppPaths.Data, source);
        SafePaths.EnsureNoLinks(AppPaths.Base, source);
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        if (!File.Exists(source) || new FileInfo(source).Length!=length || await SafePaths.HashAsync(source, token) != hash)
        {
            string temporary = source + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                resource.Position=0;
                await using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true))
                    await resource.CopyToAsync(output,token);
                if(new FileInfo(temporary).Length!=length || await SafePaths.HashAsync(temporary,token)!=hash)
                    throw new InvalidDataException("内置 Dynamic 倍率控制组件解包校验失败。");
                SafePaths.EnsureNoLinks(AppPaths.Base, source);
                File.Move(temporary, source, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        string? priorHash = File.Exists(target) ? await SafePaths.HashAsync(target, token) : null;
        plan.Add(new(source, target, hash, length, PayloadKind.Addon, priorHash));
    }
}
