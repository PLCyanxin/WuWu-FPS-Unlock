# 当前实现说明

旧dev1说明存docs/baseline/IMPLEMENTATION-dev1.md；本文覆盖旧内置注入流程。

- Core/LaunchPlan：Shipping与unlocker独立路径、已核实的INI适配；保持未知键、关闭无关高级选项。
- Core/LaunchExecution：单次启动互斥、准备/启动/等待真实渲染窗口/成功/超时/取消状态，关闭监控不结束游戏。
- ExternalUnlockerService：核验用户EXE哈希、本地.NET8运行库、同名进程/已运行解锁器阻断；只发起所选分支，不注入。GameProcesses按路径/PID/UnrealWindow确认。
- PackageReader：18项精确白名单，逐文件源哈希先校验；只遍历用户所选游戏根，不跟重解析点；平铺素材匹配现存同名文件，无同名跳过。
- DeploymentService：先生成完整文件计划与ReShade状态，确认指纹跨UAC传递并重算；确认后目标/INI/代理/包变化则中止。硬件、Dynamic条件、文件状态分层。
- DeploymentFiles：所有目标提前校验占用与哈希；Vendor采用File.Replace而不创建缺失目标，addon拒绝确认后出现的新文件。逐文件暂存复核，保存意图及完成状态。
- OwnedFileDeletion：同Windows句柄完成独占写入/删除保护、SHA256与标记删除，防哈希校验后路径替换。只清记录里实际替换的白名单DLL和自建addon；变化文件保留。
- IniDocument：键级合并和撤销，保留其他early-load项目与用户后续修改。IsIntact检查所有自有键与CSV成员。
- ReShadeService：用户本地Setup/哈希/版本验证，无下载替换源；已有Full x64复用，未知代理/多代理/双INI/外部AddonPath/BasePath重定向拒绝。安装真实退出后再验证runtime路径、PE与Full特征。Setup等待网络时不伪造百分比。
- UI：原生MainWindow和独立SettingsWindow共用VM；按钮等宽，原图裁切渐变。操作计划为可滚动确认框。旧ICO导致WPF启动失败已通过原头像格式转换修复。

当前用户包addon SHA256与上游0.9发布值一致，但其资源版本为0.2026.0909.1640。RuntimeSelectionMode=1仅影响当前进程的Streamline初始化flags，不改全局OTA；源码与二进制静态证据见knowledge/deployment/ADDON_CONFIG_AUDIT.md。包内各DLL的具体版本保留用户原组合，不另行选型。

实际测试证据：artifacts/tests.log（91/91）、artifacts/windows-tests.log（12/12）；构建/发布日志另存artifacts/build.log。所有游戏内结论仍需经确认的真实鸣潮测试。


ExternalLaunchWorker：固定命令、nonce/请求摘要/过期/防重放校验；标准UAC后验证固定解锁器及随包.NET8完整文件清单。14项协议回归不能替代真实UAC。
UserMaterialRemoval：完整材料指纹清除计划需一次明确确认；跨UAC重算，不认领既有文件所有权。
