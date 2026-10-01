using WuWaFpsUnlock.Core;

namespace WuWaFpsUnlock.Services;

internal static class MenuShortcutService
{
    public static Task ApplyBeforeLaunchAsync(UserSettings settings, Action<string> log, CancellationToken token) => settings.MenuShortcut is null ? Task.CompletedTask : Task.Run(()=>ApplyBeforeLaunch(settings,log,token),token);
    private static void ApplyBeforeLaunch(UserSettings settings, Action<string> log, CancellationToken token)
    {
        if (settings.MenuShortcut is not { } shortcut) return;
        _ = shortcut.ToIniValue();
        token.ThrowIfCancellationRequested();
        GameProcesses.RequireStopped(settings.GameRoot);
        var info = new ReShadeService(log).Inspect(settings);
        if (info.State == "Missing") { log("菜单按键已保存；部署 ReShade 后可用于切换游戏内菜单。"); return; }
        if (info.State == "Conflict") throw new IOException("菜单按键未应用：" + info.Description);
        using var maintenance = DeploymentService.AcquireMaintenanceLock(settings);
        SafePaths.EnsureNoLinks(settings.GameRoot, info.Ini);
        string? originalHash = File.Exists(info.Ini) ? SafePaths.HashAsync(info.Ini, token).GetAwaiter().GetResult() : null;
        var ini = IniDocument.Load(info.Ini);
        if (ini.Get("INPUT", "KeyOverlay") == shortcut.ToIniValue()) return;
        shortcut.Apply(ini);
        WriteProbe.Check(info.Ini);
        token.ThrowIfCancellationRequested();
        GameProcesses.RequireStopped(settings.GameRoot);
        SafePaths.EnsureNoLinks(settings.GameRoot, info.Ini);
        if ((File.Exists(info.Ini) ? SafePaths.HashAsync(info.Ini, token).GetAwaiter().GetResult() : null) != originalHash)
            throw new IOException("ReShade 配置已被修改，菜单按键未写入。请重新启动游戏。");
        ini.Save(info.Ini);
        log("菜单按键已应用：" + shortcut.DisplayName);
    }
}
