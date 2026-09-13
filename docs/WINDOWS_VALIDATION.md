# Windows 验证记录（2026-09-14）

本文件记录本次真实执行结果；旧计划见baseline/WINDOWS_VALIDATION-dev1.md。

|项目|状态|证据或限制|
|---|---|---|
|Windows本机.NET10 SDK|通过|10.0.100，官方zip SHA512通过，artifacts/sdk-verification.log|
|原生WPF编译|通过|0 warning/0 error，wpf-first-build.log；其后发现并修复运行时图标错误|
|核心与实际测试子进程|通过|91/91，artifacts/tests.log；7个首轮异常断言错误及修复日志保留|
|本地材料导入|通过|20项桌面与input副本SHA256相同；18DLL x64；来源清单记录实际签名状态|
|脚本拒绝输入|通过|4/4，knowledge/materials/script-rejection-tests.log|
|本地Setup首装|通过（假目录）|实际执行6.8.0_Addon，exit0；生成6.8.0.2155 x64 Full特征dxgi.dll|
|本地Setup缺失/错误hash|通过|运行前拒绝，无网络下载fallback|
|已有ReShade复用/INI保留|通过（假目录）|自定义游戏内AddonPath和无关键保留；无额外shader包|
|多代理、未知代理、双INI、外部AddonPath、安装重定向|通过（假目录）|Windows集成真实检查拒绝|
|18个真实用户DLL替换/核验/重复/清除|通过（假目录）|真实文件与Windows文件系统，来源及目标映射在windows-integration；ReShade保留|
|无同名跳过、多同名、锁文件、确认后变化、部分失败|通过（假目录）|核心回归与所有权记录|
|原生主窗口启动|已运行|旧ICO导致启动失败修复后控件树可读取；不是HTML|
|原生WPF离屏组件渲染|通过（15/15）|100/150/200%位图、数值/开关绑定、长路径、输入文本viewport、等宽按钮；非系统DPI或桌面截图|
|桌面主/设置窗口截图及实际鼠标键盘|待测|Windows锁屏，Computer Use无法激活；不以锁屏图/离屏渲染替代|
|100/150/200%真实系统DPI及多屏|待测|离屏像素比例测试另列，不能算系统DPI验收|
|普通/旧版ReShade实际升级、UAC取消|待测|未升级用户环境，也未制造或绕过UAC|
|断网环境Setup|待测|Setup可能等待官方兼容表；未改变系统网络/安全设置|
|真实游戏写入/清除/启动|未执行|须用户一次确认完整路径和映射后才执行|
|外部unlock.exe实启及FPS效果|未执行|只静态审计原EXE；实际fixture子进程不是解锁器|
|原有ReShade日志|已只读审计（历史证据）|旧日志报告Dynamic support/accepted及provider警告；不能算本版实测。artifacts/real-game-historical-log.md|
|MFG/Fixed/Dynamic游戏内加载及生效|未验证|驱动>=595.41、文件部署均不能代替运行态证据|
|游戏清除后自动补齐DLL|未验证|不能保证，可能需官方校验|
|干净Windows无运行库启动|未验证|已提供10自包含和8便携依赖；固定UAC工作进程使用随包8，真实UAC待测|

游戏内四组合（FPS-only、MFG-only、两者开、两者关）、240/180/240实际帧率、插件日志与加载模块、游戏更新前后哈希、反作弊兼容仍须真实测试。不能把当前包标为已通过鸣潮实测的成品。




外部启动工作进程协议：14/14 回归通过（artifacts/external-worker-tests.log）；未实际执行UAC或用户解锁器。
