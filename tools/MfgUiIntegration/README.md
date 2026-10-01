# MFG Unlock 中文界面集成

当前源码基线为上游 `MFGAdaUnlock-RenoDx` **1.3.0**，提交 `c96c7c471bb07b04fc8c4c059175a8b6398212cf`。`ui-scope.json` 固定原始 `addon.cpp` 的 SHA-256；未知源码不能直接套用补丁。

`zh-CN.json` 在显示层映射中文，保留原有配置键、诊断导出、日志和运行库实现。统一模式通过原有配置与通知接口更新，并使用独立的新配置键保存模式和隐藏的固定倍率。控件采用原始标题作为稳定 ID，翻译不会改变旧配置。新增的画质配置、V3.2 稳定性、Reflex 和帧节奏选项同样经过汉化。技术名称和 API 名称保留原文。

## 独立倍率控制

`dynamic_ui_bridge.hpp` 在 NVIDIA Dynamic MFG 选项旁调用独立控制器的版本化接口。固定倍率选择、Dynamic 最大倍率和两处实际倍率状态分别桥接至控制器。固定模式和 Dynamic 各自拥有关闭设置；控制器负责热切换、保存、状态捕获和光标管理，不重新混入主插件。

主插件未找到独立组件时保留原有固定倍率菜单；独立组件没有检测到主界面桥接时保留独立显示回退。配置与功能身份独立，不能将两个文件互相替代。

## 构建

先从对应上游标签取得完整源码，并保留维护者提供的原始发布资产。1.3.0 原资产的 SHA-256 为 `7d742480613c58dc0eb698bf5dc76f2b32a6b4c607b2face5b87940494b9b229`。

```powershell
python tools/MfgUiIntegration/recover_130_tables.py --original <原始-addon> --output <全新内核目录>
./tools/MfgUiIntegration/Build.ps1 -UpstreamDirectory <上游1.3.0源码> -GeneratedDirectory <内核目录> -DependencyDirectory dependencies/renodx-build -OutputDirectory <全新输出目录>
python tools/MfgUiIntegration/recover_130_tables.py --original <原始-addon> --rebuilt <输出目录>/renodx-mfgunlock.addon64 --report <校验报告>
```

内核恢复工具只进行静态解析，不加载或执行 DLL。它固定原资产哈希，完整保留全部 17 份 CUDA payload、两组表项、标签、顺序、指针重定位及 ELF 程序头。验证失败时停止，不使用缺少内核表的构建替代完整插件。旧版 `recover_kernel_tables.py` 仅用于历史 1.1.5 材料。

## 验证边界

`Test-Translations.ps1` 检查中文映射、控件 ID、状态缓存、文本边界及映射结果。源码集成核对现有配置键及通知路径有效，统一模式和派生倍率桥接各插入一次，两处状态显示均连接到实际捕获接口。静态检查与构建通过不代表真实游戏、GPU 或界面已通过验收。

## 模式接口

`frame_mode_load.inc` 迁移旧配置；`frame_mode_apply.inc` 仅处理明确的选择；`frame_mode_ui.inc` 绘制模式与对应派生设置。`tests/frame_mode_tests.cpp` 直接执行这些生产配置片段，验证模式切换、保存、旧数据迁移与无效值拒绝。模式选择调用 `ConfigureWuWaFrameModeV1`；派生菜单使用 Dynamic V3 与 Fixed V2 接口，旧接口保留供兼容回退。
