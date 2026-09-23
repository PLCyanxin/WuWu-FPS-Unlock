using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

Console.OutputEncoding = Encoding.UTF8;
int result;
try { result = Update(); }
catch (UnauthorizedAccessException ex)
{
    Console.Error.WriteLine("权限不足，更新未完成。请关闭启动器，右键更新器，选择“以管理员身份运行”。不会自动提权。\n" + ex.Message);
    result = 1;
}
catch (Exception ex) { Console.Error.WriteLine("更新未完成：" + ex.Message); result = 1; }
Console.WriteLine("按任意键退出。");
if (!Console.IsInputRedirected) Console.ReadKey(true);
return result;

static int Update()
{
    string root = Path.GetFullPath(AppContext.BaseDirectory);
    string payload = Path.Combine(root, "update-payload");
    string exe = Path.Combine(root, "WuWaFpsUnlock.exe");
    NoLinks(root); NoLinks(payload); NoLinks(exe);
    if (!Directory.Exists(payload)) throw new DirectoryNotFoundException("请将完整更新包解压到旧版 WuWaFpsUnlock.exe 同目录；缺少 update-payload。");
    string oldVersion = ProductVersion(exe);
    string newVersion = ProductVersion(Path.Combine(payload, "WuWaFpsUnlock.exe"));
    Console.WriteLine($"鸣潮 FPS Unlock 离线更新\n安装目录：{root}\n当前版本：{oldVersion}\n包内版本：{newVersion}");
    RejectRunning(exe);
    var entries = new List<Entry>();
    var held = new List<FileStream>();
    string? backup = null;
    try
    {
        foreach (string source in Enumerate(payload))
        {
            string relative = Path.GetRelativePath(payload, source).Replace('\\', '/');
            if (!Allowed(relative)) throw new InvalidDataException("更新包包含不允许更新的文件：" + relative);
            string target = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            NoLinks(target);
            if (Directory.Exists(target)) throw new IOException("目标是目录而非文件：" + target);
            var sourceHandle = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            held.Add(sourceHandle);
            var entry = new Entry(relative, source, target, Hash(sourceHandle));
            if (File.Exists(target))
            {
                // Keep all existing destinations exclusively locked throughout backup/staging.
                entry.TargetHandle = new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                held.Add(entry.TargetHandle);
                entry.OldHash = Hash(entry.TargetHandle);
            }
            entry.SourceHandle = sourceHandle;
            entries.Add(entry);
        }
        foreach (string required in new[] { "WuWaFpsUnlock.exe", "components/fps/ww_plugin_base.dll", "components/PROVENANCE.json" })
            if (!entries.Any(e => e.Relative.Equals(required, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("更新包不完整，缺少：" + required);
        // Use a GUID directory so every attempt keeps its own original files and staged copy.
        backup = Path.Combine(root, "update-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        NoLinks(backup);
        Directory.CreateDirectory(backup);
        Console.WriteLine("备份目录：" + backup);
        foreach (var entry in entries)
        {
            entry.Staged = Path.Combine(backup, "staged", entry.Relative);
            CopyFromHandle(entry.SourceHandle!, entry.Staged, entry.NewHash);
            if (entry.TargetHandle is not null)
            {
                entry.Backup = Path.Combine(backup, "original", entry.Relative);
                CopyFromHandle(entry.TargetHandle, entry.Backup, entry.OldHash!);
            }
        }
        File.WriteAllLines(Path.Combine(backup, "files.txt"), entries.Select(e => $"{e.Relative}\told={e.OldHash ?? "(absent)"}\tnew={e.NewHash}"), Encoding.UTF8);
        RejectRunning(exe);
        foreach (var entry in entries)
        {
            NoLinks(entry.Target); NoLinks(entry.Staged!);
            Directory.CreateDirectory(Path.GetDirectoryName(entry.Target)!);
            entry.TargetHandle?.Dispose();
            // Windows replacement needs the destination handle closed; recheck immediately.
            EnsureUnchanged(entry.Target, entry.OldHash);
            if (entry.OldHash is null) File.Move(entry.Staged!, entry.Target, false);
            else File.Replace(entry.Staged!, entry.Target, null);
            entry.Applied = true;
            EnsureUnchanged(entry.Target, entry.NewHash);
            Console.WriteLine("已更新：" + entry.Relative);
        }
        Console.WriteLine("更新完成，原文件备份已保留。\n请打开启动器，在设置中重新部署一次");
        return 0;
    }
    catch
    {
        foreach (var handle in held) handle.Dispose();
        bool restored = true;
        foreach (var entry in entries.Where(e => e.Applied).Reverse())
        {
            try
            {
                NoLinks(entry.Target);
                EnsureUnchanged(entry.Target, entry.NewHash);
                if (entry.Backup is null) File.Delete(entry.Target);
                else
                {
                    NoLinks(entry.Backup);
                    string restore = Path.Combine(backup!, "restore-" + Guid.NewGuid().ToString("N"));
                    using var original = new FileStream(entry.Backup, FileMode.Open, FileAccess.Read, FileShare.Read);
                    CopyFromHandle(original, restore, entry.OldHash!);
                    File.Replace(restore, entry.Target, null);
                    EnsureUnchanged(entry.Target, entry.OldHash);
                }
            }
            catch (Exception ex) { restored = false; Console.Error.WriteLine($"回滚失败：{entry.Target}\n{ex.Message}"); }
        }
        Console.Error.WriteLine(restored ? "本次已修改的文件均已恢复（尚未替换时原文件未改变）。" : "未能完整恢复，请勿启动应用；请从备份 original 目录手动恢复上述文件。");
        if (backup is not null) Console.Error.WriteLine("备份保留于：" + backup);
        throw;
    }
    finally { foreach (var handle in held) handle.Dispose(); }
}

static bool Allowed(string path) => path.Equals("WuWaFpsUnlock.exe", StringComparison.OrdinalIgnoreCase)
    || path.Equals("components/fps/ww_plugin_base.dll", StringComparison.OrdinalIgnoreCase)
    || path.Equals("components/PROVENANCE.json", StringComparison.OrdinalIgnoreCase)
    || path.Equals("payload/files/addon/renodx-mfgunlock.addon64", StringComparison.OrdinalIgnoreCase)
    || path.StartsWith("licenses/", StringComparison.OrdinalIgnoreCase);

static IEnumerable<string> Enumerate(string directory)
{
    NoLinks(directory);
    foreach (string path in Directory.EnumerateFileSystemEntries(directory))
    {
        NoLinks(path);
        if (Directory.Exists(path))
        {
            foreach (string child in Enumerate(path)) yield return child;
        }
        else yield return path;
    }
}

static void NoLinks(string path)
{
    for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
    {
        try
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("拒绝链接或重解析点：" + current);
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }
}

static string ProductVersion(string file)
{
    NoLinks(file);
    if (!File.Exists(file)) throw new FileNotFoundException("找不到鸣潮 FPS Unlock 主程序：" + file);
    var info = FileVersionInfo.GetVersionInfo(file);
    // .NET apphost inherits the managed assembly's OriginalFilename (.dll).
    if (!string.Equals(info.ProductName, "WuWaFpsUnlock", StringComparison.OrdinalIgnoreCase)
        || !(string.Equals(info.OriginalFilename, "WuWaFpsUnlock.dll", StringComparison.OrdinalIgnoreCase)
          || string.Equals(info.OriginalFilename, "WuWaFpsUnlock.exe", StringComparison.OrdinalIgnoreCase)))
        throw new InvalidDataException("文件产品信息不属于 WuWaFpsUnlock，已拒绝更新：" + file);
    return info.ProductVersion ?? info.FileVersion ?? "版本未知";
}

static void RejectRunning(string target)
{
    foreach (var process in Process.GetProcessesByName("WuWaFpsUnlock"))
    {
        using (process)
        {
            string? path;
            try { if (process.HasExited) continue; path = process.MainModule?.FileName; }
            catch (InvalidOperationException) { continue; }
            catch (System.ComponentModel.Win32Exception) { throw new IOException("无法确认正在运行的启动器路径。请先关闭启动器，必要时以管理员身份运行更新器。"); }
            if (path is null || Path.GetFullPath(path).Equals(target, StringComparison.OrdinalIgnoreCase))
                throw new IOException("目标启动器正在运行，请完全退出（包括通知区）后重新运行更新器；不会结束任何进程。");
        }
    }
}

static string Hash(FileStream stream)
{
    stream.Position = 0;
    string hash = Convert.ToHexString(SHA256.HashData(stream));
    stream.Position = 0;
    return hash;
}

static void CopyFromHandle(FileStream source, string destination, string expected)
{
    NoLinks(destination);
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
    source.Position = 0; source.CopyTo(output); output.Flush(true);
    if (Hash(output) != expected) throw new IOException("复制校验失败：" + destination);
}

static void EnsureUnchanged(string path, string? expected)
{
    NoLinks(path);
    if (expected is null)
    {
        if (File.Exists(path) || Directory.Exists(path)) throw new IOException("预检后目标出现，请重新运行更新器：" + path);
        return;
    }
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
    if (Hash(stream) != expected) throw new IOException("预检后文件发生变化：" + path);
}

sealed class Entry(string relative, string source, string target, string newHash)
{
    public string Relative { get; } = relative;
    public string Source { get; } = source;
    public string Target { get; } = target;
    public string NewHash { get; } = newHash;
    public string? OldHash { get; set; }
    public string? Staged { get; set; }
    public string? Backup { get; set; }
    public FileStream? SourceHandle { get; set; }
    public FileStream? TargetHandle { get; set; }
    public bool Applied { get; set; }
}
