namespace WuWaFpsUnlock.Core;

// A disabled deployment selection does not unload DLLs already present in the game.
public static class DeploymentLaunchGuard
{
    public static async Task RequireReadyAsync(DeploymentReceipt? receipt, bool requireMfg,
        Action<string> log, CancellationToken token = default)
    {
        if(receipt is null)
        {
            if(requireMfg)throw new IOException("已选择多帧生成但尚未部署，请先部署或关闭部署选择。");
            return;
        }
        if(receipt.Status is "Cleaned" or "CleanedWithSkips")
        {
            if(requireMfg)throw new IOException("多帧生成组件已清除，请重新部署或关闭部署选择。");
            if(!receipt.Files.Any(f=>f.Completed && File.Exists(f.Path)))return;
            // Inspect retained components without treating a finished cleanup as an in-progress deployment.
            receipt=new DeploymentReceipt{GameRoot=receipt.GameRoot,GameExe=receipt.GameExe,Status="Deployed",
                Files=receipt.Files.Where(f=>f.Completed && File.Exists(f.Path)).ToList(),IniPath=receipt.IniPath,IniEdits=receipt.IniEdits};
        }
        else if(receipt.Status!="Deployed")throw new IOException("部署维护尚未完成或状态无法识别，请在设置处理后再开始："+receipt.Status);
        var report=await DeploymentIntegrity.InspectAsync(receipt,token);
        foreach(var difference in report.Differences)log(difference.Message);
        if(!report.CanLaunch)throw new IOException("已部署组件的文件检查未通过，请保持游戏关闭并重新部署，或清除插件后使用官方启动器修复。\n"+
            string.Join("\n",report.Differences.Where(d=>d.Severity==DeploymentDifferenceSeverity.Blocking).Select(d=>d.Message)));
    }
}
