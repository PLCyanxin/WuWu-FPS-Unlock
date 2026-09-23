using System.Globalization;

namespace WuWaFpsUnlock.Core;

public sealed record MfgDeploymentConfiguration(bool DynamicEnabled, int FixedMultiplier, int RequestedDynamicMaxMultiplier)
{
    public int DynamicMaxMultiplier => DynamicEnabled ? DynamicMultiplierLimit.Normalize(RequestedDynamicMaxMultiplier) : 0;

    public static MfgDeploymentConfiguration Create(UserSettings settings, PayloadManifest manifest, HardwareInfo hardware)
        => new(manifest.PreferDynamic && hardware.Driver is int driver && driver >= manifest.DynamicMinimumDriver,
            manifest.FixedMultiplier, settings.DynamicMaxMultiplier);

    public string PreviewText => "Dynamic MFG：" + (DynamicEnabled ? "将启用（配置请求，运行时支持待游戏内确认）" : "不启用（Fixed）")
        + Environment.NewLine + "Dynamic 最大倍率：" + (!DynamicEnabled ? "不生效（Fixed）"
            : DynamicMaxMultiplier == 0 ? "NVIDIA 默认 / 不限制" : $"最高 {DynamicMaxMultiplier}x")
        + Environment.NewLine + "修改后需要重新部署并完全重启游戏；配置写入不代表运行时上限已生效。";

    public void ApplyOwned(IniDocument ini, DeploymentReceipt receipt)
    {
        ini.ApplyOwned("RenoDX.MFGUnlock", "Enabled", "1", receipt);
        ini.ApplyOwned("RenoDX.MFGUnlock", "DynamicMFG", DynamicEnabled ? "1" : "0", receipt);
        ini.ApplyOwned("RenoDX.MFGUnlock", "ForceMultiplier", DynamicEnabled ? "0" : FixedMultiplier.ToString(CultureInfo.InvariantCulture), receipt);
        ini.ApplyOwned("RenoDX.MFGUnlock", "DynamicMaxMultiplier", DynamicMaxMultiplier.ToString(CultureInfo.InvariantCulture), receipt);
        if (DynamicEnabled) ini.ApplyOwned("RenoDX.MFGUnlock", "RuntimeSelectionMode", "1", receipt);
    }
}
