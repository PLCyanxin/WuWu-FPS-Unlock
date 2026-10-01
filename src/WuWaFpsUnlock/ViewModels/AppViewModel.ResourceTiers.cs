using System.Collections.Concurrent;
using System.Windows.Threading;
using WuWaFpsUnlock.Services;

namespace WuWaFpsUnlock.ViewModels;
public sealed partial class AppViewModel
{
    public ResourceTierInfo UhdTier { get; private set; } = new("uhd", TierDownloadStatus.Unknown);
    public ResourceTierInfo HdTier { get; private set; } = new("hd", TierDownloadStatus.Unknown);
    public ResourceTierInfo SdTier { get; private set; } = new("sd", TierDownloadStatus.Unknown);
    private FileSystemWatcher? _tierWatcher;
    private readonly ConcurrentDictionary<string, DateTime> _tierWrites = new();
    private DispatcherTimer? _tierTimer;
    private bool _readingTiers;
    public async Task RefreshResourceTiersAsync()
    {
        if (_readingTiers || _closing) return;
        _readingTiers = true;
        var root = GameRoot;
        try
        {
            var result = await Task.Run(() => ResourceTierCatalog.Read(root, tier =>
                _tierWrites.TryGetValue(tier, out var time) && DateTime.UtcNow - time < TimeSpan.FromSeconds(8)));
            if (_closing || root != GameRoot) return;
            UhdTier = result[0]; HdTier = result[1]; SdTier = result[2];
            if (ResourceTierIndex < 0 && Directory.Exists(root))
            {
                string? tier = null;
                try { tier = (await Task.Run(() => OfficialLaunchOptions.ReadResourceTier(root))).Replace("-krqlv=", ""); }
                catch (Exception e) { Log("包体档位未迁移，请手动选择：" + e.Message); }
                if (root != GameRoot || _closing) return;
                if (tier is null)
                {
                    var installed = result.Where(x => x.Status == TierDownloadStatus.Installed).ToArray();
                    if (installed.Length == 1) tier = installed[0].Tier;
                }
                if (ResourceTierIndex < 0 && tier is "uhd" or "hd" or "sd")
                { _settings.ResourceTier = tier; Save(); Notify(nameof(ResourceTierIndex)); }
            }
            Notify(nameof(UhdTier)); Notify(nameof(HdTier)); Notify(nameof(SdTier));
        }
        finally { _readingTiers = false; }
    }
    public void ObserveResourceTiers(bool observe)
    {
        _tierWatcher?.Dispose(); _tierWatcher = null;
        _tierTimer?.Stop(); _tierWrites.Clear();
        if (!observe || !Directory.Exists(GameRoot)) return;
        try
        {
            var watchedRoot = GameRoot;
            _tierWatcher = new FileSystemWatcher(watchedRoot) { IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName };
            FileSystemEventHandler changed = (_, e) =>
            {
                var relative = Path.GetRelativePath(watchedRoot, e.FullPath);
                if (!relative.StartsWith("launcherDownload" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                    !e.FullPath.EndsWith(".pak", StringComparison.OrdinalIgnoreCase)) return;
                if (e.FullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return;
                var tier = ResourceTierCatalog.TierFromResourcePath(relative);
                if (tier is not null) _tierWrites[tier] = DateTime.UtcNow;
            };
            _tierWatcher.Changed += changed; _tierWatcher.Created += changed;
            _tierWatcher.Error += (_, _) => _tierWrites.Clear();
            _tierWatcher.EnableRaisingEvents = true;
            _tierTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            if (!_tierTimerAttached) { _tierTimer.Tick += async (_, _) => await RefreshResourceTiersAsync(); _tierTimerAttached = true; }
            _tierTimer.Start();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { Log("包体下载活动监测不可用：" + e.Message); }
        _ = RefreshResourceTiersAsync();
    }
    private bool _tierTimerAttached;
}
