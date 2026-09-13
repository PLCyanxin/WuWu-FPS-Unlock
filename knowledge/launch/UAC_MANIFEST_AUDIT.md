# Exact unlocker PE manifest / UAC audit

Read-only audit of main workspace `components/unlocker/unlock.exe`; no execution. SHA-256 `5b9cba854357a4d9ce9c56676e22e397be8d5dbd2dc10de9393155e378050fae`.

The native PE resource tree was parsed from its data directory and section RVA mapping, not string-grepped. Exactly one RT_MANIFEST payload exists: resource path `[24,1,0]`, RVA319572, file offset301652, length3315. XML parsing (comments ignored) found exactly one active node: `requestedExecutionLevel level="requireAdministrator" uiAccess="false"`. The asInvoker/highestAvailable strings are commented examples. See read-pe-manifest.py, unlocker-manifest-audit.json and unlocker-manifest.xml in this knowledge directory.

The extracted managed assembly was decompiled from this exact bundle; its three Process.Start call sites (lines115,123,275 of main artifacts/unlocker-audit/decompiled/ww_unlockfps.decompiled.cs) launch URLs. Searches for runas, Verb, WindowsPrincipal, IsInRole and admin-relaunch patterns found no managed self-elevation path. This is a static conclusion about the visible managed code, not execution evidence or a security audit of embedded native plugins.

Under ordinary non-elevated controller + enabled default UAC, this EXE cannot be launched by CreateProcess without elevation: Windows returns ERROR_ELEVATION_REQUIRED, then standard ShellExecute/runas is needed. An already elevated parent need not show another prompt; configured UAC policies can affect prompt behavior, so 'every launch always visibly prompts' is not claimed. The program's manifest must remain unchanged.

Current launch fallback defect: after adding DOTNET_ROOT_X64 to ProcessStartInfo.Environment, changing that SAME object to UseShellExecute=true is invalid. .NET10 Process.Win32.cs lines42–43 throws InvalidOperationException whenever the environment dictionary was initialized for a shell launch. Even rebuilding a fresh shell ProcessStartInfo would not transmit a per-child environment block, so packaged .NET8 use would be unverified. Merely bundling runtime files is insufficient for the audited manifest.

Concrete standard-UAC solution (proposal only; no production changes made in this audit):
1. The normal controller prepares one explicit launch request containing immutable approved Shipping/entry/config paths, targetFPS, audited binary hash, request ID and expiry; never permit arbitrary command/argument execution.
2. Invoke the SELF-CONTAINED controller EXE (or a small self-contained launcher worker) with a dedicated launch-worker mode through a fresh ProcessStartInfo { UseShellExecute=true, Verb="runas" }; leave its environment untouched. UAC displays normally. Main GUI remains non-elevated. Cancellation is reported and no game is started.
3. Elevated worker validates the request, exact managed unlocker path/hash, canonical Shipping mapping, runtime directory/hash inventory, no active game/unlocker/collision processes, and exclusive launch lock. Only then merge the managed INI. Reject changed request/inputs rather than run unexpected files.
4. Within that already approved elevated worker, create a NEW ProcessStartInfo for unchanged unlock.exe with UseShellExecute=false, absolute working directory and per-child DOTNET_ROOT_X64=<app>/components/dotnet8. This is ordinary CreateProcess under the granted token; no second elevation requirement, no global environment/registry change and no modification of the user EXE. Launch exactly once.
5. Worker writes an atomic acknowledgement with its request ID, actual child PID/start time/executable and error/phase, then exits (or monitors a bounded time). Controller monitors exact Shipping PID/path/window separately. Closing the controller never kills unlocker/game. Read-only monitoring of an elevated child must use rights-appropriate APIs such as QueryFullProcessImageName rather than assuming MainModule access; if denied, report uncertainty without privilege bypass.
6. Verify with a self-built requireAdministrator fixture and private runtime path while desktop is unlocked, then test the real user EXE only after game-launch approval. All UAC/runtime behavior in this proposal remains unexecuted.

Primary references verified during audit:
- https://learn.microsoft.com/en-us/windows/win32/dxtecharts/user-account-control-for-game-developers (manifest/CreateProcess/UAC behavior)
- https://learn.microsoft.com/en-us/windows/win32/secbp/running-with-administrator-privileges (standard runas elevation)
- https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-environment-variables (apphost DOTNET_ROOT_X64)
- https://raw.githubusercontent.com/dotnet/runtime/v10.0.0/src/libraries/System.Diagnostics.Process/src/System/Diagnostics/Process.Win32.cs (explicit environment/ShellExecute rejection)
