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
 latest=Make("update-backup-20260107-000000-a");
 DurableJson.Save(Path.Combine(latest,"snapshot.state.json"),JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(Path.Combine(latest,"snapshot.json"))));
 File.WriteAllText(Path.Combine(latest,"snapshot.sha256"),"torn legacy checksum");
 Check(RollbackBackupCatalog.Find(root)?.Directory==latest,"atomic snapshot remains authoritative when legacy checksum is torn");
 void Journal(string phase,string? owner=null)=>DurableJson.Save(Path.Combine(latest,"transaction.json"),new{Schema=1,Root=owner??root,Phase=phase,Intent="WuWaFpsUnlock.exe"});
 foreach(string phase in new[]{"Applying","Recovering"}){Journal(phase);Check(RollbackBackupCatalog.FindInterrupted(root)==latest,"interrupted phase detected: "+phase);}
 Journal("Recovered");Check(RollbackBackupCatalog.FindInterrupted(root) is null,"recovered journal no longer blocks launch");
 Journal("Applying",Path.GetTempPath());bool wrongOwner=false;try{RollbackBackupCatalog.FindInterrupted(root);}catch(InvalidDataException){wrongOwner=true;}
 Check(wrongOwner,"interrupted journal with wrong installation fails closed");Directory.Delete(latest,true);
 using var cts=new CancellationTokenSource();cts.Cancel();bool cancelled=false;try{RollbackBackupCatalog.Find(root,cts.Token);}catch(OperationCanceledException){cancelled=true;}
 Check(cancelled,"scan cancellable");
 Console.WriteLine($"{passed} passed");
}finally{Directory.Delete(root,true);}


