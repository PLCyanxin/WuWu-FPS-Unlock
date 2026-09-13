# Status and remaining work

Implemented: launch plan + preserving validated INI adapter; single external/Shipping branch; external hash guard; same-version mutex/process guard; global named-process safety guard; exact renderer path/PID/window wait and timeout; cancellation when controller closes without killing processes; next-start FPS messaging; FpsSession excluded from compilation and base DLL removed from publish inputs; user EXE/config copied to components/unlocker; 12 independent filesystem regression cases.

Tests are LaunchTests.RunAsync(Func<string,Func<Task>,Task> test), called by deployment agent in test Program. These are fake filesystem inputs, not game tests. Parent owns final Windows build and real native UI evidence. Still unverified: actual unlocker launch/UAC/tray, game start, effective FPS, runtime race behavior, tool exit on game closure, real direct Shipping launch and MFG interplay. Real launch requires user approval of the displayed paths.

Do not claim these tests validate external code execution, frame rate, ReShade or driver capability. Known limitations: FPS mode requires canonical Shipping layout and existing root entry; external persistent logs unavailable; external source/binary cannot be assumed to match a public repository commit.

## Process boundary follow-up

LaunchExecution is now the shared production/test coordinator. It enforces a single active launch, distinct Starting/Waiting/GameRunning/Failed/Cancelled states, timeout, and cancellation without killing child processes. LaunchProcessGuard is also shared with production, so a process at another path cannot evade the external binary's name-based safety guard.

LaunchProcessTests creates an actual separate process from the test runner copied to a Chinese/space-containing directory. Child fixture mode only appends its PID and waits briefly. Its renderer callback is a deliberately injected observation boundary: success tests return the fixture process, timeout tests never return a renderer. Thus these tests exercise real process creation/lifetime but are not evidence of a real Unreal window or game FPS.

Test Program integration: before any tests `if (await LaunchProcessTests.HandleFixtureAsync(args)) return;`; before summary `await LaunchProcessTests.RunAsync(Test);`.

INI regression additionally starts with all unrelated toggles true, ProcessPriorityMode=6 and custom GameParam; verifies DX11, power saving, advanced/FOV/UID/blur false, priority zero and no launch arguments, with unknown keys retained.

Maintenance receipt precheck blocks PartialFailure, Installing, PartialClean; CleanedWithSkips reports retained files without claiming fully clean.
