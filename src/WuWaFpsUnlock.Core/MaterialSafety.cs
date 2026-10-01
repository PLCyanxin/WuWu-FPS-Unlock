using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WuWaFpsUnlock.Core;

public static class MaterialSafety
{
    public static void RequireOutsideGame(string gameRoot, string materialDirectory)
    {
        if (Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameRoot)).Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(materialDirectory)), StringComparison.OrdinalIgnoreCase)
            || SafePaths.IsInside(gameRoot, materialDirectory))
            throw new InvalidDataException("请将启动器及部署材料移到鸣潮游戏目录之外，再进行部署或清除。");
    }
    public static void RequireDifferentFiles(string source, string target)
    {
        if (Path.GetFullPath(source).Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("源材料与游戏目标是同一文件，已停止操作：" + target);
        if (!OperatingSystem.IsWindows() || !File.Exists(target)) return;
        using var a = File.OpenHandle(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var b = File.OpenHandle(target, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (Identity(a) == Identity(b)) throw new InvalidDataException("源材料与游戏目标是同一文件的硬链接，已停止操作：" + target);
    }
    internal static (uint Volume, ulong Index) Identity(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var info)) throw new IOException("无法确认文件身份。", new Win32Exception(Marshal.GetLastWin32Error()));
        return (info.Volume, ((ulong)info.IndexHigh << 32) | info.IndexLow);
    }
    [StructLayout(LayoutKind.Sequential)] private struct FileInfo
    {
        public uint Attributes; public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInfo info);

    // Structural checks accept unsigned local replacements without pinning an old hash.
    public static void RequireX64Dll(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        void Invalid() => throw new InvalidDataException("部署材料不是完整的 Windows x64 DLL：" + path);
        Span<byte> dos = stackalloc byte[64];
        if (stream.Length < 64 || stream.Read(dos) != 64 || dos[0] != 'M' || dos[1] != 'Z') { Invalid(); return; }
        int offset = BinaryPrimitives.ReadInt32LittleEndian(dos[60..]);
        if (offset < 64 || offset > stream.Length - 24) { Invalid(); return; }
        stream.Position = offset;
        Span<byte> coff = stackalloc byte[24]; stream.ReadExactly(coff);
        int sections = BinaryPrimitives.ReadUInt16LittleEndian(coff[6..]), optionalSize = BinaryPrimitives.ReadUInt16LittleEndian(coff[20..]);
        if (BinaryPrimitives.ReadUInt32LittleEndian(coff) != 0x4550 || BinaryPrimitives.ReadUInt16LittleEndian(coff[4..]) != 0x8664
            || (BinaryPrimitives.ReadUInt16LittleEndian(coff[22..]) & 0x2000) == 0 || sections is < 1 or > 96 || optionalSize < 112
            || offset + 24L + optionalSize + sections * 40L > stream.Length) { Invalid(); return; }
        Span<byte> optional = stackalloc byte[112]; stream.ReadExactly(optional);
        uint imageSize = BinaryPrimitives.ReadUInt32LittleEndian(optional[56..]), headerSize = BinaryPrimitives.ReadUInt32LittleEndian(optional[60..]);
        if (BinaryPrimitives.ReadUInt16LittleEndian(optional) != 0x20b || imageSize == 0 || headerSize < offset + 24L + optionalSize + sections * 40L || headerSize > stream.Length) { Invalid(); return; }
        stream.Position = offset + 24L + optionalSize;
        Span<byte> section = stackalloc byte[40];
        for (int i = 0; i < sections; i++)
        {
            stream.ReadExactly(section);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(section[16..]), start = BinaryPrimitives.ReadUInt32LittleEndian(section[20..]);
            uint virtualSize = BinaryPrimitives.ReadUInt32LittleEndian(section[8..]), address = BinaryPrimitives.ReadUInt32LittleEndian(section[12..]);
            if ((size > 0 && (start < headerSize || (ulong)start + size > (ulong)stream.Length)) || (ulong)address + Math.Max(size, virtualSize) > imageSize) Invalid();
        }
    }
}
