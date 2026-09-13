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
public sealed class AppViewModel:INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? SettingsRequested;
    private UserSettings _settings;
    private HardwareInfo _hardware=HardwareInfo.Unknown;
    private bool _busy,_running,_validFps=true;
    private string _status="请先在设置中确认游戏路径。",_logs="",_fpsInput="240",_reShade="未设置游戏路径",_deployState="未检测",_packageState="";
    private readonly Dispatcher _dispatcher=Application.Current.Dispatcher;
    private readonly DispatcherTimer _monitor=new(){Interval=TimeSpan.FromSeconds(1)};
    private Process? _game;
    private readonly CancellationTokenSource _lifetime=new();
    private readonly string _logFile;
    private readonly object _logLock=new();
    public AppViewModel()
    {
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
        IncrementFpsCommand=new(()=>TargetFps=Math.Min(420,TargetFps+1),()=>CanEditFps);
        DecrementFpsCommand=new(()=>TargetFps=Math.Max(30,TargetFps-1),()=>CanEditFps);
        ClearLogCommand=new(()=>{_logs="";Notify(nameof(Logs));lock(_logLock)File.WriteAllText(_logFile,"");});
        RefreshCommand=new(RefreshAsync,()=>!Busy,ReportError);
        DeployCommand=new(DeployAsync,()=>!Busy&&!IsGameRunning,ReportError);
        CleanCommand=new(CleanAsync,()=>!Busy&&!IsGameRunning,ReportError);
        StartCommand=new(StartAsync,()=>!Busy&&!IsGameRunning&&_validFps,ReportError);
        _monitor.Tick+=Monitor;_monitor.Start();
        Log("鸣潮 FPS Unlock 0.9-dev2 启动。状态来自真实检测，不使用示意图中的硬编码硬件信息。");
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
    public string GameRoot{get=>_settings.GameRoot;set{_settings.GameRoot=value;Save();Notify();}}
    public string GameExe{get=>_settings.GameExe;set{_settings.GameExe=value;Save();Notify();}}
    public string Status{get=>_status;private set{_status=value;Notify();}}
    public string Logs=>_logs;
    public string Gpu=>_hardware.Gpu;
    public string Driver=>_hardware.DriverText;
    public string Os=>_hardware.Os;
    public string Hags=>_hardware.Hags;
    public string DynamicStatus=>_hardware.DynamicText;
    public string DynamicBackground=>_hardware.IsAdaGeForce&&_hardware.Driver>=59541?"#E2F7EC":"#FFF3DF";
    public string DynamicForeground=>_hardware.IsAdaGeForce&&_hardware.Driver>=59541?"#058853":"#956B1F";
    public string DynamicDetail=>_hardware.Driver is int d?$"当前驱动 {d/100}.{d%100:00}；Dynamic 门槛 ≥ 595.41。部署≠游戏内已生效。":"无法确认驱动条件；不会把未知标成支持。";
    public string ReShadeStatus=>_reShade;
    public string DeploymentState=>_deployState;
    public string PackageStatus=>_packageState;
    public string StartLabel=>Busy?"处理中…":IsGameRunning?"游戏运行中":"开始游戏";
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
        if(e is not OperationCanceledException)MessageBox.Show(Application.Current.MainWindow,Status,"鸣潮 FPS Unlock",MessageBoxButton.OK,MessageBoxImage.Warning);
    }
    private void Save(){try{JsonFiles.Save(AppPaths.Settings,_settings);}catch(Exception e){Status="设置未能保存："+e.Message;Log(Status);}}
    private void Notify([CallerMemberName]string? name=null)=>PropertyChanged?.Invoke(this,new(name));
    private void NotifyAll()
    {
        PropertyChanged?.Invoke(this,new(null));
        OpenSettingsCommand?.Refresh();BrowseDirectoryCommand?.Refresh();BrowseExeCommand?.Refresh();IncrementFpsCommand?.Refresh();DecrementFpsCommand?.Refresh();
        StartCommand?.Refresh();DeployCommand?.Refresh();CleanCommand?.Refresh();RefreshCommand?.Refresh();
    }
    private void BrowseRoot()
    {
        var dlg=new OpenFolderDialog{Title="选择鸣潮游戏根目录（不是整个磁盘）",Multiselect=false};
        if(dlg.ShowDialog()==true){GameRoot=dlg.FolderName;Log("已选择游戏目录。游戏 EXE 仍由你手动指定。");_ =RefreshAsync();}
    }
    private void BrowseExe()
    {
        var dlg=new OpenFileDialog{Title="选择真正运行游戏的 EXE（ReShade 安装目标）",Filter="游戏程序 (*.exe)|*.exe",CheckFileExists=true};
        if(Directory.Exists(GameRoot))dlg.InitialDirectory=GameRoot;
        if(dlg.ShowDialog()==true){GameExe=dlg.FileName;_ =RefreshAsync();}
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
                _deployState=receipt is null?"未部署":receipt.Status=="PartialFailure"?"上次部署未完成":receipt.Status=="Cleaned"?"已清除本工具插件":receipt.Status=="CleanedWithSkips"?"清除结束 · 部分已变化项目保留":await DeploymentFiles.IsIntactAsync(receipt)?"已部署 · 文件校验通过":"文件已变化 · 需重新检查";
            }
            else{_reShade="未设置游戏路径";_deployState="请先选择路径";}
        }
        catch(Exception e){_reShade="检测未完成";_deployState=e.Message;Log(e.Message);}
        _packageState=File.Exists(_settings.PackageManifest)?"文件包："+Path.GetFileName(Path.GetDirectoryName(_settings.PackageManifest)):"尚未导入 MFG 文件包；点击开始部署时选择清单。";
        NotifyAll();
    }
    private async Task DeployAsync()
    {
        GameProcesses.ValidateExe(_settings);
        if(!MfgSelected){Status="未选择多帧生成部署。FPS 开关仅决定下次启动方式。";return;}
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
        finally{Busy=false;}
    }
    private async Task CleanAsync()
    {
        GameProcesses.ValidateExe(_settings);GameProcesses.RequireStopped(GameRoot);
        Busy=true;
        try
        {
            var snapshot=_settings.Clone();var service=new DeploymentService(Log);
            string preview=service.CleanPreview(snapshot)+"\n保留 ReShade 本体、滤镜、其他插件。无原 DLL 备份；清除后是否自动补齐需由游戏验证，必要时使用官方校验。";
            Log(preview);
            if(!OperationReview.Show("确认清除插件",preview)){Status="已取消清除。";return;}
            Status="正在核对部署记录并清除…";
            try{await service.CleanAsync(snapshot);}
            catch(NeedsElevationException){await ElevatedWorker.RunFromUi("clean",snapshot,false,Log,service.ApprovalFingerprint);}
            _settings.MfgSelected=false;Save();
            Status=AppPaths.LoadReceipt(snapshot)?.Status=="CleanedWithSkips"?"清除结束，已变化文件或配置已跳过；请查看日志。":"已移除登记且未变化的替换 DLL 和自有插件；ReShade 已保留。";
            await RefreshCore();
        }
        finally{Busy=false;}
    }
    private async Task StartAsync()
    {
        if(string.IsNullOrWhiteSpace(GameRoot)||string.IsNullOrWhiteSpace(GameExe)){Status="请在设置中先选择鸣潮目录和真正游戏 EXE。";SettingsRequested?.Invoke();return;}
        GameProcesses.ValidateExe(_settings);
        var plan=LaunchPlanBuilder.Build(_settings.Clone(),AppPaths.Unlocker);
        var receipt=AppPaths.LoadReceipt(_settings);
        if(receipt?.Status=="PartialFailure"){Status="上次部署未完成，请先在设置中处理。";SettingsRequested?.Invoke();return;}
        if(MfgSelected && (receipt is null || receipt.Status=="Cleaned"))
        {
            var answer=MessageBox.Show(Application.Current.MainWindow,"已选择多帧生成，但本工具尚无有效部署记录。\n\n是：打开设置并开始部署\n否：仅按当前文件启动，不部署 MFG\n取消：不启动","尚未部署多帧生成",MessageBoxButton.YesNoCancel,MessageBoxImage.Information);
            if(answer==MessageBoxResult.Yes){SettingsRequested?.Invoke();return;}if(answer!=MessageBoxResult.No)return;
        }
        if(receipt?.Status=="Deployed"&&!await DeploymentFiles.IsIntactAsync(receipt))
        {
            var answer=MessageBox.Show(Application.Current.MainWindow,"MFG 组件与上次部署记录不一致，可能由游戏更新或官方文件校验导致。不会自动把旧文件覆盖到新游戏版本。\n\n是：打开设置检查\n否：保持当前文件，继续启动\n取消：不启动","组件发生变化",MessageBoxButton.YesNoCancel,MessageBoxImage.Warning);
            if(answer==MessageBoxResult.Yes){SettingsRequested?.Invoke();return;}if(answer!=MessageBoxResult.No)return;
        }
        if(Busy)return;
        GameProcesses.RequireStopped(GameRoot);
        string details=plan.FpsEnabled
            ? $"执行用户解锁器：{plan.Executable}\n工作目录：{plan.WorkingDirectory}\n更新运行配置：{plan.ConfigPath}\nINI PathValue：{plan.UnlockerGamePath}\n自动启动模式 GameLaunchExe=1，目标 FPS={plan.TargetFps}\n由外部工具启动并管理：{plan.ShippingExePath}\n\n它可能显示 UAC、窗口和托盘，内部会加载其自带插件；本工具不再注入。"
            : $"直接运行原装 Shipping：{plan.ShippingExePath}\n工作目录：{plan.WorkingDirectory}\n保留现有 MFG / ReShade。";
        if(MessageBox.Show(Application.Current.MainWindow,details+"\n\n是否按以上路径开始？","启动确认",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes){Status="已取消启动。";return;}
        Busy=true;
        try
        {
            _game=await new ExternalUnlockerService(Log).LaunchAsync(plan,_settings.Clone(),value=>Status=value,_lifetime.Token);
            IsGameRunning=true;
            Status=plan.FpsEnabled?"游戏运行中 · 外部 FPS 实际效果待确认":"游戏运行中 · 原装 Shipping 直接启动";
            Log($"已确认渲染窗口：{plan.ShippingExePath}；PID={_game.Id}。"+Status);
        }
        finally{Busy=false;}
    }
    private void Monitor(object? sender,EventArgs e)
    {
        if(_game is null||!IsGameRunning||Busy)return;
        bool exited;try{exited=_game.HasExited;}catch{exited=true;}
        if(!exited)return;
        IsGameRunning=false;_game.Dispose();_game=null;
        Status="游戏已退出，可再次开始游戏；外部解锁器若仍在托盘请先自行退出。";Log(Status);
    }
    public Task CloseAsync(){_monitor.Stop();_lifetime.Cancel();_game?.Dispose();return Task.CompletedTask;}
}
