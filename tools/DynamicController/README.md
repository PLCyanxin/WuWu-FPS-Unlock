# WuWa Dynamic Maximum

Independent ReShade companion: `wuwa-dynamicmax.addon64`. Overlay: **MFG Unlock** (shared with the upstream controls); exported add-on NAME: **WuWa Dynamic Maximum**. Widget IDs use a private scope; configuration and plugin identity stay independent. This directory can be copied and built independently; it imports no MFG Unlock implementation or private state. MFG Unlock 1.1.5 remains a separate addon; the optional Chinese UI integration calls the versioned DrawWuWaDynamicMaximumInTableV1 export next to native Dynamic settings. Without that bridge, the companion keeps its shared-overlay fallback.

The Dynamic control offers **关闭**, native bound, or a 2–6x maximum (default 4). Its separate enable switch applies only to Dynamic. Configuration uses `[WuWa.DynamicMax] Enabled` and `DynamicFrameGenerationChoiceV2`: `0` disables generated output, `-1` follows the native bound, and `2..6` caps it. When the new key is absent, the previous companion/main-addon `DynamicLiveMaxMultiplier` is migrated once; its old `0` becomes `-1`, never an unexpected Off. Old keys are retained. The retired static `DynamicMaxMultiplier` is never read.

The integrated ordinary multiplier control also offers **关闭**. Its separate `FixedFrameGenerationEnabled` flag defaults to enabled and is used only for native modes 1/2. Turning it off preserves the upstream `ForceMultiplier` and all Dynamic choices. Selecting game-decides or a multiplier resumes ordinary generation subject to the game's actual mode. The upstream multiplier itself still follows upstream SetOptions timing; this does not claim immediate reconfiguration of every ordinary multiplier. Without the companion export, the integration retains the original combo. ReShade owns persistence; its void setter cannot report disk-write failure.

## Compatibility and lifecycle

The companion never forces an already-off game mode on. Dynamic selection is sampled at its mode-3 native selector; Off changes that frame's mode to 0, generated/duplicate counts to 0 and total count to 1 after the original selector. Ordinary Off uses the common native validation point, affecting only mode 1/2 before validation. The original validator's false result is the native inactive-generation route, not a fabricated error. Saved game options are untouched and reconstruct the following frame, allowing resume. Both hooks install atomically and share strict compatibility failure guards. Ordinary Off remains independent when the Dynamic checkbox is disabled.

These are per-frame native-Off submissions, not public `slDLSSGSetOptions(eOff)` calls, GPU resource teardown, or a change to DLSS upscaling. “Native frames” here means no generated frames. Already queued work can retain the preceding choice. Game/ReShade acceptance remains separate from the isolated fixtures below.

The supported `sl.dlss_g.dll` SHA-256 is `F4A6B2B14DCC0B1485989E430D3B4E3A44AC1800B92BA1AD74F476E64FB2B09C` (audited Streamline 2.14.1 image). File hash, loaded instruction anchors, callback table targets, return address, current-thread stack bounds and frame layout must match. A different or renamed runtime, existing conflicting hook or failed Detours transaction disables operation. This is not a universal compatibility claim for future Streamline or upstream versions.

Activation occurs from normal Present, outside loader callbacks: at most one module check per second, at most 60 checks, and one automatic installation attempt after discovery. Retry is explicit. The companion does not need an early-load entry and does not install load-library, NVAPI or DRS hooks. It can join the supported native frame path after initialization because each frame reconstructs the audited snapshot. Installation pins this companion and the target until process exit; hot DLL unload/reload is unsupported. Disabling leaves the safe original trampoline in place. Failure information is logged through ReShade; the panel only contains controls.

The same per-frame bound flows through native selection, RSYNC and the copied NGX submission snapshot. A request may reduce or restore the current native bound, never increase it: a native 4x bound cannot become 6x. A maximum is not a guarantee of actual generated/displayed multiplier. Queued frames may retain the preceding request. Thread enumeration is bounded and checked, but cannot absolutely prevent an external new thread after the final snapshot or a third-party patch after activation.

## Build and test

The companion also supplies a guarded Windows native cursor while the ReShade menu is open. This separates pointer motion from the game's base render rate; it does not increase the menu's render rate. Menu sessions coordinate the owner thread's cursor requests so the game and menu do not repeatedly replace or hide the hardware cursor. Bounded restoration and exceptional failure checks remain in place. See [native cursor lifecycle and limits](NATIVE_CURSOR.md).

```powershell
./Build.ps1 -DependencyDirectory E:/path/to/renodx-mfg-build -OutputDirectory E:/path/to/build
```

Prepared dependencies: Windows x64 MSVC/C++20, Detours headers and static x64 library, ReShade API headers and matching ImGui headers. No Streamline/DLSS SDK, downloads, installed game or driver DLLs are required. Optional `-ReferenceDll` supplies the supported runtime **as data only** to verify anchors; its code is not loaded or executed by tests.

`tests/dynamiclive_tests.cpp` exercises the production policy, stack/identity guards, real Detours on a self-built fixture and injected failures. `tests/config_wiring_tests.cpp` calls actual companion LoadConfig/SaveRequest through a fake exported ReShade configuration API, covering independent namespace and one-time migration. These are not real game/ReShade acceptance tests.

## Source and licenses

This companion was separated from the WuWa custom live-cap implementation (`69595e4e75a5bf66cedd60dfc1181b9bd0d9c04c` in the development addon repository), originally maintained alongside MFGAdaUnlock-RenoDx. The surrounding checkout is upstream tag 1.1.5, commit `4406e4fadf4423afb500d8d9a08d5ace9a148d19`; no tracked upstream file is modified. The companion is MIT licensed. ReShade/ImGui and Detours are external build dependencies with their own licenses, which must accompany redistribution. No NVIDIA binaries or reverse-engineered DLL bytes beyond instruction validation anchors are distributed in this source directory.

The prepared ReShade source also confirms cross-section migration uses its global configuration: `source/addon.cpp` `ReShadeGetConfigValue`/`ReShadeSetConfigArray` ignore the module argument and select `global_config()` when runtime is null. No companion filename-specific config lookup or direct INI editing is needed.
