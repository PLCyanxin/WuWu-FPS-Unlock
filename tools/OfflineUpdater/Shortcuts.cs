using System.Runtime.InteropServices;

internal static partial class Program
{
    static void RefreshDesktopShortcut(string root)
    {
        object? shell = null;
        try
        {
            string target = Path.GetFullPath(Path.Combine(root, "WuWaFpsUnlock.exe"));
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(desktop) || !Directory.Exists(desktop)) throw new IOException("无法定位 Windows 桌面目录。");
            NoLinks(desktop);
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell") ?? throw new IOException("Windows 快捷方式组件不可用。"));
            if (shell is null) throw new IOException("无法创建 Windows 快捷方式组件。");
            int updated = 0;
            foreach (string path in Directory.EnumerateFiles(desktop, "*.lnk", SearchOption.TopDirectoryOnly))
            {
                object? shortcut = null;
                try
                {
                    NoLinks(path);
                    shortcut = ((dynamic)shell).CreateShortcut(path);
                    string existing = ((dynamic)shortcut).TargetPath;
                    if (string.IsNullOrWhiteSpace(existing) || !Path.IsPathFullyQualified(existing)
                        || !Path.GetFullPath(existing).Equals(target, StringComparison.OrdinalIgnoreCase)) continue;
                    SetShortcut(shortcut, target, root);
                    updated++;
                    Console.WriteLine("已更新桌面快捷方式：" + path);
                }
                catch (Exception ex) { Console.WriteLine("快捷方式未处理：" + path + "；" + ex.Message); }
                finally { ReleaseShortcutCom(shortcut); }
            }
            if (updated != 0) return;
            string newPath = Path.Combine(desktop, "鸣潮 FPS Unlock.lnk");
            for (int n = 2; File.Exists(newPath) || Directory.Exists(newPath); n++)
                newPath = Path.Combine(desktop, $"鸣潮 FPS Unlock ({n}).lnk");
            NoLinks(newPath);
            object created = ((dynamic)shell).CreateShortcut(newPath);
            try { SetShortcut(created, target, root); }
            finally { ReleaseShortcutCom(created); }
            Console.WriteLine("已创建桌面快捷方式：" + newPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine("程序文件操作已完成，但桌面快捷方式未能更新；可手动为安装目录中的 WuWaFpsUnlock.exe 创建快捷方式。原因：" + ex.Message);
        }
        finally { ReleaseShortcutCom(shell); }
    }

    static void ReleaseShortcutCom(object? value)
    {
        try { if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
        catch { /* Releasing COM must not turn a completed update into a failed one. */ }
    }

    static void SetShortcut(dynamic shortcut, string target, string root)
    {
        shortcut.TargetPath = target;
        shortcut.WorkingDirectory = root;
        shortcut.IconLocation = target + ",0";
        shortcut.Save();
    }
}
