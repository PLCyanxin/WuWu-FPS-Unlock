# 当前开发交接

当前版本已在Windows实际编译，核心85/85及Windows集成12/12通过；原生离屏15/15通过。现行规则见AGENTS、README与docs/WINDOWS_VALIDATION.md；docs/baseline是旧dev1，不能继续内置FPS注入路线。

子agent知识及Git隔离空间见knowledge/README.md。优先复用launch、deployment、materials三个agent；所有提交已由main整合。

已导入桌面20项材料，副本SHA一致。已实际只读发现D:\Wuthering Waves\Wuthering Waves Game中的18 DLL、ReShade、addon均与用户材料一致；候选映射见artifacts/real-game-candidate-map.md。旧INI仅提供线索，未默认填入用户设置，未写游戏。相同文件不构成本工具所有权。

剩余工作：用户解锁桌面后进行主/设置实际窗口开合、数值同步、输入/快捷键、最小化/多DPI和桌面截图；用户集中确认候选路径/完整映射/配置操作后，才可真实部署、清除或启动。已完成的原生离屏图必须标注离屏，不能冒充桌面截图。

真实游戏FPS/MFG四组合、实际加载模块、Dynamic接受状态、240/180/240测量、官方补齐文件、UAC/权限/反作弊兼容尚未验证。ExternalUnlockerService只调用用户程序，内部额外窗口/托盘/按名称进程管理仍是该二进制限制。
