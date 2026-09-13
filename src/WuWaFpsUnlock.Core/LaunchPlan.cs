using System.Globalization;

namespace WuWaFpsUnlock.Core;

public sealed record LaunchPlan(bool FpsEnabled, string Executable, string WorkingDirectory, string ShippingExePath,
    string? ConfigPath, string? UnlockerGamePath, int TargetFps)
{
    public IReadOnlyList<string> Arguments => Array.Empty<string>();
}

public static class LaunchPlanBuilder
{
    public static LaunchPlan Build(UserSettings settings, string unlockerExe)
    {
        string root = SafePaths.GameRoot(settings.GameRoot);
        string shipping = Path.GetFullPath(settings.GameExe);
        SafePaths.EnsureInside(root, shipping);
        SafePaths.EnsureNoLinks(root, shipping);
        if (!File.Exists(shipping) || !Path.GetFileName(shipping).Equals("Client-Win64-Shipping.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("请手选原装 Client-Win64-Shipping.exe；不能选择官方入口或解锁器。");
        if (!settings.FpsEnabled)
            return new(false, shipping, Path.GetDirectoryName(shipping)!, shipping, null, null, settings.TargetFps);
        if (settings.TargetFps is < 30 or > 420) throw new InvalidDataException("目标 FPS 必须是 30–420。");
        string directory = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(shipping))))!;
        string expected = Path.Combine(directory, "Client", "Binaries", "Win64", "Client-Win64-Shipping.exe");
        string entry = Path.Combine(directory, "Wuthering Waves.exe");
        if (!string.Equals(expected, shipping, StringComparison.OrdinalIgnoreCase) || !File.Exists(entry))
            throw new InvalidDataException("此解锁器已审计模式要求根入口 Wuthering Waves.exe 与 Client\\Binaries\\Win64\\Client-Win64-Shipping.exe 布局；所选路径不匹配，未猜测枚举或启动。");
        SafePaths.EnsureInside(root, entry); SafePaths.EnsureNoLinks(root, entry);
        string exe = Path.GetFullPath(unlockerExe);
        if (!File.Exists(exe)) throw new FileNotFoundException("用户提供的解锁器副本缺失。", exe);
        string cwd = Path.GetDirectoryName(exe)!;
        return new(true, exe, cwd, shipping, Path.Combine(cwd, "ww_fps_config.ini"), entry, settings.TargetFps);
    }
}

// Interface verified against the exact user-supplied binary, not inferred from a project name.
public static class UnlockerConfigAdapter
{
    public const string AuditedSha256 = "5b9cba854357a4d9ce9c56676e22e397be8d5dbd2dc10de9393155e378050fae";
    public static void Prepare(LaunchPlan plan,bool write=true)
    {
        if (!plan.FpsEnabled) return;
        if (plan.ConfigPath is null || !File.Exists(plan.ConfigPath)) throw new InvalidDataException("解锁器运行配置缺失；未生成猜测的默认配置。");
        var ini = IniDocument.Load(plan.ConfigPath);
        foreach (string key in new[] { "AutoStartEnabled", "DX11Enabled", "PowerSavingEnabled", "AdvanEnabled", "FovEnabled", "HideUidEnabled", "RemoveBlurEnabled" })
            if (!bool.TryParse(ini.Get("Settings", key), out _)) throw new InvalidDataException("解锁器 INI 布尔字段无效：" + key);
        if (!int.TryParse(ini.Get("Settings", "GameServerArea"), out int area) || area is < 0 or > 2)
            throw new InvalidDataException("GameServerArea 无效；不会猜测服务器枚举。");
        if (!int.TryParse(ini.Get("Settings", "GameLaunchExe"), out int mode) || mode is < 0 or > 1 ||
            !int.TryParse(ini.Get("Settings", "ProcessPriorityMode"), out int priority) || priority is < 0 or > 6 ||
            !double.TryParse(ini.Get("Settings", "FovValue"), NumberStyles.Float, CultureInfo.InvariantCulture, out _) ||
            ini.Get("Settings", "GameParam") is null)
            throw new InvalidDataException("解锁器 INI 已损坏；保留原文件，未启动。");
        ini.Set("Settings", "FpsValue", plan.TargetFps.ToString(CultureInfo.InvariantCulture));
        ini.Set("Settings", "PathValue", plan.UnlockerGamePath);
        ini.Set("Settings", "GameLaunchExe", "1");
        ini.Set("Settings", "AutoStartEnabled", "True");
        foreach (string key in new[] { "DX11Enabled", "PowerSavingEnabled", "AdvanEnabled", "FovEnabled", "HideUidEnabled", "RemoveBlurEnabled" }) ini.Set("Settings", key, "False");
        ini.Set("Settings", "ProcessPriorityMode", "0");
        ini.Set("Settings", "GameParam", "");
        if(write)ini.Save(plan.ConfigPath);
    }
}
