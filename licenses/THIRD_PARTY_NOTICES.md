# 第三方组件与素材

鸣潮 FPS Unlock 使用以下第三方组件。各组件保留自己的版本、版权与许可，不因随本工具分发而统一采用本项目的许可证。

## 帧率模块

`components/fps/ww_plugin_base.dll` 来自维护者提供的 FPS 解锁程序，来源说明见 `components/PROVENANCE.json`，相关 MIT 许可见 `WutherFPSUnlocker-MIT.txt`。本工具启动游戏并加载该模块，不需要运行外部 `unlock.exe`。

## 多帧生成、ReShade 与 NVIDIA 运行库

完整版包含维护者提供的 ReShade 6.8.0 Full Add-on 安装器、MFG addon 和 DLSS / Streamline 材料。相关组件的版权与使用条件属于其各自权利人。

带 Dynamic 最大倍率支持的 addon 基于 [mavismmg/MFGAdaUnlock-RenoDx](https://github.com/mavismmg/MFGAdaUnlock-RenoDx) tag `1.1`（提交 `c3733d8afd51214c46a71d18feec520b0bf54864`）重新构建，派生版本为 `1.1+WuWu.DynamicMax.Runtime.1`。修改提供版本限定的进程内 Dynamic 运行时上限控制与诊断，不使用旧的启动 DRS 上限覆盖，不是未经修改的上游发行文件。

源码补丁与构建方法见 `third_party/mfgunlock`。来源记录见源码中的 `release-assets/mfg/source.json`，随包记录为 `payload/addon-source.json`；它们列出源码基线、构建依赖和产物哈希。DLL 清单中的其他 DLSS / Streamline 材料保持既有字节，不随此补丁升级。

MFG addon 遵循 [MIT 许可](MFGAdaUnlock-MIT.txt)，保留 Dreamt、dashdogy、mavismmg 及[上游完整 credits](MFGUnlock-UPSTREAM-CREDITS.md)。构建使用的 RenoDX、ReShade、Dear ImGui、Microsoft Detours、Streamline、NVAPI ABI 资料与 NVIDIA RTX SDK 保留各自随附许可。最小 NVAPI ABI 镜像仅用于已知 DRS V1 结构，不静态链接或分发 NVAPI 驱动库。

ReShade 安装器及既有 NVIDIA 运行库来自维护者提供的材料。本地哈希用于区分文件，不证明这些材料与某个公开源码构建等价。Windows 驱动与运行库的支持范围仍由各自提供方决定。

## 图片素材

封面和头像由维护者提供。图标使用原彩色头像的透明版本，未重新绘制。素材的版权归原权利人所有。

## Microsoft .NET

Windows x64 便携包包含 Microsoft .NET 运行时。构建使用项目 `global.json` 指定的 SDK；.NET 组件遵循各自随附许可。
