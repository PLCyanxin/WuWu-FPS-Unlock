# 第三方文件与素材来源

当前便携包面向本次用户本地交付，未公开发布。各组件保留自己的来源与许可，不把整个二进制包统一声明为MIT。

## 内置FPS核心

v0.1便携包components/fps/ww_plugin_base.dll为用户提供程序input/unlocker/鸣潮.exe中的原始34304字节x64核心，与components/ww_plugin_base.dll逐字节一致。SHA256为844d7552692f53e8a1bfe45edf360a094597c5bf2b26dc058ff59b21d2250c3a，来源核验见components/PROVENANCE.json。没有重写或重新下载该核心。当前程序自行启动所选Shipping并加载核心，不执行外部unlock.exe；历史外部启动代码和原输入仅留作来源审计，不参与运行路线。新版实际游戏兼容性仍待验收。

用户EXE包含30launchers/WutheringWaves-FPS-unlocker来源引用，附WutherFPSUnlocker-MIT.txt。未做完整可复现构建比对，不将同名仓库许可证视为任意同名二进制所有内容的授权证明。

## 本地图像

Cover.original.png、Icon.original.jpg来自用户交接包；桌面更高分辨率封面保持原件。仅裁剪、等比缩放、渐变；App.ico由原彩色头像做格式转换，未重绘。

## ReShade、MFG、NVIDIA/Streamline

包内包含用户提供的ReShade_Setup_6.8.0_Addon.exe、renodx-mfgunlock.addon64和18个替换DLL；未下载另一个版本替换用户包。来源、版本、数字签名实际结果、SHA256在payload/source-manifest.json。NVIDIA DLL和addon各有自身许可，不派生自本工具代码许可。

addon静态配置审计使用上游0.9代码与公开发布哈希；相关源码片段保留原版权声明，出处见knowledge/deployment/ADDON_CONFIG_AUDIT.md。公开分发前需单独核对这些文件及原图的再分发授权；本次未公开发布。

## Microsoft .NET

Windows x64自包含使用.NET10.0.0运行库，SDK10.0.100固定，来源及校验记录见knowledge/materials。v0.1不包含或依赖旧外部解锁器的.NET8运行库；历史记录不代表当前交付内容。
