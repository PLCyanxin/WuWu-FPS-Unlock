using WuWaFpsUnlock.Core;
using WuWaFpsUnlock.Services;

// Actual cleanup service and filesystem operations; game/installer APIs are replaced by inert stubs.
var root = Path.GetFullPath(Path.Combine("artifacts", "tests", "cleanup-regression", Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);
int passed = 0, failed = 0;
void Check(bool condition, string detail = "assertion failed") { if (!condition) throw new Exception(detail); }
async Task Rejected(Func<Task> run)
{
    try { await run(); } catch (Exception ex) when (ex is IOException or InvalidDataException) { return; }
    throw new Exception("Expected cleanup rejection");
}
async Task<(UserSettings Settings, DeploymentReceipt Receipt, List<PlannedFile> Plan)> Fixture()
{
    GameProcesses.Running = false; WriteProbe.OnCheck = null;
    string dir = Path.Combine(root, Guid.NewGuid().ToString("N")); AppPaths.Root = Path.Combine(dir, "journal");
    string game = Path.Combine(dir, "game"), exeDir = Path.Combine(game, "Client", "Binaries", "Win64"), pkg = Path.Combine(dir, "payload");
    Directory.CreateDirectory(exeDir); Directory.CreateDirectory(pkg);
    File.WriteAllText(Path.Combine(pkg, "vendor.bin"), "INERT VENDOR"); File.WriteAllText(Path.Combine(pkg, "addon.bin"), "INERT ADDON");
    File.WriteAllText(Path.Combine(exeDir, "nvngx_dlssg.dll"), "INERT ORIGINAL");
    var manifest = new PayloadManifest { PackageId = "cleanup-fixture", ReShade = new() { ProxyApi = "dxgi" }, Files = [
        new() { Source = "vendor.bin", Target = "nvngx_dlssg.dll", Kind = PayloadKind.Vendor, Anchor = TargetAnchor.ExeDir },
        new() { Source = "addon.bin", Target = ManagedAddons.Main, Kind = PayloadKind.Addon, Anchor = TargetAnchor.AddonDir }] };
    string path = Path.Combine(pkg, "manifest.json"); JsonFiles.Save(path, manifest);
    var settings = new UserSettings { GameRoot = game, GameExe = Path.Combine(exeDir, "Client-Win64-Shipping.exe"), PackageManifest = path };
    var plan = await PackageReader.PlanAsync(manifest, path, game, exeDir, exeDir);
    var receipt = new DeploymentReceipt { GameRoot = game, GameExe = settings.GameExe };
    await DeploymentFiles.ApplyAsync(plan, receipt, () => AppPaths.SaveReceipt(receipt), _ => { });
    receipt.Status = "Deployed"; AppPaths.SaveReceipt(receipt); return (settings, receipt, plan);
}
void Config(DeploymentReceipt receipt, string text)
{
    receipt.IniPath = Path.Combine(Path.GetDirectoryName(receipt.GameExe)!, "ReShade.ini");
    receipt.IniEdits = [new() { Section = "RenoDX.MFGUnlock", Key = "Enabled", Written = "1" }];
    File.WriteAllText(receipt.IniPath, text); AppPaths.SaveReceipt(receipt);
}
async Task Clean(UserSettings s)
{
    var service = new DeploymentService(_ => { }); await service.CleanPreviewAsync(s); await service.CleanAsync(s);
}
async Task Test(string name, Func<Task> run)
{
    try { await run(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex); }
    finally { GameProcesses.Running = false; WriteProbe.OnCheck = null; }
}

await Test("recorded cleanup works when all source materials are missing", async () => {
    var x = await Fixture(); foreach (var file in x.Plan) File.Delete(file.Source);
    await Clean(x.Settings); Check(x.Plan.All(p => !File.Exists(p.Target))); Check(AppPaths.LoadReceipt(x.Settings)!.Status == "Cleaned");
});
await Test("interrupted launcher update blocks maintenance without game mutation", async () => {
    var x = await Fixture(); var service = new DeploymentService(_ => { }); await service.CleanPreviewAsync(x.Settings);
    string backup=Path.Combine(AppPaths.Base,"update-backup-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(backup);
    DurableJson.Save(Path.Combine(backup,"transaction.json"),new{Schema=1,Root=AppPaths.Base,Phase="Applying",Intent="WuWaFpsUnlock.exe"});
    await Rejected(()=>service.CleanAsync(x.Settings));
    Check(x.Plan.All(p=>File.Exists(p.Target))&&AppPaths.LoadReceipt(x.Settings)!.Status=="Deployed");
});
await Test("missing unowned material is preserved without blocking owned cleanup", async () => {
    var x = await Fixture(); string manual = Path.Combine(x.Settings.GameRoot, "nvngx_dlssg.dll");
    File.Copy(x.Plan[0].Source, manual); File.Delete(x.Plan[0].Source);
    await Clean(x.Settings); Check(x.Plan.All(p => !File.Exists(p.Target)) && File.Exists(manual));
    Check(AppPaths.LoadReceipt(x.Settings)!.Status == "CleanedWithSkips");
});
await Test("unused locked source is not read for recorded cleanup", async () => {
    var x = await Fixture(); using var locked = new FileStream(x.Plan[0].Source, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    await Clean(x.Settings); Check(x.Plan.All(p => !File.Exists(p.Target)));
});
await Test("owned cleanup works without a manifest", async () => {
    var x = await Fixture(); File.Delete(x.Settings.PackageManifest); await Clean(x.Settings); Check(x.Plan.All(p => !File.Exists(p.Target)));
});
await Test("late invalid receipt path is rejected before any deletion", async () => {
    var x = await Fixture(); string outside = Path.Combine(root, Guid.NewGuid() + ".addon64");
    string file = Path.Combine(Path.GetDirectoryName(outside)!, ManagedAddons.Dynamic); File.WriteAllText(file, "OUTSIDE");
    x.Receipt.Files.Add(new() { Path = file, Kind = "Addon", CreatedByTool = true, Completed = true, InstalledHash = await SafePaths.HashAsync(file) });
    AppPaths.SaveReceipt(x.Receipt); await Rejected(() => Clean(x.Settings)); Check(x.Plan.All(p => File.Exists(p.Target)) && File.ReadAllText(file) == "OUTSIDE");
});
await Test("unreadable INI is rejected before deletion", async () => {
    var x = await Fixture(); Config(x.Receipt, ""); File.WriteAllBytes(x.Receipt.IniPath, [0xff]);
    await Rejected(() => Clean(x.Settings)); Check(x.Plan.All(p => File.Exists(p.Target)) && File.ReadAllBytes(x.Receipt.IniPath).SequenceEqual(new byte[] { 0xff }));
});
await Test("duplicate owned INI key is rejected before deletion", async () => {
    var x = await Fixture(); Config(x.Receipt, "[RenoDX.MFGUnlock]\nEnabled=1\nEnabled=0");
    await Rejected(() => Clean(x.Settings)); Check(x.Plan.All(p => File.Exists(p.Target)));
});
await Test("unrecorded INI is left untouched even if encoding is unknown", async () => {
    var x = await Fixture(); x.Receipt.IniPath = Path.Combine(Path.GetDirectoryName(x.Settings.GameExe)!, "ReShade.ini");
    File.WriteAllBytes(x.Receipt.IniPath, [0xff]); AppPaths.SaveReceipt(x.Receipt); await Clean(x.Settings);
    Check(x.Plan.All(p => !File.Exists(p.Target)) && File.ReadAllBytes(x.Receipt.IniPath).SequenceEqual(new byte[] { 0xff }));
});
await Test("game starting during write preflight prevents every deletion", async () => {
    var x = await Fixture(); var service = new DeploymentService(_ => { }); await service.CleanPreviewAsync(x.Settings);
    WriteProbe.OnCheck = () => GameProcesses.Running = true; await Rejected(() => service.CleanAsync(x.Settings));
    Check(x.Plan.All(p => File.Exists(p.Target)) && AppPaths.LoadReceipt(x.Settings)!.Status == "Deployed");
});
await Test("game starting during legacy fingerprint checks prevents deletion", async () => {
    var x = await Fixture(); foreach (var file in x.Plan) File.Copy(file.Source, file.Target, true);
    var plan = await UserMaterialRemoval.PlanAsync(x.Settings.PackageManifest, x.Settings.GameRoot); GameProcesses.Running = true;
    await Rejected(() => UserMaterialRemoval.ExecuteAsync(plan, _ => { }, requireStopped: () => GameProcesses.RequireStopped(x.Settings.GameRoot)));
    Check(x.Plan.All(p => File.Exists(p.Target)));
});
await Test("game identity guard runs on the hashed deletion handle", async () => {
    var x = await Fixture(); GameProcesses.Running = true;
    await Rejected(() => OwnedFileDeletion.DeleteMatchingAsync(x.Plan[0].Target, x.Plan[0].Sha256, requireStopped: () => GameProcesses.RequireStopped(x.Settings.GameRoot)));
    Check(File.Exists(x.Plan[0].Target));
});
await Test("config changing after approval requires a new preview", async () => {
    var x = await Fixture(); Config(x.Receipt, "[RenoDX.MFGUnlock]\nEnabled=1");
    var service = new DeploymentService(_ => { }); await service.CleanPreviewAsync(x.Settings); File.AppendAllText(x.Receipt.IniPath, "\nUserKey=2");
    await Rejected(() => service.CleanAsync(x.Settings)); Check(x.Plan.All(p => File.Exists(p.Target)));
});
await Test("config changing during write probes prevents all deletion", async () => {
    var x = await Fixture(); Config(x.Receipt, "[RenoDX.MFGUnlock]\nEnabled=1");
    var service = new DeploymentService(_ => { }); await service.CleanPreviewAsync(x.Settings);
    WriteProbe.OnCheck = () => { File.AppendAllText(x.Receipt.IniPath, "\nUserKey=2"); WriteProbe.OnCheck = null; };
    await Rejected(() => service.CleanAsync(x.Settings)); Check(x.Plan.All(p => File.Exists(p.Target)));
});
await Test("changed config remains registered and reports skipped cleanup", async () => {
    var x = await Fixture(); Config(x.Receipt, "[RenoDX.MFGUnlock]\nEnabled=0\n[USER]\nKeep=1");
    await Clean(x.Settings); var after = AppPaths.LoadReceipt(x.Settings)!;
    Check(after.Status == "CleanedWithSkips" && after.IniEdits.Count == 1);
    Check(IniDocument.Load(x.Receipt.IniPath).Get("RenoDX.MFGUnlock", "Enabled") == "0" && IniDocument.Load(x.Receipt.IniPath).Get("USER", "Keep") == "1");
});
await Test("locked component keeps its config registration for retry", async () => {
    var x = await Fixture(); Config(x.Receipt, "[RenoDX.MFGUnlock]\nEnabled=1");
    using (var locked = new FileStream(x.Plan[0].Target, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
    { await Rejected(() => Clean(x.Settings)); Check(AppPaths.LoadReceipt(x.Settings)!.IniEdits.Count == 1); }
    await Clean(x.Settings); Check(x.Plan.All(p => !File.Exists(p.Target)) && AppPaths.LoadReceipt(x.Settings)!.IniEdits.Count == 0);
});
await Test("changed owned files retain existing protection", async () => {
    var x = await Fixture(); foreach (var file in x.Plan) File.AppendAllText(file.Target, "USER CHANGE");
    await Clean(x.Settings); Check(x.Plan.All(p => File.Exists(p.Target)) && AppPaths.LoadReceipt(x.Settings)!.Status == "CleanedWithSkips");
});
await Test("repeat cleanup preserves user ReShade and unrelated addon", async () => {
    var x = await Fixture(); string dir = Path.GetDirectoryName(x.Settings.GameExe)!;
    var protectedFiles = new[] { "dxgi.dll", "user.addon64", "Client-Win64-Shipping.exe", "preset.ini" }.Select(n => Path.Combine(dir, n)).ToArray();
    foreach (string path in protectedFiles) File.WriteAllText(path, "USER FILE");
    await Clean(x.Settings); await Clean(x.Settings); Check(protectedFiles.All(p => File.ReadAllText(p) == "USER FILE"));
});
Console.WriteLine($"RESULT: {passed} passed, {failed} failed. Isolated cleanup only; no game or installed launcher changed.");
return failed == 0 ? 0 : 1;
