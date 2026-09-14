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

## .NET 8 portable runtime follow-up

Official release-metadata retrieved 2026-09-14 reports latest-runtime 8.0.31, release date 2026-09-08, security=true. Downloaded Microsoft NETCore win-x64 ZIP from builds.dotnet.microsoft.com and WindowsDesktop ZIP from the Microsoft dotnetcli.blob.core.windows.net mirror. Both exact SHA-512 hashes match release metadata. See dotnet8-provenance.json for source URLs, actual download URLs, hashes and sizes; dotnet8-file-hashes.json for extracted files. Original build-host Desktop download was stopped only after equivalent mirror bytes were verified; a redundant NETCore mirror attempt timed out and was not used.

Portable directory: .agent-worktrees/materials/components/dotnet8 (host + Microsoft.NETCore.App 8.0.31 + Microsoft.WindowsDesktop.App 8.0.31, licenses/notices retained). It is intentionally not committed as binary content; parent copies this directory into delivery. No machine runtime install or global PATH/registry changes.

Reproducer: scripts/Prepare-UnlockerRuntime.ps1 -DesktopOnlyMirror. Version is pinned to audited current patch 8.0.31. Cached metadata can be reused with -ReuseMetadata, otherwise fetched fresh and the pinned release is selected. Corrupt archive cache stops at SHA-512 check before extraction. Actual verification log dotnet8-prepare-final.log contains both verified package hashes and successful dotnet --list-runtimes output. No unlock.exe execution occurred; runtime presence alone does not verify external unlocker's launch behavior.

## Real game candidate read-only audit

Parent authorized scoped read-only inspection of D:\Wuthering Waves\Wuthering Waves Game, derived from original input/unlocker/ww_fps_config.ini PathValue. This remains a candidate pending user's explicit selection/confirmation. scripts/Inspect-Game.ps1 uses a queue, skips reparse points, stays within this root and writes reports only outside it. No EXE execution or game writes. Inspected 730 directories/2551 files, no errors or skipped links.

18 Vendor names each have exactly one existing match; all 18 target hashes already equal the provided payload. nvngx_dlss.dll and nvngx_dlssd.dll are under Engine\Plugins\Runtime\Nvidia\DLSS\Binaries\ThirdParty\Win64; the other 16 are under Engine\Plugins\Runtime\Nvidia\StreamlineCore\Binaries\ThirdParty\Win64. Same hashes do NOT establish tool ownership. Existing user-installed files must not be claimed or removed without ownership evidence.

Shipping is Client\Binaries\Win64\Client-Win64-Shipping.exe, x64, 976121112 bytes, no file version resource, SHA256 0e6865a9bdb0196293d8c41d39eebbb0df782e31c5d2974949e7954f726d1985. Its presence/PE/hash is not proof of originality.

Shipping directory already contains dxgi.dll, x64 ReShade 6.8.0.2155, SHA256 0cee63f9c9f13f3ac909c5b4903f4dbb4b719a7ab3b4f13b0deaf83c814b94f7, plus renodx-mfgunlock.addon64 matching local user material. ReShade.ini (2931 bytes) and empty ReShadePreset.ini exist. No AddonPath key was present in inspected path keys. No redirected path was traversed, and compatibility/loading was not tested.

Delivery reports in isolated artifacts/real-game-candidate-map.json and .md; a durable JSON copy is in this knowledge directory. Runtime reuse/ownership and real game writes remain parent/user confirmation decisions. These reports are expressly titled candidate pending user confirmation.

## Historical ReShade log follow-up

Only the already-known Shipping directory ReShade.log was inspected (74,162 bytes, under 20MB). See real-game-historical-log.json/.md in knowledge and isolated artifacts for source hash, LastWrite, read time and exact line numbers. No extra scanning, game writes or EXE execution.

Historical lines: L1 ReShade6.8.0.2155 via dxgi into Shipping at20:39:26; L46 addon loading; L167 provider310.9.1.0; L168 Streamline2.14.1.0; L225 runtime status0x0 OK; L227 Dynamic support; L235 Dynamic accepted target160FPS; L236–241 runtime reports2–6 actual presented frames. DriverStore provider warnings L146–150 and exit addon-loaded warning L471 are also retained. File LastWrite local2026-09-13 21:47:54, but lines contain time only, so do not infer exact session date solely from LastWrite.

This is pre-existing historical self-reported runtime evidence, NOT this round's game test, independent FPS measurement, proof of current performance, or blanket compatibility success.

## v0.1 discovery and embedded FPS resource follow-up

Merged main before this work. Parent conveys user's newer decision to use the existing embedded FPS route instead of external unlock.exe; this agent only audits resources and implements discovery, with no injection or game execution.

API: static GameDiscoveryService.DiscoverAsync(string? selectedRoot, IEnumerable<string>? savedPaths=null, bool includeSystemHints=true, CancellationToken token=default). Returns GameDiscoveryResult with all Candidates and Diagnostics. Candidate has GameRoot, ShippingExePath, Evidence. VM should present choice when multiple candidates exist, and retain manual selection regardless of discovery result. Core DiscoverFromHints(IEnumerable<GameDiscoveryHint>, token) supports deterministic fake-directory tests.

Sources: explicit selected/saved paths; Windows HKCU/HKLM 32/64 uninstall keys matching Wuthering Waves/鸣潮 DisplayName and InstallLocation; Epic's ProgramData/Epic/EpicGamesLauncher/Data/Manifests/*.item matching DisplayName and InstallLocation. Known folders come from Environment.SpecialFolder, not a guessed username/drive. Limits:128 saved paths,256 hints,4096 uninstall children per hive/view,2048 Epic manifests<=512KB each,4 ancestor levels with only exact known root/layout probes. No recursive game or full-drive scan. All existing ancestors are checked for reparse points. Root entry Wuthering Waves.exe must exist and exact Client/Binaries/Win64/Client-Win64-Shipping.exe must have x64 PE executable/not-DLL header. This validates layout/architecture, not cryptographic originality.

