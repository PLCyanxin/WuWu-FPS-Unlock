# Deployment agent knowledge

## Ownership and user decisions
- User most recent explicit instruction overrides old AGENTS cleanup prohibition: flat 18 DLL materials replace same-name files found below selected game root; missing names are not created. Clear actual tool replacements if still same hash, do not restore backups. Old pre-existing same-hash files never become owned.
- Worktree: `.agent-worktrees/deployment`, branch `agent/deployment`. Parent integrates commit; do not write root workspace files from this agent.

## Implementation
- `PackageReader.PlanAsync`: validates each source size/hash, maps all existing same-name vendor matches within selected root, skips reparse entries, returns source/target/expected target hash. Unknown vendor names rejected by 18-name allowlist. Missing targets absent from plan but still source checked.
- `DeploymentFiles`: preflight every source/target; Windows File.Replace requires existing vendor destination; per-file temp/hash, receipts reflect actual writes. No original-file backups. Own vendor replacements require ReplacedByTool plus Completed; own addon requires CreatedByTool plus Completed. Clear only matching hash. Partial delete failures continue through other files and throw with PartialClean status.
- `DeploymentService.PreviewAsync`/`CleanPreview` create ApprovalFingerprint for confirmation. Same service instance must execute afterward. ElevatedWorker final argument passes fingerprint; elevated service recomputes plan/manifest/game/INI/proxy fingerprints so confirmation cannot silently expand target scope. This is freshness checking, not a security boundary against another process with the same user's permissions.
- UI parent must render entire map and use explicit confirmation; no real game actions performed by this agent.

## Tests and evidence
- Existing console regression test Program adapted to existing vendor target semantics. Added nested duplicate name mapping, missing targets, missing target after preview, corrupt unmatched material, current target mutation, file locks, partial write interruption, repeat cleanup, same-hash nonownership, user-modified preservation, incomplete receipt preservation.
- File content fixtures are clearly labeled inert text; these test filesystem behavior and cannot prove NVIDIA runtime compatibility, game startup or in-game MFG.
- SDK install handled by parent in root `.tools/dotnet`; pending actual build at time of initial knowledge entry.

## Pending / limits
- Real game paths/mapping and native game execution require parent/user confirmation.
- Windows installer and actual game not executed here. Concurrent malicious filesystem remapping cannot be fully eliminated by path-based checks; reparse points detected/ignored and destinations rechecked before writes.

## Follow-up safety audit
- Added absent-at-preview addon protection at preflight and per-file write. New addon uses non-overwriting rename, and a previous completed receipt only permits same-plan idempotence while the current file hash still matches.
- Cleanup hashes and marks deletion on one Windows handle (READ|DELETE access, share-read only). This closes the file-hash versus path-deletion race and prevents concurrent modification/replacement while the handle is held. Real fake-directory tests must verify Win32 behavior after SDK is ready.
- Integrity now verifies all recorded configuration writes; early-load CSV may have additional user entries but cannot lose recorded entries.
- Launch agent implements independent PartialClean/PartialFailure/Installing startup guard, coordinated with parent VM guard.
- `ADDON_CONFIG_AUDIT.md` records local hash, upstream release match and per-process RuntimeSelectionMode semantics. Never executed addon.

## Native Win32 component evidence
Before full SDK availability, PowerShell Add-Type compiled the exact `OwnedFileDeletion.cs` production file with only implicit-using equivalents prepended. Executed in this worktree's `artifacts/safety-tests` against inert files: mismatched hash retained, exclusive lock blocked deletion, matching file deleted by the verified handle. All three passed. Log: `knowledge/deployment/win32-delete.log`. This checks the real Windows deletion API path, not the full .NET 10 project build, installer, UI or game. Full console regression suite still pending.

## Explicit user-material cleanup extension
The parent/user explicitly requested removal of the existing replaced DLLs even where this tool did not perform their earlier manual installation. `UserMaterialRemoval` is therefore a separate confirmed-plan authority: validate every package source; scan only the 18 vendor names and the standard addon inside the selected root, without following links; include only matching sizes and SHA-256. The preview states that matching material is NOT evidence of tool installation. Full confirmation is required before execution. Unknown hashes, other addon names, ReShade/filters/EXEs remain untouched. No `ReplacedByTool` or fabricated installation ownership is assigned. Missing ownership means no INI edits are removed.

