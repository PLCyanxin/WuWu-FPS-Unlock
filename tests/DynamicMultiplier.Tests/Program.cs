using System.IO;
using System.Reflection;
using System.Text.Json;
using WuWaFpsUnlock.Core;
using WuWaFpsUnlock.Services;

string root = Path.Combine(Path.GetTempPath(), "WuWa-DynamicMultiplier-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int passed = 0, failed = 0;
void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
async Task Test(string name, Func<Task> action)
{
    try { await action(); Console.WriteLine("PASS " + name); passed++; }
    catch (Exception ex) { Console.WriteLine("FAIL " + name + ": " + ex); failed++; }
}
Task Sync(Action action) { action(); return Task.CompletedTask; }
string NewPath(string name) { string dir = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir); return Path.Combine(dir, name); }
HardwareInfo Hardware(int? driver) => new("GeForce RTX 4080", driver, true, "fixture", "fixture", true);
MfgDeploymentConfiguration Configuration(int cap, bool dynamic = true) => MfgDeploymentConfiguration.Create(
    new() { DynamicMaxMultiplier = cap }, new() { FixedMultiplier = 3 }, Hardware(dynamic ? 59541 : 59540));

await Test("missing setting retains default 4 without changing deployed INI", () => Sync(() =>
{
    string ini = NewPath("ReShade.ini"); File.WriteAllText(ini, "[RenoDX.MFGUnlock]\nDynamicMaxMultiplier=6\n");
    byte[] before = File.ReadAllBytes(ini);
    var settings = JsonSerializer.Deserialize<UserSettings>("{\"TargetFps\":165}", JsonFiles.Options)!;
    Check(new UserSettings().DynamicMaxMultiplier == 4 && settings.DynamicMaxMultiplier == 4);
    Check(before.SequenceEqual(File.ReadAllBytes(ini)));
}));
foreach (int cap in new[] { 0, 2, 3, 4, 5, 6 })
    await Test("JSON save read clone preserves cap " + cap, () => Sync(() =>
    {
        string path = NewPath("settings.json"); var settings = new UserSettings { DynamicMaxMultiplier = cap };
        JsonFiles.Save(path, settings);
        Check(JsonFiles.Read<UserSettings>(path).DynamicMaxMultiplier == cap && settings.Clone().DynamicMaxMultiplier == cap);
    }));
foreach (int invalid in new[] { int.MinValue, -1, 1, 7, int.MaxValue })
    await Test("invalid assignment becomes no override " + invalid, () => Sync(() => Check(new UserSettings { DynamicMaxMultiplier = invalid }.DynamicMaxMultiplier == 0)));
foreach (string invalid in new[] { "1", "7", "-1", "2147483648", "4.5", "4.0", "4e100", "null", "true", "false", "\"4\"", "\"damaged\"", "[]", "{\"nested\":[1,2]}" })
    await Test("invalid JSON field becomes no override: " + invalid, () => Sync(() =>
    {
        var settings = JsonSerializer.Deserialize<UserSettings>("{\"dynamicMaxMultiplier\":" + invalid + ",\"TargetFps\":165,\"Future\":{\"Keep\":true}}", JsonFiles.Options)!;
        Check(settings.DynamicMaxMultiplier == 0 && settings.TargetFps == 165 && settings.ExtensionData!.ContainsKey("Future"));
        string path = NewPath("ReShade.ini"); var ini = IniDocument.Load(path);
        MfgDeploymentConfiguration.Create(settings, new(), Hardware(59541)).ApplyOwned(ini, new()); ini.Save(path);
        Check(IniDocument.Load(path).Get("RenoDX.MFGUnlock", "DynamicMaxMultiplier") is null);
    }));
