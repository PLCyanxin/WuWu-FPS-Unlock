# Audited behavior

Exact input: input/unlocker/鸣潮.exe, 11232037 bytes, SHA-256 5b9cba854357a4d9ce9c56676e22e397be8d5dbd2dc10de9393155e378050fae. Runtime alias components/unlocker/unlock.exe is copied byte-for-byte. This is user provenance, not an official release attestation.

Static .NET bundle extraction: bundle v6, header offset 11231805, contained assembly ww_unlockfps.dll at offset 307200 length 10846208. Runtimeconfig targets Microsoft.NETCore.App + Microsoft.WindowsDesktop.App 8.0.0. Therefore a self-contained .NET10 controller does not alone satisfy the external tool dependency.

Evidence line numbers in the ILSpy output:
- MainWindow constructor 742+: LoadConfig then named Mutex `30launcher_WPF_WutheringWavesFPSunlocker`. AutoStartEnabled=true calls Start_Game; external UI is minimized/hidden to its own tray. This is not verified headless behavior.
- LoadConfig 1006+: relative ww_fps_config.ini read from working directory; malformed config is reset by external tool, so adapter validates before launch.
- Start_Game 1465+: GameLaunchExe=0 launches PathValue directly. =1 appends Client\Binaries\Win64\Client-Win64-Shipping.exe to the PathValue parent. Adapter uses =1 only when this derived path exactly equals manually selected Shipping, with an existing Wuthering Waves.exe root entry. No invented CLI.
- Unlockstart_test 1723+: =1 selects already-started Shipping without a second initial base injection; =0 waits for Shipping and adds injection. Controller never calls FpsSession or its own memory/pipe API.
- GameServerArea supports integers 0..2, but adapter preserves user's 2; server meanings not needed or guessed.
- Start_Game 1605+ forcibly ends processes named Wuthering Waves, Client-Win64-Shipping, nvngx_update and window title 鸣潮. Controller refuses FPS launch while any such process/window already exists, including another path. Cannot eliminate a race or rewrite external binary's internal behavior.
- Resources are extracted by external program into its own ulk_ww_tools subfolder. Console messages exist, but no reliable external persistent log contract was found. Controller logs requested executable, working directory, initial PID, observed exact Shipping path/PID/window and timeout separately.

No verified external realtime interface: FPS toggle/value changes only take effect at the next start; existing external instance is never sent speculative CLI/pipe commands or modified INI.

External binary dependencies and system/UAC execution remain runtime validation items. The controller can provide DOTNET_ROOT_X64 for bundled components/dotnet8 using CreateProcess; if Windows requires elevation, it requests normal shell/UAC launch which may rely on installed system .NET8 instead. No privilege bypass.
