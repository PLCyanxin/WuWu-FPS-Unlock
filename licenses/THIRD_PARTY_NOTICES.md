# 第三方文件与素材来源

当前便携包面向本次用户本地交付，未上传到远端。各组件保留自己的来源与许可，不把整个二进制包统一声明为MIT。

## 外部FPS解锁器

components/unlocker/unlock.exe来自用户input/unlocker/鸣潮.exe，原字节未改，SHA256见components/PROVENANCE.json。仅静态读取程序集、配置和有效PE清单以适配启动；本轮没有执行它。其清单要求管理员权限，按标准UAC执行，不重写内部插件。旧components/ww_plugin_base.dll仅保留于源码基线，当前编译和便携包均不使用它。

用户EXE包含30launchers/WutheringWaves-FPS-unlocker来源引用，附WutherFPSUnlocker-MIT.txt。未做完整可复现构建比对，不将同名仓库许可证视为任意同名二进制所有内容的授权证明。

## 本地图像

Cover.original.png、Icon.original.jpg来自用户交接包；桌面另外提供的更高分辨率封面保持原件。仅裁剪、等比缩放、渐变；App.ico由原头像做格式转换以修复WPF解码兼容，未重绘。

## ReShade、MFG、NVIDIA/Streamline

包内包含用户提供的ReShade_Setup_6.8.0_Addon.exe、renodx-mfgunlock.addon64和18个替换DLL；未下载另一个版本替换用户包。来源、版本、数字签名实际结果、SHA256在payload/source-manifest.json。ReShade的源码许可、预编译文件及用户输入来源需分别看待；NVIDIA DLL和addon各有自身许可，不派生自本工具代码许可。

addon静态配置审计使用上游0.9代码与公开发布哈希；相关源码片段保留原版权声明，出处见knowledge/deployment/ADDON_CONFIG_AUDIT.md。发布到第三方前仍须单独核对这些文件及原图的再分发授权；本次未进行公开发布。

## Microsoft .NET

本工具Windows x64自包含使用.NET10.0.0运行库，SDK10.0.100固定；外部解锁器附加.NET8.0.31 NETCore和WindowsDesktop x64。运行库来自微软官方zip，SHA512与官方release metadata一致，出处及逐文件哈希见components/dotnet8/PROVENANCE.json与knowledge/materials。组件目录包含Microsoft LICENSE.txt及ThirdPartyNotices.txt。
