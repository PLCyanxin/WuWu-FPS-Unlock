# 第三方组件与素材

鸣潮 FPS Unlock 使用以下第三方组件。各组件保留自己的版本、版权与许可，不因随本工具分发而统一采用本项目的许可证。

## 帧率模块

`components/fps/ww_plugin_base.dll` 来自维护者提供的 FPS 解锁程序，来源说明见 `components/PROVENANCE.json`，相关 MIT 许可见 `WutherFPSUnlocker-MIT.txt`。本工具启动游戏并加载该模块，不需要运行外部 `unlock.exe`。

## 多帧生成、ReShade 与 NVIDIA 运行库

完整版包含维护者提供的 ReShade 6.8.0 Full Add-on 安装器、MFG addon 和 DLSS / Streamline 材料。相关组件的版权与使用条件属于其各自权利人。

v1.1.1RC 使用的 addon 来源记录见源码中的 `release-assets/mfg/source.json`；包内 `payload/addon-source.json` 记录相同文件。完整版的 `payload/source-manifest.json` 和 `payload/payload-map.json` 已同步本次 addon 的大小与哈希，其余运行库未因本次更新而更换。使用更新包时保留原有整体清单，新 addon 以独立来源记录为准。

这些二进制由维护者提供并按原始字节分发。本地哈希用于区分文件，不表示对上游来源或可复现构建的证明。

## 图片素材

封面和头像由维护者提供。图标使用原彩色头像的透明版本，未重新绘制。素材的版权归原权利人所有。

## Microsoft .NET

Windows x64 便携包包含 Microsoft .NET 运行时。构建使用项目 `global.json` 指定的 SDK；.NET 组件遵循各自随附许可。
