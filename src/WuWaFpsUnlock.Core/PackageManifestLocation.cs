namespace WuWaFpsUnlock.Core;

public static class PackageManifestLocation
{
    // An existing custom selection always wins, even if later content validation rejects it.
    // Relocation only repairs absent paths; it never searches other installations or downloads material.
    public static string Resolve(string configuredPath, string applicationDirectory)
    {
        if (File.Exists(configuredPath)) return configuredPath;
        var bundled = Path.Combine(applicationDirectory, "payload", "manifest.json");
        return File.Exists(bundled) ? Path.GetFullPath(bundled) : configuredPath;
    }
}
