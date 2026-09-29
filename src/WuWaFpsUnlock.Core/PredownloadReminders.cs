namespace WuWaFpsUnlock.Core;

public static class PredownloadReminders
{
    private static string Key(string root) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)).ToUpperInvariant();
    public static bool IsIgnored(UserSettings settings, string root, string version) =>
        settings.IgnoredPredownloadVersions?.GetValueOrDefault(Key(root)) == version;
    public static void SetIgnored(UserSettings settings, string root, string version, bool ignored)
    {
        settings.IgnoredPredownloadVersions ??= new();
        if (ignored) settings.IgnoredPredownloadVersions[Key(root)] = version;
        else settings.IgnoredPredownloadVersions.Remove(Key(root));
    }
}
