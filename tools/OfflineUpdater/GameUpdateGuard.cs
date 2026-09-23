internal static partial class Program
{
    static void CheckGameCandidates(IEnumerable<int> candidates, Func<int, ILauncherProcess?> open)
    {
        try
        {
            foreach(int pid in candidates)
            {
                using var process = open(pid);
                if(process is null || process.HasExited) continue;
                string? path = process.ExecutablePath;
                if(process.HasExited) continue;
                if(string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
                    throw new IOException("无法确认游戏进程身份，已停止更新；请完全退出游戏后重试。");
                if(Path.GetFileName(path).Equals("Client-Win64-Shipping.exe", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("游戏仍在运行，已停止更新；请完全退出游戏后重试。");
            }
        }
        catch(System.ComponentModel.Win32Exception error)
        {
            throw new IOException("无法安全检查游戏进程，已停止更新；不会结束游戏进程。",error);
        }
    }
}
