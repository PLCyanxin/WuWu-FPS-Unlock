# WuWa Dynamic Maximum

Independent ReShade companion for the WuWa launcher. The host draws the persistent game-default, Fixed, Dynamic and Off controls through versioned exports. Dynamic and Fixed choices are saved independently; the Dynamic maximum defaults to4x. The companion also owns the native cursor and Shift-docking policy.

Public Dynamic eligibility uses the host's minimum-version and capability checks. The native live-maximum/Off/observation hooks require separate exact-image ABI validation, including file SHA256,28 instruction spans, three pointer targets, caller addresses and current-thread stack bounds. Profiles cover Streamline2.14.1 and the specifically recorded2.14.16 SDK/OTA image. These do not authorize writes to unknown future layouts. The2.14.16 profile has static and synthetic validation; real-game compatibility is still experimental.

See `release-assets/dynamicmax/source.json` for binary identities, source inventory and build dependencies. See `tools/Mfg172Integration` for host UI and persistent mode integration. Original export names and ReShade configuration keys are retained for migration and compatibility.

Build with `Build.ps1 -DependencyDirectory <pinned-renodx-tree> -OutputDirectory <new-output>`. Tests use inert native functions, synthetic modules and temporary directories; they do not prove in-game frame generation, latency or image quality.
