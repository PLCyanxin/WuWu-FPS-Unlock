using System.Security.Cryptography;
using System.Text.Json;
using WuWaFpsUnlock.Core;
string root=Path.Combine(Path.GetTempPath(),"回退测试-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
int passed=0;
void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS "+label);passed++;}
string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
string Make(string name,string? owner=null,bool complete=true,string relative="WuWaFpsUnlock.exe") {
 string b=Path.Combine(root,name);Directory.CreateDirectory(Path.Combine(b,"snapshot"));
 byte[] exe=[1,2,3];File.WriteAllBytes(Path.Combine(b,"snapshot","WuWaFpsUnlock.exe"),exe);
 byte[] manifest=JsonSerializer.SerializeToUtf8Bytes(new {Schema=1,Root=owner??root,Complete=complete,Files=new[]{new {Relative=relative,Hash=Hash(exe),Size=3}},Directories=Array.Empty<string>(),Updated=Array.Empty<string>()});
 File.WriteAllBytes(Path.Combine(b,"snapshot.json"),manifest);File.WriteAllText(Path.Combine(b,"snapshot.sha256"),Hash(manifest));return b;
}
try {
 Check(RollbackBackupCatalog.Find(root) is null,"no backup disabled");
 string old=Make("update-backup-20260101-000000-a");
 Check(RollbackBackupCatalog.Find(root)?.Directory==old,"legacy valid backup accepted without old worker");
 string latest=Make("update-backup-20260102-000000-a");
 Check(RollbackBackupCatalog.Find(root)?.Directory==latest,"latest chosen");
 File.WriteAllText(Path.Combine(latest,"rollback.completed"),"done");
 Check(RollbackBackupCatalog.Find(root) is null,"consumed latest never falls back");
 File.Delete(Path.Combine(latest,"rollback.completed"));File.AppendAllText(Path.Combine(latest,"snapshot.json")," ");
 Check(RollbackBackupCatalog.Find(root) is null,"manifest hash rejects corruption without fallback");
 Directory.Delete(latest,true);latest=Make("update-backup-20260103-000000-a",complete:false);
 Check(RollbackBackupCatalog.Find(root)?.Directory==old,"incomplete attempt does not hide successful backup");
 File.WriteAllText(Path.Combine(latest,"rollback.completed"),"done");
 Check(RollbackBackupCatalog.Find(root) is null,"consumed incomplete metadata blocks fallback");
 Directory.Delete(latest,true);latest=Make("update-backup-20260104-000000-a",owner:Path.GetTempPath());
 Check(RollbackBackupCatalog.Find(root) is null,"other installation rejected");
 Directory.Delete(latest,true);latest=Make("update-backup-20260105-000000-a");File.WriteAllBytes(Path.Combine(latest,"snapshot","WuWaFpsUnlock.exe"),[4,5,6]);
 Check(RollbackBackupCatalog.Find(root) is null,"snapshot content hash rejected");
 Directory.Delete(latest,true);latest=Make("update-backup-20260106-000000-a",relative:"../../outside.exe");
 Check(RollbackBackupCatalog.Find(root) is null,"path escape rejected");
 Directory.Delete(latest,true);
 using var cts=new CancellationTokenSource();cts.Cancel();bool cancelled=false;try{RollbackBackupCatalog.Find(root,cts.Token);}catch(OperationCanceledException){cancelled=true;}
 Check(cancelled,"scan cancellable");
 Console.WriteLine($"{passed} passed");
}finally{Directory.Delete(root,true);}


