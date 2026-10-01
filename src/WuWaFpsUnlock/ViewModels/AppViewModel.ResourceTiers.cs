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
    private readonly ConcurrentDictionary<string, (int Generation, DateTime At)> _tierWrites = new();
    private DispatcherTimer? _tierTimer;
    private bool _readingTiers, _tiersRefreshPending, _tierMenuOpen, _tierSeeded;
    private string _tierWatchedRoot="";
    private int _tierObserverGeneration, _tierActivityQueued, _tierMetadataDirty, _tierVisibleActivity;
    private void ApplyResourceTierInfo(ResourceTierInfo[] result)
    {
        var now=DateTime.UtcNow;
        // A disk read can finish after a newer write event; preserve the newer observed activity.
        for(int i=0;i<result.Length;i++)
            if(_tierWrites.TryGetValue(result[i].Tier,out var write)&&write.Generation==_tierObserverGeneration&&now-write.At<TimeSpan.FromSeconds(8))
                result[i]=result[i] with{Status=TierDownloadStatus.Downloading};
        if(UhdTier!=result[0]){UhdTier=result[0];Notify(nameof(UhdTier));}
        if(HdTier!=result[1]){HdTier=result[1];Notify(nameof(HdTier));}
        if(SdTier!=result[2]){SdTier=result[2];Notify(nameof(SdTier));}
        Volatile.Write(ref _tierVisibleActivity,
            (UhdTier.Status==TierDownloadStatus.Downloading?1:0)|
            (HdTier.Status==TierDownloadStatus.Downloading?2:0)|
            (SdTier.Status==TierDownloadStatus.Downloading?4:0));
    }
    private void QueueTierActivity(int generation,string root)
    {
        if(Interlocked.Exchange(ref _tierActivityQueued,1)!=0)return;
        _dispatcher.BeginInvoke(new Action(()=>
        {
            if(generation!=_tierObserverGeneration||_closing||_tierWatcher is null||root!=GameRoot)return;
            Interlocked.Exchange(ref _tierActivityQueued,0);
            if(!_tierMenuOpen)return;
            var now=DateTime.UtcNow;
            var current=new[]{UhdTier,HdTier,SdTier};
            for(int i=0;i<current.Length;i++)
                if(_tierWrites.TryGetValue(current[i].Tier,out var time)&&time.Generation==generation&&now-time.At<TimeSpan.FromSeconds(8))
                    current[i]=current[i] with{Status=TierDownloadStatus.Downloading};
            ApplyResourceTierInfo(current);
            if(Interlocked.Exchange(ref _tierMetadataDirty,0)!=0)_=RefreshResourceTiersAsync();
        }),DispatcherPriority.Background);
    }
    public async Task RefreshResourceTiersAsync()
    {
        if (_closing) return;
        EnsureResourceTierObservation();
        if (_readingTiers) { _tiersRefreshPending=true; return; }
        _readingTiers = true;
        var root = GameRoot;
        try
        {
            int generation=_tierObserverGeneration;
            bool seed=!_tierSeeded;_tierSeeded=true;
            var result = await Task.Run(() =>
            {
                if(seed)SeedRecentTierWrites(root,generation);
                return ResourceTierCatalog.Read(root, tier =>
                    _tierWrites.TryGetValue(tier,out var time)&&time.Generation==generation&&DateTime.UtcNow-time.At<TimeSpan.FromSeconds(8));
            });
            if (_closing || root != GameRoot) return;
            ApplyResourceTierInfo(result);
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

        }
        finally
        {
            _readingTiers=false;
            if(_tiersRefreshPending&&!_closing){_tiersRefreshPending=false;_=RefreshResourceTiersAsync();}
        }
    }
    // Keep only lightweight write timestamps while the menu is closed; no polling or UI updates.
    public void ObserveResourceTiers(bool observe)
    {
        _tierMenuOpen=observe;
        _tierTimer?.Stop();
        if(!observe||_closing)return;
        EnsureResourceTierObservation();
        ApplyResourceTierInfo([UhdTier,HdTier,SdTier]);
        _tierTimer ??=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
        if(!_tierTimerAttached){_tierTimer.Tick+=async(_,_)=>await RefreshResourceTiersAsync();_tierTimerAttached=true;}
        _tierTimer.Start();
        _=RefreshResourceTiersAsync();
    }
    private void StopResourceTierObservation()
    {
        ++_tierObserverGeneration;
        _tierWatcher?.Dispose();_tierWatcher=null;_tierWatchedRoot="";_tierSeeded=false;
        _tierTimer?.Stop();_tierWrites.Clear();
        Interlocked.Exchange(ref _tierActivityQueued,0);Interlocked.Exchange(ref _tierMetadataDirty,0);
        Volatile.Write(ref _tierVisibleActivity,0);
    }
    private void SeedRecentTierWrites(string root,int generation)
    {
        // One bounded metadata-only pass at attachment, confined to official tier download folders.
        // Never hash/read resource contents or follow directory links.
        var options=new EnumerationOptions{RecurseSubdirectories=true,IgnoreInaccessible=true,
            AttributesToSkip=FileAttributes.ReparsePoint,MaxRecursionDepth=16};
        foreach(var tier in ResourceTierCatalog.Tiers)
        {
            var folder=Path.Combine(root,"launcherDownload",tier);
            try
            {
                if(!Directory.Exists(folder))continue;
                WuWaFpsUnlock.Core.SafePaths.EnsureNoLinks(root,folder);
                int scanned=0;
                foreach(var file in new DirectoryInfo(folder).EnumerateFiles("*",options))
                {
                    if(generation!=Volatile.Read(ref _tierObserverGeneration))return;
                    if(++scanned>4096)break;
                    if(file.Extension.Equals(".json",StringComparison.OrdinalIgnoreCase))continue;
                    var at=file.LastWriteTimeUtc;
                    if(at>DateTime.UtcNow||DateTime.UtcNow-at>=TimeSpan.FromSeconds(8))continue;
                    _tierWrites.AddOrUpdate(tier,(generation,at),(_,old)=>old.Generation==generation&&old.At>at?old:(generation,at));
                    break;
                }
            }
            catch(Exception e) when(e is IOException or UnauthorizedAccessException){ /* Unknown remains unknown. */ }
        }
    }
    private void EnsureResourceTierObservation()
    {
        if(_closing)return;
        if(_tierWatcher is not null&&_tierWatchedRoot==GameRoot)return;
        StopResourceTierObservation();
        if(!Directory.Exists(GameRoot))return;
        int generation=_tierObserverGeneration;
        try
        {
            var watchedRoot = GameRoot;
            _tierWatcher = new FileSystemWatcher(watchedRoot) { IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName };
            FileSystemEventHandler changed = (_, e) =>
            {
                var relative = Path.GetRelativePath(watchedRoot, e.FullPath);
                if(generation!=Volatile.Read(ref _tierObserverGeneration))return;
                bool metadata=relative.Equals("launcherDownloadConfig.json",StringComparison.OrdinalIgnoreCase)||
                    relative.Equals(Path.Combine("launcherDownload","launcherDownloadConfig.json"),StringComparison.OrdinalIgnoreCase)||
                    relative.StartsWith("launcherDownloadConfig"+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
                if(metadata){Interlocked.Exchange(ref _tierMetadataDirty,1);if(Volatile.Read(ref _tierMenuOpen))QueueTierActivity(generation,watchedRoot);return;}
                if(e.ChangeType==WatcherChangeTypes.Deleted||e.FullPath.EndsWith(".json",StringComparison.OrdinalIgnoreCase))return;
                if(!relative.StartsWith("launcherDownload"+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&
                    !e.FullPath.EndsWith(".pak",StringComparison.OrdinalIgnoreCase))return;
                var tier=ResourceTierCatalog.TierFromResourcePath(relative);
                if(tier is null)return;
                _tierWrites[tier]=(generation,DateTime.UtcNow);
                int bit=tier=="uhd"?1:tier=="hd"?2:4;
                if(Volatile.Read(ref _tierMenuOpen)&&(Volatile.Read(ref _tierVisibleActivity)&bit)==0)QueueTierActivity(generation,watchedRoot);
            };
            _tierWatcher.Changed += changed; _tierWatcher.Created += changed;
            _tierWatcher.Renamed += (_,e)=>changed(null!,e);_tierWatcher.Deleted += changed;
            _tierWatcher.Error += (_, _) => _tierWrites.Clear();
            _tierWatcher.EnableRaisingEvents = true;
            _tierWatchedRoot=watchedRoot;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { Log("包体下载活动监测不可用：" + e.Message); }
    }
    private bool _tierTimerAttached;
}
