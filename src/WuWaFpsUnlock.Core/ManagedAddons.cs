namespace WuWaFpsUnlock.Core;

public static class ManagedAddons
{
    public const string Main = "renodx-mfgunlock.addon64";
    public const string Dynamic = "wuwa-dynamicmax.addon64";
    public static bool Contains(string name) => name.Equals(Main, StringComparison.OrdinalIgnoreCase) || name.Equals(Dynamic, StringComparison.OrdinalIgnoreCase);
}
