# Automatic update UI integration — 2026-09-23

Base: 55520ce, isolated branch agent/materials-update-ui.

- Added default-enabled AutoCheckUpdates and SkippedUpdateTag to UserSettings; existing JSON extension fields preserved.
- Startup checks once after initial refresh; manual check available in compact settings row. Version comes from assembly informational metadata.
- Checks leave Busy false. Highest candidate only: automatic skip does not select an older release. Manual checks ignore skip.
- Modal native WPF dialog shows plain-text notes, immediate-update/later, persisted skip, cancelable indeterminate download.
- Modal and download hold Busy; actual Shipping processes are checked before staging and again before updater handoff, including external game processes with stale settings.
- Process.Start uses shell execution and --wait-for-exit PID full-current-exe. Only success closes launcher through UpdateExitRequested, bypassing tray behavior. Cancellation/failure keeps launcher running.
- CloseAsync cancels check and download. No update is downloaded merely by checking.

Validation: actual Windows .NET 10 Release build (2026-09-23) succeeded, 0 warnings/errors. For this independent compilation, GitHubUpdateService.cs and UpdatePackageProtocol.cs were copied read-only from sibling launch worktree; these backend files are excluded from this commit and belong to launch agent. Backend fake HTTP tests and main integration/runtime rendering remain parent/launch work. No actual update installation, publishing, or game launch performed by this task.
