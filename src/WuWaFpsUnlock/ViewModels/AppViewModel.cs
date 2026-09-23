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
    private Task _sessionCleanup=Task.CompletedTask;
    private readonly RestartController _restart=new();
    private FpsSession? _fpsSession;
    private UserSettings _settings;
    private HardwareInfo _hardware=HardwareInfo.Unknown;
    private bool _busy,_running,_validFps=true;
    private string _status="请先在设置中确认游戏路径。",_logs="",_fpsInput="240",_reShade="未设置游戏路径",_deployState="未检测",_packageState="";
    private readonly Dispatcher _dispatcher=Application.Current.Dispatcher;
    private readonly DispatcherTimer _monitor=new(){Interval=TimeSpan.FromSeconds(1)};
    private string _lastValidExe="";

    private bool _findingGame;
    private readonly Func<string,IEnumerable<string>,CancellationToken,Task<GameDiscoveryResult>> _discover;
    private Process? _game;
    private readonly CancellationTokenSource _lifetime=new();
    private readonly string _logFile;
    private readonly object _logLock=new();
    public AppViewModel(Func<string,IEnumerable<string>,CancellationToken,Task<GameDiscoveryResult>>? discover=null)
    {
        _discover=discover??((root,hints,token)=>GameDiscoveryService.DiscoverAsync(root,hints,true,token));
        Directory.CreateDirectory(AppPaths.Data);Directory.CreateDirectory(Path.Combine(AppPaths.Data,"logs"));
        _logFile=Path.Combine(AppPaths.Data,"logs",DateTime.Now.ToString("yyyyMMdd-HHmmss",CultureInfo.InvariantCulture)+".log");
        try{_settings=File.Exists(AppPaths.Settings)?JsonFiles.Read<UserSettings>(AppPaths.Settings):new();}
        catch(Exception e){_settings=new();Log("设置文件无法读取，保留原文件且载入空设置："+e.Message);}
        if(string.IsNullOrWhiteSpace(_settings.PackageManifest) && File.Exists(Path.Combine(AppPaths.Base,"payload","manifest.json")))
            _settings.PackageManifest=Path.Combine(AppPaths.Base,"payload","manifest.json");
        if(_settings.TargetFps is <30 or >420)_settings.TargetFps=240;
        _fpsInput=_settings.TargetFps.ToString(CultureInfo.InvariantCulture);
        OpenSettingsCommand=new(()=>SettingsRequested?.Invoke(),()=>!Busy);
        BrowseDirectoryCommand=new(BrowseRoot,()=>!Busy&&!IsGameRunning);
        BrowseExeCommand=new(BrowseExe,()=>!Busy&&!IsGameRunning);
        FindGameCommand=new(FindGameAsync,()=>CanEditSettings,ReportError);
        IncrementFpsCommand=new(()=>TargetFps=Math.Min(420,TargetFps+1),()=>CanEditFps);
        DecrementFpsCommand=new(()=>TargetFps=Math.Max(30,TargetFps-1),()=>CanEditFps);
        ClearLogCommand=new(()=>{_logs="";Notify(nameof(Logs));lock(_logLock)File.WriteAllText(_logFile,"");});
        RefreshCommand=new(RefreshAsync,()=>!Busy,ReportError);
        DeployCommand=new(DeployAsync,()=>!Busy&&!IsGameRunning,ReportError);
        CleanCommand=new(CleanAsync,()=>!Busy&&!IsGameRunning,ReportError);
        StartCommand=new(StartAsync,()=>!Busy&&_validFps,ReportError);
        InitializeUpdates();
        _monitor.Tick+=Monitor;_monitor.Start();
        RememberValidSelection();
        Log("鸣潮 FPS Unlock 1.1.1RC 启动。");
    }
    public bool Busy {get=>_busy;private set{_busy=value;NotifyAll();}}
    public bool IsGameRunning {get=>_running;private set{_running=value;NotifyAll();}}
    public bool CanChangeFpsMode=>!Busy;
    public bool CanEditFps=>!Busy&&FpsEnabled;
    public bool CanEditSettings=>!Busy&&!IsGameRunning;
    public bool FpsEnabled {get=>_settings.FpsEnabled;set{if(!CanChangeFpsMode)return;_settings.FpsEnabled=value;Save();Status="FPS 开关下次启动生效";NotifyAll();}}
    public bool MfgSelected {get=>_settings.MfgSelected;set{if(!CanEditSettings)return;_settings.MfgSelected=value;Save();NotifyAll();}}
    public int TargetFps
    {
        get=>_settings.TargetFps;
        set
        {
            value=Math.Clamp(value,30,420);if(_settings.TargetFps==value&&_validFps)return;
            _settings.TargetFps=value;_fpsInput=value.ToString(CultureInfo.InvariantCulture);_validFps=true;Save();NotifyAll();Status="FPS 设置已保存 · 下次启动生效";
        }
    }
    public string FpsInput
    {
        get=>_fpsInput;
        set
        {
            _fpsInput=value;
            if(int.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out int fps)&&fps is >=30 and <=420){_settings.TargetFps=fps;_validFps=true;Save();Status="FPS 设置已保存 · 下次启动生效";}
            else{_validFps=false;Status="目标 FPS 必须是 30–420 的整数；无效值没有保存。";}
            NotifyAll();
        }
    }
    public string GameRoot{get=>_settings.GameRoot;set{RememberValidSelection();_settings.GameRoot=value;Save();Notify();}}
    public string GameExe{get=>_settings.GameExe;set{RememberValidSelection();_settings.GameExe=value;Save();Notify();}}
    private void RememberValidSelection(){if(GameDiscoveryService.TryValidateSelection(_settings.GameRoot,_settings.GameExe,out var current,out _))_lastValidExe=current!.ShippingExePath;}
    public string Status{get=>_status;private set{_status=value;Notify();}}
    public string Logs=>_logs;
    public string Gpu=>_hardware.Gpu;
    public string Driver=>_hardware.DriverText;
    public string Os=>_hardware.Os;
    public string Hags=>_hardware.Hags;
    public string DynamicStatus=>_hardware.DynamicText.Replace("；游戏内能力待确认","");
    public string DynamicBackground=>_hardware.IsAdaGeForce&&_hardware.Driver>=59541?"#E2F7EC":"#FFF3DF";
    public string DynamicForeground=>_hardware.IsAdaGeForce&&_hardware.Driver>=59541?"#058853":"#956B1F";
    public string DynamicDetail=>_hardware.Driver is int d?$"当前驱动 {d/100}.{d%100:00}；Dynamic 门槛 ≥ 595.41。部署≠游戏内已生效。":"无法确认驱动条件；不会把未知标成支持。";
    public string ReShadeStatus=>_reShade;
    public string DeploymentState=>_deployState;
    public string PackageStatus=>_packageState;
    public string StartLabel=>Busy?"处理中…":IsGameRunning?"游戏中":"开始游戏";
    public string DeployLabel=>Busy?"处理中…":"开始部署";
    public RelayCommand OpenSettingsCommand{get;}
    public RelayCommand BrowseDirectoryCommand{get;}
    public RelayCommand BrowseExeCommand{get;}
    public RelayCommand IncrementFpsCommand{get;}
    public RelayCommand DecrementFpsCommand{get;}
    public RelayCommand ClearLogCommand{get;}
    public AsyncCommand RefreshCommand{get;}
    public AsyncCommand DeployCommand{get;}
    public AsyncCommand CleanCommand{get;}
    public AsyncCommand StartCommand{get;}
    public AsyncCommand FindGameCommand{get;}
    public string FindGameLabel=>_findingGame?"正在查找…":"帮我查找鸣潮";
    public void Log(string text)
    {
        if(!_dispatcher.CheckAccess()){_dispatcher.Invoke(()=>Log(text));return;}
        string line=$"[{DateTime.Now:HH:mm:ss}] {text}";
        _logs+=line+Environment.NewLine;if(_logs.Length>180000)_logs=_logs[^120000..];Notify(nameof(Logs));
        try{if(!string.IsNullOrWhiteSpace(_logFile))lock(_logLock)File.AppendAllText(_logFile,line+Environment.NewLine);}catch{ /* UI retains error details if log destination is unavailable. */ }
    }
    public void ReportError(Exception e)
    {
        Status=e is OperationCanceledException?e.Message:"操作未完成："+e.Message;Log(Status);NotifyAll();
    }
    private void Save(){try{JsonFiles.Save(AppPaths.Settings,_settings);}catch(Exception e){Status="设置未能保存："+e.Message;Log(Status);}}
    private void Notify([CallerMemberName]string? name=null)=>PropertyChanged?.Invoke(this,new(name));
    private void NotifyAll()
    {
        PropertyChanged?.Invoke(this,new(null));
        OpenSettingsCommand?.Refresh();BrowseDirectoryCommand?.Refresh();BrowseExeCommand?.Refresh();IncrementFpsCommand?.Refresh();DecrementFpsCommand?.Refresh();
        StartCommand?.Refresh();DeployCommand?.Refresh();CleanCommand?.Refresh();RefreshCommand?.Refresh();
        FindGameCommand?.Refresh();CheckUpdatesCommand?.Refresh();
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
        if(Busy)return;Busy=true;
        try{await RefreshCore();}finally{Busy=false;}
    }
    private async Task RefreshCore()
    {
        _hardware=await Task.Run(()=>EnvironmentProbe.Read(Log));
        try
        {
            if(!string.IsNullOrWhiteSpace(GameRoot)&&!string.IsNullOrWhiteSpace(GameExe))
            {
                var info=await Task.Run(()=>new ReShadeService(Log).Inspect(_settings.Clone()));_reShade=info.Description;
                var receipt=AppPaths.LoadReceipt(_settings);
                _deployState=receipt is null?"未部署":receipt.Status=="PartialFailure"?"上次部署未完成":receipt.Status=="Cleaned"?"已清除本工具插件":receipt.Status=="CleanedWithSkips"?"清除结束 · 部分已变化项目保留":"已有部署记录";
            }
            else{_reShade="未设置游戏路径";_deployState="请先选择路径";}
        }
        catch(Exception e){_reShade="检测未完成";_deployState=e.Message;Log(e.Message);}
        _packageState=File.Exists(_settings.PackageManifest)?"文件包："+Path.GetFileName(Path.GetDirectoryName(_settings.PackageManifest)):"尚未导入 MFG 文件包；点击开始部署时选择清单。";
        NotifyAll();
    }
    private async Task DeployAsync()
    {
        if(!MfgSelected){Status=FpsEnabled?"FPS 解锁无需部署，点击开始游戏即可。":"未选择多帧生成，无需部署。";Log(Status);return;}
        GameProcesses.ValidateExe(_settings);
        if(!File.Exists(_settings.PackageManifest))
        {
            var choose=new OpenFileDialog{Title="选择用户材料 manifest.json",Filter="部署清单 (manifest.json)|manifest.json",CheckFileExists=true};
            if(choose.ShowDialog()!=true)return;
            PackageReader.Load(choose.FileName);_settings.PackageManifest=choose.FileName;Save();
        }
        Busy=true;Status="正在校验材料并搜索同名目标…";
        try
        {
            var snapshot=_settings.Clone();var service=new DeploymentService(Log);
            string preview=await service.PreviewAsync(snapshot);
            var manifest=PackageReader.Load(snapshot.PackageManifest);
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
        {Status=GameFileBaselineStore.MissingFilesMessage;Log(e.Message);MessageBox.Show(Status,"文件缺失",MessageBoxButton.OK,MessageBoxImage.Warning);}
        finally{Busy=false;}
    }
    private async Task CleanAsync()
    {
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
            _settings.MfgSelected=false;Save();
            Status=AppPaths.LoadReceipt(snapshot)?.Status=="CleanedWithSkips"?"清除结束，已变化文件或配置已跳过；请查看日志。":"已移除登记且未变化的替换 DLL 和自有插件；ReShade 已按来源记录处理。";
            await RefreshCore();
        }
        finally{Busy=false;}
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
        if(Busy||_closing)return;Busy=true;
        bool startedThisAttempt=false;
        var snapshot=_settings.Clone();
        try
        {
            var process=await _restart.RunAsync(new(snapshot.GameExe,snapshot.FpsEnabled),new WindowsRestartProcessCatalog(),new RestartActions
            {
                Preflight=async token=>
                {
                    GameProcesses.ValidateExe(snapshot);
                    if(snapshot.TargetFps is <30 or >420)throw new InvalidDataException("目标FPS无效。");
                    var receipt=AppPaths.LoadReceipt(snapshot);
                    if(receipt?.Status is "PartialFailure" or "Installing" or "PartialClean")throw new IOException("部署维护尚未完成，请在设置处理后再开始。");
                    if(snapshot.MfgSelected&&(receipt is null||receipt.Status.StartsWith("Cleaned")))throw new IOException("已选择多帧生成但尚未部署，请先部署或关闭部署选择。");
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
                ReleaseOldSession=async _=>{await ReleaseFpsSessionAsync();_game?.Dispose();_game=null;IsGameRunning=false;},
                Start=(exe,token)=>
                {
                    token.ThrowIfCancellationRequested();
                    var startInfo=new ProcessStartInfo(exe){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(exe)!};
                    if(snapshot.MfgSelected){startInfo.ArgumentList.Add("-dx12");Log("多帧生成启动参数：-dx12；实际D3D12加载以游戏日志为准。");}
                    var process=Process.Start(startInfo)??throw new IOException("系统未返回游戏进程。");
                    _game=process;startedThisAttempt=true;IsGameRunning=true;GameStarted?.Invoke();
                    Log($"仅启动所选Shipping一次：{exe}；PID={process.Id}；FPS={(snapshot.FpsEnabled?"ON":"OFF")}；目标={snapshot.TargetFps}");
                    return Task.FromResult(process);
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
            },phase=>{Status=phase switch{"Preflight"=>"正在检查路径与组件…","AwaitingNotice"=>"检查首次部署须知…","ReleaseOldSession"=>"释放旧游戏会话…","Stopping"=>"正在结束同一安装的旧游戏…","Rechecking"=>"确认旧实例已退出…","Starting"=>"正在启动游戏…","AttachingFps"=>"等待游戏并连接内置FPS核心…","Cancelled"=>"已取消，未继续启动。",_=>"正在检查游戏状态…"};},TimeSpan.FromSeconds(15),_lifetime.Token);
            if(process is null){Status="已取消须知，未结束或启动游戏。";return;}
            if(!snapshot.FpsEnabled){await BuiltinFpsService.WaitForRendererAsync(process,snapshot.GameExe,_lifetime.Token,Log);GameReady?.Invoke();}
            Status=snapshot.FpsEnabled?"游戏已启动 · 内置FPS已连接，实际效果以游戏为准":"游戏已启动 · FPS关闭";Log(Status);
        }
        catch
        {
            if(startedThisAttempt)
            {
                LaunchFailed?.Invoke();
                try{await ReleaseFpsSessionAsync();}
                catch(Exception cleanupError){Log("释放失败启动会话失败："+cleanupError.Message);}
            }
            throw;
        }
        finally{Busy=false;}
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
        if(_closing||_game is null||Busy)return;
        var game=_game;
        bool exited;
        try{exited=game.HasExited;}catch{return;} // Unreadable identity is not evidence of exit.
        if(!exited)return;
        Busy=true;_game=null;IsGameRunning=false;
        try
        {
            await ReleaseFpsSessionAsync();
            Status="游戏已退出，启动器即将退出。";Log(Status);
        }
        catch(Exception error){Log("释放游戏会话失败："+error.Message);}
        finally{game.Dispose();Busy=false;}
        if(!_closing)GameExited?.Invoke();
    }
    public async Task CloseAsync()
    {
        _closing=true;_monitor.Stop();_lifetime.Cancel();CancelUpdateWork();
        await ReleaseFpsSessionAsync();
        _game?.Dispose();_game=null;
    }
}
