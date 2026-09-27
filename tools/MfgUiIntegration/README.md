# MFG Unlock 简体中文界面映射

`zh-CN.json` 是区分大小写的“原始 C++ 字符串字面量内容 → 中文内容”映射。基线为上游 `MFGAdaUnlock-RenoDx` 的 `1.1.5`（提交 `066c734259f3672559078e68d9c7f0616bb6c1b0`），目标文件为 `src/addons/mfgunlock/addon.cpp`。此映射仅描述界面文字，不更改插件功能。

应用时使用 C++ 字符串词法解析和函数／调用边界，不能全文件替换。JSON 中的 `\\n` 表示源码中的转义字符 `\n`；相邻 C++ 字符串分别映射，再由编译器连接。请按 UTF-8 写入，并使用 `/utf-8` 编译。保留映射的大小写区别，例如 `FRAME GENERATION` 与 `Frame Generation`。

## 应用范围

`ui-scope.json` 提供稳定函数名和锚点，行号仅用于基线导航。主界面是 `OnRegisterOverlay`，包括常规、DLSS 信息、延迟、支持与诊断四页；高级画质和延迟控件分别由 `DrawAdvancedQualityControls`、`DrawLatencyGuardControl` 绘制。`DrawLegacyOverlay` 与 `DrawInputDiagnosticsPanel` 在基线中没有调用，不属于当前活动界面。

状态辅助函数返回的字符串可供界面显示层查表翻译，但不要直接改变共享辅助函数的原始返回值。`super_resolution_preset`、`frame_generation_preset`、`ray_reconstruction_preset`、`streamline_text`、`dlssg_text` 以及时序和标记状态也用于诊断导出。应在传给 ImGui、StatusRow、SettingLabel 等显示调用时翻译，或使用仅供显示的副本；保持诊断报告中的原始英文值。

在 `OnRegisterOverlay` 内，从 `std::string intermediate_report = "Disabled";` 到 `if (ImGui::Button("Copy diagnostics"))` 之前的区域是诊断导出构造，不应用映射。翻译“复制诊断信息”按钮及其后可见的状态说明，但保持 `report.str()` 和剪贴板内容不变。

## 必须保持不变的内容

- `reshade::get_config_value` / `set_config_value` 的节名与键名，包括 `Enabled`、`DynamicMFG`、`ForceMultiplier`、`MaxCount`、`RuntimeSelectionMode` 等。
- 以 `##` 开头的控件、表格、标签栏标识，以及已有 `###` 后的稳定标识。四个页签和首次启动弹窗的译文显式保留原始名称为 `###` 后缀，相关打开／查询／绘制调用必须使用同一个值。
- DLL、API、技术名称、数值、格式占位符、日志内容、诊断导出及所有功能逻辑。
- 其他字符串若同时承担配置、比较或标识用途，只翻译显示调用的副本，不更改原始值。

不同来源的动态诊断详情，例如运行库补丁返回的任意错误说明，不适合通过全局字面量替换。需要完整中文显示时，应在显示层按已知原文映射；未知详情保留原文，不能隐藏诊断信息或编造解释。

## Dynamic 控件位置

在 `OnRegisterOverlay` 的 `##frame_generation_settings` 表格内，`##dynamic_mfg` 复选框对应的 `if (block_dynamic_enable) ImGui::EndDisabled();` 之后、`int dynamic_target = static_cast<int>(` 之前插入独立控制器界面。让控制器另起表格行，并为其控件使用 `WuWa.DynamicMax` ID 范围；不要依赖同名窗口追加来保证位置。此处仅为 UI 接口位置，独立控制器继续负责其配置与运行时控制。

## 验证

映射共有 366 项。制作时检查了 JSON 可解析性、printf 格式项顺序一致、没有 `##` 内部 ID 或配置键条目。应用补丁后仍须检查配置键／API 调用不变、诊断导出不变、活动 UI 的英文遗漏、中文字体字形及实际换行。编译或字符串覆盖率不代表游戏内界面已经验收。
