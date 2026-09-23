using System.Diagnostics;

internal static partial class Program
{
    static void RequireGameStopped()
    {
        var candidates = Process.GetProcessesByName("Client-Win64-Shipping");
        try { CheckGameCandidates(candidates.Select(p => p.Id), OpenLauncherProcess); }
        finally { foreach(var candidate in candidates) candidate.Dispose(); }
    }
}
