using WuWaFpsUnlock.Core;

internal static partial class Program
{
    sealed record UpdateJournal(int Schema,string Root,string Phase,string? Intent);
    static void WriteUpdateJournal(string backup,string phase,string? intent=null) =>
        DurableJson.Save(Scoped(backup,"transaction.json"),new UpdateJournal(1,Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(backup))!,phase,intent));

    static string? RecoverInterruptedUpdates(string root)
    {
        string? finished=null;
        foreach(string backup in Directory.EnumerateDirectories(root,"update-backup-*",SearchOption.TopDirectoryOnly))
        {
            string journalPath=Scoped(backup,"transaction.json");
            if(!File.Exists(journalPath))continue; // Historical backups have no interrupted-write proof.
            var journal=DurableJson.Read<UpdateJournal>(journalPath);
            if(journal.Phase is "Prepared" or "Applying" or "Recovering")
            {
                bool complete=ReadSnapshot(backup).Complete;
                RecoverInterruptedBackup(backup);
                if(complete)finished=backup;
            }
        }
        return finished;
    }
    static int RecoverCommand(string input)
    {
        string backup=Path.TrimEndingDirectorySeparator(Path.GetFullPath(input));
        var snapshot=ReadSnapshot(backup);
        using var installationLock=AcquireInstallationLock(snapshot.Root);
        RecoverInterruptedBackup(backup);
        if(snapshot.Complete)
        {
            RefreshDesktopShortcut(snapshot.Root);PrunePreviousBackups(snapshot.Root,backup);
            completedUpdateLauncher=Scoped(snapshot.Root,"WuWaFpsUnlock.exe");
            Console.WriteLine("上次更新已完成，已确认文件完整。请点击重新部署。");
        }
        else Console.WriteLine("已恢复更新前的启动器文件。请重新运行更新包；更新完成后请点击重新部署。");
        return 0;
    }
    static void RecoverInterruptedBackup(string backup,Action? guard=null)
    {
        var snapshot=ReadSnapshot(backup);
        var journal=DurableJson.Read<UpdateJournal>(Scoped(backup,"transaction.json"));
        if(journal.Schema!=1||!Path.GetFullPath(journal.Root).Equals(Path.GetFullPath(snapshot.Root),StringComparison.OrdinalIgnoreCase)
            ||journal.Phase is not ("Prepared" or "Applying" or "Recovering"))throw new InvalidDataException("没有可安全恢复的未完成更新事务。");
        if(File.Exists(Scoped(backup,RollbackCompleted)))throw new InvalidDataException("已使用的回退备份不能用于中断恢复。");
        void RequireStopped(){if(guard is not null)guard();else{RejectRunning(Scoped(snapshot.Root,"WuWaFpsUnlock.exe"));RequireGameStopped();}}
        RequireStopped();
        if(snapshot.Complete)
        {
            foreach(var file in snapshot.Updated)EnsureUnchanged(Scoped(snapshot.Root,file.Relative),file.NewHash);
            // Repair compatibility metadata before marking the atomic commit fully finished.
            WriteSnapshot(backup,snapshot);
            WriteUpdateJournal(backup,"Completed");return;
        }
        var entries=new List<Entry>();
        // Validate every installed file before the first restoration. A file
        // changed by another program is never treated as an interrupted write.
        foreach(var file in snapshot.Updated)
        {
            string target=Scoped(snapshot.Root,file.Relative);
            if(Directory.Exists(target))throw new IOException("恢复目标被目录占用："+target);
            string? current=File.Exists(target)?FileHash(target):null;
            if(current!=file.OldHash&&current!=file.NewHash)throw new IOException("更新中断后文件已被其他程序更改，已停止恢复："+target);
            entries.Add(new(file.Relative,"",target,file.NewHash){OldHash=file.OldHash,Applied=current==file.NewHash&&current!=file.OldHash,
                Backup=file.OldHash is null?null:Scoped(backup,"snapshot/"+file.Relative)});
        }
        WriteUpdateJournal(backup,"Recovering");
        foreach(var entry in entries.Where(e=>e.Applied).Reverse())
        {
            RequireStopped();
            if(!RestoreFailedUpdate([entry],backup,requireStopped:RequireStopped))throw new IOException("中断恢复未完成，备份和事务记录已保留。");
        }
        foreach(var entry in entries)EnsureUnchanged(entry.Target,entry.OldHash);
        WriteUpdateJournal(backup,"Recovered");
        Console.WriteLine("已恢复中断的更新："+backup);
    }
}
