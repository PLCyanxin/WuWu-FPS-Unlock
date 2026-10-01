internal static partial class Program
{
    // Integration tests exercise production CLI/filesystem code without desktop COM.
    static int gameProbeCalls;
    static void RequireGameStopped()
    {
        gameProbeCalls++;
        if(int.TryParse(Environment.GetEnvironmentVariable("WUWA_FIXTURE_GAME_AT"),out int failAt) && failAt>0 && gameProbeCalls >= failAt)
            throw new IOException("Fixture game appeared before write");
    }
    static void RefreshDesktopShortcut(string root)=>Console.WriteLine("FIXTURE: desktop shortcut adapter suppressed");
    static partial void UpdateCommitCheckpoint(int count)
    {
        if(int.TryParse(Environment.GetEnvironmentVariable("WUWA_FIXTURE_KILL_AFTER"),out int requested)&&count==requested)Environment.Exit(77);
    }
    static partial void SnapshotStateCheckpoint(bool complete)
    {
        if(complete&&Environment.GetEnvironmentVariable("WUWA_FIXTURE_KILL_METADATA")=="1")Environment.Exit(78);
        if(complete&&Environment.GetEnvironmentVariable("WUWA_FIXTURE_FAIL_METADATA")=="1")throw new IOException("Fixture legacy metadata write failed after durable completion");
    }
}
