# 交接核查：v2 需求与 dev1 基线差异

此记录由本次静态读取 dev1 ZIP、用户上传配置和原图后生成；没有执行用户 EXE，没有 Windows 构建或鸣潮测试。本包只是交接资料补充，代码还没有迁移到 v2。

## 1. 启动路线冲突

`src/WuWaFpsUnlock/ViewModels/AppViewModel.cs` 仍持有 FpsSession 并在 FPS 启用时检查 AppPaths.Plugin、加载基础 DLL。
`Services/AppPaths.cs` 的 Plugin 指向 components/ww_plugin_base.dll；项目文件也将其复制到发布目录。
这些要按照新的“外部 unlock.exe / 原装 Shipping”路由改造，不应继续作为默认路径。

## 2. 本地 ReShade Setup 还没有优先入口

`Services/ReShadeService.cs` 的入口调用 DownloadSetupAsync，构造并下载 ReShade_Setup_{Version}_Addon.exe。
用户现已提供本地 ReShade Setup，因此要增加经过校验的本地来源优先，并保留复用已有 Runtime 的逻辑。

## 3. 两处 DLL 白名单不覆盖实际截图

`scripts/Prepare-Payload.ps1` 与 `src/WuWaFpsUnlock.Core/SafePaths.cs` 都列有 13 个 NVIDIA/Streamline 文件名。
用户截图 18 项中，有 5 项不在旧表：NvLowLatencyVk.dll、nvngx_deepdvc.dll、nvngx_dlssnr.dll、sl.dlss_nr.dll、sl.nvperf.dll。
需要同步扩充和按实际内容审核，而不是放开所有文件或悄悄丢弃它们。

## 4. 平铺材料和目录树有区别

旧准备脚本把 GameRootFiles 下的相对路径直接作为 GameRoot 目的路径。
用户的“替换”目录是平铺输入，因此直接运行旧脚本可能生成错误目标。
先形成明确的逐文件部署映射，再生成清单；多份同名 DLL 不得批量猜测覆盖。

## 5. 配置提示

用户上传 INI：FpsValue=240，AutoStartEnabled=False，DX11Enabled=False，PathValue 指向根入口 Wuthering Waves.exe；GameLaunchExe=0、GameServerArea=2。
这些只证明现有配置值，不证明枚举语义，也不证明外部工具已支持静默/无窗口启动。必须核对实际程序。

## 6. 本次打包资产

用户封面和头像与 dev1 Assets 文件 SHA-256 一致，没有重绘或修改。最终认可的 UI 效果图另外存入 reference/APPROVED_UI.png，不能使用 HTML 预览替代它的布局权威性。
用户 EXE 和 INI 已按原字节加入 input/unlocker。本包仍不含截图中桌面 18 DLL、addon64 或 ReShade Setup 的二进制。

## 7. 本包状态

旧源码/脚本/测试逐文件保持原 ZIP 内容；新增交接说明、AGENTS、素材引用和用户解锁器。没有生成新的成品 EXE，没有执行任何解锁器或游戏。
