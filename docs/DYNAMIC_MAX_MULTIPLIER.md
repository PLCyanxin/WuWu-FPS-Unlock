# Dynamic 最大倍率

Dynamic 最大倍率为 NVIDIA 原生 Dynamic MFG 调度器提供一个倍率上限。默认值为 **4x**；目标是在原生调度器继续选择 2x、3x、4x 的同时，限制更高倍率。是否成功传递并被运行时使用，需要分别验证，不能仅凭部署完成判断。

此功能需要带 `DynamicMaxMultiplier` 支持的派生 addon。未修改的上游 addon 或其他本地替换版本可能不识别该设置。

## 设置与部署

在启动器设置的“多帧生成部署”中选择“Dynamic 最大倍率”。合法值只有 `0、2、3、4、5、6`；`1` 不是有效设置。`0` 表示不覆盖 NVIDIA 默认行为，并不代表驱动没有自己的上限。选择 5x 或 6x 也不保证设备和运行时支持该倍率。

| 启动器选项 | 配置值 | DRS 生成帧数量 |
| --- | ---: | ---: |
| NVIDIA 默认 / 不限制 | 0 | 不覆盖 |
| 最高 2x | 2 | 1 |
| 最高 3x | 3 | 2 |
| 最高 4x（默认） | 4 | 3 |
| 最高 5x | 5 | 4 |
| 最高 6x | 6 | 5 |

关闭游戏后重新部署，确认预览中的 Dynamic 状态和倍率上限，再完全重启游戏。预览后修改倍率必须重新预览确认。旧设置文件缺少此字段时，启动器使用默认值 4，但不会因此直接改写已经部署的游戏配置；只有重新部署才写入。非法值及损坏的字段值按 0 处理，不通过截断数值来启用上限。

Dynamic 配置示例：

```ini
[RenoDX.MFGUnlock]
Enabled=1
DynamicMFG=1
DynamicMaxMultiplier=4
ForceMultiplier=0
RuntimeSelectionMode=1
```

`DynamicTargetFPS` 保持原有设置；倍率上限不改变目标 FPS、`MaxCount` 或其他调优参数。环境条件使部署选择 Fixed 时，启动器写入 `DynamicMFG=0`、原有 Fixed 倍率对应的 `ForceMultiplier`，并将 `DynamicMaxMultiplier` 写为 0。

配置使用现有 INI 所有权记录写入，不重建整个 ReShade 配置。清除时可恢复部署前的该键值；若此后已手动改过该键，则保留后来的修改。无关 ReShade 配置保持不动。

游戏内 MFG Unlock 的 **Dynamic maximum multiplier** 也可预先配置，但同样需要完全重启游戏。DRS 读取可能发生在初始化阶段，修改界面选项不会替换本次启动已缓存的上限。再次从启动器部署时，以启动器中保存的选值为准。

## 原生 Dynamic 与 DRS 读取覆盖

Dynamic 仍使用 Streamline 的 `DLSSGMode::eDynamic`，实际倍率由 NVIDIA runtime 根据帧率、目标及帧间隔控制决定。上限不是 `ForceMultiplier=4`，不改变 `MaxCount`，也不在 addon 中按每帧 FPS 手动切换倍率。

派生 addon 在当前游戏进程内拦截合格的 Streamline / DLSS-G consumer 对 `nvapi_QueryInterface` 的查询，为函数 ID `0x73BF8338`（`NvAPI_DRS_GetSetting`）提供包装函数。包装函数先调用真实 NVAPI，再仅对 DRS 设置 `0x10562D0F`（`NGX_DLSSG_DYNAMIC_MULTI_FRAME_COUNT_MAX_ID`）覆盖读取结果。

这个设置编码的是生成帧数量，因此 4x 对应 `4 - 1 = 3`。覆盖作用于已知 DWORD 设置的当前值，并保持 current-profile 语义。调用来源、配置、NVAPI 返回状态、结构版本或类型无法确认时，保留真实 NVAPI 结果；其他 DRS key 原样转发。

consumer 识别包含已知模块特征以及 Streamline 插件导出特征，以兼容部分使用内容寻址文件名的 OTA 插件；不能识别的 consumer 不会获得覆盖。是否实际经过该路径，以运行时诊断为准。

此方式不写 NVIDIA 全局或游戏 Profile，不改注册表，不修改 `nvapi64.dll` 文件或其函数入口代码。覆盖仅存在于加载 addon 的进程中，进程退出后消失。设为 0 或不使用 Dynamic 时不启用该上限覆盖；拦截失败则保留 NVIDIA 原始行为。

部署继续复用 ReShade 的 early-load 配置：

```ini
[ADDON]
LoadFromDllMain=renodx-mfgunlock.addon64
```

