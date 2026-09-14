# v0.1 当前交接

最新要求优先于docs/baseline及历史外部unlock.exe说明。主实现是原生WPF，v0.1/0.1.0.0，保留配置、来源与部署记录。FPS单一路线为应用启动Shipping和内置核心。自动重启仅终止规范完整路径匹配的游戏，持句柄核对身份，失败停止。须知在终止前展示，仅首次新部署或核心首次使用；不重置已确认状态。游戏就绪后收起通知区，系统决定折叠位置。

测试：核心93、重启fixture25、本地Setup与假目录12、原生WPF离屏19、真实窗口通知区生命周期7、迁移19全部通过；真实旧包8个data文件迁移完成，源不变。对应日志在artifacts/v0.1和knowledge/materials。通知区GameReady在测试中模拟，不能充当真实游戏联动验收。

旧版用户确认FPS及Dynamic正常，日志accepted160；清除后通过官方校验恢复游戏。旧证据不证明v0.1内置核心工作，也不证明普通启动自动补齐。详情见docs/WINDOWS_VALIDATION.md。

当前发布artifacts/v0.1/win-x64；打包scripts/Package.ps1。新实机计划artifacts/v0.1/REAL_GAME_TEST_PLAN.md，包含强制结束同安装游戏和加载内置核心，尚待一次集中确认。未获新确认前不操作真实游戏；不得把原外部路线批准自动扩展为新注入/强制重启批准。

复用launch、deployment、materials三个agent及.agent-worktrees隔离Git工作区，各自知识库knowledge下保留。主工作树由root整合。桌面原材料只读，不重新下载或混搭；清除替换DLL规则未改变。

最新托盘补充：主窗口×收起托盘；游戏就绪自动收起；双击恢复；右键退出启动器才真正退出。使用用户最新图标透明版.png原件生成32位ICO，无新增圆角或抠图。实际原生托盘回归7/7，游戏就绪事件仍为测试模拟。

