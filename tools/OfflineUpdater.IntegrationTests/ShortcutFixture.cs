internal static partial class Program
{
    // Integration tests exercise production CLI/filesystem code without desktop COM.
    static int gameProbeCalls;
    static void RequireGameStopped()
    {
        gameProbeCalls++;
        if(int.TryParse(Environment.GetEnvironmentVariable("WUWA_FIXTURE_GAME_AT"),out int failAt) && gameProbeCalls == failAt)
            throw new IOException("Fixture game appeared before write");
    }
    static void RefreshDesktopShortcut(string root)=>Console.WriteLine("FIXTURE: desktop shortcut adapter suppressed");
}
