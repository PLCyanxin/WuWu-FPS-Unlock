# FPS session lifetime audit — 2026-09-14

Scope: static reads only; no user EXE/DLL execution, no game attachment, no UAC, no protection bypass. The material is the user's exact native core, SHA-256 `844d7552692f53e8a1bfe45edf360a094597c5bf2b26dc058ff59b21d2250c3a`. Source provenance remains knowledge/materials/fps-plugin-availability.json. This is not proof of current-game compatibility or actual FPS.

## Evidence and conclusion

Original managed tool: main workspace artifacts/unlocker-audit/decompiled/ww_unlockfps.decompiled.cs, ILSpy extraction of the user's EXE. Unlockstart while-loop starts at line 1977, sleeps 115 ms at 2092, connects/reconnects FPS pipe at 2124–2132 and writes ReadAdvancedSettings(newValue) at 2137–2140. ReadAdvancedSettings at 2366 emits the six-field message. Earlier foreground/power saving logic can change newValue to 15. Therefore original sender repeats updates, but this alone does not prove a heartbeat requirement.

Exact native core disassembly (objdump -d -Mintel; image base 0x180000000):

- RVA 1D08 calls ReadFile. RVA 1D26 supplies address of persistent global RVA 90C4 to sscanf; RVA 64E0 is `%*d,%*d,%*f,%*d,%*d,%d`. Only sixth field is stored; the five advanced fields are ignored by this core.
- RVA 1D4C loops to blocking ReadFile. On disconnect, RVA 1D56–1D7A flushes/disconnects/closes pipe and recreates it. That path does not reset global FPS or unload the DLL.
- Separate writer routine RVA 21B5 sleeps 0x33 (51 ms), RVA 21C0 reads the SAME persistent global RVA 90C4, converts to float and calls RVA 1E00. On success RVA 21D5 loops to 21B5. RVA 1E00 is the target float store. This repeated operation has no pipe-read/last-message timeout dependency.
- DLL startup creates separate workers for main operation (RVA 2260) and pipe receiver (RVA 1BE0), via calls at RVA 2637 and 2685.

**The module itself continuously maintains its last received target. A single successful SetFps on a retained session is sufficient to supply a fixed target; adding a periodic sender is not an evidenced fix for the reported failure.** DLL load/initialization/target discovery must first succeed. The current FpsSession retains its client pipe and game reference after SetFps; Dispose closes IPC but does not unload the module. Consequently closing the GUI cannot be claimed to revert the game's FPS value either.

## Actual failure and limits

Main artifacts/desktop-runtime-20260914-172011/data/logs/20260914-173317.log: 17:36:41 AttachingFps access denied; 20260914-173916.log: 17:40:19 and 17:41:03 same. No successful IPC connection/message evidence in those runs. This is an unconfirmed load/attachment failure, not evidence that an established session stopped maintaining FPS. Independent MFG/Dynamic success says nothing about this separate module.

No operational change made in this audit: no justified timer or injection modification. Prior diagnostic commit b7922c6 distinguishes renderer identity, module enumeration, process access, loader, IPC phases; root must use those logs to identify current failure. Access rights/protection remain unresolved and are not circumvented. Even IPC connected + target sent proves neither native scan success nor actual in-game FPS because protocol is outbound only and provides no application acknowledgment.

## Reproduction

Run `python tests/fps-core-static-evidence.py components/ww_plugin_base.dll`. Six checks validate exact hash and relevant byte sequences without loading native code. Actual output: artifacts/fps-core-static-evidence.log, 6/6 STATIC checks passed. Disassembly: artifacts/core-static-disassembly.txt. Both artifacts are local, uncommitted generated evidence. These tests do not execute the DLL, do not emulate its success, and do not substitute for game tests.