`CleanPreviewAsync`/`CleanAsync` bind manifest hash, matched candidates, skipped entries, selected game and existing ownership receipt into the approval fingerprint. The elevated worker recomputes the same plan. Source or target changes fail before deletion. Per-file deletion retains the already-tested same-handle hash/delete implementation. Existing ownership cleanup protection tests remain unchanged.

Deployment receipt now reports `SkippedVendorNames` and deployment logs state actual matched target count plus skipped material-name count/list, so an addon-only or partially matched deployment cannot imply all 18 DLLs were installed.

Validation command (Windows native SDK 10.0.100): `E:\yanxin_ws\wuwa-fps-unlock\.tools\dotnet\dotnet.exe run --project tests/WuWaFpsUnlock.Tests/WuWaFpsUnlock.Tests.csproj -c Release`. Actual result: 91 passed, 0 failed, including six new user-material cleanup tests. `material-clean-tests.log` is the complete console output. These are filesystem fixtures and the existing fixture-process tests; no game or real material deletion was performed.

WPF build attempted after restore: it reaches compilation with exactly one error, the intentionally changed async preview call still referenced by this isolated worktree's old VM (`CleanPreview` at AppViewModel.cs:197). Parent owns and updates that VM to `await CleanPreviewAsync`; no claim of full WPF build success is made here. `material-clean-build.log` preserves that actual result. Core/tests compilation and execution above succeeded.

## Version 0.1 — per-installation notice and ReShade provenance
- VM API: `DeploymentNoticeStore.RequiresAcknowledgement(receipt)`; only after the user explicitly confirms call `Acknowledge(receipt)` and `AppPaths.SaveReceipt(receipt)`. Cancellation performs no mutation. The notice itself is displayed by parent-owned UI.
- Service initializes old receipts conservatively: an old successful deployment without notice state remains pending until a real acknowledgement. Existing settings, file evidence and INI records are retained. No-op redeployment does not increment the generation or clear/recreate acknowledgement. Successful deployment only increments after post-write integrity passes AND actual payload, proxy or INI hash changes are detected.
- Cleaning ends the notice for that installation. A later changed deployment gets a new generation; `BeginAfterClean` archives the old receipt's files/INI evidence and carries notice/history forward instead of dropping records.
- ReShade detection remains scoped to the selected renderer EXE's load directory. New installs record SourceKind=ToolInstalled, exact local Setup path and Setup SHA-256. Existing user installs/upgrades do not gain tool ownership; missing legacy source metadata remains LegacyUnknown. Reused runtime hash changes invalidate tool-owned provenance.
- Cleanup removes a ReShade DLL only when its completed source record says ToolInstalled, CreatedByTool is true, source SHA metadata is valid, the path equals the recorded proxy in the selected renderer directory, and the same-handle current hash check succeeds. UserExisting/LegacyUnknown runtime files remain preserved. INI/presets/shaders are not claimed as ReShade DLL ownership. Existing 18-DLL explicit mapped/material cleanup remains unchanged.
- Actual Windows validation: core suite 101 passed / 0 failed; native WPF project build succeeded with 0 warnings / 0 errors. Logs: notice-tests.log and notice-build.log. Tests include state persistence through JSON, cancelled notice semantics, unchanged reuse, changed installation generation, legacy migration, clean/redeploy history, tool/user/legacy ReShade source cleanup and wrong load-directory refusal. No real game writes or executable launches by this agent.

Follow-up: actual writes in a partial installation now persist `PendingDeploymentChanges`. A later successful verification-only retry consumes that flag and shows the notice once; intent alone never sets it. Cleaning cancels that unfinished generation. A ReShade installer that exits with failure may still have changed the proxy; the observed hash difference is retained as a pending change without fabricating verified runtime ownership.

Latest full suite: 102 passed / 0 failed. The immediately preceding run had one existing child-process marker-file sharing race (101 passed / 1 failed) while the FPS ON fixture marker was still being written; the failure is preserved in notice-tests-marker-race.log. All notice/source tests passed in both runs. WPF build remains 0 warnings / 0 errors. This transient test synchronization issue was reported to the parent for the launch owner; it was not hidden or counted as an initial pass.

## Unknown JSON preservation
Added JsonExtensionData to UserSettings, DeploymentReceipt, FileReceipt and IniReceipt. Unknown nested objects, arrays, booleans and null fields survive normal saves, UserSettings.Clone, notice acknowledgement and clean-cycle archival; BeginAfterClean carries top-level receipt extension data forward. Two real disk roundtrip regressions passed; latest complete console result 104 passed / 0 failed in json-preservation-tests.log. AppPaths was reviewed only: Receipt still hashes the uppercase absolute Shipping path with SHA-256 and uses the first 24 hex characters under data/deployments; LoadReceipt still resolves through that unchanged function. No receipt filename migration or main VM changes were made by this agent.

