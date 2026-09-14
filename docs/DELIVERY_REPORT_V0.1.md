# 鸣潮 FPS Unlock v0.1 开发与验收报告

日期：2026-09-14。工程：E:\yanxin_ws\wuwa-fps-unlock。原生Windows x64 / WPF / .NET 10。

## 代码完成

- UI v0.1，自有程序集与文件0.1.0.0；第三方保持真实版本。旧配置、部署、ReShade来源/所有权与未知JSON字段保留，实际迁移8个data文件，旧包不变。
- FPS OFF单次启动所选Shipping；ON同一路线启动Shipping并加载原字节内置FPS核心。无unlock.exe运行依赖、无外部.NET8运行库。
- 开始游戏按规范完整EXE路径核对并终止同安装旧实例，等待退出后单次启动；持句柄核对身份、启动互斥、超时/权限/身份失败停止。新PID使用当前FPS设置和新会话。
- 新部署后的风险须知在结束游戏前显示一次，取消无杀进程/启动/确认写入；确认持久化，无变化复用不重置，清除重新部署重置。内置核心首次使用另以安装路径与核心哈希记录确认。
- 沿用定稿双窗口，加入自动查找并保留手选。使用原彩色头像，游戏渲染就绪且ON核心连接成功后隐藏主/设置窗口到通知区，双击头像恢复；折叠区域归Windows控制。
- 所选游戏实际ReShade探测、兼容版本复用、来源记录控制清除。本工具替换的DLL依真实映射与哈希删除；不改成保留替换DLL。
- 18 DLL仅来自用户桌面原材料，递归同名现存位置覆盖，不凭猜测创建路径。Setup优先用户本地6.8.0文件。未改驱动、HAGS、全局OTA或绕过系统安全。

## 测试通过

| 实际执行项目 | 结果 | 范围 |
|---|---:|---|
| Core | 93/93 | 业务逻辑、须知、来源、发现、未知JSON |
| Restart.ProcessTests | 25/25 | 自建真实假进程、临时目录；ON/OFF单启动、路径隔离、重复点击、退出竞态、超时/权限模拟、取消、新FPS快照 |
| Windows/ReShade | 12/12 | 用户真实Setup与18DLL在工程假目录实际安装/替换/校验/清除 |
| WPF离屏 | 19/19 | 真实WPF绑定/布局/按钮状态/版本/编译路线；不是桌面截图 |
| WPF通知区生命周期 | 4/4 | 实际窗口显示、隐藏、图标、恢复；GameReady由测试模拟 |
| 数据迁移 | 19/19 | 临时目录冲突/链接保护、字段保留；另实际迁移8文件 |
| Windows x64自包含发布 | 成功 | 真实dotnet publish日志 |

日志：工程artifacts/v0.1下tests.log、restart-tests.log、windows-tests.log、ui-tests.log、tray-tests.log、build.log、migration.log；迁移回归原日志knowledge/materials/migration-tests.log。初次失败记录保留，未删掉后宣称从未失败。fixture测试不替代真实游戏验收。

## 实机通过与证据边界

以下仅属于此前0.9外部解锁器路线：用户反馈游戏无报错、帧率解锁成功、Dynamic看起来正常；ReShade/MFG界面可见，历史日志包含accepted目标160和2–6实际呈现。截图约93–96FPS，不证明稳定160。

文件部署完成、日志accepted和用户观察实际效果分别记录，不把其中任一个自动等同另外两个。历史记录见artifacts/real-run及USER_TEST_RESULTS.md。

用户清除19文件后执行官方文件校验，反馈恢复后可进入游戏，FPS解锁和多帧生成消失。官方校验恢复有用户证据；普通启动自行补齐没有独立成功证据，不能写成清除后自动恢复通过。

## 未验证与限制

- v0.1内置核心真实加载/帧率控制、真实游戏自动终止重启、会话重建、游戏就绪后自动收起，尚未实机通过。旧外部路线结果不延用。
- 新版本会使用普通Windows进程访问权限；拒绝访问即停止，未绕过UAC或反作弊。必要的管理员启动由用户按Windows正常方式操作。
- 稳定160输出、长期稳定、其他驱动/游戏更新兼容、多显示器实际DPI、普通启动自动补齐未验证。
- Windows决定托盘图标在可见区还是折叠区，应用不修改用户全局任务栏偏好。
- 新真实验收会强制结束同安装游戏并加载内置核心，超出之前外部解锁器路线范围，待一次集中确认。计划详见REAL_GAME_TEST_PLAN.md。

## 交付位置

便携包：E:\yanxin_ws\wuwa-fps-unlock\artifacts\v0.1\WuWaFPSUnlock-0.1-win-x64-portable.zip

完整源码：同目录WuWaFPSUnlock-0.1-source.zip。源码包含历史审计文件；旧外部路线不编译、不发布。

可运行入口：同目录win-x64\WuWaFpsUnlock.exe。关闭旧启动器后使用，保留整个文件夹。

材料版本/哈希：payload/source-manifest.json；逐文件游戏映射：artifacts/real-game-candidate-map.md（历史扫描，不代表当前修复后的路径状态）；新版逐文件包哈希：artifacts/v0.1/portable-file-hashes.json；ZIP哈希：DELIVERY_SHA256.txt。

新版WPF离屏图：artifacts/v0.1/native-offscreen；旧版实际桌面截图：artifacts/real-run/screenshots。没有把离屏图或旧版图当作新版真实游戏截图。
