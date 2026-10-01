using System.Diagnostics;
using WuWaFpsUnlock.Core;
if(args.Length!=1)throw new ArgumentException("Pass the source-built ReShade 6.8 INI reader fixture.");
string root=Path.Combine(Path.GetTempPath(),"ww-ini-roundtrip-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
try
{
    string path=Path.Combine(root,"ReShade.ini");
    File.WriteAllText(path,"; preserve\n[INPUT]\nKeyOverlay=36,0,0,0\n[ADDON] ; comment\nLoadFromDllMain=vendor,,extras.addon64,other.addon64\nAddonPath=addons,,shared\n[renodx.mfgunlock]\nEnabled=7\n[RenoDX.MFGUnlock]\nEnabled=0\n[USER]\nKeep=a,,b\n");
    void Read(string stage)
    {
        var psi=new ProcessStartInfo(Path.GetFullPath(args[0])){UseShellExecute=false,CreateNoWindow=true};psi.ArgumentList.Add(path);psi.ArgumentList.Add(stage);
        using var process=Process.Start(psi)!;if(!process.WaitForExit(10000)){process.Kill();throw new Exception("Private parser test timed out");}if(process.ExitCode!=0)throw new Exception("Upstream parser rejected "+stage);
    }
    Read("original");var ini=IniDocument.Load(path);var receipt=new DeploymentReceipt();
    ini.ApplyManagedAddonLoading(receipt);ini.ApplyOwned("RenoDX.MFGUnlock","Enabled","1",receipt);new MenuShortcut(112,true,false,true).Apply(ini);ini.Save(path);Read("deployed");
    ini=IniDocument.Load(path);ini.RemoveOwnedEdits(receipt,_=>{});ini.Save(path);Read("cleaned");
    Console.WriteLine("RESULT: 3 production INI stages verified by actual upstream ReShade 6.8.0 parser; no runtime DLL/game loaded.");
}
finally{Directory.Delete(root,true);}
