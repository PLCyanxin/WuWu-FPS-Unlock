# v0.1 implementation

RestartController is the sole runtime restart coordinator. WindowsRestartProcessCatalog canonicalizes full image paths and holds a query/terminate/synchronize handle; no tree kill or name-only kill. Notice occurs before session disposal or termination. Already-exited held handles are accepted; access/identity failures stop, and a newly appeared instance blocks another start. Narrow external process-creation races cannot be made atomic without cooperation from the game.

AppViewModel captures settings once per restart, verifies inputs twice around notice, releases old FpsSession, creates one Shipping process, waits for its rendering window, connects the pinned FPS-only module when enabled, and emits GameReady only on success. Failures stay in the existing status/log. No external unlocker is built or distributed.

FpsSession validates source SHA/x64, holds the DLL read-only across loading, checks target creation time, verifies loaded module and named-pipe server PID, and sends FPS-only fields. No privilege/anti-cheat workaround. Actual game access may be denied; report and stop instead of asserting success.

DeploymentNoticeStore maintains per-install generations and acknowledgement. Actual partial deployment changes carry into later successful completion; unchanged reuse does not reset. Legacy records conservatively migrate without deleting history. Builtin FPS consent is a separate per-game/core-hash sidecar, never reset on FPS changes.

ReShade source records distinguish ToolInstalled from prior/unknown origins. Discovery uses bounded saved/registry/Epic hints and validated x64 Shipping candidates, never full-disk scanning. Migration is explicit source/destination, source-read-only, collision-blocking, hash-verified.

MainWindow creates a notification icon only after GameReady; double-click restores it. Original colored icon used everywhere. Windows controls overflow placement. Legacy single-instance mutex identifier retained for cross-version protection; not an application version display.
