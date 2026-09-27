using System.Diagnostics;
using System.Text;
using WuWaFpsUnlock.Services;
var directory=Path.Combine(AppContext.BaseDirectory,"fixture");Directory.CreateDirectory(directory);
byte[] Image(int size,string marker,bool hasRegister=true,bool hasVersion=true){
 var b=new byte[size];void U32(int o,uint v)=>BitConverter.GetBytes(v).CopyTo(b,o);void U16(int o,ushort v)=>BitConverter.GetBytes(v).CopyTo(b,o);
 b[0]=77;b[1]=90;U32(0x3c,0x80);U32(0x80,0x4550);U16(0x86,1);U16(0x94,0xf0);U16(0x98,0x20b);U32(0x98+112,0x1000);
 int sec=0x98+0xf0;U32(sec+8,(uint)size-0x200);U32(sec+12,0x1000);U32(sec+16,(uint)size-0x200);U32(sec+20,0x200);
 U32(0x200+24,2);U32(0x200+32,0x1040);U32(0x240,0x1060);U32(0x244,0x10a0);
 Encoding.ASCII.GetBytes(hasVersion?"ReShadeVersion":"OtherVersion").CopyTo(b,0x260);Encoding.ASCII.GetBytes(hasRegister?"ReShadeRegisterAddon":"OtherRegister").CopyTo(b,0x2a0);
 Encoding.ASCII.GetBytes(marker).CopyTo(b,size-1024);return b;
}
string Put(string name,byte[] b){string p=Path.Combine(directory,name);File.WriteAllBytes(p,b);return p;}
int passed=0;void Check(bool v,string name){if(!v)throw new Exception(name);Console.WriteLine("PASS "+name);passed++;}
foreach(var item in new[]{("full","Loading add-on from",true,true,"FullCandidate"),("limited","only limited add-on functionality Loading add-on from",true,true,"Limited"),("unknown","none",true,true,"UnknownReShade"),("notreshade","Loading add-on from",true,false,"Unknown"),("noreg","Loading add-on from",false,true,"UnknownReShade")}){
 var p=Put(item.Item1,Image(4096,item.Item2,item.Item3,item.Item4));Check(PeInspector.AddonBuild(p)==item.Item5&&BaselinePeInspector.AddonBuild(p)==item.Item5,item.Item1);
}
foreach(var item in new[]{("empty",Array.Empty<byte>()),("truncated",new byte[64]),("badRva",Image(4096,"Loading add-on from"))}){
 if(item.Item1=="badRva")BitConverter.GetBytes(uint.MaxValue).CopyTo(item.Item2,0x240);
 var p=Put(item.Item1,item.Item2);bool oldRejected=false,newRejected=false;
 try{BaselinePeInspector.AddonBuild(p);}catch{oldRejected=true;}
 try{PeInspector.AddonBuild(p);}catch{newRejected=true;}
 Check(oldRejected&&newRejected,item.Item1+" rejected by both");
}
var oversize=Path.Combine(directory,"oversize");using(var f=File.Create(oversize))f.SetLength(128L*1024*1024+1);
bool rejected=false;try{PeInspector.AddonBuild(oversize);}catch(InvalidDataException){rejected=true;}Check(rejected,"size limit retained");
var large=Put("large",Image(16*1024*1024,"Loading add-on from"));
void Measure(string name,Func<string,string> inspect){inspect(large);GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();long before=GC.GetAllocatedBytesForCurrentThread();var cpu=Process.GetCurrentProcess().TotalProcessorTime;var timer=Stopwatch.StartNew();for(int i=0;i<12;i++)Check(inspect(large)=="FullCandidate",name+" result");timer.Stop();Console.WriteLine($"METRIC {name}: 12x16MiB allocation={GC.GetAllocatedBytesForCurrentThread()-before} elapsedMs={timer.Elapsed.TotalMilliseconds:F2} processCpuMs={(Process.GetCurrentProcess().TotalProcessorTime-cpu).TotalMilliseconds:F2}");}
Measure("baseline",BaselinePeInspector.AddonBuild);Measure("current",PeInspector.AddonBuild);Console.WriteLine($"{passed} assertions passed; fixture-only, no game execution");
