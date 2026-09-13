# Materials agent durable context

## Scope / workspace

Current branch agent/materials; isolated worktree .agent-worktrees/materials. Main workspace input and desktop materials are read-only here. The parent owns integration, build, ReShade runtime testing, UI and packaging. No game directory was scanned/written and no installer/unlocker/game was run by this agent.

## Completed before shared checkpoint

Read AGENTS.md, CODEX_TASK.md, docs/HANDOFF_V2_AUDIT.md. Imported the unique desktop source C:\Users\StrixZ16\Desktop\input\多帧生成 into project input/desktop-materials using scripts/Import-DesktopMaterials.ps1. All 20 SHA-256 copy comparisons passed (18 DLL + addon + Setup). Actual PE/version/signature records: input/desktop-materials/source-manifest.json and artifacts/material-source-manifest.json. CSV: artifacts/material-audit.csv. Runtime target rules: artifacts/payload-map.json and payload/payload-map.json. Logs: artifacts/logs/material-import.log, payload-prepare.log, payload-verification.log.

18 DLLs are x64 with local Authenticode Valid. Most NV DLSS DLLs 310.9.1.0, nvngx_dlssnr 310.8.0.0; most Streamline 2.14.1.0, sl.dlss_nr 2.13.0.0. NvLowLatencyVk has no file version. This is the user's exact set, not version normalization.

Addon x64, file version 0.2026.0909.1640, NotSigned. Setup x86 bootstrapper version 6.8.0.0, hash afe4c8f13048306307983b8b3d41d5bf00a86820440b0e57dea10950e1176445. Its signer is CN=ReShade, E=info@reshade.me; local status UnknownError because certificate chain terminates at untrusted root. No trust settings were changed. Local hashes prove integrity only, not official provenance.

Prepare-Payload.ps1 accepts ReplacementFiles (GameRootFiles alias retained), exactly 18 approved flat DLLs, addon and mandatory LocalReShadeSetup. All copies rehash-verified. Manifest LocalSetupPath=setup/ReShade_Setup_6.8.0_Addon.exe. Deployment agent agreed no schema extension: Vendor target is name-only existing-file search key; deployment runtime resolves actual paths under selected game root and skips absent names. No guessed real game target paths are claimed. dxgi remains candidate until parent validates installer/runtime.

## Additional isolated verification

scripts/Test-MaterialScripts.ps1 rejects four cases before generating payload: missing18, unknown DLL, nested input, output inside source. Actual execution log is script-rejection-tests.log. These are script safety tests with rejection-only fixture files, not game deployment/MFG/native UI tests.

asset-comparison.json compares the three desktop input images. Desktop 图标.jpg is byte-identical to Assets/Icon.original.jpg. Desktop 参考图.png differs from reference/APPROVED_UI.png. Desktop 封面.jpg differs from Assets/Cover.original.png. No image changed. Parent informed to visually inspect differences and retain user's intended authority.

## Remaining / parent integration

Keep all binary payload/setup and payload/files content when packaging. Preserve artifacts logs in final distribution evidence. Actual game path mapping, runtime installation, deployment/cleanup and game-internal behavior remain parent-owned verification. No new game results claimed here.
