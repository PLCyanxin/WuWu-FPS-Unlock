# 实现结构与交付边界

## 工程

`WuWaFpsUnlock.Core`：纯 .NET 核心，包含包清单、路径验证、INI 键级合并、写入记录与所有权清除。

`WuWaFpsUnlock`：原生 WPF，包含两个独立窗口、自绘矢量控件与原图横幅、共享 ViewModel、Windows/NVAPI/PE 查询、官方 ReShade Setup 调度、FPS 会话与受限提权工作进程。当前使用本项目 WPF Styles，不依赖完整 WPF UI/第三方主题包；这是为了稳定复现定稿样式，而不是界面换成网页。

`WuWaFpsUnlock.Tests`：无需第三方测试框架的控制台回归测试。只在测试目录操作假数据，不加载 DLL 或启动游戏。执行结果作为发布前门槛。

`preview/index.html`：独立交互模型，用于布局和基础交互预览。它不是前端运行时、不是 WPF 的实际截图，也不会访问本机 GPU 或游戏。

## 状态机

UI：Idle → Precheck → Busy(Deploy / Clean / Launch) → Ready 或 Error。两个窗口共享 Busy，不能不同窗口各自发起并行破坏操作。

部署记录：NotInstalled → InstallingReShade / Installing → Deployed。异常落为 PartialFailure，不能被“文件有一部分存在”覆盖成成功。清除后为 Cleaned。

启动：确认路径 → 检查既有部署记录 → 启动或确认连接已有游戏 → 等待实际渲染进程 → 可选加载 FPS DLL → 管道 PID 校验 → 发送上限。正常日常启动不会自动重跑 ReShade 安装。

FPS：未连接/已连接/发送失败/游戏退出。原插件没有已验证的停用和实测 FPS 返回协议，因此不虚构 Active FPS 数字或热卸载；连接成功与实际生效分层。

MFG：文件部署完成与运行态接受 Dynamic 是不同状态。本版仅输出驱动门槛与运行态待确认，不用日志关键字的偶然命中假冒 capability。

## 关键设计约束

- 用户手选目标，阻止磁盘根目录、已知越界/ADS/设备名/重复目标/链接写入。
- 文件包是明确允许文件名与目标锚点的描述，不是任意 shell/PowerShell 脚本执行器。
- ReShade 已有安装优先复用，不改用户 AddonPath，不把一个导出符号当成完整运行验证。
- 原文件不备份；逐文件暂存与日志不能被描述成可完整回滚的总事务。
- 清除权只来自写入记录和哈希；曾经看到同名文件不能证明本工具拥有它。
- UI 常规权限。UAC worker 只接受部署/清除两种任务，并重新校验请求、目标与载荷。
- 游戏插件失败时不修改反作弊、不给驱动/安全软件做白名单、不会隐藏或规避检测。
- 仅使用用户原图。预览模拟数据与实际原生检测分离。

## 主要公开来源

以下来源用于接口与方案核对；除明确列出的代码许可和已上传资源外，并未把所有上游源码打包。源代码读取不能替代实机验证。

1. ReShade 6.8.0 Setup 参数、安装路径和更新流程：
   https://github.com/crosire/reshade/blob/v6.8.0/setup/MainWindow.xaml.cs
2. ReShade 6.8.0 AddonPath、Full/Limited 构建差异和 API 注册：
   https://github.com/crosire/reshade/blob/v6.8.0/source/addon_manager.cpp
3. ReShade 官方下载、Full Add-on 和分发提示：
   https://reshade.me/
4. MFG Unlock 0.9 需求和版本/运行态说明：
   https://github.com/mavismmg/MFGAdaUnlock-RenoDx/blob/0.9/README.md
5. FPS 解锁器的 WPF/原生插件项目与许可：
   https://github.com/30launchers/WutheringWaves-FPS-unlocker
   另核对到同名近似仓库 30LAUNCHER/WutheringWaves-FPS-unlocker。上传二进制包含前者链接；没有完成完整可复现构建匹配，不把名称对应等同于逐字节来源审计。
6. 微软 .NET SDK 的本地安装入口及发布文档：
   https://dot.net/v1/dotnet-install.ps1
   https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-publish

## 尚未闭合

尚未编译 WPF，尚未执行 .NET 回归测试，尚未运行 Windows/RTX/鸣潮测试。实际 MFG 包、目标相对路径和成功代理布局缺失。当前 ReShade 检测仅覆盖明确支持的游戏内布局；游戏外共享 addon 目录、别的 hook 链和未知版本会停止。FPS 原生 DLL 保持用户上传版本，未审计每一条内存扫描路径、未验证当前游戏更新后的地址匹配，也没有已确认的 native 停止/ACK 协议。

原生界面的比例、DPI 和交互需要在 WPF 本身复核；HTML 通过并不说明 WPF 一定没有编译错误、文本裁剪或布局差异。开发接续应先处理这些门槛，再合入真实载荷，而不是再生成一张看起来像安装成功的图片。
