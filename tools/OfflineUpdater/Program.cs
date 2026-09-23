using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

internal static partial class Program
{
static string? completedUpdateLauncher;
[STAThread]
static int Main(string[] args)
{
Console.OutputEncoding = Encoding.UTF8;
if (args.Length == 0 || args[0] == "--wait-for-exit") Console.WriteLine("请勿关闭窗口与游戏启动器。程序会自动完成必要的退出与重新打开。");
int result;
try
{
    result = args.Length == 0 ? Update()
        : args.Length == 3 && args[0] == "--wait-for-exit" ? WaitThenUpdate(args[1], args[2])
        : args.Length == 4 && args[0] == "--wait-for-rollback" ? WaitThenRollback(args[1], args[2], args[3])
        : args.Length == 2 && args[0] == "--rollback" ? Rollback(args[1])
        : args.Length == 1 && args[0] == "--self-test" ? SelfTest()
        : args.Length == 1 && args[0] == "--self-test-child" ? SelfTestChild()
        : args.Length == 3 && args[0] == "--validate-package" ? ValidatePackageCommand(args[1], args[2])
        : throw new ArgumentException("用法：WuWaUpdater.exe [--rollback <备份目录> | --wait-for-exit <PID> <启动器完整路径> | --wait-for-rollback <PID> <启动器完整路径> <备份目录> | --validate-package <包目录> <版本>]");
}
catch (UnauthorizedAccessException ex)
{
    Console.Error.WriteLine("权限不足，操作未完成。请关闭启动器，右键更新器，选择“以管理员身份运行”。不会自动提权。\n" + ex.Message);
    result = 1;
}
catch (Exception ex) { Console.Error.WriteLine("操作未完成：" + ex.Message); result = 1; }
if (result == 0 && completedUpdateLauncher is not null) ReopenUpdatedLauncher(completedUpdateLauncher);
if (!args.Contains("--self-test") && !args.Contains("--self-test-child") && !args.Contains("--validate-package"))
{
    Console.WriteLine("按任意键退出。");
    if (!Console.IsInputRedirected) Console.ReadKey(true);
}
return result;
}

static void ReopenUpdatedLauncher(string exe)
{
    try
    {
        NoLinks(exe);ProductVersion(exe);
        var start = new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! };
        start.ArgumentList.Add("--update-completed");
        using var process = Process.Start(start) ?? throw new IOException("无法创建启动器进程。");
        Console.WriteLine("更新完成，请点击重新部署。已请求打开启动器设置。");
    }
    catch (Exception error)
    {
        Console.Error.WriteLine("更新已完成，但未能自动打开启动器。请手动打开启动器设置并点击重新部署：" + error.Message);
        ShowCompletionFallback();
    }
}
static int Update(string? expectedLauncher = null)
{
    string updaterDirectory = Path.GetFullPath(AppContext.BaseDirectory);
    string payload = Path.Combine(updaterDirectory, "update-payload");
    NoLinks(updaterDirectory); NoLinks(payload);
    bool requireManifest = expectedLauncher is not null || File.Exists(Path.Combine(updaterDirectory, "update-manifest.json"));
    ValidateUpdateManifest(updaterDirectory, required: requireManifest);
    if (!Directory.Exists(payload)) throw new DirectoryNotFoundException("更新器旁缺少 update-payload，请保留完整更新包目录结构。");
    string root = FindInstallation(updaterDirectory);
    using var installationLock = AcquireInstallationLock(root);
    string exe = Path.Combine(root, "WuWaFpsUnlock.exe");
    if (expectedLauncher is not null) MatchExpectedLauncher(exe, expectedLauncher);
    NoLinks(root); NoLinks(payload); NoLinks(exe);
    string oldVersion = ProductVersion(exe);
    string newVersion = ProductVersion(Path.Combine(payload, "WuWaFpsUnlock.exe"));
    Console.WriteLine($"鸣潮 FPS Unlock 离线更新\n安装目录：{root}\n当前版本：{oldVersion}\n包内版本：{newVersion}");
    RejectRunning(exe);
    RequireGameStopped();
    var entries = new List<Entry>();
    var held = new List<FileStream>();
    string? backup = null;
    string inventoryScratch = Path.Combine(Path.GetTempPath(), "WuWaUpdater-inventory-" + Guid.NewGuid().ToString("N"));
    try
    {
        foreach (string source in Enumerate(payload))
        {
            string relative = Path.GetRelativePath(payload, source).Replace('\\', '/');
            if (!AllowedPackageFile(relative)) throw new InvalidDataException("更新包包含不允许更新的文件：" + relative);
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
        // Recheck while source handles prevent changes; protocol hashes must describe
        // the actual bytes staged below, not an earlier unlocked observation.
        ValidateUpdateManifest(updaterDirectory, required: requireManifest);
        foreach (string required in new[] { "WuWaFpsUnlock.exe", "components/fps/ww_plugin_base.dll", "components/PROVENANCE.json", "payload/files/addon/renodx-mfgunlock.addon64", "payload/addon-source.json" })
            if (!entries.Any(e => e.Relative.Equals(required, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("更新包不完整，缺少：" + required);
        AddInventoryEntries(root, entries, held, inventoryScratch);
        // Use a GUID directory so every attempt keeps its own original files and staged copy.
        backup = Path.Combine(root, "update-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fffffff") + "-" + Guid.NewGuid().ToString("N"));
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
        var snapshot = CaptureSnapshot(root, backup, updaterDirectory, entries);
        PrepareRollbackLauncher(backup, updaterDirectory);
        RejectRunning(exe);
    RequireGameStopped();
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
        WriteSnapshot(backup, snapshot with { Complete = true });
        RefreshDesktopShortcut(root);
        try { PrunePreviousBackups(root,backup); }
        catch(Exception error){Console.WriteLine("更新已完成；旧备份暂未清理："+error.Message);}
        Console.WriteLine("更新完成，最近一次更新前完整快照已保留。可删除更新文件夹，但请保留安装目录中的备份文件夹。\n需要回退时在启动器设置中选择“回退版本”，或使用更新器文件夹中的“回退.cmd”及备份中的 rollback.cmd。\n自动回退恢复旧程序与材料，保留当前配置和部署记录；data 快照仅供人工参考，避免丢失后续部署记录。\n请打开启动器，在设置中重新部署一次");
        completedUpdateLauncher = exe;
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
    finally
    {
        foreach (var handle in held) handle.Dispose();
        try { if(Directory.Exists(inventoryScratch)){NoLinks(inventoryScratch);Directory.Delete(inventoryScratch,true);} }
        catch { Console.WriteLine("临时材料清单保留于：" + inventoryScratch); }
    }
}

static string FindInstallation(string updaterDirectory)
{
    var candidates = new List<string>();
    var rejected = new List<string>();
    string? directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(updaterDirectory));
    // Direct children only: updater directory, then at most three parent directories.
    for (int level = 0; level <= 3 && directory is not null; level++, directory = Path.GetDirectoryName(directory))
    {
        NoLinks(directory);
        string candidate = Path.Combine(directory, "WuWaFpsUnlock.exe");
        NoLinks(candidate);
        if (!File.Exists(candidate)) continue;
        try { ProductVersion(candidate); candidates.Add(directory); }
        catch (InvalidDataException) { rejected.Add(candidate); }
    }
    if (rejected.Count > 0)
        Console.WriteLine("以下同名文件的产品身份不符，已排除：\n" + string.Join("\n", rejected));
    if (candidates.Count == 0)
        throw new DirectoryNotFoundException("未找到有效的鸣潮 FPS Unlock 安装。请将完整更新包放入原版 WuWaFpsUnlock.exe 所在目录或其子目录（最多三层）。");
    if (candidates.Count > 1)
        throw new InvalidDataException("找到多个有效安装，无法自动选择；请调整更新包位置，只保留一个候选：\n"
            + string.Join("\n", candidates.Select(path => Path.Combine(path, "WuWaFpsUnlock.exe"))));
    return candidates[0];
}

static bool Allowed(string path) => AllowedPackageFile(path) || InventoryPaths.Contains(path, StringComparer.OrdinalIgnoreCase);
static bool AllowedPackageFile(string path) => path.Equals("WuWaFpsUnlock.exe", StringComparison.OrdinalIgnoreCase)
    || path.Equals("components/fps/ww_plugin_base.dll", StringComparison.OrdinalIgnoreCase)
    || path.Equals("components/PROVENANCE.json", StringComparison.OrdinalIgnoreCase)
    || path.Equals("payload/files/addon/renodx-mfgunlock.addon64", StringComparison.OrdinalIgnoreCase)
    || path.Equals("payload/addon-source.json", StringComparison.OrdinalIgnoreCase)
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
            try { using var identity = LauncherProcess.Open(process.Id); if (identity is null || identity.HasExited) continue; path = identity.ExecutablePath; }
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
