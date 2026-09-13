using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using WuWaFpsUnlock.Core;

namespace WuWaFpsUnlock.Services;
public static class EnvironmentProbe
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr QueryInterface(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Initialize();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Enumerate([Out] IntPtr[] handles, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FullName(IntPtr gpu, StringBuilder name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DriverVersion(out uint version, StringBuilder branch);
    public static HardwareInfo Read(Action<string> log)
    {
        string gpu = "未检测到 NVIDIA 显卡"; int? driver = null; bool ada = false; IntPtr library = IntPtr.Zero; Action? unload = null;
        try
        {
            library = NativeLibrary.Load(Path.Combine(Environment.SystemDirectory, "nvapi64.dll"));
            var query = Marshal.GetDelegateForFunctionPointer<QueryInterface>(NativeLibrary.GetExport(library,"nvapi_QueryInterface"));
            T Get<T>(uint id) where T: Delegate { var p=query(id); return p==IntPtr.Zero ? throw new InvalidOperationException("NVAPI 接口不存在："+id.ToString("X")) : Marshal.GetDelegateForFunctionPointer<T>(p); }
            int result=Get<Initialize>(0x0150E828)(); if(result!=0) throw new InvalidOperationException("NVAPI 初始化失败："+result);
            unload=()=>Get<Initialize>(0xD22BDD7E)();
            if(Get<DriverVersion>(0x2926AAAD)(out uint d,new StringBuilder(64))==0) driver=checked((int)d);
            var handles=new IntPtr[64]; var names=new List<string>();
            if(Get<Enumerate>(0xE5AC921F)(handles,out int count)==0)
                for(int i=0;i<Math.Clamp(count,0,64);i++) { var name=new StringBuilder(64); if(Get<FullName>(0xCEEE8E9F)(handles[i],name)==0) names.Add(name.ToString()); }
            bool Is40(string name) => Regex.IsMatch(name,@"\bGeForce\s+RTX\s+40(50|60|70|80|90)(?:\b|\s)",RegexOptions.IgnoreCase);
            ada=names.Any(Is40); gpu=names.FirstOrDefault(Is40) ?? names.FirstOrDefault() ?? gpu;
            if(names.Count>1) log("全部 NVIDIA 设备："+string.Join("；",names));
        }
        catch(Exception e) { log("GPU/驱动检测未完成："+e.Message); }
        finally { try{unload?.Invoke();}catch{ } if(library!=IntPtr.Zero) NativeLibrary.Free(library); }
        bool? hags=null;
        try { using var key=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers"); object? v=key?.GetValue("HwSchMode"); if(v is int mode) hags=mode==2?true:mode==1?false:null; } catch(Exception e) { log("HAGS 读取失败："+e.Message); }
        string hs=hags switch { true=>"已配置开启（运行态待确认）",false=>"已配置关闭",_=>"系统默认 / 未确认" };
        return new(gpu,driver,ada,RuntimeInformation.OSDescription,hs,hags);
    }
}
