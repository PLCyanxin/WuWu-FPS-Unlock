internal static partial class Program
{
    // Integration tests exercise production CLI/filesystem code without desktop COM.
    static void RefreshDesktopShortcut(string root)=>Console.WriteLine("FIXTURE: desktop shortcut adapter suppressed");
}
