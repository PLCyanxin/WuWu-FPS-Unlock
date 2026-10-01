using WuWaFpsUnlock.Core;
internal static partial class Program
{
    static void AuditRecoveryTests(Action<string,Action> test)
    {
        void Check(bool ok){if(!ok)throw new Exception("audit recovery fixture failed");}
        void Reject(Action run){try{run();}catch(Exception e) when(e is IOException or InvalidDataException){return;}throw new Exception("Expected recovery rejection");}
        (string Root,string Backup,Snapshot Snapshot) Fixture(string parent)
        {
            string root=Path.Combine(parent,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            Write(root,"WuWaFpsUnlock.exe","old app");Write(root,"components/fps/ww_plugin_base.dll","old core");
            string backup=Scoped(root,"update-backup-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(backup);
            var entries=new List<Entry>();
            foreach(string relative in new[]{"WuWaFpsUnlock.exe","components/fps/ww_plugin_base.dll"})
            {
                string source=Scoped(root,"update/"+relative);Directory.CreateDirectory(Path.GetDirectoryName(source)!);File.WriteAllText(source,"new "+relative);
                entries.Add(new(relative,source,Scoped(root,relative),FileHash(source)){OldHash=FileHash(Scoped(root,relative))});
            }
            var snapshot=CaptureSnapshot(root,backup,Scoped(root,"update"),entries);WriteUpdateJournal(backup,"Applying");
            foreach(var entry in entries)File.Copy(entry.Source,entry.Target,true);
            return(root,backup,snapshot);
        }
        string parent=Path.Combine(Path.GetTempPath(),"ww-audit-recovery-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(parent);
        try
        {
            test("interrupted recovery preserves unexpected external target before any restoration",()=>{
                var x=Fixture(parent);Write(x.Root,"components/fps/ww_plugin_base.dll","external changed core");
                Reject(()=>RecoverInterruptedBackup(x.Backup,()=>{}));Check(File.ReadAllText(Scoped(x.Root,"WuWaFpsUnlock.exe")).StartsWith("new ")&&File.ReadAllText(Scoped(x.Root,"components/fps/ww_plugin_base.dll"))=="external changed core");
            });
            test("interrupted recovery refuses corrupt backup and active game guard",()=>{
                var x=Fixture(parent);Reject(()=>RecoverInterruptedBackup(x.Backup,()=>throw new IOException("game running")));Check(File.ReadAllText(Scoped(x.Root,"WuWaFpsUnlock.exe")).StartsWith("new "));
                Write(x.Backup,"snapshot/components/fps/ww_plugin_base.dll","corrupt snapshot");Reject(()=>RecoverInterruptedBackup(x.Backup,()=>{}));Check(File.ReadAllText(Scoped(x.Root,"WuWaFpsUnlock.exe")).StartsWith("new "));
            });
            test("rollback rechecks game guard between actual file replacements",()=>{
                var x=Fixture(parent);WriteSnapshot(x.Backup,x.Snapshot with{Complete=true});bool appeared=false;
                Reject(()=>RestoreSnapshot(x.Backup,ReadSnapshot(x.Backup),()=>{if(appeared)throw new IOException("game appeared");},_=>appeared=true));
                Check(File.ReadAllText(Scoped(x.Root,"components/fps/ww_plugin_base.dll")).StartsWith("new ")&&Directory.EnumerateDirectories(x.Root,"rollback-attempt-*").Any());
            });
            test("completed rollback attempt cleanup retains newest and unfinished evidence",()=>{
                var x=Fixture(parent);WriteSnapshot(x.Backup,x.Snapshot with{Complete=true});RestoreSnapshot(x.Backup,ReadSnapshot(x.Backup),()=>{});
                string first=Directory.EnumerateDirectories(x.Root,"rollback-attempt-*").Single();
                DurableJson.Save(Scoped(first,"completed.state.json"),DurableJson.Read<AttemptCompletion>(Scoped(first,"completed.state.json")));
                File.SetLastWriteTimeUtc(Scoped(first,"completed.state.json"),DateTime.UtcNow.AddMinutes(-1));
                RestoreSnapshot(x.Backup,ReadSnapshot(x.Backup),()=>{});
                string newest=Directory.EnumerateDirectories(x.Root,"rollback-attempt-*").Single(d=>d!=first);
                string unknown=Scoped(x.Root,"rollback-attempt-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(unknown);Write(unknown,"user.txt","keep evidence");
                PruneCompletedRollbackAttempts(x.Root);Check(!Directory.Exists(first)&&Directory.Exists(newest)&&File.ReadAllText(Scoped(unknown,"user.txt"))=="keep evidence");
            });
            test("rollback attempt cleanup preserves unknown directory and tampered evidence",()=>{
                var x=Fixture(parent);WriteSnapshot(x.Backup,x.Snapshot with{Complete=true});RestoreSnapshot(x.Backup,ReadSnapshot(x.Backup),()=>{});
                string older=Directory.EnumerateDirectories(x.Root,"rollback-attempt-*").Single();Directory.CreateDirectory(Scoped(older,"unregistered"));
                File.SetLastWriteTimeUtc(Scoped(older,"completed.state.json"),DateTime.UtcNow.AddMinutes(-1));RestoreSnapshot(x.Backup,ReadSnapshot(x.Backup),()=>{});
                PruneCompletedRollbackAttempts(x.Root);Check(Directory.Exists(Scoped(older,"unregistered")));
            });
        }
        finally{NoLinks(parent);Directory.Delete(parent,true);}
    }
}
