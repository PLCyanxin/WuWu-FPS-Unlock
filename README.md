# 鸣潮 FPS Unlock

用于《鸣潮》的 Windows 启动工具，提供帧率上限解锁和可选的多帧生成组件部署。使用原生 WPF 界面，当前版本为 **v1.1RC**。

[下载 Windows x64 便携版](https://github.com/PLCyanxin/WuWu-FPS-Unlock/releases/latest) · [查看所有版本](https://github.com/PLCyanxin/WuWu-FPS-Unlock/releases)

## 快速开始

**已有旧版本？** 下载 [升级到 v1.1RC 的通用更新包](https://github.com/PLCyanxin/WuWu-FPS-Unlock/releases/download/v1.1RC/WuWaFPSUnlock-universal-to-1.1RC-update.zip)。先退出游戏和托盘里的启动器，将更新包解压到原 `WuWaFpsUnlock.exe` 所在目录，双击 `更新.exe`。完成后打开启动器，在设置中重新部署一次。

更新器不要求旧版本号或文件哈希相同，可用于本项目 GitHub 和网盘分发的旧版本；需要保留原程序名称及目录结构。它更新启动器、内置 FPS 核心与随附许可，并备份被替换的文件；保留配置、部署记录和自行更新的 MFG/ReShade 材料，不自动修改游戏文件。不支持的目录或被占用的文件会明确提示。首次使用请下载下方完整便携版。

1. 在 Release 页面下载 `WuWaFPSUnlock-1.1RC-win-x64.zip`，完整解压后运行 `WuWaFpsUnlock.exe`，按 Windows 提示授予管理员权限。
2. 打开“设置”，点击“帮我查找鸣潮”，或手动选择游戏根目录。正确的游戏程序是 `Client\Binaries\Win64\Client-Win64-Shipping.exe`；选择正确目录后会自动填写。
3. 开启“解锁帧率上限”，设置目标 FPS，返回主窗口点击“开始游戏”。

只使用 FPS 解锁时无需部署多帧生成。便携包包含 .NET 运行时，请保留整个目录，不要单独移动 EXE。

## 帧率与启动

目标帧率可设为 **30–420 FPS**。开关和帧率设置在下一次启动游戏时应用，实际帧率还取决于硬件、画质和游戏场景。

FPS 开启时由内置核心工作，无需另开外部解锁器。游戏运行期间请让启动器保持运行，收起到托盘即可。

同一安装的游戏已经运行时，点击开始游戏会结束旧实例，再启动新实例，可能丢失尚未保存的状态。工具根据完整 EXE 路径识别游戏，不按进程名批量结束其他安装或官方启动器。

首次使用内置 FPS 核心或完成新的插件部署后会显示须知，确认后保存；取消不会结束或启动游戏。

## 窗口与托盘

- 游戏进程启动后，启动器立即收起到通知区；启动完成后按钮显示“游戏中”。确认游戏退出后，启动器会自动退出。
- 后续启动或 FPS 连接失败时，启动器会恢复窗口显示错误。
- 游戏运行时点击 × 会收起到托盘，没有游戏运行时点击 × 会退出。
- 双击托盘图标或再次打开桌面快捷方式，可恢复已有窗口。
- 完全退出请使用托盘右键菜单中的“退出启动器”。

“游戏中”按钮仍可点击，用于重新启动。自动搜索只在点击“帮我查找鸣潮”后执行，当前路径有效时直接使用。

## 多帧生成

在游戏关闭时，打开设置中的“多帧生成部署”，点击“开始部署”，查看文件映射并确认。开启这一选项后，启动游戏会附加 `-dx12` 参数。

工具在所选游戏目录中查找同名 DLL，并在原位置替换；找不到的文件会跳过，不创建猜测路径。此前记录为存在的目标文件如果缺失，会提示先通过官方启动器修复游戏。

ReShade 只检测所选游戏实际使用的安装。已有兼容版本会优先复用；需要安装时使用本地 ReShade 6.8.0 Full Add-on 安装器，升级已有版本前会显示确认。

Fixed 与 Dynamic 使用同一套组件。当前 Dynamic 驱动预检查门槛为 595.41，不作为 FPS 解锁的门槛。驱动条件满足、文件部署完成和游戏内实际生效是不同结果，最终效果以游戏内表现为准。

### 更新组件

便携包中的材料位于：

```text
payload/
├─ manifest.json
├─ files/
│  ├─ addon/renodx-mfgunlock.addon64
│  └─ game/                       # DLSS / Streamline DLL
└─ setup/ReShade_Setup_6.8.0_Addon.exe
```

更新 addon 时，将新版文件放到 `payload/files/addon/renodx-mfgunlock.addon64`，关闭游戏后重新点击“开始部署”。DLL 材料可在 `payload/files/game/` 中按原文件名更新。仅替换材料目录不会自动更新游戏文件。

部署读取当前材料，不要求其大小或哈希与清单旧值一致；日常启动也不会因已部署 MFG 文件的哈希变化而被拦住。文件名和路径限制、部署确认后的变化检查、清除时的归属保护仍然保留。内置 FPS 核心和 ReShade 安装器有各自的检查规则。

## 清除插件

退出游戏后，在设置中点击“清除插件”，查看清除范围并确认。

工具依据部署记录、来源和当前哈希处理替换 DLL、MFG addon 及相关配置。无法确认归属或已被修改的项目可能跳过，原因会写入日志。用户原有的 ReShade、滤镜、其他 addon 和无关配置会保留；由本工具安装的 ReShade 按来源记录处理。

清除不会恢复原版 DLL，之后可能需要官方文件校验和修复。普通启动自动补齐尚无独立成功证据。

第三方组件可能因游戏更新出现不兼容、崩溃或账号风险，不保证所有游戏版本均可使用。

## 配置与日志

配置、部署记录和日志保存在程序目录的 `data/` 中，运行日志也可以在设置窗口查看。更新程序时请保留该目录，以免丢失清除所需记录。

迁移到新目录前，先从托盘退出旧版本。在 PowerShell 7 中执行：

```powershell
./scripts/Migrate-UserData.ps1 -SourceDirectory '旧程序目录' -DestinationDirectory '新程序目录'
```

脚本保留旧目录，复制已有配置和记录；目标已有不同内容时会停止，避免覆盖。

## 从源码构建

开发环境：Windows x64、.NET SDK 10.0.100（见 `global.json`）。在仓库根目录运行：

```powershell
dotnet publish ./src/WuWaFpsUnlock/WuWaFpsUnlock.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./artifacts/v1.1RC/win-x64
```

FPS 核心与许可证随项目发布。源码仓库不包含完整的 MFG 材料包；需要多帧生成时，还应将准备好的完整 `payload` 目录复制到发布目录。构建成功不代表组件已经在游戏内生效。

| 目录 | 内容 |
| --- | --- |
| `src/WuWaFpsUnlock` | WPF 界面、游戏启动、FPS 会话和 ReShade 集成 |
| `src/WuWaFpsUnlock.Core` | 部署规划、路径处理、配置与清除记录 |
| `components` | FPS 核心及来源说明 |
| `scripts` | 构建、材料准备和配置迁移脚本 |
| `tests` | 核心逻辑及 Windows / WPF 测试 |
| `licenses` | 第三方许可证与说明 |

第三方组件保留各自的版本和许可信息。

