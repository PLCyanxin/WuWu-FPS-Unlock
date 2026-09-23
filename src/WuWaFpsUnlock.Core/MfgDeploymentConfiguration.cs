using System.Globalization;

namespace WuWaFpsUnlock.Core;

public sealed record MfgDeploymentConfiguration(bool DynamicEnabled, int FixedMultiplier)
{
    public static MfgDeploymentConfiguration Create(UserSettings settings, PayloadManifest manifest, HardwareInfo hardware)
        => new(manifest.PreferDynamic && hardware.Driver is int driver && driver >= manifest.DynamicMinimumDriver,
            manifest.FixedMultiplier);

    public string PreviewText => "Dynamic MFG：" + (DynamicEnabled ? "将启用（配置请求，运行时支持待游戏内确认）" : "不启用（Fixed）");

    public void ApplyOwned(IniDocument ini, DeploymentReceipt receipt, IEnumerable<KeyValuePair<string, string>>? additionalSettings = null)
    {
        foreach (var pair in additionalSettings ?? [])
            if (!pair.Key.Equals("DynamicMaxMultiplier", StringComparison.OrdinalIgnoreCase))
                ini.ApplyOwned("RenoDX.MFGUnlock", pair.Key, pair.Value, receipt);
        ini.ApplyOwned("RenoDX.MFGUnlock", "Enabled", "1", receipt);
        ini.ApplyOwned("RenoDX.MFGUnlock", "DynamicMFG", DynamicEnabled ? "1" : "0", receipt);
        ini.ApplyOwned("RenoDX.MFGUnlock", "ForceMultiplier", DynamicEnabled ? "0" : FixedMultiplier.ToString(CultureInfo.InvariantCulture), receipt);
        // Retired startup-only setting must not override the add-on runtime selection.
        ini.RemoveKey("RenoDX.MFGUnlock", "DynamicMaxMultiplier");
        receipt.IniEdits.RemoveAll(edit => edit.Section.Equals("RenoDX.MFGUnlock", StringComparison.OrdinalIgnoreCase)
            && edit.Key.Equals("DynamicMaxMultiplier", StringComparison.OrdinalIgnoreCase));
        if (DynamicEnabled) ini.ApplyOwned("RenoDX.MFGUnlock", "RuntimeSelectionMode", "1", receipt);
    }
}