## 2026-09-16 — direct payload updates

User requested removal of stale-package validation so local addon/DLL updates do not require regenerating manifest hashes. PackageReader now treats manifest file hash/size fields as import metadata and captures actual source SHA-256 and size using one read-only FileStream. Safe relative paths, file existence, names and anchors remain enforced. Deployment plans therefore contain current source fingerprints; existing preview approval, execution rechecks, write verification and ownership receipts still use those fingerprints.

UserMaterialRemoval planning uses the same current source fingerprint rather than old manifest metadata. Its execution-time source/target checks and hash-protected deletion are unchanged. Old owned receipts retain their installed fingerprints; they are not rewritten to claim an unobserved update.

No build or tests were run, as explicitly requested. No actual game file was accessed or modified. Older tests expecting stale manifest hash/size to reject source updates now describe superseded behavior and have not been rerun.

主整合补充：2026-09-16 移除 VM 启动/刷新部署完整性检查，安装到 D:\software\鸣潮 FPS Unlock 并迁移44项data；桌面鸣潮.lnk已更新。build成功，无测试/实机操作。材料更新说明见 artifacts/update-20260916/更新说明.txt。

## 2026-09-23 — offline updater

Added tools/OfflineUpdater: .NET 10 Windows x64 self-contained single-file console output WuWaUpdater.exe. Uses its own directory as target; reads update-payload beside it. Allowlist: WuWaFpsUnlock.exe, components/fps/ww_plugin_base.dll, components/PROVENANCE.json, licenses descendants. data and user payload cannot be targets. ProductName WuWaFpsUnlock and OriginalFilename WuWaFpsUnlock.dll/exe required for both executables; no old version/hash pinning.

Preflights all paths, refuses reparse ancestors and links, checks exact running target and acquires exclusive destination handles. Copies originals and stages replacements under a unique update-backup timestamp/GUID directory, compares copy hashes, then replaces. Existing destination handles must close immediately before Windows File.Replace; rechecks original fingerprint at that point. This reduces incidental races but is not an adversarial filesystem transaction. On failure, changed files roll back in reverse order only if still equal to updater's new bytes; changed external files are preserved and incomplete restoration explicitly reported. Backups always retained. No termination, auto-elevation, app launch, game launch or deployment. Success prompts: 请打开启动器，在设置中重新部署一次.

No build, executable run, filesystem integration test or real-game action performed by this agent in this task. Parent owns publishing workflow/README and CI. Product identity follows default project assembly metadata, not a release-specific hash.

Follow-up: user requested bundling the newer addon with the updater. Added only the optional exact target payload/files/addon/renodx-mfgunlock.addon64 to the allowlist. It follows the same backup/staging/rollback path; other user payload files remain rejected. No game deployment occurs, and the success message still asks the user to redeploy in settings. No tests run for this one-line allowlist change. Parent owns the required-core-files check added separately on main.

## 2026-09-23 — nested update-package folder support

Updater install discovery now checks its own directory and at most three parents for a direct WuWaFpsUnlock.exe. Each existing candidate must pass ProductVersion product identity; folder names are irrelevant. Zero candidates gives placement guidance, multiple valid candidates lists exact EXE paths and refuses selection. Wrong-product names are reported and excluded; no recursion or drive scan. NoLinks checks remain applied to directories, candidates and source paths. update-payload always resolves beside the updater, independent of selected installation root; transaction destinations/backups use the selected installation. Preserved main's three required files and optional exact addon allowlist. No tests or executables run for this change.

## v1.1.1 — local snapshot rollback and desktop shortcut

Interfaces: normal WuWaUpdater.exe updates; WuWaUpdater.exe --rollback <absolute backup directory> restores that completed update; WuWaUpdater.exe --self-test runs eight inert temporary-directory fixtures and returns nonzero on failure. CI can invoke the published exe with --self-test; it does not invoke COM, resolve a real installation, launch software/game, or touch the real desktop. No tests were run locally by this agent for this task.

Before modifying installation files, capture the actual local root executable/runtime/config/icon/docs files (.exe/.dll/.json/.config/.pdb/.ico/.ini/.txt/.md, excluding updater) plus complete components, payload, licenses and data trees, including empty directories. Only those named trees are traversed: the update folder and root backup directories are never recursed. An updater inside a snapshot tree is rejected. Validate the full snapshot before writes. Root metadata/files not in that explicit extension list are not claimed as captured.

