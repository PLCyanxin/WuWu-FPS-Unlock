# 桌面材料清单（依据用户截图；不是已读取的实际二进制）

请将桌面那个素材目录的内容复制到本目录，或由 Codex 只读定位后复制。保留用户桌面原件。

```text
input\desktop-materials\
  替换\
    NvLowLatencyVk.dll
    nvngx_deepdvc.dll
    nvngx_dlss.dll
    nvngx_dlssd.dll
    nvngx_dlssg.dll
    nvngx_dlssnr.dll
    sl.common.dll
    sl.deepdvc.dll
    sl.directsr.dll
    sl.dlss.dll
    sl.dlss_d.dll
    sl.dlss_g.dll
    sl.dlss_nr.dll
    sl.interposer.dll
    sl.nis.dll
    sl.nvperf.dll
    sl.pcl.dll
    sl.reflex.dll
  renodx-mfgunlock.addon64
  ReShade_Setup_6.8.0_Addon.exe
```

实际内容与截图不同，应以文件审计结果说明差异；不要创建占位 DLL 来凑齐清单。
“替换”是源材料分组，不能证明这些 DLL 都要放在游戏根或 Shipping 旁边。
另需确认用户的游戏根、原装 Shipping 完整路径，以及 ReShade 成功代理位置/文件名。
