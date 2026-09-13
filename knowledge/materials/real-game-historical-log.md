# 已有ReShade历史日志证据—不是本轮游戏实测

仅读取候选Shipping旁原有ReShade.log，没有启动游戏或写入日志。
文件：D:\Wuthering Waves\Wuthering Waves Game\Client\Binaries\Win64\ReShade.log
大小：74162；LastWrite UTC：2026-09-13T13:47:54.0376865Z；本地：2026-09-13T21:47:54.0376865+08:00
SHA256：a10f09ba842f5d222c09b5ef0c182a07e1de33ce69ddbacd514aa18878c6d7e5

日志从20:39:26初始化；行仅有时分秒，不能仅凭文件时间证明各行日期。历史日志明确记录ReShade 6.8.0.2155由dxgi加载进Shipping、addon加载、Dynamic support及accepted目标160FPS、报告2–6帧实际presented。此为原有日志自报，不是本轮重新实测，也不能替代独立FPS测量/画质检查。

同时含DriverStore provider不适用警告和退出时addon仍加载的警告；未以历史正向记录掩盖这些限制。

## 关键行

L1 : 20:39:26:951 [20912] | INFO  | Initializing crosire's ReShade version '6.8.0.2155' (64-bit) loaded from 'D:\Wuthering Waves\Wuthering Waves Game\Client\Binaries\Win64\dxgi.dll' into 'D:\Wuthering Waves\Wuthering Waves Game\Client\Binaries\Win64\Client-Win64-Shipping.exe' (0x8499FE8C) ...

L46 : 20:39:31:869 [20912] | INFO  | Loading add-on from 'D:\Wuthering Waves\Wuthering Waves Game\Client\Binaries\Win64\renodx-mfgunlock.addon64' ...

L167 : 20:40:02:726 [20912] | INFO  | [MFG Unlock] mfgunlock: verified mapped DLSS-G provider candidate version 310.9.1.0 (Dynamic MFG release stack).

L168 : 20:40:02:726 [20912] | INFO  | [MFG Unlock] mfgunlock: observed Streamline DLSS-G wrapper version 2.14.1.0 from D:\Wuthering Waves\Wuthering Waves Game\Engine\Plugins\Runtime\Nvidia\StreamlineCore\Binaries\ThirdParty\Win64\sl.dlss_g.dll (Dynamic MFG release stack).

L225 : 20:40:08:452 [20912] | INFO  | [MFG Unlock] mfgunlock: slDLSSGGetState runtime status is 0x0 (OK).

L227 : 20:40:08:452 [20912] | INFO  | [MFG Unlock] mfgunlock: slDLSSGGetState confirms NVIDIA Dynamic MFG support.

L235 : 20:41:03:484 [28240] | INFO  | [MFG Unlock] mfgunlock: NVIDIA Dynamic MFG accepted (target 160.000000 FPS); multiplier selection and pacing remain provider-controlled.

L236 : 20:41:03:737 [28240] | INFO  | [MFG Unlock] mfgunlock: slDLSSGGetState reports 6 frame(s) actually presented since its previous call.

L237 : 20:41:03:768 [28240] | INFO  | [MFG Unlock] mfgunlock: slDLSSGGetState reports 4 frame(s) actually presented since its previous call.

L238 : 20:41:03:786 [28240] | INFO  | [MFG Unlock] mfgunlock: slDLSSGGetState reports 2 frame(s) actually presented since its previous call.

L239 : 20:41:03:850 [28240] | INFO  | [MFG Unlock] mfgunlock: slDLSSGGetState reports 3 frame(s) actually presented since its previous call.

L241 : 20:41:04:004 [28240] | INFO  | [MFG Unlock] mfgunlock: slDLSSGGetState reports 5 frame(s) actually presented since its previous call.

L146 : 20:40:01:605 [20912] | WARN  | [MFG Unlock] mfgunlock: found 0 arch-gate comparisons in C:\Windows\System32\DriverStore\FileRepository\nvamui.inf_amd64_bf2a94de1e0dc342\nvngx_dlssg.dll (expected 1-4); leaving this provider alone.

L147 : 20:40:01:608 [20912] | WARN  | [MFG Unlock] mfgunlock: full Blackwell framework path not available for C:\Windows\System32\DriverStore\FileRepository\nvamui.inf_amd64_bf2a94de1e0dc342\nvngx_dlssg.dll -- no exact Ada cubin slot matched the generated table for 160_E658700.bin, nvngx_dlssg.dll; trying the 0.7 midpoint fallback.

L148 : 20:40:01:609 [20912] | WARN  | [MFG Unlock] mfgunlock: temporal fix not applied to C:\Windows\System32\DriverStore\FileRepository\nvamui.inf_amd64_bf2a94de1e0dc342\nvngx_dlssg.dll -- no supported temporal-kernel descriptor found.

L471 : 21:47:53:008 [20912] | WARN  | Add-ons are still loaded! Application may crash on exit.

