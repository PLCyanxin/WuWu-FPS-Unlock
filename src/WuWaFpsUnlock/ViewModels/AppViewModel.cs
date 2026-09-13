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
    private FpsSession? _session;
    private Process? _game;
    private CancellationTokenSource? _fpsChangeCts;
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
        Log("鸣潮 FPS Unlock 0.9-dev1 启动。状态来自真实检测，不使用示意图中的硬编码硬件信息。");
    }
    public bool Busy {get=>_busy;private set{_busy=value;NotifyAll();}}
    public bool IsGameRunning {get=>_running;private set{_running=value;NotifyAll();}}
    public bool CanChangeFpsMode=>!Busy&&!IsGameRunning;
    public bool CanEditFps=>!Busy&&FpsEnabled&&(!IsGameRunning||_session?.Connected==true);
    public bool CanEditSettings=>!Busy&&!IsGameRunning;
    public bool FpsEnabled {get=>_settings.FpsEnabled;set{if(!CanChangeFpsMode)return;_settings.FpsEnabled=value;Save();NotifyAll();}}
    public bool MfgSelected {get=>_settings.MfgSelected;set{if(!CanEditSettings)return;_settings.MfgSelected=value;Save();NotifyAll();}}
    public int TargetFps
    {
        get=>_settings.TargetFps;
        set
        {
            value=Math.Clamp(value,30,420);if(_settings.TargetFps==value&&_validFps)return;
            _settings.TargetFps=value;_fpsInput=value.ToString(CultureInfo.InvariantCulture);_validFps=true;Save();NotifyAll();_ =SendFpsDebounced();
        }
    }
    public string FpsInput
    {
        get=>_fpsInput;
        set
        {
            _fpsInput=value;
            if(int.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out int fps)&&fps is >=30 and <=420){_settings.TargetFps=fps;_validFps=true;Save();_ =SendFpsDebounced();}
            else{_validFps=false;Status="目标 FPS 必须是 30–420 的整数；无效值没有发送到游戏。";}
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
                _deployState=receipt is null?"未部署":receipt.Status=="PartialFailure"?"上次部署未完成":receipt.Status=="Cleaned"?"已清除本工具插件":await DeploymentFiles.IsIntactAsync(receipt)?"已部署 · 文件校验通过":"文件已变化 · 需重新检查";
            }
            else{_reShade="未设置游戏路径";_deployState="请先选择路径";}
        }
        catch(Exception e){_reShade="检测未完成";_deployState=e.Message;Log(e.Message);}
        _packageState=File.Exists(_settings.PackageManifest)?"文件包："+Path.GetFileName(Path.GetDirectoryName(_settings.PackageManifest)):"尚未导入 MFG 文件包；点击开始部署时选择清单。";
        NotifyAll();
    }
    private bool ConfirmRisk()
    {
        if(_settings.RiskAccepted)return true;
        var answer=MessageBox.Show(Application.Current.MainWindow,"本工具会使用第三方插件。FPS 功能会在游戏进程中加载你提供的 FPS 基础 DLL；MFG 使用 ReShade Full Add-on。\n\n这些改动不是鸣潮官方功能，无法保证不触发反作弊或账号风险。不会关闭安全软件、修改反作弊或尝试绕过其限制。\n\n是否继续？","首次使用确认",MessageBoxButton.YesNo,MessageBoxImage.Warning);
        if(answer!=MessageBoxResult.Yes)return false;_settings.RiskAccepted=true;Save();return true;
    }
    private async Task DeployAsync()
    {
        GameProcesses.ValidateExe(_settings);
        if(!_validFps)throw new InvalidDataException("请先修正目标 FPS。");
        if(!FpsEnabled&&!MfgSelected)throw new InvalidOperationException("没有选择要部署的功能。");
        if(!ConfirmRisk())return;
        if(MfgSelected&&!File.Exists(_settings.PackageManifest))
        {
            var choose=new OpenFileDialog{Title="选择你整理好的 MFG 文件包 manifest.json",Filter="部署清单 (manifest.json)|manifest.json|JSON 清单 (*.json)|*.json",CheckFileExists=true};
            if(choose.ShowDialog()!=true)return;PackageReader.Load(choose.FileName);_settings.PackageManifest=choose.FileName;Save();
        }
        bool upgrade=false;
        if(MfgSelected)
        {
            var existing=new ReShadeService(Log).Inspect(_settings);
            if(existing.State=="UpgradeRequired")
            {
                upgrade=MessageBox.Show(Application.Current.MainWindow,existing.Description+"\n\n只更新官方运行库并保留原配置、滤镜和其他插件。是否允许？","ReShade 更新确认",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes;
                if(!upgrade)return;
            }
        }
        Busy=true;Status="正在预检查并部署；请保持游戏关闭。";
        try
        {
            var snapshot=_settings.Clone();var service=new DeploymentService(Log);
            try{await service.DeployAsync(snapshot,upgrade);}
            catch(NeedsElevationException)
            {
                if(MessageBox.Show(Application.Current.MainWindow,"目标目录需要管理员写入权限。仅部署工作进程将请求 UAC，主窗口保持普通权限。","授权部署",MessageBoxButton.OKCancel,MessageBoxImage.Information)!=MessageBoxResult.OK)return;
                await ElevatedWorker.RunFromUi("deploy",snapshot,upgrade,Log);
            }
            Status="部署完成。关闭设置后可点击开始游戏；实际 MFG 能力需在游戏内确认。";await RefreshCore();
        }
        finally{Busy=false;}
    }
    private async Task CleanAsync()
    {
        GameProcesses.ValidateExe(_settings);GameProcesses.RequireStopped(GameRoot);
        if(MessageBox.Show(Application.Current.MainWindow,"仅清除有本工具所有权记录的 MFG 插件，并撤销本工具自己的 INI 键修改。\n\n保留用户原有 ReShade、滤镜、其他插件及 NVIDIA DLL，不恢复游戏官方文件。被他人修改的文件也会保留。\n\n是否清除？","清除插件",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
        Busy=true;Status="正在核对所有权并清除插件…";
        try
        {
            try{await new DeploymentService(Log).CleanAsync(_settings.Clone());}
            catch(NeedsElevationException){await ElevatedWorker.RunFromUi("clean",_settings.Clone(),false,Log);}
            _settings.MfgSelected=false;Save();Status="清除完成；ReShade 与 NVIDIA 运行库已保留。";await RefreshCore();
        }
        finally{Busy=false;}
    }
    private async Task StartAsync()
    {
        if(string.IsNullOrWhiteSpace(GameRoot)||string.IsNullOrWhiteSpace(GameExe)){Status="请在设置中先选择鸣潮目录和真正游戏 EXE。";SettingsRequested?.Invoke();return;}
        string exe=GameProcesses.ValidateExe(_settings);if(FpsEnabled&&!ConfirmRisk())return;
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
        Busy=true;Status="正在启动游戏…";
        try
        {
            if(FpsEnabled&&(!File.Exists(AppPaths.Plugin)||await SafePaths.HashAsync(AppPaths.Plugin)!=FpsSession.PluginHash))throw new InvalidDataException("FPS 插件缺失或校验失败。没有启动或注入。");
            var running=GameProcesses.Find(GameRoot);Process target;
            if(running.Count>0)
            {
                if(running.Count!=1||!GameProcesses.HasUnrealWindow(running[0].Id)){foreach(var p in running)p.Dispose();throw new IOException("存在游戏进程但渲染目标不唯一或未就绪；请关闭后重试。");}
                target=running[0];
                if(FpsEnabled&&MessageBox.Show(Application.Current.MainWindow,"鸣潮已经运行。是否仅为这次运行加载 FPS 基础插件？不会重复启动游戏。","连接当前游戏",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes){target.Dispose();return;}
            }
            else
            {
                var launchedAt=DateTime.UtcNow;
                using var initial=Process.Start(new ProcessStartInfo(exe){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(exe)!})??throw new IOException("游戏没有启动。");
                Status="等待鸣潮渲染窗口…";
                target=await GameProcesses.WaitForRenderer(GameRoot,launchedAt,CancellationToken.None);
            }
            _game=target;
            if(FpsEnabled)
            {
                Status="正在连接 FPS 基础插件…";_session=new();await _session.ConnectAsync(target,AppPaths.Plugin,Log,CancellationToken.None);
                await _session.SetFpsAsync(TargetFps);Log($"已发送 FPS 上限：{TargetFps}。收到管道连接不等于已测得实际帧率。");
                Status=$"游戏已启动 · FPS {TargetFps} 指令已发送";
            }
            else Status="游戏已启动 · 未加载 FPS 插件";
            IsGameRunning=true;
        }
        catch
        {
            if(_game is not null){try{IsGameRunning=!_game.HasExited;}catch{}}
            throw;
        }
        finally{Busy=false;}
    }
    private async Task SendFpsDebounced()
    {
        _fpsChangeCts?.Cancel();_fpsChangeCts?.Dispose();_fpsChangeCts=new();var token=_fpsChangeCts.Token;
        try
        {
            await Task.Delay(200,token);
            if(IsGameRunning&&_session?.Connected==true)
            {await _session.SetFpsAsync(TargetFps,token);Status=$"已发送 FPS 上限 {TargetFps} · 游戏内实际结果待确认";}
        }
        catch(OperationCanceledException){}
        catch(Exception e){Log("FPS 更新失败："+e.Message);Status="FPS 通信中断；最后设置可能仍在游戏中生效。";NotifyAll();}
    }
    private async void Monitor(object? sender,EventArgs e)
    {
        if(_game is null||!IsGameRunning||Busy)return;
        bool exited;try{exited=_game.HasExited;}catch{exited=true;}
        if(!exited){Notify(nameof(CanEditFps));return;}
        IsGameRunning=false;if(_session is not null){await _session.DisposeAsync();_session=null;}else _game.Dispose();_game=null;
        Status="游戏已退出，可再次开始游戏。";Log(Status);
    }
    public async Task CloseAsync(){_monitor.Stop();_fpsChangeCts?.Cancel();if(_session is not null)await _session.DisposeAsync();else _game?.Dispose();}
}
