# 更新协议 1

## 渠道与命名

固定仓库 `PLCyanxin/WuWu-FPS-Unlock`。使用公开 HTTPS Releases 列表（含 prerelease，排除 draft），按产品版本比较，不使用上传顺序作为版本大小。

标签 `v1.1.1`、`v1.1.2RC`；文件名不带开头 v：

- `WuWaFPSUnlock-<version>-win-x64.zip`：完整 Windows x64 自包含包。
- `WuWaFPSUnlock-<version>-update.zip`：通用更新包。
- `SHA256SUMS.txt`：每行 `<64位SHA256>  <资产文件名>`，包含上述 ZIP。
- Release body：用户看到的更新说明，按纯文本显示，不能执行其中命令或当作 HTML。

版本数字比较优先；同数字正式版高于 RC，RC2 高于 RC1。跳过状态保存确切标签；手动检查忽略跳过，自动检查不再提示该版本，但更高版本仍显示。

## ZIP 根结构

```text
update-manifest.json
更新.exe
更新说明.txt
update-payload/
  WuWaFpsUnlock.exe
  components/PROVENANCE.json
  components/fps/ww_plugin_base.dll
  licenses/...
  payload/files/addon/renodx-mfgunlock.addon64
  payload/addon-source.json
```

所有包内文件都应在清单中声明，清单自身除外。清单格式：

```json
{
  "protocolVersion": 1,
  "productId": "WuWaFpsUnlock",
  "version": "1.1.1",
  "updater": "更新.exe",
  "files": [
    { "path": "更新.exe", "size": 123, "sha256": "64位小写十六进制" }
  ]
}
```

示例省略其他条目；正式包必须列全。路径使用 `/`，不得绝对路径、`..`、盘符、ADS、链接、重复路径、Windows 保留设备名或尾随点/空格。解压需限制文件数量、总大小和单文件大小，禁止落出独立暂存目录。

ZIP 外层 SHA256 验下载完整性，包内清单验证版本、产品、协议与逐文件内容。SHA256 不是数字签名；不能声称它证明任意发布者可信。仅接受仓库自己的 Release 资产 URL，经 HTTPS 下载，不能从更新说明接受任意执行地址。

协议 1 只替换清单所列允许范围内的程序/组件。禁止包携带 `data/`、真实游戏路径、用户账号信息和整份自定义部署记录。DLSS/Streamline 或其他新增更新目标需要先升级契约和消费者，不能临时放开任意路径。

## 启动器与独立更新器

自动检测默认开启，用户可关闭；后台只查版本，有更新时提供更新说明、立即更新、暂不更新和跳过该版本。手动检测始终可用。下载/解压/校验期间允许取消，不得半途替换启动器。

暂存目录是 `<安装根>/.updates/<唯一编号>/`，不可放 `data/` 或材料目录，以免快照递归自身。协议 1 更新包必须保留独立文件夹，不平铺混入已有安装根目录；旧无清单手动包仍按旧规则支持。更新器固定从自身目录读取 update-payload，安装目录只匹配自身及最多三层父目录直接包含的、产品身份正确的 WuWaFpsUnlock.exe。多个候选时停止。

严格 ZIP 白名单禁止附带回退脚本。目录校验可以忽略更新器自身随后生成的根 `回退.cmd`，以支持同包重试；该文件不能进入远端 ZIP，校验过程不执行它。

在线安装 IPC：

```text
更新.exe --wait-for-exit <启动器PID> <启动器完整EXE路径>
```

ProcessStartInfo.ArgumentList 逐参数传递，不拼 shell。更新器验证期望路径等于识别出的安装 EXE，并验证活 PID 归属，等待自然退出最多 30 秒；不 kill、不绕权限。启动器成功创建更新器进程后才退出，创建失败继续保留界面。

更新前校验完整协议包，备份完成才写入。备份 `<安装根>/update-backup-日期-编号/` 包括更新前程序、components、payload、licenses 和 data 快照，不纳入更新/备份目录自身。

回退入口：更新目录 `回退.cmd`，独立备份内 `rollback.cmd` 与 WuWaUpdater.exe。恢复更新前实际字节；自动回退保留当前 data 和后来新增的无关文件，本次引入文件仅在仍与登记一致时移除。回退失败应事务恢复并报告备份位置。

更新/回退只改启动器目录，不直接部署或清除游戏；成功均提醒“请打开启动器，在设置中重新部署一次”。桌面链接使用 Windows 真实桌面路径，按完整目标匹配，找不到创建唯一名称。

## 兼容演进

旧手动包可继续手动使用，但不能假定它支持在线协议。协议 1 客户端遇到未知协议或缺少清单必须拒绝执行，并保留手动升级路径。未来若需协议 2，应先发布兼容过渡启动器，或提供协议 1 引导包；不得无提示破坏已安装客户端。
