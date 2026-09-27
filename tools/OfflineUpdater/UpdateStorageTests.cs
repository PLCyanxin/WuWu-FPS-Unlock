internal static partial class Program
{
    static void UpdateStorageTests(Action<string,Action> test)
    {
        void Check(bool value,string message){if(!value)throw new Exception(message);}
        void Fixture(Action<string,string,List<Entry>,Action> run)
        {
            string root=Path.Combine(Path.GetTempPath(),"WuWaStorage-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            var entries=new List<Entry>();
            void Dispose(){foreach(var entry in entries){entry.SourceHandle?.Dispose();entry.TargetHandle?.Dispose();}}
            try
            {
                var sizes=new[]{("WuWaFpsUnlock.exe",8*1024*1024),("components/fps/ww_plugin_base.dll",2*1024*1024),("payload/files/addon/renodx-mfgunlock.addon64",64*1024),("licenses/new.txt",0)};
                foreach(var (relative,size) in sizes)
                {
                    string target=Scoped(root,relative),source=Scoped(root,"update/update-payload/"+relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(source)!);File.WriteAllBytes(source,Enumerable.Repeat((byte)2,Math.Max(size,32)).ToArray());
                    if(size>0){Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.WriteAllBytes(target,Enumerable.Repeat((byte)1,size).ToArray());}
                    var entry=new Entry(relative,source,target,FileHash(source)){SourceHandle=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read)};
                    if(size>0){entry.TargetHandle=new FileStream(target,FileMode.Open,FileAccess.ReadWrite,FileShare.None);entry.OldHash=Hash(entry.TargetHandle);}
                    entries.Add(entry);
                }
                Write(root,"data/settings.json",new string('U',1024*1024));Write(root,"payload/personal.bin","user material");
                Directory.CreateDirectory(Scoped(root,"payload/empty"));
                string backup=Scoped(root,"update-backup-fixture");Directory.CreateDirectory(backup);
                run(root,backup,entries,Dispose);
            }
            finally{Dispose();NoLinks(root);Directory.Delete(root,true);}
        }
        long Bytes(string root)=>Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories).Sum(p=>new FileInfo(p).Length);
        foreach(int failAfter in new[]{0,3,4})
        test(failAfter>0?"shared snapshot automatically restores update failing after file "+failAfter:"shared snapshot supports update and later rollback without duplicate originals",()=>Fixture((root,backup,entries,dispose)=>
        {
            bool fail=failAfter>0;
            var snapshot=StageUpdateSnapshot(root,backup,Scoped(root,"update"),entries);
            Check(!Directory.Exists(Scoped(backup,"original")),"redundant original directory created");
            Check(entries.Where(e=>e.OldHash is not null).All(e=>e.Backup==Scoped(backup,"snapshot/"+e.Relative)),"recovery paths do not use verified snapshot");
            Check(snapshot.Files.Any(f=>f.Relative=="data/settings.json")&&Directory.Exists(Scoped(backup,"snapshot/payload/empty")),"full user snapshot lost");
            long staged=Bytes(root),peak=staged;
            void Measure(){peak=Math.Max(peak,Bytes(root));}
            try{ApplyUpdateEntries(entries,n=>{Measure();if(fail&&n==failAfter)throw new IOException("fixture interruption");});}
            catch(IOException) when(fail){dispose();Check(RestoreFailedUpdate(entries,backup,Measure),"automatic restore failed");Measure();}
            dispose();
            if(fail)
            {
                Check(!ReadSnapshot(backup).Complete,"failed update became usable rollback");
                foreach(var entry in entries)Check(entry.OldHash is null?!File.Exists(entry.Target):FileHash(entry.Target)==entry.OldHash,"old target not recovered");
            }
            else
            {
                WriteSnapshot(backup,snapshot with{Complete=true});
                Check(entries.All(e=>FileHash(e.Target)==e.NewHash),"update mismatch");
                // Legacy Schema-1 rollback reads snapshot, not original: no schema change.
                var ready=ReadSnapshot(backup);RequireUnusedBackup(backup,ready);
                long completed=Bytes(root);
                RestoreSnapshot(backup,ready,()=>{});
                foreach(var entry in entries)Check(entry.OldHash is null?!File.Exists(entry.Target):FileHash(entry.Target)==entry.OldHash,"manual rollback mismatch");
                Console.WriteLine($"STORAGE success completed_bytes={completed}");
            }
            Check(File.ReadAllText(Scoped(root,"data/settings.json"))==new string('U',1024*1024),"current data changed");
            long duplicateBytes=entries.Where(e=>e.OldHash is not null).Sum(e=>new FileInfo(Scoped(backup,"snapshot/"+e.Relative)).Length);
            Console.WriteLine($"STORAGE {(fail?"failed-auto-restore":"success")} staged_bytes={staged} observed_peak_bytes={peak} previous_layout_extra_bytes={duplicateBytes} previous_layout_peak_bytes={peak+duplicateBytes}");
        }));
        test("staging failure occurs before any installation write",()=>Fixture((root,backup,entries,dispose)=>
        {
            entries[0].SourceHandle!.Dispose();
            try{StageUpdateSnapshot(root,backup,Scoped(root,"update"),entries);throw new Exception("expected failure");}catch(ObjectDisposedException){}
            dispose();Check(entries.All(e=>!e.Applied),"unexpected target write");
            Check(entries.Where(e=>e.OldHash is not null).All(e=>FileHash(e.Target)==e.OldHash),"preflight changed target");
        }));
        test("snapshot failure leaves installation unchanged",()=>Fixture((root,backup,entries,dispose)=>
        {
            using(var locked=new FileStream(Scoped(root,"data/settings.json"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))
            {
                try{StageUpdateSnapshot(root,backup,Scoped(root,"update"),entries);throw new Exception("expected failure");}catch(IOException){}
            }
            dispose();Check(entries.All(e=>!e.Applied),"snapshot failure wrote targets");
            Check(entries.Where(e=>e.OldHash is not null).All(e=>FileHash(e.Target)==e.OldHash),"snapshot failure changed installation");
        }));
        test("automatic restore refuses corrupted shared backup",()=>Fixture((root,backup,entries,dispose)=>
        {
            StageUpdateSnapshot(root,backup,Scoped(root,"update"),entries);
            ApplyUpdateEntries(entries);dispose();
            Write(backup,"snapshot/WuWaFpsUnlock.exe","corrupt");
            Check(!RestoreFailedUpdate(entries,backup),"corrupt backup reported complete recovery");
            Check(FileHash(entries[0].Target)==entries[0].NewHash,"corrupt bytes written over installed exe");
        }));
        test("legacy snapshot with original copies still validates and rolls back",()=>Fixture((root,backup,entries,dispose)=>
        {
            foreach(var entry in entries.Where(e=>e.TargetHandle is not null))CopyFromHandle(entry.TargetHandle!,Scoped(backup,"original/"+entry.Relative),entry.OldHash!);
            var snapshot=StageUpdateSnapshot(root,backup,Scoped(root,"update"),entries);ApplyUpdateEntries(entries);dispose();
            WriteSnapshot(backup,snapshot with{Complete=true});RestoreSnapshot(backup,ReadSnapshot(backup),()=>{});
            Check(entries.Where(e=>e.OldHash is not null).All(e=>FileHash(e.Target)==e.OldHash),"legacy backup broke");
        }));
    }
}
