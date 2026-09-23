---
name: wuwa-release
description: Build, update, and publish WuWa FPS Unlock launchers and compatible update packages. Use for this project's release versions, auto-update integration, backup/rollback, or release documentation; not for unrelated GitHub projects.
---

# 鸣潮启动器发布与更新守则

本 skill 适用于 PLCyanxin/WuWu-FPS-Unlock。仓库 `skills/wuwa-release/` 是维护源，本机安装副本保持同步。先读取项目 AGENTS.md 和用户本轮要求；本 skill 不授予发布、真实游戏写入或进程终止的额外权限。

## 发布前先固定契约

修改启动器检测、更新器、打包脚本之前，读取 [更新协议](references/update-protocol.md)。代码当前支持的协议是权威实现，变更契约必须同时改消费者、生成器、测试与本文档。不能仅修改说明后假定旧客户端兼容。

- RC 是本项目正常分发的准正式版，必须参与自动检测；同数字版本正式版高于 RC。不能依赖 GitHub `/releases/latest`，否则漏掉 prerelease。
- 使用用户指定的新版本，不能擅自把已发布版本改名或覆盖资产。确有更名/替换授权时，先生成新资产并核对，最后处理旧条目。已发布版本原则上不可变。
- 用户规定：正式版公开且完整包、更新包和校验文件均就绪后，移除同一数字版本对应的 RC Release（包括其资产），让正式版成为下载入口；保留源码标签便于追溯，不顺带删除其他历史版本。正式版发布失败时保留 RC。
- UI/InformationalVersion 使用产品版本，例如 `1.1.1` 或 `1.1.2RC`；程序集/FileVersion 使用四段数字，第三方版本不可跟随改写。
- Release 文案面向用户，说明新增功能、适用范围、操作步骤；不把构建日志、历史改动流水或测试数量当产品介绍。保留 README 简介之后的账号风险提示，实测结论仅报告实际证据。

## 交付链

1. 维护版本元数据和 `docs/RELEASE_v<版本>.md`；更新材料仅来自维护者明确提供的文件。同步来源记录，不重新挑选 NVIDIA、ReShade 或 addon 版本。
2. 用 `scripts/Build-ReleasePackages.ps1` 生成完整包和更新包；它调用 `scripts/New-UpdateManifest.ps1`。勿手拼一个与协议不同的 ZIP。
3. 发布工作流在标签推送或人工 dispatch 后构建 WPF 与更新器，执行隔离测试，创建 draft，上传两种包和 SHA256SUMS，再公开。所有资产就绪前不得公开半成品 Release。
4. 自动检测只提供提示和说明，安装需用户选择。游戏运行时禁止安装；网络失败不得阻断正常游戏启动。跳过只针对选定版本，手动检查可重新显示。
5. 启动器下载校验后调用独立更新器，更新器按完整 EXE 路径核对并等待指定 PID 退出。失败/超时不继续，不强杀进程。UAC 交系统处理。
6. 更新前备份用户自己的文件，而不是固定旧 Release。回退保留当前部署记录，恢复旧程序和组件材料；更新/回退均提示重新部署，均不直接写游戏。
7. 成功时刷新或创建本安装的桌面快捷方式；其他安装和无关同名链接不覆盖。更新文件夹可删，独立备份及其回退入口必须仍可用。

## 必须区分的验证

- 对版本比较、RC、跳过、协议/哈希拒绝、ZIP 路径限制、取消和备份恢复执行临时目录或伪 HTTP 测试。
- 声称兼容已发布客户端前，使用该版冻结的协议消费者或实际二进制验证新包；新更新器自身校验通过不等于旧启动器兼容。没有在线能力的旧版先提供离线引导包。
- 完整包同样排除 data、.updates、备份、日志与个人配置；打包脚本应有目录白名单断言，不只依赖更新包白名单。
- 运行现有 Core 测试，编译原生 WPF 与独立更新器；运行更新器 `--self-test` 和发布包 `--validate-package`。
- 原生 UI 和游戏实测单独记录；不能用编译/伪目录通过替代。用户要求人工验收时不要擅自启动、更新真实安装或操作游戏。
- 若发布报错，先看失败步骤与日志。历史失败邮件不会因后续成功自动消失；避免无理由反复重跑旧提交。

用户已授权发布的情况下，完成资产及说明后直接发布，不重复请求批准。未经授权时也应先把可审查的本地结果做完整。