await Test("Dynamic enables native selection without writing startup cap", () => Sync(() =>
{
    var ini = IniDocument.Load(NewPath("ReShade.ini")); var receipt = new DeploymentReceipt();
    Configuration(4).ApplyOwned(ini, receipt);
    Check(ini.Get("RenoDX.MFGUnlock", "DynamicMFG") == "1");
    Check(ini.Get("RenoDX.MFGUnlock", "ForceMultiplier") == "0");
    Check(ini.Get("RenoDX.MFGUnlock", "DynamicMaxMultiplier") is null);
    Check(ini.Get("RenoDX.MFGUnlock", "RuntimeSelectionMode") == "1");
    Check(!receipt.IniEdits.Any(e => e.Key == "DynamicMaxMultiplier"));
}));
await Test("Fixed omits startup cap and preserves fixed multiplier", () => Sync(() =>
{
    var ini = IniDocument.Load(NewPath("ReShade.ini")); Configuration(6, false).ApplyOwned(ini, new());
    Check(ini.Get("RenoDX.MFGUnlock", "DynamicMFG") == "0" && ini.Get("RenoDX.MFGUnlock", "ForceMultiplier") == "3");
    Check(ini.Get("RenoDX.MFGUnlock", "DynamicMaxMultiplier") is null);
}));
await Test("unknown driver and disabled preference preserve existing mode selection", () => Sync(() =>
{
    Check(!MfgDeploymentConfiguration.Create(new(), new(), Hardware(null)).DynamicEnabled);
    Check(!MfgDeploymentConfiguration.Create(new(), new() { PreferDynamic = false }, Hardware(59541)).DynamicEnabled);
}));
await Test("preview omits retired startup cap and restart instructions", () => Sync(() =>
{
    Check(Configuration(4).PreviewText.Contains("Dynamic MFG：将启用"));
    Check(Configuration(4, false).PreviewText.Contains("不启用（Fixed）"));
    Check(Configuration(4).PreviewText == Configuration(0).PreviewText);
    Check(!Configuration(4).PreviewText.Contains("最大倍率") && !Configuration(4).PreviewText.Contains("重启"));
}));
foreach (bool dynamic in new[] { true, false })
await Test("deployment removes legacy cap without restoring it on cleanup " + dynamic, () => Sync(() =>
{
    string path = NewPath("ReShade.ini");
    File.WriteAllText(path, ";user comment\r\n[GENERAL]\r\nPresetPath=user.ini\r\nDynamicMaxMultiplier=3\r\n[RenoDX.MFGUnlock]\r\nDynamicMaxMultiplier=6\r\ndynamicmaxmultiplier=2\r\nMaxCount=5\r\nDynamicTargetFPS=165\r\nLatencyGuard=1\r\n");
    var ini = IniDocument.Load(path); var receipt = new DeploymentReceipt();
    receipt.IniEdits.Add(new() { Section="RenoDX.MFGUnlock", Key="DynamicMaxMultiplier", Previous="6", Written="4" });
    Configuration(4, dynamic).ApplyOwned(ini, receipt, new Dictionary<string,string> { ["dynamicmaxmultiplier"]="5", ["ManifestOption"]="keep" }); ini.Save(path);
    Check(ini.Get("RenoDX.MFGUnlock", "ManifestOption") == "keep");
    Check(!receipt.IniEdits.Any(e => e.Key == "DynamicMaxMultiplier"));
    Check(ini.Get("RenoDX.MFGUnlock", "DynamicMaxMultiplier") is null);
    Configuration(3, dynamic).ApplyOwned(ini, receipt);
    ini.RemoveOwnedEdits(receipt, _ => { }); ini.Save(path);
    Check(ini.Get("RenoDX.MFGUnlock", "DynamicMaxMultiplier") is null);
    Check(ini.Get("GENERAL", "DynamicMaxMultiplier") == "3");
    Check(ini.Get("RenoDX.MFGUnlock", "MaxCount") == "5" && ini.Get("RenoDX.MFGUnlock", "DynamicTargetFPS") == "165" && ini.Get("RenoDX.MFGUnlock", "LatencyGuard") == "1");
    Check(File.ReadAllText(path).Contains(";user comment\r\n[GENERAL]\r\nPresetPath=user.ini"));
}));
await Test("cleanup removes a previously absent cap and preserves later user edits", () => Sync(() =>
{
    var ini = IniDocument.Load(NewPath("ReShade.ini")); var receipt = new DeploymentReceipt();
    Configuration(4).ApplyOwned(ini, receipt); ini.RemoveOwnedEdits(receipt, _ => { });
    Check(ini.Get("RenoDX.MFGUnlock", "DynamicMaxMultiplier") is null);
    receipt = new(); Configuration(4).ApplyOwned(ini, receipt); ini.Set("RenoDX.MFGUnlock", "DynamicMaxMultiplier", "2");
    ini.RemoveOwnedEdits(receipt, _ => { }); Check(ini.Get("RenoDX.MFGUnlock", "DynamicMaxMultiplier") == "2");
}));
await Test("Fixed transition does not restore retired cap", () => Sync(() =>
{
    var ini = IniDocument.Load(NewPath("ReShade.ini")); ini.Set("RenoDX.MFGUnlock", "DynamicMaxMultiplier", "5");
    var receipt = new DeploymentReceipt(); Configuration(4).ApplyOwned(ini, receipt); Configuration(4, false).ApplyOwned(ini, receipt);
    Check(ini.Get("RenoDX.MFGUnlock", "DynamicMaxMultiplier") is null);
    ini.RemoveOwnedEdits(receipt, _ => { }); Check(ini.Get("RenoDX.MFGUnlock", "DynamicMaxMultiplier") is null);
}));
foreach (bool dynamic in new[] { true, false })
    await Test("real deployment approval ignores legacy cap in " + (dynamic ? "Dynamic" : "Fixed"), async () =>
    {
        string manifest = NewPath("manifest.json"); File.WriteAllText(manifest, "{}");
        var settings = new UserSettings { GameRoot = Path.GetDirectoryName(manifest)!, GameExe = Path.Combine(Path.GetDirectoryName(manifest)!, "Shipping.exe"), PackageManifest = manifest, DynamicMaxMultiplier = 4 };
        var info = new ReShadeInfo("fixture", null, Path.Combine(settings.GameRoot, "ReShade.ini"), settings.GameRoot, "fixture");
        var method = typeof(DeploymentService).GetMethod("PlanFingerprint", BindingFlags.NonPublic | BindingFlags.Static)!;
        Task<string> Fingerprint(MfgDeploymentConfiguration config) => (Task<string>)method.Invoke(null, [settings, new List<PlannedFile>(), info, config, CancellationToken.None])!;
        string approved = await Fingerprint(Configuration(4, dynamic));
        Check(approved == await Fingerprint(Configuration(4, dynamic)), "Identical plans must remain stable");
        settings.DynamicMaxMultiplier = 6;
        Check(approved == await Fingerprint(Configuration(settings.DynamicMaxMultiplier, dynamic)), "Retired cap must not affect approval");
    });
