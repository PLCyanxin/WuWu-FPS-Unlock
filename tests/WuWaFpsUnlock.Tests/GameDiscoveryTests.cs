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
            string Shipping(string game) => Path.Combine(game,"Client","Binaries","Win64","Client-Win64-Shipping.exe");
            await test("discovery exact selected pair can be reused without searching", () => Run(() =>
            {
                Check(GameDiscoveryService.TryValidateSelection(a,Shipping(a),out var current,out var reason));
                Check(current!.GameRoot == a && current.ShippingExePath == Shipping(a) && reason.Contains("可复用"));
            }));
            await test("discovery correct root with Cinebench executable cannot be reused", () => Run(() =>
            {
                var wrong=Path.Combine(root,"Cinebench.exe");File.Copy(Shipping(a),wrong);
                Check(!GameDiscoveryService.TryValidateSelection(a,wrong,out var current,out _) && current is null);
            }));
            await test("discovery existing wrong root with correct executable cannot be reused", () => Run(() =>
                Check(!GameDiscoveryService.TryValidateSelection(b,Shipping(a),out _,out _))));
            await test("discovery same-name EXE outside canonical layout is rejected", () => Run(() =>
            {
                var fake=Path.Combine(root,"Client-Win64-Shipping.exe");File.Copy(Shipping(a),fake);
                Check(!GameDiscoveryService.TryValidateSelection(root,fake,out _,out _));
            }));
            await test("discovery renamed fake Shipping with no game layout is rejected", () => Run(() =>
            {
                var fakeRoot=Path.Combine(root,"Cinebench masquerade");var fake=Shipping(fakeRoot);
                Directory.CreateDirectory(Path.GetDirectoryName(fake)!);File.Copy(Shipping(a),fake);
                Check(!GameDiscoveryService.TryValidateSelection(fakeRoot,fake,out _,out _));
                Check(GameDiscoveryService.DiscoverFromHints([new(fakeRoot,"incorrect user choice"),new(a,"saved valid game")]).Candidates.Single().GameRoot==a);
            }));
            await test("discovery wrong selection does not suppress fresh valid saved hint", async () =>
            {
                var wrong=Path.Combine(root,"Cinebench");Directory.CreateDirectory(wrong);
                var result=await GameDiscoveryService.DiscoverAsync(wrong,[a],false);
                Check(result.Candidates.Single().GameRoot==a);
            });
            await test("discovery rechecks on-disk selection and never reuses cached success", () => Run(() =>
            {
                var changed=MakeGame("changed after discovery");
                Check(GameDiscoveryService.TryValidateSelection(changed,Shipping(changed),out _,out _));
                Check(GameDiscoveryService.DiscoverFromHints([new(changed,"selected")]).Candidates.Count==1);
                File.WriteAllText(Shipping(changed),"renamed non-PE file");
                Check(!GameDiscoveryService.TryValidateSelection(changed,Shipping(changed),out _,out _));
                Check(GameDiscoveryService.DiscoverFromHints([new(changed,"selected")]).Candidates.Count==0);
            }));
            await test("discovery empty partial and root-launcher selections cannot be reused", () => Run(() =>
            {
                Check(!GameDiscoveryService.TryValidateSelection(a,null,out _,out _));
                Check(!GameDiscoveryService.TryValidateSelection(null,Shipping(a),out _,out _));
                Check(!GameDiscoveryService.TryValidateSelection(a,Path.Combine(a,"Wuthering Waves.exe"),out _,out _));
            }));
            await test("discovery correct selected folder resolves EXE and relative path directly", () => Run(() =>
            {
                Check(GameDiscoveryService.TryResolveGameRoot(a,out var direct,out _));
                Check(direct!.ShippingExePath==Shipping(a));
                Check(direct.RelativeShippingExePath.Replace(Path.DirectorySeparatorChar,'/')==GameDiscoveryService.ShippingRelativePath);
            }));
            await test("discovery valid root never enumerates saved hints or starts fallback", async () =>
            {
                IEnumerable<string> Unexpected(){yield return Throw();} string Throw()=>throw new Exception("saved enumeration should not execute");
                var direct=await GameDiscoveryService.DiscoverAsync(a,Unexpected(),false);
                Check(direct.Candidates.Count==1&&direct.Diagnostics.Any(d=>d.Contains("未搜索磁盘")));
            });
            await test("directory-name search finds moved official folders at varying depths", () => Run(() =>
            {
                var scan=Path.Combine(root,"scan");
                var one=MakeGame(Path.Combine("scan","launcherA","Wuthering Waves Game"));
                var two=MakeGame(Path.Combine("scan","资料","另一位置","Wuthering Waves Game"));
                var result=GameDiscoveryService.SearchDirectoriesByName([scan],["Wuthering Waves Game"]);
                Check(result.Candidates.Count==2&&result.Candidates.Any(c=>c.GameRoot==one)&&result.Candidates.Any(c=>c.GameRoot==two));
            }));
            await test("directory-name search rejects matching name without valid game and ignores other names", () => Run(() =>
            {
                var scan=Path.Combine(root,"scan-exact");Directory.CreateDirectory(Path.Combine(scan,"Wuthering Waves Game"));
                MakeGame(Path.Combine("scan-exact","Wuthering Waves Game fake"));
                Check(GameDiscoveryService.SearchDirectoriesByName([scan],["Wuthering Waves Game"]).Candidates.Count==0);
            }));
            await test("directory-name search directory cap reports incomplete instead of success", () => Run(() =>
            {
                var result=GameDiscoveryService.SearchDirectoriesByName([root],["Wuthering Waves Game"],new(MaxDirectories:1));
                Check(result.Candidates.Count==0&&result.Diagnostics.Any(d=>d.Contains("可能不完整")));
            }));
            await test("directory-name search depth cap prevents descending to hidden candidates", () => Run(() =>
            {
                var result=GameDiscoveryService.SearchDirectoriesByName([root],["Wuthering Waves Game"],new(MaxDepth:0));
                Check(result.Candidates.Count==0&&result.Diagnostics.Any(d=>d.Contains("可能不完整")));
            }));
            await test("directory-name search cancellation stops before reading roots", () => Run(() =>
            {
                using var cancel=new CancellationTokenSource();cancel.Cancel();
                try{GameDiscoveryService.SearchDirectoriesByName([root],["Wuthering Waves Game"],token:cancel.Token);}
                catch(OperationCanceledException){return;}throw new Exception("expected search cancellation");
            }));
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
                Check((await GameDiscoveryService.DiscoverAsync(a,[b],false)).Candidates.Single().GameRoot == a));
        }
        finally { Directory.Delete(root, true); }
    }
}