Primary documentation checked: Microsoft https://learn.microsoft.com/en-us/windows/win32/msi/uninstall-registry-key (InstallLocation) and Epic https://dev.epicgames.com/documentation/en-us/unreal-engine/academic-installation-of-unreal-engine (.item manifest directory). Unknown launcher formats are not guessed; user can manually select.

New10 discovery cases passed, total core101 passed/0 failed (discovery-tests.log). These are temporary-directory tests with non-executed PE header fixtures, not native game discovery integration or actual game testing. UI/VM/csproj untouched.

FPS component audit: components/ww_plugin_base.dll remains34304 bytes x64 DLL, SHA256844d7552692f53e8a1bfe45edf360a094597c5bf2b26dc058ff59b21d2250c3a, NotSigned. Re-read main input/unlocker/鸣潮.exe hash5b9cba854357a4d9ce9c56676e22e397be8d5dbd2dc10de9393155e378050fae and resource bytes offset4230325 length34304; exact SHA256 matches component and baseline. fps-plugin-availability.json preserves evidence. Resource is statically available but current runtime protocol/game compatibility remains unverified. No executable load/injection occurred.

## v0.1 explicit portable-data migration

Standalone migration service is scripts/Migrate-UserData.ps1 (PowerShell7.4+). It intentionally is not startup auto-discovery and does not modify the application. Parameters are full portable PACKAGE root directories, not data directories:

    ./scripts/Migrate-UserData.ps1 -SourceDirectory '<old portable root>' -DestinationDirectory '<new portable root>'

Both roots must already exist, cannot be identical/nested, and all paths are checked for reparse points. Only source/data is traversed by a bounded-scope queue; settings/deployments/logs/provenance and unknown data files are copied without moving/deleting originals. Other package content is excluded. Source bytes and destination copies are SHA256 checked; the only content change permitted is settings.json PackageManifest if it is an absolute path exactly resolving to old-root/payload/manifest.json AND both old/new package manifests exist with plain ancestors. New value points to new-root/payload/manifest.json. External or relative manually chosen manifest paths are preserved unchanged. JsonNode preserves unknown settings values, including large JSON numbers; unchanged settings copy byte-for-byte. Deployment receipts never undergo schema normalization.

All input/link/conflict checks happen before data copying. Any existing destination with differing expected content causes BlockedConflicts and NO data copies; identical files are verified/skipped. File creation uses CreateNew so late collisions cannot overwrite. A report is saved under destination/migration-reports/migration-<time>-<guid>.json with source/expected/destination hashes, relocation fields, per-file outcome and errors. Partial failures are reported; old data is never cleared and no executable is launched.

scripts/Test-UserDataMigration.ps1 executed only isolated .tmp fixtures.19 checks passed, including Chinese/space package paths, unknown JSON fields/large integer preservation, source immutability, byte-identical receipt/log/provenance retention, repeat migration, external manifest retention, conflict no-overwrite/no-partial-copy, missing manifest, invalid JSON, same/nested/relative root rejection and actual Windows source/destination junction rejection. Evidence migration-tests.log. No actual running user's data was read or migrated by this agent; parent will invoke migration with explicit old/new roots.

## FPS card icon-only refinement

Synchronized main in isolated branch (merge21d817d; the test Program merge conflict resolved using main's integrated version). User requested replacing the disliked blue gauge without changing the card layout/copy. Only FpsCard.xaml icon element changed: native Viewbox/Canvas/Path with three horizontal speed strokes and one forward chevron, round line caps/joins. Original36x36 size, Blue resource, stroke1.7 and grid position preserved. No bitmap generation, other icons, UI text, spacing or bindings changed.

WPF build initially reported missing local project.assets.json with --no-restore (fps-icon-build.log). Ordinary build then restored dependencies and succeeded with0 warnings/0 errors (fps-icon-build-restored.log). Parent owns integrated native screenshot/visual review; this agent has not claimed a native screenshot pass.

## Current selection validation / Cinebench recovery

Synchronized main before patch. Core discovery has no cache and no existing-path short circuit; every DiscoverAsync call collects fresh supplied/system hints. The UI must not equate nonempty/existing root+EXE with a validated game pair.

Added GameDiscoveryService.TryValidateSelection(string? selectedRoot, string? selectedExe, out GameDiscoveryCandidate? candidate, out string reason). This checks only the exact pair and performs no discovery/substitution: EXE must resolve to root/Client/Binaries/Win64/Client-Win64-Shipping.exe, root entry must exist, PE must be x64 executable not DLL, paths cannot traverse links. Valid pair may be reused by VM without system search. Invalid pair must trigger ordinary DiscoverAsync, which still considers saved/system hints independently. DiscoverFromHints now shares this validator, avoiding inconsistent reuse/search rules.

Eight new regression cases cover reusable correct pair, correct root+Cinebench exe, wrong root+correct exe, same-named EXE outside canonical layout, renamed fixture without real game layout, recovery from wrong selected directory using independent valid hint, file mutation invalidating previous discovery, and incomplete/root-entry selections. Core total101 passed/0 failed on current main baseline (discovery-selection-tests.log). Tests use non-executed PE fixtures and do not prove actual game behavior. Exact layout+PE cannot cryptographically authenticate an executable deliberately substituted inside a complete cloned game layout; no such claim is made. VM and actual discovery binding/desktop updates belong to root agent.
