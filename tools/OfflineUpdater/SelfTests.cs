internal static partial class Program
{
    // CI fixtures never start the launcher/game, resolve a real installation,
    // invoke COM, create shortcuts or read user data. One updater child exits itself.
    static int SelfTest()
    {
        string sandbox = Path.Combine(Path.GetTempPath(), "WuWaUpdater-fixtures-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        int passed = 0, failed = 0;
        void Test(string name, Action test)
        {
            try { test(); Console.WriteLine("PASS " + name); passed++; }
            catch (Exception ex) { Console.WriteLine("FAIL " + name + ": " + ex); failed++; }
        }
        void Check(bool value) { if (!value) throw new Exception("fixture assertion failed"); }
        void Reject(Action action)
        {
            try { action(); }
            catch (Exception ex) when (ex is IOException or InvalidDataException) { return; }
            throw new Exception("Expected IOException/InvalidDataException");
        }
        (string Root, string Backup, Snapshot Snapshot) Fixture()
        {
            string root = Path.Combine(sandbox, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Write(root, "WuWaFpsUnlock.exe", "inert old app");
            Write(root, "components/fps/ww_plugin_base.dll", "old component");
            Write(root, "payload/files/addon/renodx-mfgunlock.addon64", "old addon");
            Write(root, "data/deployments/game.json", "old ownership");
            Write(root, "licenses/old.txt", "old license");
            Directory.CreateDirectory(Scoped(root, "payload/empty"));
            string backup = Path.Combine(root, "update-backup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(backup);
            var changes = new List<Entry>();
            foreach (string relative in new[] { "WuWaFpsUnlock.exe", "components/fps/ww_plugin_base.dll", "payload/files/addon/renodx-mfgunlock.addon64", "licenses/new.txt" })
            {
                string target = Scoped(root, relative);
                string? old = File.Exists(target) ? FileHash(target) : null;
                string newSource = Path.Combine(sandbox, Guid.NewGuid().ToString("N"));
                File.WriteAllText(newSource, "inert new " + relative);
                changes.Add(new(relative, newSource, target, FileHash(newSource)) { OldHash = old });
            }
            var snapshot = CaptureSnapshot(root, backup, Path.Combine(root, "update-folder"), changes) with { Complete = true };
            WriteSnapshot(backup, snapshot);
            foreach (var change in changes) { Directory.CreateDirectory(Path.GetDirectoryName(change.Target)!); File.Copy(change.Source, change.Target, true); }
            return (root, backup, snapshot);
        }
        Test("snapshot includes complete data and empty directories", () =>
        {
            var f = Fixture(); var s = ReadSnapshot(f.Backup);
            Check(s.Files.Any(x => x.Relative == "data/deployments/game.json"));
            Check(Directory.Exists(Scoped(f.Backup, "snapshot/payload/empty")));
            Check(!s.Files.Any(x => x.Relative.Contains("update-backup-")));
        });
        Test("rollback restores old materials, preserves current data and later files", () =>
        {
            var f = Fixture(); Write(f.Root, "data/deployments/game.json", "new ownership");
            Write(f.Root, "payload/user-added.dll", "user file");
            RestoreSnapshot(f.Backup, f.Snapshot, () => { });
            Check(File.ReadAllText(Scoped(f.Root, "WuWaFpsUnlock.exe")) == "inert old app");
            Check(File.ReadAllText(Scoped(f.Root, "payload/files/addon/renodx-mfgunlock.addon64")) == "old addon");
            Check(File.ReadAllText(Scoped(f.Root, "data/deployments/game.json")) == "new ownership");
            Check(File.Exists(Scoped(f.Root, "payload/user-added.dll")) && !File.Exists(Scoped(f.Root, "licenses/new.txt")));
        });
        Test("rollback preserves changed file introduced by update", () =>
        {
            var f = Fixture(); Write(f.Root, "licenses/new.txt", "user changed");
            RestoreSnapshot(f.Backup, f.Snapshot, () => { });
            Check(File.ReadAllText(Scoped(f.Root, "licenses/new.txt")) == "user changed");
        });
        Test("rollback is idempotent", () =>
        {
            var f = Fixture(); RestoreSnapshot(f.Backup, f.Snapshot, () => { }); RestoreSnapshot(f.Backup, f.Snapshot, () => { });
            Check(File.ReadAllText(Scoped(f.Root, "WuWaFpsUnlock.exe")) == "inert old app");
        });
        Test("damaged snapshot rejected before restore", () =>
        {
            var f = Fixture(); Write(f.Backup, "snapshot/data/deployments/game.json", "damaged");
            Reject(() => RestoreSnapshot(f.Backup, f.Snapshot, () => { }));
            Check(File.ReadAllText(Scoped(f.Root, "WuWaFpsUnlock.exe")).StartsWith("inert new"));
        });
        Test("failure during rollback restores pre-rollback bytes", () =>
        {
            var f = Fixture(); string before = FileHash(Scoped(f.Root, "WuWaFpsUnlock.exe"));
            Reject(() => RestoreSnapshot(f.Backup, f.Snapshot, () => { }, count => { if (count == 2) throw new IOException("fixture injected interruption"); }));
            Check(FileHash(Scoped(f.Root, "WuWaFpsUnlock.exe")) == before);
            Check(File.ReadAllText(Scoped(f.Root, "components/fps/ww_plugin_base.dll")).StartsWith("inert new"));
        });
        Test("corrupt metadata and traversal rejected", () =>
        {
            var f = Fixture(); File.AppendAllText(Scoped(f.Backup, "snapshot.json"), "corrupt");
            Reject(() => ReadSnapshot(f.Backup)); Reject(() => Scoped(f.Root, "../outside.dll"));
            Reject(() => ValidateSnapshot(f.Backup, f.Snapshot with { Root = sandbox }));
        });
        Test("locked restore target fails before any write", () =>
        {
            var f = Fixture(); using var locked = new FileStream(Scoped(f.Root, "components/fps/ww_plugin_base.dll"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Reject(() => RestoreSnapshot(f.Backup, f.Snapshot, () => { }));
            Check(File.ReadAllText(Scoped(f.Root, "WuWaFpsUnlock.exe")).StartsWith("inert new"));
        });
        GameGuardTests(Test);
        BackupLifecycleTests(Test);
        InventoryTests(Test);
        WaitModeTests(Test);
        Console.WriteLine($"RESULT: {passed} passed, {failed} failed; inert filesystem fixtures, injected checks and native updater-child wait. No launcher/game launch or desktop changes.");
        // Delete only the exact generated fixture root, never a path supplied by a caller.
        try { NoLinks(sandbox); Directory.Delete(sandbox, true); } catch { Console.WriteLine("Fixture files retained: " + sandbox); }
        return failed == 0 ? 0 : 1;
    }

    static string FileHash(string path) { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); return Hash(stream); }
    static void Write(string root, string relative, string value)
    {
        string path = Scoped(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, value);
    }
}