Backup layout: installation/update-backup-<timestamp>-<guid>/snapshot/<original files>, snapshot.json + SHA-256 corruption checksum, original/ transaction targets, own single-file WuWaUpdater.exe, ASCII rollback.cmd. Updating folder receives 回退.cmd using generated ASCII relative path and %~dp0; backup launcher works after deleting the update folder. Hash checksum detects accidental corruption, not a malicious same-user forgery. Snapshot Root must equal backup parent and all relative paths remain scoped/non-reparse. Existing and snapshot EXEs must identify this product at rollback entry.

Automatic rollback restores snapshot program/components/payload/licenses and root runtime/docs. It intentionally preserves current data (configuration, ownership and deployment records) even though data is fully backed up and verified. It preserves later unknown new files. Only update-introduced files whose current bytes still match that update are deleted. Restoration first locks and backs up all affected current files to a separate rollback-attempt GUID directory, stages all restores, then applies. Injected failures recover applied operations in reverse; it reports incomplete recovery explicitly. Replay is idempotent. Backups are retained. Directory metadata/timestamps/ACLs are not a disk-image snapshot; file bytes, paths and empty directories are represented. Previously absent empty directories created by failed writes may remain.

Update and rollback success refresh only real-desktop shortcuts whose full normalized target equals this installation EXE, setting target, working directory and EXE icon. If none matches, create a unique 鸣潮 FPS Unlock link without overwriting other links. Unknown/other-install shortcuts stay untouched. COM errors print a manual-shortcut warning and do not undo completed file transactions. Both success paths tell the user to redeploy from settings; no game files are ever written automatically.

Static implementation review completed; no local build, fixture execution, updater execution or game action claimed. Parent owns CI build/test evidence and release packaging.

The only additional optional payload metadata target is payload/addon-source.json, for bundled addon provenance. No general payload/manifest overwrite allowance was added; user-customized full manifests remain untouched by updates.

## Online handoff — wait for launcher exit

Branch codex/updater-wait-for-exit starts from main 55520ce; previous deployment branch and commits are retained. IPC: WuWaUpdater.exe --wait-for-exit <positive PID> <expected fully-qualified WuWaFpsUnlock.exe path>. Use ProcessStartInfo.ArgumentList, do not assemble shell command text. Package may live at installation/.updates/<guid>; existing bounded parent discovery finds installation two levels above. Source remains updaterDirectory/update-payload.

Before waiting, resolve the installation, validate the supplied path equals that installation EXE and verify its product identity. A live PID must expose the same full executable path; only that process is waited on, at most 30 seconds, never killed. Missing/already exited PID is accepted as naturally gone and never treated as verified live identity; Update then re-resolves expected installation and runs existing RejectRunning and exclusive-file preflight. Wrong live process, denied identity lookup or timeout aborts. No application/game launch or injection added.

Six injected process-API tests added to --self-test (14 total with existing snapshot cases). These test control flow, identity mismatch, bounded wait, permission failure, natural exit and required complete path; they are explicitly not real Windows child-process evidence. No local self-test or game execution performed for this change. Shared package-manifest protocol integration is coordinated with launch agent separately.

## Shared package protocol integration

OfflineUpdater links the single BCL-only Core UpdatePackageProtocol.cs source (no WPF dependency). New read-only IPC: WuWaUpdater.exe --validate-package <directory> <version>, returns 0 only when ValidateDirectory(directory, version) succeeds, nonzero on errors, and never waits for a key. This path does not resolve an installation, inspect processes, mutate files, touch shortcuts, or launch software.

Normal manual updates validate update-manifest.json when present, preserving historical no-manifest manual package support. --wait-for-exit requires the manifest and validates before waiting and again on entering Update. Update revalidates after acquiring all source handles, before staging/backups/writes, to bind the protocol fingerprints to the immutable bytes actually copied. A present manifest cannot silently disappear between those checks. Namespace/API: WuWaFpsUnlock.Core.UpdatePackageProtocol.ValidateDirectory(string, string? expectedVersion = null).

Dependency: shared protocol commit dc18441 from launch agent (locally fd53d2b cherry-pick). Root handles protocol follow-up for generated 回退.cmd and advises isolated package folders for manifest packages; strict directory validation deliberately rejects unrelated installation files in a flattened package folder. No local compile or executable invocation performed by this agent for this integration.
