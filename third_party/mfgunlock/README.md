# MFG Unlock：Dynamic 最大倍率派生构建

基线为上游 mavismmg/MFGAdaUnlock-RenoDx tag 1.1，提交 c3733d8afd51214c46a71d18feec520b0bf54864。dynamic-max.patch 提供仅运行时的 Dynamic 最大倍率控制，默认4x，移除旧启动DRS覆盖；仍由 NVIDIA 原生调度决定实际倍率。版本门禁、线程校验和原生能力上限保留。使用方法见 [Dynamic 最大倍率](../../docs/DYNAMIC_MAX_MULTIPLIER.md)。

## Windows x64 构建

需要 Visual Studio C++ x64 Build Tools、Windows SDK，以及 PowerShell。`Build-DynamicMaxAddon.ps1` 使用 MSVC 直接编译这个独立 addon，不构建 RenoDX 的其他游戏 addon，也不需要 shader 编译器。依赖仓库仅提供头文件和 Detours 静态库；不会把其中的 NVIDIA 二进制部署到游戏或运行库材料包。

先准备两个独立源码目录：

```powershell
git clone https://github.com/mavismmg/MFGAdaUnlock-RenoDx.git mfg-source
git -C mfg-source checkout c3733d8afd51214c46a71d18feec520b0bf54864
git -C mfg-source apply <本项目绝对路径>/third_party/mfgunlock/dynamic-max.patch

git clone https://github.com/clshortfuse/renodx.git renodx-deps
git -C renodx-deps checkout 9b212edad4dde9bca2b823b1e045b712b1a8d854
git -C renodx-deps submodule update --init external/reshade external/Streamline external/DLSS external/Detours external/json
git -C renodx-deps/external/reshade submodule update --init deps/imgui
```

在本项目目录运行，参数使用绝对路径：

```powershell
./scripts/Build-DynamicMaxAddon.ps1 -SourceDirectory <mfg-source> -DependencyDirectory <renodx-deps> -OutputDirectory <输出目录>
```

输出包括 `renodx-mfgunlock.addon64`、原生配置/ABI 测试和 `build.log`。该脚本不安装插件、不启动游戏。`/MT` 静态链接 C++ 运行时，`/Brepro` 消除链接时间戳；不同编译器或 SDK 的输出不保证逐字节相同。构建环境、补丁哈希及最终文件哈希见 `release-assets/mfg/source.json`。

运行时控制测试使用惰性数据验证默认4x、4→5→6切换、原生上限约束与失效透传；加载器回归验证正常安装加载hook后仍不覆盖NVAPI DRS返回。测试不执行真实NVIDIA DLL，不启动游戏，不能替代游戏内验收。

## 固定依赖

| 依赖 | 提交 |
| --- | --- |
| RenoDX 构建依赖集合 | `9b212edad4dde9bca2b823b1e045b712b1a8d854` |
| ReShade addon API | `4a50d1eddace85734871d91792ff214f13f66c01` |
| Dear ImGui | `3912b3d9a9c1b3f17431aebafd86d2f40ee6e59c` |
| Streamline headers | `e8aaa6eaac968711fb62473d4ae8256dde20919b` |
| DLSS/NGX headers | `a291cc7d2cc642a51566f3dfd5376f635cd1b284` |
| Microsoft Detours | `9764cebcb1a75940e68fa83d6730ffaf0f669401` |
| nlohmann/json（诊断测试） | `55f93686c01528224f448c19128836e7df245f72` |

NVAPI DRS V1 最小 ABI 的核对来源为 [NVIDIA/nvapi `70d337d`](https://github.com/NVIDIA/nvapi/tree/70d337db9186e968eab622f7e786de7e437faf3d)，以静态断言核对大小及字段偏移。没有链接 NVAPI SDK 库。驱动返回错误、未知结构或不能识别的调用者均保留原结果。

## 许可

保留上游 [MIT 许可](../../licenses/MFGAdaUnlock-MIT.txt) 和[完整 credits](../../licenses/MFGUnlock-UPSTREAM-CREDITS.md)，包括 Dreamt、dashdogy、mavismmg、RenoDX、ReShade 与其他贡献者。依赖各自的许可随 `licenses/` 分发。
