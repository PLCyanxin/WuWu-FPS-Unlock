using System.Runtime.InteropServices;

internal static partial class Program
{
    static void ShowCompletionFallback()
        => MessageBoxW(IntPtr.Zero, "更新完成，请点击重新部署。\n未能自动打开启动器，请手动打开启动器设置。", "更新完成", 0x00000040u | 0x00010000u | 0x00040000u);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBoxW(IntPtr owner, string text, string caption, uint flags);
}
