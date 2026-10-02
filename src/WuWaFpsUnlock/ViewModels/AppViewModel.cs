using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using WuWaFpsUnlock.Core;
using WuWaFpsUnlock.Services;

namespace WuWaFpsUnlock.ViewModels;
public sealed partial class AppViewModel:INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? SettingsRequested;
    public event Action? GameReady;
    public event Action? GameStarted;
    public event Action? GameExited;
    public event Action? LaunchFailed;
    private bool _closing;
    private bool _gameExitPending, _completingGameExit;
    private bool _sessionReady;
    private Task _sessionCleanup=Task.CompletedTask;
    private readonly RestartController _restart=new();
    private FpsSession? _fpsSession;
    private UserSettings _settings;
    private UserSettings _savedSettings;
    private HardwareInfo _hardware=HardwareInfo.Unknown;
    private bool _busy,_running,_starting,_validFps=true;
    private string _status="请先在设置中确认游戏路径。",_logs="",_fpsInput="240",_reShade="未设置游戏路径",_deployState="未检测",_packageState="";
    private readonly Dispatcher _dispatcher=Application.Current.Dispatcher;
    private readonly DispatcherTimer _monitor=new(){Interval=TimeSpan.FromSeconds(1)};
    private string _lastValidExe="";
    private MenuShortcut? _observedMenuShortcut=MenuShortcut.Home;

    private bool _findingGame;
    private bool _refreshing;
    private int _refreshGeneration;
    private readonly Func<Action<string>,HardwareInfo> _probeEnvironment;
    private readonly Func<HardwareInfo,Action<string>,HardwareInfo> _probeHags;
    private readonly Func<string,IEnumerable<string>,CancellationToken,Task<GameDiscoveryResult>> _discover;
    private Process? _game;
    private readonly CancellationTokenSource _lifetime=new();
    private readonly string _logFile;
    private readonly object _logLock=new();
    public AppViewModel(Func<string,IEnumerable<string>,CancellationToken,Task<GameDiscoveryResult>>? discover=null, Func<Action<string>,HardwareInfo>? probeEnvironment=null, Func<HardwareInfo,Action<string>,HardwareInfo>? probeHags=null)
    {
        _probeEnvironment=probeEnvironment??EnvironmentProbe.ReadBasic;
        _probeHags=probeHags??EnvironmentProbe.ReadHagsRuntime;
        _discover=discover??((root,hints,token)=>GameDiscoveryService.DiscoverAsync(root,hints,true,token));
        Directory.CreateDirectory(AppPaths.Data);Directory.CreateDirectory(Path.Combine(AppPaths.Data,"logs"));
        _logFile=Path.Combine(AppPaths.Data,"logs",DateTime.Now.ToString("yyyyMMdd-HHmmss",CultureInfo.InvariantCulture)+".log");
        try{_settings=File.Exists(AppPaths.Settings)?JsonFiles.Read<UserSettings>(AppPaths.Settings):new();}
        catch(Exception e){_settings=new();Log("设置文件无法读取，保留原文件且载入空设置："+e.Message);}
        _settings.PackageManifest=PackageManifestLocation.Resolve(_settings.PackageManifest,AppPaths.Base);
        if(_settings.TargetFps is <30 or >420)_settings.TargetFps=240;
        _savedSettings=_settings.Clone();
        _fpsInput=_settings.TargetFps.ToString(CultureInfo.InvariantCulture);
        OpenSettingsCommand=new(()=>SettingsRequested?.Invoke(),()=>!Busy);
        BrowseDirectoryCommand=new(BrowseRoot,()=>!Busy&&!IsGameRunning);
        BrowseExeCommand=new(BrowseExe,()=>!Busy&&!IsGameRunning);
        FindGameCommand=new(FindGameAsync,()=>CanEditSettings,ReportError);
        IncrementFpsCommand=new(()=>TargetFps=Math.Min(420,TargetFps+1),()=>CanEditFps);
        DecrementFpsCommand=new(()=>TargetFps=Math.Max(30,TargetFps-1),()=>CanEditFps);
        ClearLogCommand=new(()=>{_logs="";Notify(nameof(Logs));lock(_logLock)File.WriteAllText(_logFile,"");});
        ExportLogCommand=new(ExportLogsAsync,()=>!_closing,ReportError);
        RefreshCommand=new(RefreshAsync,()=>!Busy&&!_refreshing,ReportError);
        DeployCommand=new(DeployAsync,()=>!Busy&&!IsGameRunning,ReportError);
        CleanCommand=new(CleanAsync,()=>!Busy&&!IsGameRunning,ReportError);
        StartCommand=new(StartAsync,()=>!Busy&&(!FpsEnabled||_validFps),ReportError);
        InitializeUpdates();
        _monitor.Tick+=Monitor;
        RememberValidSelection();
        Log($"鸣潮 FPS Unlock {CurrentUpdateVersion} 启动。");
    }
    public bool Busy {get=>_busy;private set{if(_busy==value)return;_busy=value;LauncherScheduling.SetBusy(value);NotifyAll();}}
    public bool IsGameRunning {get=>_running;private set{if(_running==value)return;_running=value;if(value)_monitor.Start();else _monitor.Stop();NotifyAll();}}
    public bool CanChangeFpsMode=>!Busy;
    public bool CanEditFps=>!Busy&&FpsEnabled;
    public bool CanEditSettings=>!Busy&&!IsGameRunning;
    public bool FpsEnabled {get=>_settings.FpsEnabled;set{if(!CanChangeFpsMode)return;_settings.FpsEnabled=value;if(Save())Status="FPS 开关下次启动生效";NotifyAll();}}
    public bool MfgSelected {get=>_settings.MfgSelected;set{if(!CanEditSettings)return;_settings.MfgSelected=value;Save();NotifyAll();}}
    public int ResourceTierIndex
    {
        get => _settings.ResourceTier switch { "uhd" => 0, "hd" => 1, "sd" => 2, _ => -1 };
        set
        {
            if (Busy || value is < 0 or > 2) return;
            _settings.ResourceTier = value switch { 0 => "uhd", 1 => "hd", _ => "sd" };
            if(Save()) Status = "包体档位已保存，下次启动游戏生效"; Notify(nameof(ResourceTierIndex));
        }
    }
    public int TargetFps
    {
        get=>_settings.TargetFps;
        set
        {
            value=Math.Clamp(value,30,420);if(_settings.TargetFps==value&&_validFps)return;
            _settings.TargetFps=value;_fpsInput=value.ToString(CultureInfo.InvariantCulture);_validFps=true;if(Save())Status="FPS 设置已保存 · 下次启动生效";NotifyAll();
        }
    }
    public string FpsInput
    {
        get=>_fpsInput;
        set
        {
            _fpsInput=value;
            if(int.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out int fps)&&fps is >=30 and <=420){_settings.TargetFps=fps;_validFps=true;if(Save())Status="FPS 设置已保存 · 下次启动生效";}
            else{_validFps=false;Status="目标 FPS 必须是 30–420 的整数；无效值没有保存。";}
            NotifyAll();
        }
    }
    public string GameRoot{get=>_settings.GameRoot;set{RememberValidSelection();if(!ApplyGameEntry(value)){_settings.GameRoot=value;_settings.GameExe="";}Save();NotifyAll();_ = RefreshResourceTiersAsync();}}
    public string GameExe{get=>_settings.GameExe;set{RememberValidSelection();if(!ApplyGameEntry(value))_settings.GameExe=value;Save();NotifyAll();_ = RefreshResourceTiersAsync();}}
    private bool ApplyGameEntry(string entry)
    {
        if(!GameDiscoveryService.TryNormalizeEntry(entry,out var candidate,out _))return false;
        _settings.GameRoot=candidate!.GameRoot;_settings.GameExe=candidate.ShippingExePath;return true;
    }
    private void RememberValidSelection(){if(GameDiscoveryService.TryValidateSelection(_settings.GameRoot,_settings.GameExe,out var current,out _))_lastValidExe=current!.ShippingExePath;}
    public string Status{get=>_status;private set{_status=value;Notify();}}
    public string Logs=>_logs;
    public string VersionLabel=>"v"+CurrentUpdateVersion;
    public string Gpu=>_hardware.Gpu;
    public string Driver=>_hardware.DriverText;
    public string Os=>_hardware.Os;
    public string Hags=>_hardware.Hags;
    public string MenuKeyText=>(_settings.MenuShortcut??_observedMenuShortcut)?.DisplayName??"未识别";
    public bool SetMenuShortcut(MenuShortcut shortcut)
    {
        if(!CanEditSettings||!shortcut.IsValid)return false;
        _settings.MenuShortcut=shortcut;
        bool saved=Save();
        if(saved)Status=shortcut.Key==0?"菜单按键已清空，下次启动游戏生效。":"菜单按键已保存，下次启动游戏生效。";
        Notify(nameof(MenuKeyText));return saved;
    }
    public string DynamicStatus=>_hardware.DynamicText.Replace("；游戏内能力待确认","");
    public string DynamicBackground=>_hardware.IsAdaGeForce&&_hardware.Driver>=59541?"#E2F7EC":"#FFF3DF";
    public string DynamicForeground=>_hardware.IsAdaGeForce&&_hardware.Driver>=59541?"#058853":"#956B1F";
    public string DynamicDetail=>_hardware.Driver is int d?$"当前驱动 {d/100}.{d%100:00}；Dynamic 门槛 ≥ 595.41。部署≠游戏内已生效。":"无法确认驱动条件；不会把未知标成支持。";
    public string ReShadeStatus=>_reShade;
    public string DeploymentState=>_deployState;
    public string PackageStatus=>_packageState;
    public string StartLabel=>_starting?"正在启动…":IsGameRunning?"游戏中":"开始游戏";
    public string DeployLabel=>Busy?"处理中…":"开始部署";
    public RelayCommand OpenSettingsCommand{get;}
    public RelayCommand BrowseDirectoryCommand{get;}
    public RelayCommand BrowseExeCommand{get;}
    public RelayCommand IncrementFpsCommand{get;}
    public RelayCommand DecrementFpsCommand{get;}
    public RelayCommand ClearLogCommand{get;}
    public AsyncCommand ExportLogCommand{get;}
    public AsyncCommand RefreshCommand{get;}
    public AsyncCommand DeployCommand{get;}
    public AsyncCommand CleanCommand{get;}
    public AsyncCommand StartCommand{get;}
    public AsyncCommand FindGameCommand{get;}
    public string FindGameLabel=>_findingGame?"正在查找…":"帮我查找鸣潮";
    public void Log(string text)
    {
        if(!_dispatcher.CheckAccess()){_dispatcher.Invoke(()=>Log(text));return;}
        string line=$"[{DateTime.Now:HH:mm:ss}] {DiagnosticExport.Redact(text)}";
        _logs+=line+Environment.NewLine;if(_logs.Length>180000)_logs=_logs[^120000..];Notify(nameof(Logs));
        try{if(!string.IsNullOrWhiteSpace(_logFile))lock(_logLock)File.AppendAllText(_logFile,line+Environment.NewLine);}catch{ /* UI retains error details if log destination is unavailable. */ }
    }
    public void ReportError(Exception e)
    {
        Status=e is OperationCanceledException?e.Message:"操作未完成："+e.Message;Log("异常详情："+e);NotifyAll();
    }
    private async Task ExportLogsAsync()
    {
        var dialog=new SaveFileDialog { Title="导出诊断日志", Filter="文本文件 (*.txt)|*.txt", DefaultExt=".txt", AddExtension=true,
            FileName="WuWa-FPS-Unlock-诊断-"+DateTime.Now.ToString("yyyyMMdd-HHmmss",CultureInfo.InvariantCulture)+".txt" };
        if(dialog.ShowDialog()!=true)return;
        var destination=Path.GetFullPath(dialog.FileName);
        var snapshot=_settings.Clone();
        if(destination.Equals(Path.GetFullPath(_logFile),StringComparison.OrdinalIgnoreCase) ||
            (Directory.Exists(snapshot.GameRoot) && SafePaths.IsInside(snapshot.GameRoot,destination)))
            throw new IOException("诊断日志不能覆盖运行日志或写入所选游戏目录，请选择其他位置。");
        var hardware=_hardware;
        var currentStatus=Status;
        var currentReShade=_reShade;
        var currentDeployment=_deployState;
        var exportedAt=DateTimeOffset.Now;
        await Task.Run(async ()=>
        {
            var details=new List<string>
            {
                "导出时间："+exportedAt.ToString("O",CultureInfo.InvariantCulture),
                "产品版本："+CurrentUpdateVersion,
                "构建标识："+(typeof(AppViewModel).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute),false).OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion??"未捕获"),
                "程序集版本："+(typeof(AppViewModel).Assembly.GetName().Version?.ToString()??"未知"),
                "启动器路径："+AppPaths.Base,
                "所选游戏目录："+snapshot.GameRoot,
                "所选 Shipping EXE："+snapshot.GameExe,
                "FPS："+(snapshot.FpsEnabled?"开启":"关闭")+"；目标："+snapshot.TargetFps,
                "MFG 部署选择："+(snapshot.MfgSelected?"开启":"关闭"),
                "菜单按键："+(snapshot.MenuShortcut?.DisplayName??"未设置；使用游戏内配置"),
                "包体档位："+snapshot.ResourceTier,
                "材料清单："+snapshot.PackageManifest+"；存在："+File.Exists(snapshot.PackageManifest),
                "系统："+hardware.Os+"；GPU："+hardware.Gpu+"；驱动："+hardware.DriverText+"；HAGS："+hardware.Hags,
                "后台调度："+LauncherScheduling.Status,
                "启动器当前状态："+currentStatus+"；部署概况："+currentDeployment+"；ReShade 概况："+currentReShade,
                "启动器日志源："+_logFile
            };
            string? reShadeDirectory=null;
            string selection="未确认所选游戏的 ReShade 加载目录。";
            try
            {
                var receipt=AppPaths.LoadReceipt(snapshot);
                details.Add("部署记录："+(receipt is null?"无":$"{receipt.Status}；包：{receipt.PackageId}；更新时间：{receipt.Updated:O}；文件来源："+
                    string.Join(", ",receipt.Files.GroupBy(f=>f.SourceKind).Select(g=>g.Key+"="+g.Count()))));
            }
            catch(Exception error){details.Add("部署记录读取失败："+error);}
            try
            {
                if(!string.IsNullOrWhiteSpace(snapshot.GameRoot)&&!string.IsNullOrWhiteSpace(snapshot.GameExe))
                {
                    var info=new ReShadeService(_=>{}).Inspect(snapshot);
                    selection="ReShade 检测："+info.Description+"；代理："+(info.Proxy??"无");
                    if(info.Proxy is not null && (info.State is "Reusable" or "UpgradeRequired" ||
                        PeInspector.AddonBuild(info.Proxy) is "FullCandidate" or "UnknownReShade"))
                    {
                        reShadeDirectory=Path.GetDirectoryName(info.Proxy);
                        selection+="；日志仅取已识别代理的加载目录："+reShadeDirectory;
                    }
                }
            }
            catch(Exception error){selection="ReShade/部署来源检测失败："+error;}
            await DiagnosticExport.WriteAsync(destination,details,DiagnosticExport.RecentLauncherLogs(_logFile),reShadeDirectory,selection);
        });
        Log("诊断日志已导出："+destination);
        Status="诊断日志已导出。";
    }
    private bool Save()
    {
        try{JsonFiles.Save(AppPaths.Settings,_settings);_savedSettings=_settings.Clone();return true;}
        catch(Exception e)
        {
            _settings=_savedSettings.Clone();_fpsInput=_settings.TargetFps.ToString(CultureInfo.InvariantCulture);_validFps=true;
            Status="设置未能保存："+e.Message;Log(Status);NotifyAll();return false;
        }
    }
    private void Notify([CallerMemberName]string? name=null)=>PropertyChanged?.Invoke(this,new(name));
    private void NotifyAll()
    {
        PropertyChanged?.Invoke(this,new(null));
        OpenSettingsCommand?.Refresh();BrowseDirectoryCommand?.Refresh();BrowseExeCommand?.Refresh();IncrementFpsCommand?.Refresh();DecrementFpsCommand?.Refresh();
        StartCommand?.Refresh();DeployCommand?.Refresh();CleanCommand?.Refresh();RefreshCommand?.Refresh();
        FindGameCommand?.Refresh();CheckUpdatesCommand?.Refresh();RollbackVersionCommand?.Refresh();
    }
    private void BrowseRoot()
    {
        var dlg=new OpenFolderDialog{Title="选择鸣潮游戏根目录（不是整个磁盘）",Multiselect=false};
        if(dlg.ShowDialog()==true){GameRoot=dlg.FolderName;if(GameDiscoveryService.TryResolveGameRoot(GameRoot,out var candidate,out _)){GameExe=candidate!.ShippingExePath;Log("已按所选目录定位游戏 EXE："+GameExe);}else Log("已选择目录；如需搜索，请点击帮我查找鸣潮。");_ =RefreshAsync();}
    }
    private void BrowseExe()
    {
        var selected=GameExecutablePicker.Show(Directory.Exists(GameRoot)?GameRoot:GameExe);
        if(selected is not null){GameExe=selected;_ =RefreshAsync();}
    }
    public async Task RefreshAsync()
    {
        // A read-only diagnostic must not reserve the launch/deployment operation lock.
        if(Busy||_refreshing||_closing)return;
        _refreshing=true;RefreshCommand.Refresh();
        try{await RefreshCore(passive:true);}
        finally{_refreshing=false;RefreshCommand.Refresh();}
    }
    private async Task RefreshCore(bool passive=false)
    {
        int generation=++_refreshGeneration;
        var hardware=await Task.Run(()=>_probeEnvironment(Log));
        if(_closing||generation!=_refreshGeneration)return;
        _hardware=hardware;NotifyAll();
        _ = RefreshHagsAsync(hardware,generation);
        await RefreshRollbackAvailabilityAsync();
        if(_closing||generation!=_refreshGeneration||(passive&&Busy))return;
        var snapshot=_settings.Clone();

        string reShade,deployState;
        MenuShortcut? observedShortcut=MenuShortcut.Home;
        try
        {
            if(!string.IsNullOrWhiteSpace(snapshot.GameRoot)&&!string.IsNullOrWhiteSpace(snapshot.GameExe))
            {
                var result=await Task.Run(()=>
                {
                    var info=new ReShadeService(Log).Inspect(snapshot);
                    MenuShortcut? shortcut=null;
                    if(info.State!="Conflict")
                        try{shortcut=MenuShortcut.Read(IniDocument.Load(info.Ini));}
                        catch(Exception e){Log("菜单按键读取未完成："+e.Message);}
                    return (info,receipt:AppPaths.LoadReceipt(snapshot),shortcut);
                });
                reShade=result.info.Description;
                var receipt=result.receipt;
                observedShortcut=result.shortcut;
                deployState=receipt is null?"未部署":receipt.Status=="PartialFailure"?"上次部署未完成":receipt.Status=="Cleaned"?"已清除本工具插件":receipt.Status=="CleanedWithSkips"?"清除结束 · 部分已变化项目保留":"已有部署记录";
            }
            else{reShade="未设置游戏路径";deployState="请先选择路径";}
        }
        catch(Exception e){reShade="检测未完成";deployState=e.Message;Log("环境与部署状态检测失败："+e);}
        // A path change or a newer post-operation refresh invalidates older results.
        if(_closing||generation!=_refreshGeneration||(passive&&Busy)||
           snapshot.GameRoot!=GameRoot||snapshot.GameExe!=GameExe)return;
        _reShade=reShade;_deployState=deployState;
        _observedMenuShortcut=observedShortcut;
        _packageState=File.Exists(_settings.PackageManifest)?"文件包："+Path.GetFileName(Path.GetDirectoryName(_settings.PackageManifest)):"启动器部署清单缺失，请完整解压完整版启动器。";
        NotifyAll();
    }
    private async Task RefreshHagsAsync(HardwareInfo basic,int generation)
    {
        try
        {
            var completed=await Task.Run(()=>_probeHags(basic,Log));
            if(_closing||generation!=_refreshGeneration||_hardware.Gpu!=basic.Gpu||_hardware.Driver!=basic.Driver)return;
            // Runtime diagnostics are display-only; configured HAGS and all safety inputs stay unchanged.
            _hardware=_hardware with { Hags=completed.Hags };Notify(nameof(Hags));
        }
        catch(Exception e)
        {
            if(_closing||generation!=_refreshGeneration)return;
            _hardware=_hardware with { Hags=EnvironmentStatus.HagsText(null,basic.HagsConfigured) };
            Notify(nameof(Hags));Log("HAGS 后台检测未完成："+e);
        }
    }
    private async Task DeployAsync()
    {
        var elapsed=Stopwatch.StartNew();
        Log($"部署请求：游戏={GameExe}；MFG={MfgSelected}；清单={_settings.PackageManifest}");
        if(!MfgSelected){Status=FpsEnabled?"FPS 解锁无需部署，点击开始游戏即可。":"未选择多帧生成，无需部署。";Log(Status);return;}
        GameProcesses.ValidateExe(_settings);
        var materialPath=PackageManifestLocation.Resolve(_settings.PackageManifest,AppPaths.Base);
        if(materialPath!=_settings.PackageManifest){_settings.PackageManifest=materialPath;if(!Save())return;Log("已使用当前便携包中的材料清单："+materialPath);}
        if(!File.Exists(_settings.PackageManifest))
        {
            Status="启动器部署文件缺失，请完整解压完整版启动器。";
            var message=PackageManifestLocation.RecoveryMessage(_settings.PackageManifest);
            Log(message);MessageBox.Show(message,"部署文件缺失",MessageBoxButton.OK,MessageBoxImage.Warning);
            return;
        }
        Busy=true;Status="正在校验材料并搜索同名目标…";
        try
        {
            var snapshot=_settings.Clone();var service=new DeploymentService(Log);
            var manifest=PackageReader.Load(snapshot.PackageManifest);
            PackageManifestLocation.RequireSources(manifest,snapshot.PackageManifest);
            string preview=await service.PreviewAsync(snapshot);
            var existing=new ReShadeService(Log).Inspect(snapshot,manifest.ReShade);
            bool upgrade=existing.State=="UpgradeRequired";
            if(existing.State!="Reusable")
            {
                string setup=await new ReShadeService(Log).ResolveLocalSetupAsync(snapshot.PackageManifest,manifest.ReShade);
                preview+=$"\n本地安装器：{setup}\nReShade 目标：{existing.Proxy??Path.Combine(Path.GetDirectoryName(snapshot.GameExe)!,manifest.ReShade.ProxyApi+".dll")}\n";
            }
            preview+=upgrade?"\n本次需要升级已有 ReShade 本体，保留配置、滤镜及其他 addon。":"\n已有兼容 ReShade 优先复用。";
            preview+="\n第三方插件的游戏内兼容性仍待验证。安装器可能访问官方兼容表，不下载额外滤镜。";
            Log(preview);
            if(!OperationReview.Show("确认部署",preview)){Status="已取消部署，未写入游戏。";return;}
            Status="正在部署；请保持游戏关闭。";
            try{await service.DeployAsync(snapshot,upgrade);}
            catch(NeedsElevationException){await ElevatedWorker.RunFromUi("deploy",snapshot,upgrade,Log,service.ApprovalFingerprint);}
            Status="文件部署及校验完成；MFG 游戏内实际效果待确认。";await RefreshCore();
        }
        catch(IOException e) when(e.Message.StartsWith(GameFileBaselineStore.MissingFilesMessage,StringComparison.Ordinal))
        {
            Status=GameFileBaselineStore.MissingFilesMessage;Log(e.Message);
            if(MessageBox.Show(Status+"。\n\n如果已经完成官方修复，但官方更新移动或移除了旧运行库，可以查看并重建路径记录。是否查看重建计划？",
                "文件缺失",MessageBoxButton.OKCancel,MessageBoxImage.Warning)==MessageBoxResult.OK)
            {
                GameProcesses.RequireStopped(GameRoot);MaterialSafety.RequireOutsideGame(GameRoot,AppPaths.Base);
                var plan=await BaselineRebuild.PrepareAsync(AppPaths.Baseline(GameExe),AppPaths.Receipt(GameExe),GameRoot,GameExe);
                if(OperationReview.Show("重建游戏路径记录",plan.Preview))
                {
                    string archive=await BaselineRebuild.CommitAsync(plan,()=>GameProcesses.RequireStopped(GameRoot));
                    Status="路径记录已重建，请再次点击开始部署。";Log(Status+" 原记录："+archive);
                }
            }
        }
        finally{Busy=false;Log($"部署操作结束：{Status}；耗时={elapsed.Elapsed.TotalSeconds:F1}s");}
    }
    private async Task CleanAsync()
    {
        var elapsed=Stopwatch.StartNew();
        Log("清除请求：游戏="+GameExe);
        GameProcesses.ValidateExe(_settings);GameProcesses.RequireStopped(GameRoot);
        Busy=true;
        try
        {
            var snapshot=_settings.Clone();var service=new DeploymentService(Log);
            string preview=(await service.CleanPreviewAsync(snapshot))+"\n按来源记录处理本工具新装ReShade，保留用户本体、滤镜及其他插件。无原 DLL 备份；清除后是否自动补齐需由游戏验证，必要时使用官方校验。";
            Log(preview);
            if(!OperationReview.Show("确认清除插件",preview)){Status="已取消清除。";return;}
            Status="正在核对部署记录并清除…";
            try{await service.CleanAsync(snapshot);}
            catch(NeedsElevationException){await ElevatedWorker.RunFromUi("clean",snapshot,false,Log,service.ApprovalFingerprint);}
            _settings.MfgSelected=false;bool saved=Save();
            if(!saved){Log("插件已清除，但部署选择未能保存；请检查配置文件权限后关闭多帧生成部署选择。");return;}
            Status=AppPaths.LoadReceipt(snapshot)?.Status=="CleanedWithSkips"?"清除结束，已变化文件或配置已跳过；请查看日志。":"已移除登记且未变化的替换 DLL 和自有插件；ReShade 已按来源记录处理。";
            await RefreshCore();
        }
        finally{Busy=false;Log($"清除操作结束：{Status}；耗时={elapsed.Elapsed.TotalSeconds:F1}s");}
    }
    private async Task FindGameAsync()
    {
        if(!CanEditSettings)return;_findingGame=true;Notify(nameof(FindGameLabel));Busy=true;
        bool alreadyValid=GameDiscoveryService.TryValidateSelection(GameRoot,GameExe,out _,out _);
        try{await EnsureGameSelectionAsync();if(!alreadyValid)await RefreshCore();}
        finally{_findingGame=false;Notify(nameof(FindGameLabel));Busy=false;}
    }
    private async Task EnsureGameSelectionAsync()
    {
        string rootAtStart=GameRoot,exeAtStart=GameExe;
            Status="正在查找鸣潮…";Log(Status);
            var result=await _discover(rootAtStart,[exeAtStart,_lastValidExe],_lifetime.Token);
            if(GameRoot!=rootAtStart||GameExe!=exeAtStart){Log("查找期间路径已修改，本次结果未应用；需要时请再次点击帮我查找鸣潮。");return;}
            foreach(var line in result.Diagnostics)Log(line);
            if(result.Candidates.Count==0){Status="未找到可验证的鸣潮安装，请使用手动选择。";Log(Status);return;}
            GameDiscoveryCandidate? chosen=result.Candidates.Count==1?result.Candidates[0]:GameSelection.Show(result.Candidates);
            if(chosen is null){Status="未选择安装，保留当前路径。";Log(Status);return;}

            GameRoot=chosen.GameRoot;GameExe=chosen.ShippingExePath;_lastValidExe=chosen.ShippingExePath;

            Log("自动查找已更正："+GameExe);
    }
    private async Task StartAsync()
    {
        if(Busy||_closing)return;_starting=true;Busy=true;
        var elapsed=Stopwatch.StartNew();
        bool startedThisAttempt=false;
        string? resourceTier = null;
        var snapshot=_settings.Clone();
        Log($"启动请求：Shipping={snapshot.GameExe}；FPS={snapshot.FpsEnabled}；目标={snapshot.TargetFps}；MFG={snapshot.MfgSelected}；包体={snapshot.ResourceTier}；菜单={snapshot.MenuShortcut?.DisplayName??"游戏内配置"}");
        string? lastPhase=null;
        try
        {
            var process=await _restart.RunAsync(new(snapshot.GameExe,snapshot.FpsEnabled),new WindowsRestartProcessCatalog(),new RestartActions
            {
                Preflight=async token=>
                {
                    if(RollbackBackupCatalog.FindInterrupted(AppPaths.Base) is string interrupted)
                        throw new IOException("检测到上次更新中断。请关闭启动器，运行备份目录中的恢复.cmd后重试："+interrupted);
                    GameProcesses.ValidateExe(snapshot);
                    if(snapshot.MenuShortcut is { } shortcut)_=shortcut.ToIniValue();
                    resourceTier = snapshot.ResourceTier switch { "uhd" => "-krqlv=uhd", "hd" => "-krqlv=hd", "sd" => "-krqlv=sd", _ => throw new InvalidDataException("请在主页选择包体档位，并在官方启动器完成对应资源下载。") };
                    var tierInfo = (await Task.Run(() => ResourceTierCatalog.Read(snapshot.GameRoot), token)).First(x => x.Tier == snapshot.ResourceTier);
                    if(tierInfo.Status == TierDownloadStatus.Missing)throw new IOException("所选“"+tierInfo.Name+"”包体尚未下载完成，请先在官方启动器下载该档位。");
                    if(snapshot.FpsEnabled && (snapshot.TargetFps is <30 or >420 || !_validFps))throw new InvalidDataException("目标FPS无效。");
                    var receipt=AppPaths.LoadReceipt(snapshot);
                    if(receipt is not null && (!Path.GetFullPath(receipt.GameRoot).Equals(Path.GetFullPath(snapshot.GameRoot),StringComparison.OrdinalIgnoreCase)||!Path.GetFullPath(receipt.GameExe).Equals(Path.GetFullPath(snapshot.GameExe),StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("部署记录与所选游戏安装不一致，请检查路径后重新部署。");
                    await DeploymentLaunchGuard.RequireReadyAsync(receipt,snapshot.MfgSelected,Log,token);
                    if(receipt is not null&&!string.IsNullOrWhiteSpace(receipt.ProxyPath)&&File.Exists(receipt.ProxyPath))
                    {
                        var info=new ReShadeService(Log).Inspect(snapshot,new ReShadeSpec{ProxyApi=Path.GetFileNameWithoutExtension(receipt.ProxyPath)});
                        if(info.State=="Conflict")throw new IOException(info.Description);
                    }
                    if(snapshot.FpsEnabled)await BuiltinFpsService.PreflightAsync(token);
                },
                ConfirmNotice=(_,token)=>
                {
                    var receipt=AppPaths.LoadReceipt(snapshot);
                    bool mfgPending=DeploymentNoticeStore.RequiresAcknowledgement(receipt);
                    bool fpsPending=snapshot.FpsEnabled&&BuiltinFpsService.RequiresNotice(snapshot.GameExe);
                    if(!mfgPending&&!fpsPending)return Task.FromResult(true);
                    const string notice="第三方组件可能存在兼容性问题、崩溃及账号风险；无法保证所有游戏版本兼容。\n\n开始游戏会直接结束同一安装的现有游戏并重新启动，可能中断当前操作或丢失尚未保存的状态。\n\n目标FPS不是实际帧率保证。\n\n清除DLL后可能需要官方文件校验；普通启动自动补齐尚未取得独立成功证据。\n\n确认后保存本次部署须知；后续日常启动不再提示。";
                    if(!OperationReview.Show("首次启动风险与须知",notice,fitContent:true))return Task.FromResult(false);
                    token.ThrowIfCancellationRequested();
                    if(mfgPending){DeploymentNoticeStore.Acknowledge(receipt!);AppPaths.SaveReceipt(receipt!);}
                    if(fpsPending)BuiltinFpsService.Acknowledge(snapshot.GameExe);
                    return Task.FromResult(true);
                },
                ReleaseOldSession=async _=>{await ReleaseFpsSessionAsync();_game?.Dispose();_game=null;_sessionReady=false;_gameExitPending=false;IsGameRunning=false;},
                Start=async (exe,token)=>
                {
                    token.ThrowIfCancellationRequested();
                    await MenuShortcutService.ApplyBeforeLaunchAsync(snapshot,Log,token);
                    var startInfo=new ProcessStartInfo(exe){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(exe)!};
                    if(resourceTier is not null){startInfo.ArgumentList.Add(resourceTier);Log("包体启动参数："+resourceTier);}
                    if(snapshot.MfgSelected){startInfo.ArgumentList.Add("-dx12");Log("多帧生成启动参数：-dx12；实际D3D12加载以游戏日志为准。");}
                    var process=LauncherScheduling.StartProcess(startInfo)??throw new IOException("系统未返回游戏进程。");
                    _game=process;startedThisAttempt=true;_sessionReady=false;IsGameRunning=true;GameStarted?.Invoke();
                    Log($"仅启动所选Shipping一次：{exe}；PID={process.Id}；FPS={(snapshot.FpsEnabled?"ON":"OFF")}；目标={snapshot.TargetFps}");
                    return process;
                },
                AttachFps=async (process,token)=>
                {
                    await BuiltinFpsService.WaitForRendererAsync(process,snapshot.GameExe,token,Log);
                    GameReady?.Invoke();
                    _fpsSession=new FpsSession();
                    await _fpsSession.ConnectAsync(process,AppPaths.FpsCore,Log,token);
                    await _fpsSession.SetFpsAsync(snapshot.TargetFps,token);
                    Log($"内置FPS设置已发送；PID={process.Id}；目标={snapshot.TargetFps}，实际帧率待游戏验证。");
                }
            },phase=>{Status=phase switch{"Preflight"=>"正在检查路径与组件…","AwaitingNotice"=>"检查首次部署须知…","ReleaseOldSession"=>"释放旧游戏会话…","Stopping"=>"正在结束同一安装的旧游戏…","Rechecking"=>"确认旧实例已退出…","Starting"=>"正在启动游戏…","AttachingFps"=>"等待游戏并连接内置FPS核心…","Cancelled"=>"已取消，未继续启动。",_=>"正在检查游戏状态…"};if(lastPhase!=phase){lastPhase=phase;Log("启动阶段："+Status);}},TimeSpan.FromSeconds(15),_lifetime.Token);
            if(process is null){Status="已取消须知，未结束或启动游戏。";return;}
            if(!snapshot.FpsEnabled){await BuiltinFpsService.WaitForRendererAsync(process,snapshot.GameExe,_lifetime.Token,Log);GameReady?.Invoke();}
            _sessionReady=true;_deferredUpdate.GameStarted();
            Status=snapshot.FpsEnabled?"游戏已启动 · 内置FPS已连接，实际效果以游戏为准":"游戏已启动 · FPS关闭";Log(Status);
        }
        catch
        {
            if(startedThisAttempt)
            {
                _sessionReady=false;_gameExitPending=false;
                LaunchFailed?.Invoke();
                try{await ReleaseFpsSessionAsync();}
                catch(Exception cleanupError){Log("释放失败启动会话失败："+cleanupError.Message);}
            }
            throw;
        }
        finally{_starting=false;Busy=false;Log($"启动操作结束：{Status}；耗时={elapsed.Elapsed.TotalSeconds:F1}s");}
    }
    private Task ReleaseFpsSessionAsync()
    {
        var session=_fpsSession;_fpsSession=null;
        if(session is not null)_sessionCleanup=DisposeAfterAsync(_sessionCleanup,session);
        return _sessionCleanup;
    }
    private static async Task DisposeAfterAsync(Task previous,FpsSession session)
    {
        try{await previous;}finally{await session.DisposeAsync();}
    }
    private async void Monitor(object? sender,EventArgs e)
    {
        // Only an update offer may keep its modal operation lock while observing exit.
        // Launch/deployment/cleanup locks retain their original exclusion.
        if(_closing||_game is null||(Busy&&_updateDialog is null))return;
        var game=_game;
        bool exited;
        try{exited=game.HasExited;}catch{return;}
        if(!exited)return;
        _game=null;
        try{await CompleteObservedGameExitAsync();}
        finally{game.Dispose();}
    }
    private async Task CompleteObservedGameExitAsync()
    {
        bool ownsBusy=!Busy;
        if(ownsBusy)Busy=true;
        bool completedSession=_sessionReady;_sessionReady=false;
        _completingGameExit=true;_gameExitPending=completedSession;IsGameRunning=false;
        _updateDialog?.SetGameRunning(false);
        try
        {
            await ReleaseFpsSessionAsync();
            Status=!completedSession?"游戏启动未完成，已释放会话，可以重新尝试。":HasDeferredUpdate?"游戏已退出，准备执行预约更新。":"游戏已退出，启动器即将退出。";Log(Status);
        }
        catch(Exception error){Log("释放游戏会话失败："+error.Message);}
        finally{_completingGameExit=false;if(ownsBusy)Busy=false;}
        DispatchPendingGameExit();
    }
    private void DispatchPendingGameExit()
    {
        if(_closing||Busy||_updateDialog is not null||_completingGameExit||!_gameExitPending)return;
        _gameExitPending=false;
        GameExited?.Invoke();
    }
    public async Task CloseAsync()
    {
        _closing=true;StopResourceTierObservation();_monitor.Stop();_lifetime.Cancel();CancelUpdateWork();
        await ReleaseFpsSessionAsync();
        _game?.Dispose();_game=null;
    }
}
