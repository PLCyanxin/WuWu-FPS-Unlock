using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
namespace WuWaFpsUnlock.Services;

public sealed record SchedulingCpu(uint Id,ushort Group,byte Logical,byte Efficiency,byte Flags);
public interface ILauncherSchedulingApi
{
    bool IsIntel();
    uint[] ReadDefaults();
    SchedulingCpu[] ReadTopology();
    ulong ReadAffinity();
    void SetDefaults(uint[] ids);
    void SetAffinity(ulong mask);
}

// Only the current launcher is changed. No caller-supplied PID or process handle.
public sealed class LauncherSchedulingPolicy(ILauncherSchedulingApi api)
{
    private readonly object _gate=new();
    private uint[] _original=[],_efficient=[];
    private ulong _originalMask,_efficientMask;
    private bool _initialized,_background,_busy,_applied;
    private int _childDepth;
    public string Status {get;private set;}="未启用后台调度";
    public static uint[] Select(SchedulingCpu[] cpus,uint[] original,ulong affinity)
    {
        if(cpus.Length==0||cpus.Select(c=>c.Group).Distinct().Count()!=1||cpus[0].Group!=0||cpus.Select(c=>c.Efficiency).Distinct().Count()<2)return [];
        byte lowest=cpus.Min(c=>c.Efficiency);
        return cpus.Where(c=>c.Efficiency==lowest&&(c.Flags&1)==0&&((c.Flags&2)==0||(c.Flags&4)!=0)&&(c.Flags&8)==0&&
            c.Logical<64&&(affinity&(1UL<<c.Logical))!=0&&(original.Length==0||original.Contains(c.Id))).Select(c=>c.Id).Distinct().ToArray();
    }
    public void Initialize()
    {
        lock(_gate)
        {
            if(_initialized)return;
            try
            {
                if(!api.IsIntel()){_initialized=true;Status="非 Intel 平台；沿用系统调度";return;}
                _original=api.ReadDefaults();_originalMask=api.ReadAffinity();
                var topology=api.ReadTopology();
                _efficient=Select(topology,_original,_originalMask);
                _efficientMask=topology.Where(c=>_efficient.Contains(c.Id)).Aggregate(0UL,(mask,c)=>mask|(1UL<<c.Logical));
                _initialized=true;
                Status=topology.Any(c=>c.Group!=0)?"多处理器组暂不支持；沿用系统调度":
                    _efficient.Length==0?"未确认可用混合节能核心，或原亲和范围无 E 核；沿用系统调度":
                    "已确认 Intel E 类核心；后台硬亲和掩码 0x"+_efficientMask.ToString("X");
            }
            catch(Exception e){Status="CPU Sets 检测失败；沿用系统调度："+e.Message;}
        }
    }
    public void SetBackground(bool value){lock(_gate){_background=value;Apply();}}
    public void SetBusy(bool value){lock(_gate){_busy=value;Apply();}}
    private void Restore()
    {
        if(!_applied)return;
        Exception? failure=null;
        // Both restorations must be attempted, even if the first API fails.
        try{api.SetDefaults(_original);Verify(_original);}catch(Exception e){failure=e;}
        try{api.SetAffinity(_originalMask);VerifyAffinity(_originalMask);}catch(Exception e){failure=failure is null?e:new AggregateException(failure,e);}
        if(failure is not null){Status="启动器调度恢复未完成；禁止创建子进程。";throw new IOException(Status,failure);}
        _applied=false;
    }
    private void Verify(uint[] expected)
    {
        if(!api.ReadDefaults().Order().SequenceEqual(expected.Order()))throw new IOException("启动器 CPU Sets 回读不一致");
    }
    private void VerifyAffinity(ulong expected)
    {
        if(api.ReadAffinity()!=expected)throw new IOException("启动器硬亲和掩码回读不一致");
    }
    private void Apply()
    {
        try
        {
            if(!_initialized||_efficient.Length==0)return;
            if(!_background||_busy||_childDepth!=0){Restore();return;}
            if(!_applied){_applied=true;api.SetAffinity(_efficientMask);VerifyAffinity(_efficientMask);api.SetDefaults(_efficient);Verify(_efficient);}
        }
        catch(Exception e){Status="后台调度未完成："+e.Message;try{Restore();}catch{ /* Keep applied flag: child creation must retry a verified restore. */ }}
    }
    public T CreateChild<T>(Func<T> create)
    {
        lock(_gate)
        {
            // Restore errors must stop creation; never knowingly pass our restriction to a child.
            _childDepth++;
            try{Restore();return create();}
            finally{_childDepth--;Apply();}
        }
    }
}

