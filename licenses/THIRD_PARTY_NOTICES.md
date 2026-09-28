# 第三方组件与素材

鸣潮 FPS Unlock 使用以下第三方组件。各组件保留自己的版本、版权与许可，不因随本工具分发而统一采用本项目的许可证。

## 帧率模块

`components/fps/ww_plugin_base.dll` 由 [30launchers/WutheringWaves-FPS-unlocker](https://github.com/30launchers/WutheringWaves-FPS-unlocker) 的 FPS-only 源码重构编译，修改源码位于 `tools/FpsCore`，来源说明见 `components/PROVENANCE.json`，相关 MIT 许可见 `WutherFPSUnlocker-MIT.txt`。本工具启动游戏并加载该模块，不需要运行外部 `unlock.exe`。

## 多帧生成、ReShade 与 NVIDIA 运行库

完整版包含维护者提供的 ReShade 6.8.0 Full Add-on 安装器、MFG addon 和 DLSS / Streamline 材料。相关组件的版权与使用条件属于其各自权利人。

`renodx-mfgunlock.addon64` 基于 [mavismmg/MFGAdaUnlock-RenoDx 1.1.5](https://github.com/mavismmg/MFGAdaUnlock-RenoDx/releases/tag/1.1.5)，源码标签对应提交 `4406e4fadf4423afb500d8d9a08d5ace9a148d19`。本项目增加中文显示层与倍率控件的界面桥接，源码和构建入口位于 `tools/MfgUiIntegration`；此文件是定制构建，并非上游原始发布文件。原版 CUDA 数据及其索引表保持不变。

Dynamic 倍率上限由独立的 `wuwa-dynamicmax.addon64` 提供。其 MIT 许可、源码和构建说明位于 `tools/DynamicController`。该组件只适配经过核对的运行库版本，不修改驱动配置。启动器内嵌此组件，并在部署时安装到游戏的 ReShade addon 目录。

主插件来源和哈希见 `release-assets/mfg/source.json`（随包为 `payload/addon-source.json`）；独立组件来源见 `release-assets/dynamicmax/source.json`。其他 DLSS / Streamline 材料保持原有版本。
MFG addon 遵循 [MIT 许可](MFGAdaUnlock-MIT.txt)，保留 Dreamt、dashdogy、mavismmg 及[上游完整 credits](MFGUnlock-UPSTREAM-CREDITS.md)。构建使用的 RenoDX、ReShade、Dear ImGui、Microsoft Detours、Streamline、NVAPI ABI 资料与 NVIDIA RTX SDK 保留各自随附许可。最小 NVAPI ABI 镜像仅用于已知 DRS V1 结构，不静态链接或分发 NVAPI 驱动库。

ReShade 安装器及既有 NVIDIA 运行库来自维护者提供的材料。本地哈希用于区分文件，不证明这些材料与某个公开源码构建等价。Windows 驱动与运行库的支持范围仍由各自提供方决定。

## 图片素材

封面和头像由维护者提供。图标使用原彩色头像的透明版本，未重新绘制。素材的版权归原权利人所有。

## Microsoft .NET

Windows x64 便携包包含 Microsoft .NET 运行时。构建使用项目 `global.json` 指定的 SDK；.NET 组件遵循各自随附许可。
