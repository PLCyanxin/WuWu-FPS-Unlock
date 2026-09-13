# 鸣潮 FPS Unlock：Codex 交接包 v2

这是“旧源码 + 新要求 + 原始素材 + 用户解锁器”的交接包，不是已经修改并编译好的 v2 软件。
本包不执行任何程序；旧 `src/`、`tests/`、`scripts/` 保持 dev1 原样，需由 Codex 按新版任务修订。

## 先后顺序

先读 `AGENTS.md`，再完整阅读 `CODEX_TASK.md` 和 `docs/HANDOFF_V2_AUDIT.md`。
本次最新决定：勾选“解锁帧率上限”时仅调用用户的 `unlock.exe`；未勾选时直接运行用户选择的原装 Shipping EXE。
这取代旧 README 和交接文档里的“本程序加载 FPS DLL 并通过管道控制”方案。

## 放在哪里

建议解压包内内容到 `E:\yanxin_ws\wuwa-fps-unlock\`，让 AGENTS.md、src、scripts 位于该项目根目录。
已有同名工作目录时，先检查现有代码和未提交更改；不要覆盖已有工作。
开发、依赖缓存、日志和产物放在此独立项目目录；桌面素材作为只读输入。

## 已有资料

- dev1 WPF 源码、原测试与构建脚本。
- `reference/APPROVED_UI.png`：最终认可的双窗口效果图，只用于布局/风格参考。
- `src/WuWaFpsUnlock/Assets/Cover.original.png`、`Icon.original.jpg`：用户原始封面和头像；不能重绘。
- `input/unlocker/鸣潮.exe`、`ww_fps_config.ini`：用户上传的原解锁器与配置，字节未改、没有运行。发布时可将经验证的副本命名为 `components/unlocker/unlock.exe`。
- 桌面资料的两张截图，便于只读定位。截图不是对应 DLL 的二进制，也不能提供可信版本或目标路径。

## 需要在用户电脑读取的资料

桌面素材目录：`替换\`（截图显示 18 个 DLL）、`renodx-mfgunlock.addon64`、`ReShade_Setup_6.8.0_Addon.exe`。
这些实际二进制不在本交接包中，应由具有本机文件访问权限的 Codex 从用户桌面接入；不需要为了开发重新上传数百 MB 到聊天。
若桌面已有另一个 `unlock.exe`，先与包内用户程序比较哈希与版本。不同就报告并确认采用哪一版，不混用其配置。

若用户已提供的游戏根目录/Shipping 路径无效，在设置中手动选择；不得把旧 INI 的 PathValue 当成真实 Shipping 路径的证明。

## 最短启动提示词

“请在 E:\yanxin_ws\wuwa-fps-unlock 工作，先完整阅读 AGENTS.md、CODEX_TASK.md、docs/HANDOFF_V2_AUDIT.md，按最新规则修改并完成 Windows 构建、原生界面及假目录测试。桌面已有替换 DLL、addon64 和 ReShade Setup，请先只读定位，再接入。不要继续使用旧的内置 FPS 注入路线；勾选 FPS 时调用 unlock.exe，否则直接运行原装 Shipping。真实游戏写入或启动前先给一次明确确认，不要只交付计划或 HTML 预览。”
