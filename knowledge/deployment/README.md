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