已有列表项会保留。即使配置了 early load，也不能据此假定初始化时序一定满足：Streamline 可能已缓存 DRS getter 或设置值。没有观察到匹配读取，就不能声称上限已应用。

## 如何读取诊断

在 ReShade 的 MFG Unlock Support / diagnostics 中检查以下三个层次：

| 层次 | 可观察信息 | 能说明什么 |
| --- | --- | --- |
| 配置 | `Configured Dynamic maximum`、`Startup Dynamic maximum` | 保存的值及本次启动使用的值；两者不同需完全重启 |
| 拦截与读取 | `DRS override`、`NvAPI DRS getter wrapped`、`Dynamic max DRS read observed`、`Returned value` | 是否准备好拦截、是否提供 getter、是否观察到读取，以及是否实际返回覆盖值 |
| 运行时 | 原生 Dynamic 支持/接受状态、Dynamic 当前实际倍率 | NVIDIA 是否进入 Dynamic，以及当前场景下的倍率表现 |

`armed` 只表示覆盖准备就绪，`wrapped: yes` 只表示已提供包装 getter；二者都不等于发生了目标 key 的读取。`read observed: yes` 也需结合 `Returned value` 检查：ABI、类型或真实 NVAPI 结果不符合条件时，可能观察到了读取但没有覆盖。

4x 上限对应的诊断日志应包含以下信息，可在当前游戏的 ReShade 日志中搜索 `mfgunlock` 和 `0x10562D0F`：

```text
Dynamic maximum configured: 4x (DRS value 3)
wrapped NvAPI_DRS_GetSetting for verified Streamline/DLSS-G DRS consumer
answered Dynamic MFG maximum DRS key 0x10562D0F with 3 (4x)
```

这些是日志中的关键片段。最后一项证明向匹配的 DRS 调用返回了 3，不证明 NVIDIA 后续一定采用了该值。若出现 `Dynamic maximum override unavailable`、提前缓存提示或没有观察到读取，应记录为“未确认上限”，不能显示为“已生效”。

## 本地游戏验证步骤

1. 确认使用支持此功能的派生 addon，记录其版本，以及显卡、驱动、游戏版本。保留本次启动的 ReShade 日志，避免把前一次运行的记录当作本次证据。
2. 完全退出游戏，在启动器中选择“最高 4x”并重新部署。检查预览请求 Dynamic，部署配置为 `DynamicMFG=1`、`ForceMultiplier=0`、`DynamicMaxMultiplier=4`，early-load 列表包含该 addon。
3. 重新启动游戏，在 MFG Unlock 中确认 addon 已加载、原生 Dynamic 支持与接受状态，并核对本次启动上限为 4。仅驱动预检查通过不足以完成这一步。
4. 检查包装 getter、目标 DRS key 读取和返回值诊断。应观察到读取 `0x10562D0F` 并返回 `3 (4x)`；没有这项证据时，先记录时序或兼容问题，不继续宣称上限生效。
5. 选择能明显改变基础帧率的场景或画质负载，在同一套目标 FPS 设置下持续观察插件的 **Dynamic 当前实际倍率**。验证原生调度器能在 2x、3x、4x 间选择，且压力场景中不出现 5x 或 6x。不要仅用“输出 FPS ÷ 目标 FPS”推算倍率。
6. 如需对照，可在可接受的设备负载下，将上限设为 0，重新部署并完全重启，以同样条件观察。只有对照确实能触发更高倍率，才有充分条件检验 4x ceiling；设置为 0 不保证一定出现 5x/6x。
7. 记录场景、负载条件、目标 FPS、实际倍率变化及对应日志。若启用 4x 上限后仍出现 5x/6x，记录为验证失败；若始终只有单一倍率或场景无法触发更高倍率，记录为覆盖返回已确认、压力场景下的硬上限仍未验证。

**当前游戏内效果待用户实机验证。** 自动化测试、构建成功和 DRS 日志分别验证不同层次，均不能替代 RTX 40 与《鸣潮》运行时的压力场景验证。没有足够倍率变化时，不能声称已证明硬上限。

## Addon 来源

- 上游：[mavismmg/MFGAdaUnlock-RenoDx](https://github.com/mavismmg/MFGAdaUnlock-RenoDx)，tag [`1.1`](https://github.com/mavismmg/MFGAdaUnlock-RenoDx/tree/1.1)。
- tag 对应提交：`c3733d8afd51214c46a71d18feec520b0bf54864`。
- 派生版本标识：`1.1+WuWu.DynamicMax.1`，增加进程内 `DynamicMaxMultiplier` 支持，并非未经修改的上游 1.1。
- 上游 MIT 许可与原有作者、RenoDX / ReShade 等 attribution 继续保留。具体构建来源和文件哈希以随包 addon 来源记录及[第三方说明](../licenses/THIRD_PARTY_NOTICES.md)为准。
