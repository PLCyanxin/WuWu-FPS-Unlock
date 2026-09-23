# MFG Unlock attribution

Base: mavismmg/MFGAdaUnlock-RenoDx, tag 1.1, commit c3733d8afd51214c46a71d18feec520b0bf54864. Upstream fork maintained by mavismmg; the following credits are retained from its README.

## Credits

- [dashdogy/RTX40MFG-Unlock](https://github.com/dashdogy/RTX40MFG-Unlock)
  provided the foundational reverse engineering and original working ASI
  implementation. Dashdogy diagnosed the midpoint compaction bug, demonstrated
  the corrected slot-9 temporal program, established the verified
  Streamline/NGX interception strategy, and showed how to apply the fix only to
  mapped process memory without modifying NVIDIA DLLs on disk.
- Dashdogy's project is published under the
  [MIT License](https://github.com/dashdogy/RTX40MFG-Unlock/blob/main/LICENSE).
  The implementation in `midpoint.hpp` remains independently written for the
  ReShade-addon format and was verified by reproducing the original patcher's
  output digest byte-for-byte.
- [Dreamt](https://github.com/ImDreamt) created the original ReShade/RenoDX addon
  adaptation and repository from which this project is forked.
- [sdli1995](https://github.com/sdli1995) developed the separate
  [`dlssg_for_sm86`](https://github.com/sdli1995/dlssg_for_sm86) implementation
  that brings DLSS-G multi-frame generation to supported RTX 30-series/SM86
  configurations.
- [Matias Lombo](https://github.com/matiasLombo/mfg-unlock) identified and
  validated the benefit of rebuilding DLSS-G's Blackwell framework kernels for
  Ada, including the motion-vector estimate, inpaint, and inpaint-decision
  stages. This fork's experimental full-kernel path follows his proven
  precompiled-cubin, exact-fingerprint, in-place replacement method; its
  release payload table is generated with his `rebuild_cubins.py` workflow.
- [Tony Joaca](https://github.com/TonyJoaca/DLSSG-Transfusion), author of DLSSG-Transfusion, publicly identified
  `Kernel_BlendCandidatesFused` as the useful intervention point behind his
  `qualityValidWarp` quality option. That research informed this fork's
  separately implemented and more conservative **Validated warp blend**
  experiment. No code or binary payload from DLSSG-Transfusion is included.
- The **Intermediate scatter retention** analysis and experimental `+120`
  motion-consistency variant were developed independently in this fork. The
  underlying DLSS-G kernels remain NVIDIA technology and are not claimed as
  original project code.
- Special thanks to [mugensc](https://next.nexusmods.com/profile/mugensc) for the
  RenoDX DLSS5 compatibility testing and known-good runtime combination.
- Special thanks to Artur from DLSS Enabler for the valuable debugging insights
  during the investigation of the Hogwarts Legacy HDR + Frame Generation issue,
  which helped lead to the fix included in this fork.
- [u/amart565](https://www.reddit.com/user/amart565/) tested and documented the
  ReShade + MFG Unlock installation workflow for Xbox Game Pass / UWP-style game packages,
  including Vulkan titles such as Indiana Jones and DOOM: The Dark Ages.
  See the [community installation guide](https://www.reddit.com/r/ReShade/comments/1wd6dyr/guide_to_installing_reshade_on_uwpxbox_game_pass/).
- Built on [RenoDX](https://github.com/clshortfuse/renodx) by clshortfuse, and
  [ReShade](https://github.com/crosire/reshade) by crosire.

## Disclaimer

Not affiliated with or endorsed by NVIDIA. This modifies process memory of a
running game; use it on your own hardware at your own risk, and expect anti-cheat
in multiplayer titles to object. Results on hardware NVIDIA did not ship this
feature for are to be judged by eye.

## Licence

MIT — see [MFGAdaUnlock-MIT.txt](MFGAdaUnlock-MIT.txt).
