using System.Security.Cryptography;
using System.Text.Json;

namespace WuWaFpsUnlock.Core;

public sealed record BaselineRebuildPlan(string BaselinePath,string ReceiptPath,string BaselineHash,string? ReceiptHash,
    GameFileBaseline Previous,GameFileBaseline Current,DeploymentReceipt? Receipt,List<string> RetiredPaths)
{
    public string Preview => "仅在官方启动器完成游戏修复后使用。以下旧位置不再列为当前安装的必需路径，原记录将归档：\n"+
        string.Join("\n",Previous.Files.Where(f=>!Current.Files.Any(n=>n.RelativePath.Equals(f.RelativePath,StringComparison.OrdinalIgnoreCase))).Select(f=>f.RelativePath))+
        "\n\n当前扫描到的运行库位置：\n"+string.Join("\n",Current.Files.Select(f=>f.RelativePath))+
        "\n\n从活动部署记录归档的已不存在文件：\n"+string.Join("\n",RetiredPaths)+
        "\n\n此操作只重建当前安装的路径记录，不复制或删除游戏文件。请核对以上变化后确认。";
}
public static class BaselineRebuild
{
    public static async Task<BaselineRebuildPlan> PrepareAsync(string baselinePath,string receiptPath,string root,string exe,CancellationToken token=default)
    {
        string oldHash=await SafePaths.HashAsync(baselinePath,token);
        var previous=JsonFiles.Read<GameFileBaseline>(baselinePath);
        GameFileBaselineStore.Check(previous,root,exe); // Validates installation and path identity even if files moved.
        var current=await GameFileBaselineStore.CaptureAsync(root,exe,token);
        string? receiptHash=File.Exists(receiptPath)?await SafePaths.HashAsync(receiptPath,token):null;
        var receipt=receiptHash is null?null:JsonFiles.Read<DeploymentReceipt>(receiptPath);
        var retired=new List<string>();
        if(receipt is not null)
        {
            if(!Path.GetFullPath(receipt.GameRoot).Equals(Path.GetFullPath(root),StringComparison.OrdinalIgnoreCase)||!Path.GetFullPath(receipt.GameExe).Equals(Path.GetFullPath(exe),StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("部署记录与重建安装不一致。");
            foreach(var file in receipt.Files)
            {
                SafePaths.EnsureInside(root,file.Path);SafePaths.EnsureNoLinks(root,file.Path);
                if(!File.Exists(file.Path)&&((file.Kind=="Vendor"&&PackageReader.VendorNames.Contains(Path.GetFileName(file.Path)))
                    ||(file.Kind=="Addon"&&ManagedAddons.Contains(Path.GetFileName(file.Path)))))retired.Add(file.Path);
            }
        }
        if(await SafePaths.HashAsync(baselinePath,token)!=oldHash||(File.Exists(receiptPath)?await SafePaths.HashAsync(receiptPath,token):null)!=receiptHash)
            throw new IOException("路径记录在扫描期间已改变，请重新预览。");
        return new(baselinePath,receiptPath,oldHash,receiptHash,previous,current,receipt,retired);
    }
    public static async Task<string> CommitAsync(BaselineRebuildPlan plan,Action requireStopped,CancellationToken token=default)
    {
        using var baselineLock=new FileStream(plan.BaselinePath+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        requireStopped();
        if(await SafePaths.HashAsync(plan.BaselinePath,token)!=plan.BaselineHash||(File.Exists(plan.ReceiptPath)?await SafePaths.HashAsync(plan.ReceiptPath,token):null)!=plan.ReceiptHash)
            throw new IOException("确认后路径记录已改变，已停止重建。");
        var observed=await GameFileBaselineStore.CaptureAsync(plan.Current.GameRoot,plan.Current.GameExe,token);
        string Fingerprint(GameFileBaseline b)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(b.Files.OrderBy(f=>f.RelativePath,StringComparer.OrdinalIgnoreCase))));
        if(Fingerprint(observed)!=Fingerprint(plan.Current))throw new IOException("确认后游戏运行库位置或内容已改变，请重新预览。");
        foreach(string path in plan.RetiredPaths)if(File.Exists(path))throw new IOException("待归档路径重新出现，请重新预览。");
        requireStopped();token.ThrowIfCancellationRequested();
        string archive=plan.BaselinePath+".history-"+Guid.NewGuid().ToString("N")+".json";
        DurableJson.Save(archive,new{plan.Previous,plan.Receipt,plan.RetiredPaths,RebuiltAt=DateTimeOffset.UtcNow});
        byte[]? oldReceipt=plan.ReceiptHash is null?null:File.ReadAllBytes(plan.ReceiptPath);
        bool receiptWritten=false;
        try
        {
            if(plan.Receipt is not null)
            {
                var receipt=JsonSerializer.Deserialize<DeploymentReceipt>(JsonSerializer.Serialize(plan.Receipt))!;
                receipt.Files.RemoveAll(f=>plan.RetiredPaths.Contains(f.Path,StringComparer.OrdinalIgnoreCase));
                receiptWritten=true;JsonFiles.Save(plan.ReceiptPath,receipt);
            }
            JsonFiles.Save(plan.BaselinePath,observed);
        }
        catch
        {
            // JsonFiles uses atomic replacement; compensate a preceding receipt
            // write if the second metadata write fails. Never touch game bytes.
            if(receiptWritten&&oldReceipt is not null)JsonFiles.Save(plan.ReceiptPath,JsonSerializer.Deserialize<DeploymentReceipt>(oldReceipt,JsonFiles.Options)!);
            throw;
        }
        return archive;
    }
}
