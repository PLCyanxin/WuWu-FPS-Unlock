# Status and remaining work

Implemented: launch plan + preserving validated INI adapter; single external/Shipping branch; external hash guard; same-version mutex/process guard; global named-process safety guard; exact renderer path/PID/window wait and timeout; cancellation when controller closes without killing processes; next-start FPS messaging; FpsSession excluded from compilation and base DLL removed from publish inputs; user EXE/config copied to components/unlocker; 12 independent filesystem regression cases.

Tests are LaunchTests.RunAsync(Func<string,Func<Task>,Task> test), called by deployment agent in test Program. These are fake filesystem inputs, not game tests. Parent owns final Windows build and real native UI evidence. Still unverified: actual unlocker launch/UAC/tray, game start, effective FPS, runtime race behavior, tool exit on game closure, real direct Shipping launch and MFG interplay. Real launch requires user approval of the displayed paths.

Do not claim these tests validate external code execution, frame rate, ReShade or driver capability. Known limitations: FPS mode requires canonical Shipping layout and existing root entry; external persistent logs unavailable; external source/binary cannot be assumed to match a public repository commit.
