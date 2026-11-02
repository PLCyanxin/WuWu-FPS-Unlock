# WuWa DLSS-G 上游集成

当前固定上游：MAVIS 1.7.2，标签提交 `383ba576ad7bea804f1f50c6e9be0307549add45`。本目录构建 NVIDIA DLSS-G 后端，保留本项目独立倍率控制器，不启用已弃用的 XeFG 路线。

`upstream172.json` 固定原始源码；`recover_tables.py` 静态解析官方资产的 PE 表和重定位，恢复 DLSS-G 的 6+13 个 CUDA 内核及其元数据，不执行官方 addon。编译后逐项比对实际内核字节。不能将旧版本偏移套到未知新资产。

`prepare172.py` 只改新建输出：四态菜单、独立 UI 桥接、配置迁移、中文显示、Dynamic 160 默认目标、最低公开 API 版本以及能力与模块生命周期绑定。原始上游源码和 CUDA 程序不改动。

公开 Dynamic 最低版本为 Streamline 2.14.1、DLSS-G 310.9.1；更高版本不因版本号被拒绝，仍需 GetState 的肯定能力结果。公开能力按单一原子样本绑定模块 epoch，过期返回不能授权新实例。

独立控制器保留帧生成模式、最大倍率、持久化、关闭、倍率观测、光标接管和菜单停靠。私有接口按各自完整 SHA、28 组指令、三组目标指针、返回地址和线程栈范围验证，当前提供 2.14.1 与特定 2.14.16 画像。仅通过最低版本检查不授予未知机器码写权限。2.14.16 画像已经静态与合成验证，实际游戏效果仍待人工测试。

构建参数：

```powershell
./tools/Mfg172Integration/Build.ps1 -UpstreamDirectory <1.7.2源码根> -GeneratedDirectory <recover_tables输出> -DependencyDirectory <固定renodx-build> -OutputDirectory <全新输出目录>
```

启动器发布构建须同时更新 `release-assets/mfg`、`release-assets/dynamicmax`、`payload` 的来源和材料清单，重编 EXE 内嵌控制器，运行模式/翻译/版本策略/原生快照/光标/别名发现/内核完整性测试，再按原协议生成两种包。产品版本由仓库版本元数据确定；上游版本和插件界面修订独立于启动器产品版本。Windows x64 包构建通过不等于实机帧生成通过。
