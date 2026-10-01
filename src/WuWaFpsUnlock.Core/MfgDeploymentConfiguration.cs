using System.Globalization;

namespace WuWaFpsUnlock.Core;

public sealed record MfgDeploymentConfiguration(bool DynamicEnabled, int FixedMultiplier)
{
    public int Mode {get;init;}=DynamicEnabled?2:1;
    public bool SavedMode {get;init;}
    public bool DynamicDriverReady {get;init;}=DynamicEnabled;
    public static MfgDeploymentConfiguration Create(UserSettings settings, PayloadManifest manifest, HardwareInfo hardware, IniDocument? ini=null)
    {
        bool ready=hardware.Driver is int driver && driver>=manifest.DynamicMinimumDriver;
        bool dynamic=manifest.PreferDynamic&&ready;int mode=dynamic?2:1,fixedMultiplier=manifest.FixedMultiplier;
        string? saved=ini?.Get("RenoDX.MFGUnlock","WuWaFrameGenerationModeV1");
        if(saved is not null)
        {
            if(!int.TryParse(saved,NumberStyles.Integer,CultureInfo.InvariantCulture,out mode)||mode is <0 or >3)
                throw new InvalidDataException("已保存的帧生成模式无法识别，请先在游戏内确认设置。");
            if((mode==1&&ini?.Get("WuWa.DynamicMax","FixedFrameGenerationEnabled")=="0")||
               (mode==2&&ini?.Get("WuWa.DynamicMax","DynamicFrameGenerationChoiceV2")=="0"))mode=3;
            string? lastFixed=ini?.Get("RenoDX.MFGUnlock","WuWaLastFixedMultiplier");
            if(!int.TryParse(lastFixed,NumberStyles.Integer,CultureInfo.InvariantCulture,out fixedMultiplier)||fixedMultiplier is <2 or >6)
            {
                string? force=ini?.Get("RenoDX.MFGUnlock","ForceMultiplier");
                if(!int.TryParse(force,NumberStyles.Integer,CultureInfo.InvariantCulture,out fixedMultiplier)||fixedMultiplier is <2 or >6)fixedMultiplier=4;
            }
        }
        return new(mode==2,fixedMultiplier){Mode=mode,SavedMode=saved is not null,DynamicDriverReady=ready};
    }

    public string PreviewText => "帧生成模式："+(Mode switch{0=>"游戏默认",1=>$"固定倍率（Fixed）{FixedMultiplier}x",2=>"Dynamic",_=>"关闭"})+
        (SavedMode?"（保留游戏内已保存的选择）":"（首次部署默认值）")+
        (Mode==2?DynamicDriverReady?"；Dynamic 驱动条件已满足，运行时支持需在游戏内确认。":"；当前驱动条件未满足，保留选择但不保证可用。":"");

    public void ApplyOwned(IniDocument ini, DeploymentReceipt receipt, IEnumerable<KeyValuePair<string, string>>? additionalSettings = null)
    {
        foreach (var pair in additionalSettings ?? [])
            if (!new[]{"DynamicMaxMultiplier","DynamicMFG","ForceMultiplier","WuWaFrameGenerationModeV1","WuWaLastFixedMultiplier"}.Contains(pair.Key,StringComparer.OrdinalIgnoreCase))
                ini.ApplyOwned("RenoDX.MFGUnlock", pair.Key, pair.Value, receipt);
        ini.ApplyOwned("RenoDX.MFGUnlock", "Enabled", "1", receipt);
        ini.ApplyOwned("RenoDX.MFGUnlock", "DynamicMFG", DynamicEnabled ? "1" : "0", receipt);
        ini.ApplyOwned("RenoDX.MFGUnlock", "ForceMultiplier", Mode==1 ? FixedMultiplier.ToString(CultureInfo.InvariantCulture) : "0", receipt);
        // The user's mode is a preference, not an owned deployment edit.
        if(SavedMode)ini.Set("RenoDX.MFGUnlock","WuWaFrameGenerationModeV1",Mode.ToString(CultureInfo.InvariantCulture));
        // Retired startup-only setting must not override the add-on runtime selection.
        ini.RemoveKey("RenoDX.MFGUnlock", "DynamicMaxMultiplier");
        receipt.IniEdits.RemoveAll(edit => edit.Section.Equals("RenoDX.MFGUnlock", StringComparison.OrdinalIgnoreCase)
            && edit.Key.Equals("DynamicMaxMultiplier", StringComparison.OrdinalIgnoreCase));
        if (DynamicEnabled) ini.ApplyOwned("RenoDX.MFGUnlock", "RuntimeSelectionMode", "1", receipt);
    }
}
