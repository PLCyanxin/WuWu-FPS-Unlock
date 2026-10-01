namespace WuWaFpsUnlock.Core;

public static class PackageManifestLocation
{
    // Prefer this installation's materials over a surviving path from an older installation.
    public static string Resolve(string configuredPath, string applicationDirectory)
    {
        var bundled = Path.GetFullPath(Path.Combine(applicationDirectory, "payload", "manifest.json"));
        if (File.Exists(bundled)) return bundled;
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            try
            {
                var legacy = Path.GetFullPath(configuredPath, Path.GetFullPath(applicationDirectory));
                if (File.Exists(legacy)) return legacy;
            }
            catch (ArgumentException) { }
            catch (NotSupportedException) { }
        }
        return bundled;
    }

    public static string RecoveryMessage(string manifestPath) =>
        "启动器部署文件缺失，无法开始部署。\n\n缺少文件：" + manifestPath +
        "\n\n请重新下载并完整解压完整版启动器，保留 EXE 同目录下的 payload 文件夹。更新包不能替代完整安装包。" +
        "\n无需在鸣潮游戏目录中寻找或选择 manifest.json。";

    public static void RequireSources(PayloadManifest manifest, string manifestPath)
    {
        var root = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        var missing = manifest.Files.Select(f => SafePaths.Under(root, f.Source)).Where(p => !File.Exists(p)).ToArray();
        if (missing.Length > 0)
            throw new FileNotFoundException("启动器部署材料不完整，缺少以下文件：\n" + string.Join("\n", missing) +
                "\n请重新完整解压完整版启动器；仅更换游戏路径或安装更新包无法补齐这些材料。");
        foreach (var file in manifest.Files) MaterialSafety.RequireX64Dll(SafePaths.Under(root, file.Source));
    }
}
