# User addon configuration audit — static only

Local file read: `input/desktop-materials/renodx-mfgunlock.addon64`, 601088 bytes.
SHA-256: `64184BB370F223C3CABB359010A9A64E114CDAE6B62D8B014A731A602AF0A0DA`.
This exactly matches the hash published in the author's [0.9 release](https://github.com/mavismmg/MFGAdaUnlock-RenoDx/releases/tag/0.9). No replacement binary was downloaded and the addon was not loaded or executed.

Read-only ASCII extraction found `RenoDX.MFGUnlock`, `Enabled`, `DynamicMFG`, `ForceMultiplier`, `RuntimeSelectionMode`, and a local runtime/OTA restart explanation.

The tagged [addon.cpp](https://github.com/mavismmg/MFGAdaUnlock-RenoDx/blob/0.9/src/addons/mfgunlock/addon.cpp#L2549) reads the same keys. `RuntimeSelectionMode` accepts 0–2; `ForceMultiplier` accepts zero or 2–6; DynamicMFG accepts a nonzero boolean. Early loading is read from ReShade's ADDON LoadFromDllMain array.

The tagged [framecount.hpp](https://github.com/mavismmg/MFGAdaUnlock-RenoDx/blob/0.9/src/addons/mfgunlock/framecount.hpp#L1044) implements the runtime preference in HookedInit. Mode 1 copies the game's sl::Preferences, clears the two OTA flags in that copy, then forwards the copy to the original slInit within the running process. This configuration path does not modify global registry, driver profiles, NVIDIA App settings or global OTA files. Mode 0 preserves the request; mode 2 enables the two flags. An unknown Preferences ABI leaves the request unchanged. Thus mode 1 is a per-game/process runtime-selection request; actual early-load timing and selected runtime remain unverified until game logs are inspected.

The same version's Dynamic path also checks actual loaded Streamline 2.14.1 and DLSS-G 310.9.1, D3D12 and runtime-reported support. A successful driver precheck or file copy cannot establish Dynamic activation. No source build reproducibility claim is made solely from the published release hash.

Checked 2026-09-14 using primary upstream source and the local input file. Scope was the configuration path, not a complete binary security audit.
