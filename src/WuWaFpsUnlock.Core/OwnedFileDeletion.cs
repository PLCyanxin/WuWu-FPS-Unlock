using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace WuWaFpsUnlock.Core;

// Delete the very same file handle that was hashed. While held, Windows denies
// other writers and path replacement; no close-hash-open-delete race is possible.
public static class OwnedFileDeletion
{
    public static async Task<bool> DeleteMatchingAsync(string path, string expectedHash, CancellationToken token = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Owned deletion is Windows-only.");
        using var handle = CreateFileW(path, 0x80010000, 1, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
        if (handle.IsInvalid) throw new IOException("无法独占写入/删除权限进行安全清除：" + path, new Win32Exception(Marshal.GetLastWin32Error()));
        await using var stream = new FileStream(handle, FileAccess.Read, 131072, false);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
        if (!hash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase)) return false;
        token.ThrowIfCancellationRequested();
        var disposition = new FileDispositionInfo { DeleteFile = 1 };
        if (!SetFileInformationByHandle(handle, 4, ref disposition, 1))
            throw new IOException("已校验文件但 Windows 拒绝清除：" + path, new Win32Exception(Marshal.GetLastWin32Error()));
        return true;
    }
    [StructLayout(LayoutKind.Sequential)] private struct FileDispositionInfo { public byte DeleteFile; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint access, uint share, IntPtr security, uint creation, uint attributes, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle file, int informationClass, ref FileDispositionInfo information, uint size);
}
