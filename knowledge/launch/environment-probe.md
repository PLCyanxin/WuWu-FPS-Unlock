# Windows product name and HAGS probe (2026-09-27)

Independent codex/environment from main100c15b; version1.2.2RC unchanged, no addon/menu edits. Read WORKSPACE.md and current probe/model/deployment usages. Windows names now combine Environment.OSVersion build/revision with readonly CurrentVersion ProductName/InstallationType/DisplayVersion/UBR. Client build>=22000 maps stale Windows10 registry naming to Windows11; Server stays Server. Unknown role not guessed.

Do NOT use D3DKMT_WDDM_2_7_CAPS as a supported application API: Microsoft explicitly labels it reserved/system-only despite documenting HwSchEnabled. DXCore public adapter properties lack a HAGS enabled field. Instead collect Windows' DxDiag XML using system32/dxdiag.exe /whql:off /x, bounded30s, one process at a time,120s successful/30s failure in-memory cache. Existing VM calls EnvironmentProbe.Read via Task.Run. No UAC, graphics settings or driver change. Temporary report is removed; only matched status is logged. Any failure/absent/contradictory field remains unknown. Only our own timeout diagnostic process may be terminated, never a tree or user/game process.

Actual XML field: DisplayDevices/DisplayDevice/HardwareSchedulingAttributes = DriverSupportState:Stable Enabled:True. Match normalized complete NVAPI GPU name, not first adapter or VendorID: this machine has2 NVIDIA4080Laptop entries and several virtual adapters sharing10DE plus Intel integrated AlwaysOff/False. Both exact NVIDIA entries agreeTrue. HagsConfigured retains original registry-only semantics and deployment guard is unchanged. Runtime/config mismatch is explicit in display.

Actual test evidence (main artifacts/tests/environment): probe-tests.log first diagnostic16.1seconds, cached0.0seconds; OS Windows11Pro23H2 build22631.4602, GPU NVIDIA GeForce RTX4080Laptop GPU, HAGS enabled fromDxDiag, registryConfigured=null. parser-tests.log19 assertions (including Win10/server/unknownrole, duplicateGPU conflicts, integrated/virtual exclusions, malformedfields, config-runtime distinction, DTD rejection). build.log native WPF0errors/0warnings. No real game/install or UI changes. DxDiag full raw XML stays in internal artifacts only; not a public deliverable.

Sources:
- https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/d3dkmdt/ns-d3dkmdt-d3dkmt_wddm_2_7_caps (reserved; do not use)
- https://learn.microsoft.com/en-us/windows/win32/api/dxcore_interface/ne-dxcore_interface-dxcoreadapterproperty
- https://support.microsoft.com/en-us/windows/hardware/display-graphics/which-version-of-directx-is-on-your-pc (system diagnostic report)
- https://learn.microsoft.com/en-us/windows/release-health/windows11-release-information (Win11 build families)

DxDiag report text/XML is a diagnostic format, not a promised stable HAGS application API. Field changes returnUnknown instead of treating registry configuration as runtime state.