public static class LauncherScheduling
{
    private static readonly LauncherSchedulingPolicy Policy=new(new WindowsLauncherSchedulingApi());
    public static event Action<string>? StatusChanged;
    private static string? _reported;
    private static void Report()
    {
        string status=Policy.Status;
        if(Interlocked.Exchange(ref _reported,status)!=status)
        {
            try{StatusChanged?.Invoke(status);}
            catch(Exception error){Debug.WriteLine(error);} // Logging must never lose a successfully created child.
        }
    }
    public static void Initialize()=>Policy.Initialize();
    public static string Status=>Policy.Status;
    public static void SetBackground(bool value){Policy.SetBackground(value);Report();}
    public static void SetBusy(bool value){Policy.SetBusy(value);Report();}
    public static Process? StartProcess(ProcessStartInfo info){try{return Policy.CreateChild(()=>Process.Start(info));}finally{Report();}}
}

public sealed class WindowsLauncherSchedulingApi:ILauncherSchedulingApi
{
    private static readonly IntPtr Current=new(-1);
    public bool IsIntel()
    {
        if(!System.Runtime.Intrinsics.X86.X86Base.IsSupported)return false;
        var id=System.Runtime.Intrinsics.X86.X86Base.CpuId(0,0);
        return id.Ebx==0x756e6547&&id.Edx==0x49656e69&&id.Ecx==0x6c65746e; // GenuineIntel
    }
    public uint[] ReadDefaults()
    {
        if(!GetProcessDefaultCpuSets(Current,null,0,out uint count)&&Marshal.GetLastWin32Error()!=122)throw new Win32Exception();
        if(count>65536)throw new InvalidDataException("CPU Set count invalid");
        if(count==0)return [];
        var ids=new uint[count];if(!GetProcessDefaultCpuSets(Current,ids,count,out uint actual)||actual>count)throw new Win32Exception();
        return ids[..(int)actual];
    }
    public SchedulingCpu[] ReadTopology()
    {
        if(!GetSystemCpuSetInformation(null,0,out uint size,Current,0)&&Marshal.GetLastWin32Error()!=122)throw new Win32Exception();
        if(size==0||size>1024*1024)throw new InvalidDataException("CPU Set topology length invalid");
        var data=new byte[size];if(!GetSystemCpuSetInformation(data,size,out uint actual,Current,0)||actual>size)throw new Win32Exception();
        return Parse(data.AsSpan(0,(int)actual));
    }
    public static SchedulingCpu[] Parse(ReadOnlySpan<byte> data)
    {
        var result=new List<SchedulingCpu>();
        while(!data.IsEmpty)
        {
            if(data.Length<8)throw new InvalidDataException("CPU Set record truncated");
            uint size=BitConverter.ToUInt32(data);if(size<8||size>data.Length)throw new InvalidDataException("CPU Set record size invalid");
            if(BitConverter.ToUInt32(data[4..])==0)
            {
                if(size<32)throw new InvalidDataException("CPU Set record too short");
                result.Add(new(BitConverter.ToUInt32(data[8..]),BitConverter.ToUInt16(data[12..]),data[14],data[18],data[19]));
            }
            data=data[(int)size..];
        }
        return result.ToArray();
    }
    public ulong ReadAffinity(){if(!GetProcessAffinityMask(Current,out nuint process,out _))throw new Win32Exception();return process;}
    public void SetAffinity(ulong mask){if(mask==0||!SetProcessAffinityMask(Current,checked((nuint)mask)))throw new Win32Exception(Marshal.GetLastWin32Error(),"无法设置/恢复启动器硬亲和掩码");}
    public void SetDefaults(uint[] ids){if(!SetProcessDefaultCpuSets(Current,ids.Length==0?null:ids,(uint)ids.Length))throw new Win32Exception(Marshal.GetLastWin32Error(),"无法设置/恢复启动器 CPU Sets");}
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool GetSystemCpuSetInformation([Out]byte[]? data,uint length,out uint returned,IntPtr process,uint flags);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool GetProcessDefaultCpuSets(IntPtr process,[Out]uint[]? ids,uint count,out uint required);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool SetProcessDefaultCpuSets(IntPtr process,uint[]? ids,uint count);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool SetProcessAffinityMask(IntPtr process,nuint mask);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool GetProcessAffinityMask(IntPtr process,out nuint processMask,out nuint systemMask);
}
