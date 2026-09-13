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
