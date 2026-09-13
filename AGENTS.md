# 鸣潮 FPS Unlock：项目规则

开始前完整阅读 CODEX_TASK.md 和 docs/HANDOFF_V2_AUDIT.md。本文件与新版任务反映用户最新决定；旧 README/docs 中冲突的产品要求已过时，但其代码位置、来源和未验证记录仍应审查。不要删除仍适用的安全边界或测试。

- 主整合区 E:\yanxin_ws\wuwa-fps-unlock；子agent在.agent-worktrees内各自Git worktree和分支工作，knowledge/按角色持久保存上下文；优先复用原agent。先保护已有工作，不直接覆盖桌面原材料。
- 原生 WPF 双窗口；reference/APPROVED_UI.png 是布局定稿。封面和头像使用 Assets 原文件，只裁剪、等比缩放、渐变遮罩；不重绘、不做网页套壳。
- 主窗口只有 FPS 开关/数值/滑块、设置、开始游戏；设置窗口负责路径、环境、MFG 选择、部署、清除和日志。
- 最新启动分支：FPS 开 -> 调用用户提供的 unlock.exe；FPS 关 -> 直接运行用户手选的原装 Shipping。我们的进程不再注入 FPS DLL或独立扫描/写入游戏内存，也不并行再启动第二个游戏。
- 使用用户本地 DLSS/Streamline/addon/Full Add-on Setup，不自行下载“更合适”或 latest 的一套替换用户包。不根据同名 DLL 猜目标路径。
- 未经验证不假设 unlock.exe 有 CLI；先确认其 INI、自动启动、工作目录、游戏路径与日志行为。运行中改 INI不等于已即时调帧。
- MFG 的 Fixed 与 Dynamic 是能力层次，不是互斥安装包。595.41 仅是当前 Dynamic 驱动条件，不能把它用作整个应用或 FPS 功能门槛。Unknown 不等于 Unsupported。
- ReShade 已有则检查并优先复用；未知代理不覆盖；升级需确认；保留已有 INI、滤镜和其他插件。不叉出精简 ReShade。
- 当前用户最新要求：在所选游戏根内搜索同名DLL替换，无同名跳过，不新增，不跟链接。所有目标逐项显示后一次确认。清除移除本工具实际替换且当前哈希仍匹配的白名单DLL和自有addon/配置；不备份或恢复原DLL。不认领本来已相同的文件，不删除Shipping、unlock.exe、用户ReShade/滤镜。不能保证游戏自行补齐。
- 编译、测试、文件写入、进程启动、游戏内效果是不同成功层级；不能用 HTML/静态检查伪装 WPF/实机通过。
- 真实游戏写入、执行 unlock.exe 或启动游戏前先明确确认一次。不得关闭安全软件、反作弊、签名校验、全局 NVIDIA OTA 或任意绕过权限。
- 最终交付 Windows x64 产物、哈希、依赖目录、构建/测试日志、原生 UI 截图和未验证项。做到可做的部分，不以方案文字替代开发。

- 文档现状：v2包的CODEX_TASK.md、docs/HANDOFF_V2_AUDIT.md、reference/APPROVED_UI.png对应原提示词的新版交接内容；旧handoff文件不在本包，不伪称读取。docs/baseline仅为旧审计参考。

