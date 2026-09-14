using WuWaFpsUnlock.Core;

public static class GameDiscoveryTests
{
    public static async Task RunAsync(Func<string, Func<Task>, Task> test)
    {
        var root = Path.GetFullPath(Path.Combine("artifacts", "test-work", "discovery-中文 空格-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        void Check(bool condition) { if (!condition) throw new Exception("discovery assertion failed"); }
        string MakeGame(string name, ushort machine = 0x8664, ushort flags = 0x0022)
        {
            var game = Path.Combine(root, name); var shipping = Path.Combine(game, "Client", "Binaries", "Win64", "Client-Win64-Shipping.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(shipping)!);
            var pe = new byte[128]; BitConverter.GetBytes((ushort)0x5a4d).CopyTo(pe, 0); BitConverter.GetBytes(64).CopyTo(pe, 0x3c);
            BitConverter.GetBytes(0x4550).CopyTo(pe, 64); BitConverter.GetBytes(machine).CopyTo(pe, 68); BitConverter.GetBytes(flags).CopyTo(pe, 86);
            File.WriteAllBytes(shipping, pe); File.WriteAllText(Path.Combine(game, "Wuthering Waves.exe"), "not executed root-entry fixture"); return game;
        }
        Task Run(Action action) { action(); return Task.CompletedTask; }
        try
        {
            var a = MakeGame("游戏A"); var b = MakeGame("游戏B");
            await test("discovery validates exact Shipping layout from selected root", () => Run(() =>
                Check(GameDiscoveryService.DiscoverFromHints([new(a,"selected")]).Candidates.Single().GameRoot == a)));
            await test("discovery resolves saved Shipping and merges evidence", () => Run(() =>
            {
                var found = GameDiscoveryService.DiscoverFromHints([new(a,"selected"),new(Path.Combine(a,"Client","Binaries","Win64","Client-Win64-Shipping.exe"),"saved")]);
                Check(found.Candidates.Count == 1 && found.Candidates[0].Evidence.Count == 2);
            }));
            await test("discovery returns every candidate instead of silently picking first", () => Run(() =>
                Check(GameDiscoveryService.DiscoverFromHints([new(b,"saved"),new(a,"selected")]).Candidates.Count == 2)));
            await test("discovery supports exact launcher child directory", () => Run(() =>
            {
                var game = MakeGame(Path.Combine("launcher","Wuthering Waves Game"));
                Check(GameDiscoveryService.DiscoverFromHints([new(Path.GetDirectoryName(game)!,"launcher")]).Candidates.Single().GameRoot == game);
            }));
            await test("discovery does not recursively search unrelated children", () => Run(() =>
                Check(GameDiscoveryService.DiscoverFromHints([new(root,"parent")]).Candidates.Count == 0)));
            await test("discovery rejects x86 Shipping and DLL masquerading as EXE", () => Run(() =>
            {
                var x86 = MakeGame("x86",0x14c); var dll = MakeGame("dll",0x8664,0x2022);
                Check(GameDiscoveryService.DiscoverFromHints([new(x86,"x86"),new(dll,"dll")]).Candidates.Count == 0);
            }));
            await test("discovery rejects missing entry and malformed PE", () => Run(() =>
            {
                var missing = MakeGame("missing"); File.Delete(Path.Combine(missing,"Wuthering Waves.exe"));
                var malformed = MakeGame("malformed"); File.WriteAllText(Path.Combine(malformed,"Client","Binaries","Win64","Client-Win64-Shipping.exe"),"fake EXE");
                Check(GameDiscoveryService.DiscoverFromHints([new(missing,"missing"),new(malformed,"bad")]).Candidates.Count == 0);
            }));
            await test("discovery ignores relative and absent saved paths", () => Run(() =>
                Check(GameDiscoveryService.DiscoverFromHints([new("relative","bad"),new(Path.Combine(root,"absent"),"bad")]).Candidates.Count == 0)));
            await test("discovery supports cancellation without process launch", () => Run(() =>
            {
                using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
                try { GameDiscoveryService.DiscoverFromHints([new(a,"selected")],cancellation.Token); } catch(OperationCanceledException) { return; }
                throw new Exception("expected cancellation");
            }));
            await test("discovery async saved-only mode avoids system hints", async () =>
                Check((await GameDiscoveryService.DiscoverAsync(a,[b],false)).Candidates.Count == 2));
        }
        finally { Directory.Delete(root, true); }
    }
}
