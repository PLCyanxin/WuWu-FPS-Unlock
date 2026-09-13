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

## Actual run 2026-09-14

.NET SDK 10.0.100 from main .tools/dotnet. Ran isolated artifacts/launch-runner/LaunchRunner.csproj with DOTNET_ROOT_X64 and NUGET_PACKAGES pointing to shared local dependencies. Result: 21 passed, 0 failed (13 plan/INI plus 8 process-boundary cases). Evidence: .agent-worktrees/launch/artifacts/launch-tests.log. Child fixture copies and PID markers remain under this worktree artifacts/test-work. Actual child processes were created; no user unlocker, installer, game or plugin ran.

Corrected test-only Reject helper to catch InvalidDataException explicitly as well as IOException; these are separate expected error families. The first parent integration run revealed the helper issue, not a successful invalid-input launch.

## Native WPF offscreen work, actual run 2026-09-14

Merged main into the same isolated agent branch, reused all existing context and knowledge. Added tests/UiRendering.Wpf with exact production App resources, MainWindow, SettingsWindow and shared VM. Native RenderTargetBitmap outputs: .agent-worktrees/launch/artifacts/native-offscreen. Build/run log: artifacts/ui-offscreen-build-run.log. Result: 15 passed, 0 failed. Viewed PNGs using image tools; text and artwork visibly present.

No Window.Show, Application.Run, desktop automation or game execution. The production Startup handler is detached before bounded Dispatcher flushing. Final default images use fresh VM, empty game paths, FPS240, and actual RefreshAsync hardware detection. This machine reported NVIDIA GeForce RTX4080 Laptop GPU, driver596.60, Windows10.0.22631, HAGS default/unknown. These values were actually read, not copied from reference art. A separately named long-path-fixture render carries fake test paths.

Found real template defect: PART_ContentHost had Margin bound to TextBox.Padding while its default template already applies Padding. Text viewport was6DIPs high inside40DIP FPS field. Removed only the duplicate Margin in App.xaml; viewport now22DIPs, verified >=1.15×font size in all editable native inputs. Deployment/clean buttons measured258.67DIPs each and filled their529.33DIP row with12DIP gap. No redesign.

Offscreen raster DPI100/150/200% is not an actual monitor-DPI test or desktop screenshot. Desktop interaction/settings-single-instance activation and game effects remain unverified pending unlock/approval.

## Exact UAC manifest follow-up

Read UAC_MANIFEST_AUDIT.md before any further FPS launch work. Exact native RT_MANIFEST is requireAdministrator. Current false+customEnvironment -> same-object shell=true fallback is invalid under .NET10. Standard-UAC self-contained launch-worker proposal documented; no production code changes or unlocker execution made for this audit.

## Implemented standard UAC boundary, 2026-09-14

ExternalLaunchWorker now replaces the known invalid740+custom Environment shell fallback. Parent uses a fresh fixed self-contained controller ProcessStartInfo with runas and no custom environment. Request path is restricted to AppPaths.Data/jobs, nonce-named, digest-pinned, expires in5minutes, and has a persistent exclusive claim against replay. Worker receives only request/nonce/hash, never arbitrary executable/CLI.

Worker revalidates elevated admin token, plan reconstructed from settings with fixed AppPaths.Unlocker, plan fingerprint, Shipping x64 identity/path, game stopped, maintenance state, user binary exact SHA, unrelated config disabled, process/mutex conflicts and complete embedded .NET8 file inventory. Rejects extra runtime files/links to avoid a second unpinned hostfxr/version. After UAC approval it sets DOTNET_ROOT_X64 only on its child CreateProcess environment and starts unchanged unlock.exe exactly once. Main GUI remains non-elevated. Response records nonce, phase/error, PID/start time; parent verifies identity then uses shared LaunchExecution to wait for exact Shipping renderer. UAC1223, worker failure and timeout never trigger fallback or retry.

GameProcesses uses QueryFullProcessImageName with PROCESS_QUERY_LIMITED_INFORMATION for process-path identity, not MainModule/process memory access, supporting read-only checks across the elevation boundary where Windows permits.

Actual verification: production WPF build succeeded0warnings/0errors; ExternalLaunch.WorkerTests14/14 (protocol roundtrip/tampering/path/nonce/expiry/off-mode/fingerprint/replay/results/fixedUACarguments/limitedQuery/async1223/failure). Existing21launch tests also passed again with the async start boundary. Logs: artifacts/external-worker-build.log, external-worker-tests.log, launch-tests.log. No actual UAC, user unlocker or game was executed. Real UAC+private runtime end-to-end remains pending approval and unlocked desktop. Parent must package components/dotnet8 exactly matching embedded knowledge/materials/dotnet8-file-hashes.json; system .NET8 is not considered a portable guarantee.
