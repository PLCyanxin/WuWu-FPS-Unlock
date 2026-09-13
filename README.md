# 鸣潮 FPS Unlock — 0.9 本机开发版

原生 Windows x64 WPF/.NET 10 双窗口。延续用户定稿和原图；没有网页套壳、页签、侧栏或恢复原版功能。

当前已完成真实 WPF 编译、91 项核心/测试子进程回归、12 项 Windows 集成回归，以及15项原生WPF离屏组件测试。Windows 集成在工程假目录实际运行用户 ReShade Setup，并使用全部18个用户DLL完成替换、哈希复核、重复部署与清除。它们不是鸣潮游戏内兼容性证明。当前游戏内 FPS、MFG/Dynamic、自动补齐原DLL、UAC升级及真实游戏启动尚未验证。

## 运行与材料

便携入口：artifacts/win-x64/WuWaFpsUnlock.exe。请保留整个目录，包含components/unlocker、components/dotnet8、payload及licenses。本工具.NET10自包含；用户解锁器另外带有经微软SHA512核验的.NET8 Desktop 8.0.31运行库。已核实原解锁器要求管理员权限：独立固定工作进程请求标准UAC，提升后使用随包.NET8启动一次。实际UAC与无.NET干净Windows仍未实测。

所有DLSS/Streamline/addon/ReShade Setup来自本次桌面input/多帧生成，未换成其他版本。20项来源、版本、签名结果和SHA256见input/desktop-materials/source-manifest.json、artifacts/material-audit.csv；payload/manifest.json是平铺素材清单，不是游戏目录镜像。解锁器来自input/unlocker/鸣潮.exe的原字节副本，发布别名unlock.exe，哈希在knowledge/launch中。

## 实际工作流程

1. 设置中选择游戏根目录和原装Client-Win64-Shipping.exe。FPS关闭时直接启动该文件；FPS开启时调用用户unlock.exe，由它启动/管理Shipping，本应用不注入FPS DLL或扫描/写入游戏内存。
2. 外部解锁器INI按静态核实的字段适配。目标FPS、开关只在下次启动生效，不宣称实时调帧。FPS开启要求已核实的Client/Binaries/Win64布局及对应根入口；其他布局会明确停止。
3. 需要多帧生成时打开设置中的“多帧生成部署”。工具在所选根目录内搜索18个白名单DLL的同名现存目标，逐项展示；无同名文件跳过，不把整套DLL塞到根目录或EXE旁。一个名称存在多个副本时，全部列出映射供一次确认。
4. 本地ReShade 6.8.0 Full Add-on安装器优先；已有兼容本体复用，升级需在整份操作计划中确认，未知代理/多份本体/配置歧义停止。保留用户INI、滤镜和其他addon。Setup可能访问官方兼容表；本工具没有在线下载另一套Setup或滤镜。
5. “清除插件”展示完整对象清单：本工具所有权记录之外，也可在用户明确确认后直接移除与本次材料同名、同哈希的既有DLL/addon；不把指纹相同误记为本工具安装。未知或已改文件保留，无所有权INI项不动。保留ReShade本体与其他插件。不备份或恢复原DLL；清除后游戏能否自行补齐尚待验证，必要时用官方文件校验。

FPS与MFG相互独立。Fixed/Dynamic是同一包的能力层次；595.41只用于Dynamic驱动判断。驱动条件通过、文件已部署、插件加载和游戏内实际接受Dynamic分别报告。HAGS只读，不改驱动、系统开关或全局OTA。

## 构建与测试

Windows PowerShell运行scripts/Build.ps1；缺SDK时传-InstallSdk，安装到工程.tools。固定.NET SDK 10.0.100；不需要WSL。顺序：核心测试→Windows/ReShade假目录集成→win-x64自包含发布。日志在artifacts。ReShade集成测试会运行用户提供的Setup，但目标只在artifacts/windows-integration，绝不运行假Shipping/真实游戏/解锁器。

原生界面离屏布局测试和真实桌面交互分开记录。HTML preview目录及docs/baseline均为旧基线资料，不用于当前验收。完整验证状态见docs/WINDOWS_VALIDATION.md。

## 安全边界及已知限制

外部用户解锁器内部会按名称处理游戏及nvngx_update进程，并有窗口/托盘/UAC行为。工具在启动前阻断已运行同名进程及原解锁器；不能保证消除外部二进制内的进程竞态，不伪称完全无窗口。缺配置/超时不自动改走普通启动。

本工具以普通权限运行；必要文件工作进程走标准UAC，并重新校验用户确认的路径/文件指纹。权限、反作弊或杀软阻断时停止，不绕过。部分写入或清除失败有逐项日志，不声称完整回滚。

该包只供本地交付，未发布远端。第三方DLL、解锁器和用户原图各有自己的来源/许可，不统一套用MIT。许可证和静态出处见licenses及knowledge。


