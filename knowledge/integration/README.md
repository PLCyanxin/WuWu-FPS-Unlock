# 整合知识库

- 实际工程 E:\yanxin_ws\wuwa-fps-unlock 开始为空；用户消息的 E:\yanxin\_ws\wuwa-fps-unlock 不存在。
- 从 KnownFolder Desktop/input/WuWaFPSUnlock_Codex_Handoff_v2 复制旧源码；桌面原件只读。
- 包内实际规则名：AGENTS.md、00_START_HERE.md、CODEX_TASK.md、docs/HANDOFF_V2_AUDIT.md、reference/APPROVED_UI.png。用户列出的旧 handoff 五文件、00_先读我.md、references/UI-approved.png 不在此v2包中，未伪称读取不存在的文件。
- 完整读 CODEX_TASK v2，覆盖旧内置注入路线；当前用户追加的同名DLL搜索及哈希保护移除优先于旧AGENTS的保留Vendor要求。
- 本地.NET10.0.100 SDK官方zip SHA512核验，安装到.tools/dotnet；下载曾超时，断点续传完整校验后成功。SDK只读供agent复用。
- 本地ReShade Setup真实在artifacts/reshade-fixture运行，退出0；官方dxgi.dll实际6.8.0.2155，sha256 0cee63f9c9f13f3ac909c5b4903f4dbb4b719a7ab3b4f13b0deaf83c814b94f7。等待官方兼容表可耗时数分钟，不用假进度报成功。
- 图标App.ico旧格式WPF无法解码；实际启动失败后由原Icon.original.jpg做64px等比格式转换，原图未改，WPF解码和启动成功。主/设置布局未重新设计。
- 2026-09-14：核心91/91，Windows集成12/12；日志artifacts/tests.log、windows-tests.log。Windows集成实际Setup执行和全18用户DLL假目录替换/校验/重复/清除，无游戏运行。
- 桌面锁屏时Computer Use返回锁屏画面且无法激活；已停止桌面输入，请用户解锁。原生控件树可读不能代替交互与桌面截图。
- INI原路径D:\Wuthering Waves\Wuthering Waves Game实查根/入口/Shipping存在，交给materials agent生成只读候选映射；尚未用户选定/授权真实操作。
- 仍需：打包验证、原生UI屏幕截图和交互、真实游戏路径/映射一次确认、FPS/MFG游戏内验收。不要把离屏渲染或fake子进程写成游戏通过。


2026-09-14 最终集成：deployment d13dd72→3a9fe58，launch fcdbc65→fe330bd。主工程最终 Build.ps1 真实通过：core91、worker14、Windows12、WPF离屏15，publish成功。桌面锁屏、真实UAC/游戏待确认，见artifacts/DELIVERY_REPORT.md。子agent worktree及上下文保留供复用。
