# 第三方来源与素材范围

## FPS 基础插件

`components/ww_plugin_base.dll` 是从用户上传的 `鸣潮.exe` 中原样静态提取并返回给同一用户的资源，不是本次从源码重新编译的产物。它的偏移、资源长度、PE 架构、哈希和源 EXE 哈希见 PROVENANCE.json。未运行原 EXE，未重新创作或改写该 DLL。只提取 FPS-only 资源，未带入高级插件或完整旧 GUI。

上传 EXE 含 `https://github.com/30launchers/WutheringWaves-FPS-unlocker`，已核对该仓库 LICENSE 文本为 MIT、Copyright (c) 2026 30launchers。许可文本附在 WutherFPSUnlocker-MIT.txt。此前同时检索到 `30LAUNCHER` 等相似仓库；尚未做完整来源审计和可复现构建比对，因此不能仅因某个仓库有 MIT，就认定任何同名二进制和所有内嵌内容自动覆盖该许可。

公开分发前应从确认的上游来源构建并记录提交、构建依赖、许可证和产物哈希，或取得原作者相应授权。此包中的用户二进制只作为本次整合的开发输入。

## 封面和图标

Cover.original.png 和 Icon.original.jpg 为用户提供素材。原始封面版权归相应权利人；头像权利归相应权利人。这里仅按用户要求用于本次界面开发，未对它们授予新许可，也不意味着可以任意公开或商业分发。App.ico 是原头像尺寸/格式转换，不是原创授权替代品。没有重新分发系统字体。

## ReShade、MFG 与 NVIDIA

未捆绑官方 ReShade 二进制，程序采用官网 HTTPS 自动获取官方 Full Add-on 安装器的方式。ReShade 源码许可、官网预编译文件分发要求和实际安装器条款应分别遵守。

未捆绑 MFG addon 或 NVIDIA DLL；必须另外提供已整理且来源清楚的包。它们分别受各自许可约束，不能因应用代码或 FPS 项目采用 MIT 就推导出整个包任意分发权。

本工程提供技术接口与开发文件，不保证游戏官方许可或账号安全。
