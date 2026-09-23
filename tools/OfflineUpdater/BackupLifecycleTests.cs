internal static partial class Program
{
    static void BackupLifecycleTests(Action<string,Action> test)
    {
        void Check(bool ok){if(!ok)throw new Exception("backup lifecycle assertion failed");}
        void Reject(Action action){try{action();}catch(Exception error) when(error is IOException or InvalidDataException){return;}throw new Exception("Expected refusal");}
        void Fixture(Action<string,Func<string,bool,string>> action)
        {
            string root=Path.Combine(Path.GetTempPath(),"WuWaBackup-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try
            {
                Write(root,"WuWaFpsUnlock.exe","previous launcher");Write(root,"data/settings.json","current settings");
                string Backup(string suffix,bool complete)
                {
                    string backup=Scoped(root,"update-backup-"+suffix);Directory.CreateDirectory(backup);
                    string old=FileHash(Scoped(root,"WuWaFpsUnlock.exe"));
                    var snapshot=CaptureSnapshot(root,backup,Scoped(root,"update"),[new("WuWaFpsUnlock.exe","unused",Scoped(root,"WuWaFpsUnlock.exe"),old){OldHash=old}]) with{Complete=complete};
                    WriteSnapshot(backup,snapshot);Write(backup,"WuWaUpdater.exe","inert updater");Write(backup,"rollback.cmd","inert rollback");
                    return backup;
                }
                action(root,Backup);
            }
            finally{NoLinks(root);Directory.Delete(root,true);}
        }
        test("successful complete backup supersedes previous backup",()=>Fixture((root,create)=>
        {
            var old=create("old",true);var current=create("new",true);PrunePreviousBackups(root,current);
            Check(!Directory.Exists(old)&&Directory.Exists(current));Check(File.ReadAllText(Scoped(root,"data/settings.json"))=="current settings");
        }));
        test("incomplete replacement never removes previous backup",()=>Fixture((root,create)=>
        {
            var old=create("old",true);var current=create("new",false);Reject(()=>PrunePreviousBackups(root,current));Check(Directory.Exists(old));
        }));
        test("corrupt replacement never removes previous backup",()=>Fixture((root,create)=>
        {
            var old=create("old",true);var current=create("new",true);Write(current,"snapshot/WuWaFpsUnlock.exe","corrupt");Reject(()=>PrunePreviousBackups(root,current));Check(Directory.Exists(old));
        }));
        test("cleanup preserves unknown files and incomplete old recovery",()=>Fixture((root,create)=>
        {
            var unknown=create("unknown",true);Write(unknown,"personal.txt","keep");var partial=create("partial",false);var current=create("new",true);
            PrunePreviousBackups(root,current);Check(File.Exists(Scoped(unknown,"personal.txt"))&&Directory.Exists(partial));
        }));
        test("completed rollback marks backup unavailable",()=>Fixture((root,create)=>
        {
            var backup=create("old",true);Write(root,"WuWaFpsUnlock.exe","new launcher");var snapshot=ReadSnapshot(backup);
            RequireUnusedBackup(backup,snapshot);RestoreSnapshot(backup,snapshot,()=>{},completed:()=>MarkRollbackCompleted(backup));
            Check(File.ReadAllText(Scoped(root,"WuWaFpsUnlock.exe"))=="previous launcher"&&!ReadSnapshot(backup).Complete);Reject(()=>RequireUnusedBackup(backup,ReadSnapshot(backup)));
        }));
        test("failed rollback leaves backup reusable",()=>Fixture((root,create)=>
        {
            var backup=create("old",true);Write(root,"WuWaFpsUnlock.exe","new launcher");var snapshot=ReadSnapshot(backup);
            Reject(()=>RestoreSnapshot(backup,snapshot,()=>{},_=>throw new IOException("injected failure"),()=>MarkRollbackCompleted(backup)));
            RequireUnusedBackup(backup,ReadSnapshot(backup));Check(File.ReadAllText(Scoped(root,"WuWaFpsUnlock.exe"))=="new launcher");
        }));
        test("failed completion marker restores pre-rollback state",()=>Fixture((root,create)=>
        {
            var backup=create("old",true);Write(root,"WuWaFpsUnlock.exe","new launcher");var snapshot=ReadSnapshot(backup);
            Reject(()=>RestoreSnapshot(backup,snapshot,()=>{},completed:()=>throw new IOException("marker write denied")));
            RequireUnusedBackup(backup,ReadSnapshot(backup));Check(File.ReadAllText(Scoped(root,"WuWaFpsUnlock.exe"))=="new launcher");
        }));
        test("installation operations cannot overlap",()=>Fixture((root,create)=>
        {
            using var held=AcquireInstallationLock(root);Reject(()=>AcquireInstallationLock(root));
        }));
        test("next successful update clears consumed backup",()=>Fixture((root,create)=>
        {
            var old=create("old",true);MarkRollbackCompleted(old);var current=create("new",true);PrunePreviousBackups(root,current);Check(!Directory.Exists(old));
        }));
    }
}
