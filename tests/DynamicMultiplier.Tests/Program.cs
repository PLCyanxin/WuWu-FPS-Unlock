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
        Check(IniDocument.Load(path).Get("RenoDX.MFGUnlock", "DynamicMaxMultiplier") == "0");
    }));
await Test("Dynamic 4x requests native Dynamic with ForceMultiplier zero", () => Sync(() =>
{
    var ini = IniDocument.Load(NewPath("ReShade.ini")); var receipt = new DeploymentReceipt();
    Configuration(4).ApplyOwned(ini, receipt);
    Check(ini.Get("RenoDX.MFGUnlock", "DynamicMFG") == "1");
    Check(ini.Get("RenoDX.MFGUnlock", "ForceMultiplier") == "0");
    Check(ini.Get("RenoDX.MFGUnlock", "DynamicMaxMultiplier") == "4");
    Check(ini.Get("RenoDX.MFGUnlock", "RuntimeSelectionMode") == "1");
    Check(receipt.IniEdits.Single(e => e.Key == "DynamicMaxMultiplier").Written == "4");
}));
await Test("Fixed writes cap zero and preserves existing fixed multiplier", () => Sync(() =>
{
    var ini = IniDocument.Load(NewPath("ReShade.ini")); Configuration(6, false).ApplyOwned(ini, new());
    Check(ini.Get("RenoDX.MFGUnlock", "DynamicMFG") == "0" && ini.Get("RenoDX.MFGUnlock", "ForceMultiplier") == "3");
    Check(ini.Get("RenoDX.MFGUnlock", "DynamicMaxMultiplier") == "0");
}));
await Test("unknown driver and disabled preference preserve existing mode selection", () => Sync(() =>
{
    Check(!MfgDeploymentConfiguration.Create(new(), new(), Hardware(null)).DynamicEnabled);
    Check(!MfgDeploymentConfiguration.Create(new(), new() { PreferDynamic = false }, Hardware(59541)).DynamicEnabled);
}));
await Test("preview reports configured cap and distinguishes Fixed and no override", () => Sync(() =>
{
    Check(Configuration(4).PreviewText.Contains("Dynamic MFG：将启用") && Configuration(4).PreviewText.Contains("最高 4x"));
    Check(Configuration(0).PreviewText.Contains("NVIDIA 默认 / 不限制"));
    Check(Configuration(4, false).PreviewText.Contains("不生效（Fixed）"));
    Check(Configuration(4).PreviewText.Contains("完全重启游戏") && Configuration(4).PreviewText.Contains("不代表运行时上限已生效"));
}));
await Test("cap cleanup restores the original value and unrelated ReShade content", () => Sync(() =>
{
    string path = NewPath("ReShade.ini");
    File.WriteAllText(path, ";user comment\r\n[GENERAL]\r\nPresetPath=user.ini\r\n[RenoDX.MFGUnlock]\r\nDynamicMaxMultiplier=6\r\nMaxCount=5\r\nDynamicTargetFPS=165\r\nLatencyGuard=1\r\n");
    var ini = IniDocument.Load(path); var receipt = new DeploymentReceipt();
    Configuration(4).ApplyOwned(ini, receipt); ini.Save(path);
    var cap = receipt.IniEdits.Single(e => e.Key == "DynamicMaxMultiplier"); Check(cap.Previous == "6" && cap.Written == "4");
    Configuration(3).ApplyOwned(ini, receipt); Check(cap.Previous == "6" && cap.Written == "3");
    ini.RemoveOwnedEdits(receipt, _ => { }); ini.Save(path);
    Check(ini.Get("RenoDX.MFGUnlock", "DynamicMaxMultiplier") == "6");
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
await Test("Fixed transition restores pre-deployment cap on cleanup", () => Sync(() =>
{
    var ini = IniDocument.Load(NewPath("ReShade.ini")); ini.Set("RenoDX.MFGUnlock", "DynamicMaxMultiplier", "5");
    var receipt = new DeploymentReceipt(); Configuration(4).ApplyOwned(ini, receipt); Configuration(4, false).ApplyOwned(ini, receipt);
    Check(ini.Get("RenoDX.MFGUnlock", "DynamicMaxMultiplier") == "0");
    ini.RemoveOwnedEdits(receipt, _ => { }); Check(ini.Get("RenoDX.MFGUnlock", "DynamicMaxMultiplier") == "5");
}));
foreach (bool dynamic in new[] { true, false })
    await Test("real deployment approval changes with cap in " + (dynamic ? "Dynamic" : "Fixed"), async () =>
    {
        string manifest = NewPath("manifest.json"); File.WriteAllText(manifest, "{}");
        var settings = new UserSettings { GameRoot = Path.GetDirectoryName(manifest)!, GameExe = Path.Combine(Path.GetDirectoryName(manifest)!, "Shipping.exe"), PackageManifest = manifest, DynamicMaxMultiplier = 4 };
        var info = new ReShadeInfo("fixture", null, Path.Combine(settings.GameRoot, "ReShade.ini"), settings.GameRoot, "fixture");
        var method = typeof(DeploymentService).GetMethod("PlanFingerprint", BindingFlags.NonPublic | BindingFlags.Static)!;
        Task<string> Fingerprint(MfgDeploymentConfiguration config) => (Task<string>)method.Invoke(null, [settings, new List<PlannedFile>(), info, config, CancellationToken.None])!;
        string approved = await Fingerprint(Configuration(4, dynamic));
        Check(approved == await Fingerprint(Configuration(4, dynamic)), "Identical plans must remain stable");
        settings.DynamicMaxMultiplier = 6;
        Check(approved != await Fingerprint(Configuration(settings.DynamicMaxMultiplier, dynamic)), "Changed cap must invalidate approval");
    });
Console.WriteLine($"RESULT: {passed} passed, {failed} failed. Temporary JSON/INI and deployment-plan checks only; no game or driver changes.");
try { Directory.Delete(root, true); } catch { Console.WriteLine("Retained fixture directory: " + root); }
return failed == 0 ? 0 : 1;
