using WuWaFpsUnlock.Core;

internal static partial class Program
{
    // Build/release entry point: pure read-only validation, no install discovery,
    // process inspection, UI/COM, key wait, extraction or filesystem mutation.
    static int ValidatePackageCommand(string directory, string expectedVersion)
    {
        NoLinks(directory);
        UpdatePackageProtocol.ValidateDirectory(directory, expectedVersion);
        Console.WriteLine("更新包协议校验通过：" + Path.GetFullPath(directory) + "；版本 " + expectedVersion);
        return 0;
    }

    static void ValidateUpdateManifest(string directory, bool required)
    {
        string manifest = Path.Combine(directory, "update-manifest.json");
        NoLinks(manifest);
        if (!File.Exists(manifest))
        {
            if (required) throw new InvalidDataException("在线更新包缺少 update-manifest.json，已停止更新。");
            return; // Compatibility for older manually extracted offline packages.
        }
        UpdatePackageProtocol.ValidateDirectory(directory);
    }
}