foreach (string liveValue in new[] { "0", "2", "3", "4", "5", "6" })
await Test("repeated Dynamic/Fixed deployment preserves game-owned live cap " + liveValue, () => Sync(() =>
{
    string path = NewPath("ReShade.ini");
    File.WriteAllText(path, ";preserve\r\n[GENERAL]\r\nPresetPath=user.ini\r\n[RenoDX.MFGUnlock]\r\nDynamicMaxMultiplier=4\r\nDynamicLiveMaxMultiplier=" + liveValue + "\r\nDynamicTargetFPS=165\r\n");
    var receipt = new DeploymentReceipt();
    receipt.IniEdits.Add(new() { Section = "RenoDX.MFGUnlock", Key = "DynamicMaxMultiplier", Previous = "6", Written = "4" });
    foreach (bool dynamic in new[] { true, true, false, true })
    {
        var ini = IniDocument.Load(path);
        Configuration(4, dynamic).ApplyOwned(ini, receipt,
            new Dictionary<string, string> { ["DynamicMaxMultiplier"] = "4", ["OtherDeploymentOption"] = "1" });
        ini.Save(path);
        Check(IniDocument.Load(path).Get("RenoDX.MFGUnlock", "DynamicLiveMaxMultiplier") == liveValue,
            "Deployment must preserve the separately persisted in-game request");
        Check(!receipt.IniEdits.Any(e => e.Key.Equals("DynamicLiveMaxMultiplier", StringComparison.OrdinalIgnoreCase)),
            "Deployment must not claim ownership of the game-owned request");
    }
    var cleanup = IniDocument.Load(path); cleanup.RemoveOwnedEdits(receipt, _ => { }); cleanup.Save(path);
    var final = IniDocument.Load(path);
    Check(final.Get("RenoDX.MFGUnlock", "DynamicLiveMaxMultiplier") == liveValue);
    Check(final.Get("RenoDX.MFGUnlock", "DynamicMaxMultiplier") is null);
    Check(final.Get("RenoDX.MFGUnlock", "DynamicTargetFPS") == "165" && final.Get("GENERAL", "PresetPath") == "user.ini");
}));
await Test("new deployment leaves missing live cap for addon default and preserves later game selection", () => Sync(() =>
{
    string path = NewPath("ReShade.ini"); var receipt = new DeploymentReceipt();
    var ini = IniDocument.Load(path); Configuration(4).ApplyOwned(ini, receipt); ini.Save(path);
    Check(IniDocument.Load(path).Get("RenoDX.MFGUnlock", "DynamicLiveMaxMultiplier") is null);
    foreach (string selected in new[] { "6", "0" })
    {
        ini = IniDocument.Load(path); ini.Set("RenoDX.MFGUnlock", "DynamicLiveMaxMultiplier", selected); ini.Save(path);
        ini = IniDocument.Load(path); Configuration(4).ApplyOwned(ini, receipt); ini.Save(path);
        Check(IniDocument.Load(path).Get("RenoDX.MFGUnlock", "DynamicLiveMaxMultiplier") == selected);
    }
    ini = IniDocument.Load(path); ini.RemoveOwnedEdits(receipt, _ => { }); ini.Save(path);
    Check(IniDocument.Load(path).Get("RenoDX.MFGUnlock", "DynamicLiveMaxMultiplier") == "0");
}));
Console.WriteLine($"RESULT: {passed} passed, {failed} failed. Temporary JSON/INI and deployment-plan checks only; no game or driver changes.");
try { Directory.Delete(root, true); } catch { Console.WriteLine("Retained fixture directory: " + root); }
return failed == 0 ? 0 : 1;
