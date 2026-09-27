using WuWaFpsUnlock.Core;
using WuWaFpsUnlock.Services;
int tests=0;
void Check(bool pass,string name){if(!pass)throw new Exception(name);Console.WriteLine("PASS "+name);tests++;}
string Xml(params string[] devices)=>"<DxDiag><DisplayDevices>"+string.Concat(devices)+"</DisplayDevices></DxDiag>";
string Device(string name,string field)=>$"<DisplayDevice><CardName>{name}</CardName><HardwareSchedulingAttributes>{field}</HardwareSchedulingAttributes></DisplayDevice>";
const string gpu="NVIDIA GeForce RTX 4080 Laptop GPU";
const string on="DriverSupportState:Stable Enabled:True ",off="DriverSupportState:AlwaysOff Enabled:False ";
Check(EnvironmentStatus.WindowsName(10,0,22631,4602,"Windows 10 Pro","Client","23H2")=="Windows 11 Pro 23H2（版本 22631.4602）","Windows11 marketing name with actual build and revision");
Check(EnvironmentStatus.WindowsName(10,0,22631,1,"Windows 11 Pro","Client","23H2").StartsWith("Windows 11 Pro"),"already correct product keeps edition");
Check(EnvironmentStatus.WindowsName(10,0,19045,1,"Windows 10 Pro","Client","22H2").StartsWith("Windows 10 Pro"),"Windows10 retained");
Check(EnvironmentStatus.WindowsName(10,0,26100,1,"Windows Server 2025","Server","24H2").StartsWith("Windows Server 2025"),"server not misclassified by high build");
Check(!EnvironmentStatus.WindowsName(10,0,26100,1,null,null,null).Contains("Windows 11"),"unknown OS product stays conservative");
Check(EnvironmentStatus.ParseDxDiagHags("<Other>"+Device(gpu,on)+"</Other>",gpu)==null,"unknown XML schema not treated as system report");
Check(EnvironmentStatus.ParseDxDiagHags(Xml(Device("Intel UHD",off),Device(gpu,on)),gpu)==true,"NVIDIA selected rather than first integrated GPU");
Check(EnvironmentStatus.ParseDxDiagHags(Xml(Device(gpu,on)),"GeForce RTX 4080 Laptop GPU")==true,"NVAPI NVIDIA prefix normalization");
Check(EnvironmentStatus.ParseDxDiagHags(Xml(Device(gpu,on),Device(gpu,on)),gpu)==true,"matching duplicate outputs agree");
Check(EnvironmentStatus.ParseDxDiagHags(Xml(Device(gpu,on),Device(gpu,off)),gpu)==null,"conflicting outputs remain unknown");
Check(EnvironmentStatus.ParseDxDiagHags(Xml(Device(gpu,on),Device(gpu,"")),gpu)==null,"missing duplicate status not hidden");
Check(EnvironmentStatus.ParseDxDiagHags(Xml(Device("Virtual Display Adapter",on)),gpu)==null,"virtual adapter cannot stand in for NVIDIA");
Check(EnvironmentStatus.ParseDxDiagHags(Xml(Device(gpu,off)),gpu)==false,"runtime false reported independently of default support");
Check(EnvironmentStatus.ParseDxDiagHags(Xml(Device(gpu,"DriverSupportState:Stable")),gpu)==null,"support not mistaken for active scheduling");
Check(EnvironmentStatus.ParseDxDiagHags(Xml(Device(gpu,"Enabled:Maybe")),gpu)==null,"unknown diagnostic field not guessed");
Check(EnvironmentStatus.ParseDxDiagHags(Xml(Device(gpu,"Enabled:True Enabled:False")),gpu)==null,"duplicate enabled token rejected");
Check(EnvironmentStatus.HagsText(null,true).Contains("运行态未确认"),"configured true not runtime true");
Check(EnvironmentStatus.HagsText(true,false).Contains("配置为关闭"),"runtime versus pending config mismatch visible");
bool rejected=false;try{EnvironmentStatus.ParseDxDiagHags("<!DOCTYPE foo [<!ENTITY x SYSTEM 'file:///ignored'>]><DxDiag>&x;</DxDiag>",gpu);}catch(System.Xml.XmlException){rejected=true;}Check(rejected,"DTD rejected");
Console.WriteLine($"{tests} meaningful assertions passed");
if(args.Contains("--live")){
 var timer=System.Diagnostics.Stopwatch.StartNew();var value=EnvironmentProbe.Read(Console.WriteLine);
 Console.WriteLine($"LIVE OS={value.Os}; GPU={value.Gpu}; HAGS={value.Hags}; RegistryConfigured={value.HagsConfigured}; elapsed={timer.Elapsed.TotalSeconds:F1}s");
 timer.Restart();var cached=EnvironmentProbe.Read(Console.WriteLine);Console.WriteLine($"CACHED HAGS={cached.Hags}; elapsed={timer.Elapsed.TotalSeconds:F1}s");
}
