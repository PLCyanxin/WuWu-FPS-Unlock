using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Text;
namespace WuWaFpsUnlock.Services;
public static class PeInspector
{
    public static bool IsAmd64(string path)
    { using var stream=File.OpenRead(path); using var pe=new PEReader(stream); return pe.PEHeaders.CoffHeader.Machine==Machine.Amd64; }
    public static HashSet<string> Exports(string path)
    {
        if(new FileInfo(path).Length>128*1024*1024) throw new InvalidDataException("不读取异常大小的代理 DLL。");
        byte[] b=File.ReadAllBytes(path); using var reader=new BinaryReader(new MemoryStream(b));
        int pe=I32(0x3c); if(pe<0 || pe+256>b.Length || I32(pe)!=0x00004550) throw new BadImageFormatException();
        int count=U16(pe+6), opt=pe+24, optSize=U16(pe+20), section=opt+optSize;
        int dir=opt+(U16(opt)==0x20b?112:96); uint exportRva=U32(dir);
        var result=new HashSet<string>(StringComparer.Ordinal); if(exportRva==0) return result;
        int Rva(uint rva)
        {
            for(int i=0;i<count && i<96;i++) { int s=section+i*40; uint va=U32(s+12),sz=Math.Max(U32(s+8),U32(s+16)); if(rva>=va && (ulong)rva< (ulong)va+sz) { long offset=(long)U32(s+20)+rva-va; if(offset<0||offset>=b.Length) break; return (int)offset; } }
            throw new BadImageFormatException("PE RVA 超出映像。");
        }
        int exp=Rva(exportRva), names=Rva(U32(exp+32)); uint n=U32(exp+24); if(n>100000) throw new BadImageFormatException();
        for(uint i=0;i<n;i++) { int pos=Rva(U32(names+checked((int)i*4))); int end=Array.IndexOf(b,(byte)0,pos); if(end<0 || end-pos>512) throw new BadImageFormatException(); result.Add(Encoding.ASCII.GetString(b,pos,end-pos)); }
        return result;
        int I32(int o) { if(o<0||o+4>b.Length)throw new BadImageFormatException();return BitConverter.ToInt32(b,o); }
        uint U32(int o)=>unchecked((uint)I32(o));
        ushort U16(int o) { if(o<0||o+2>b.Length)throw new BadImageFormatException();return BitConverter.ToUInt16(b,o); }
    }
    public static string Version(string path)=>FileVersionInfo.GetVersionInfo(path).FileVersion??"未知";
    public static string AddonBuild(string path)
    {
        var exports=Exports(path);
        if(!exports.Contains("ReShadeVersion")) return "Unknown";
        var ascii=Encoding.ASCII.GetString(File.ReadAllBytes(path));
        if(ascii.Contains("only limited add-on functionality",StringComparison.Ordinal)) return "Limited";
        if(exports.Contains("ReShadeRegisterAddon") && ascii.Contains("Loading add-on from",StringComparison.Ordinal)) return "FullCandidate";
        return "UnknownReShade";
    }
}
