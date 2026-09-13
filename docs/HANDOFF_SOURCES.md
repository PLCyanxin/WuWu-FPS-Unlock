# 交接依据与核验范围

本次交接主要依据当前对话中的最终产品决定、用户桌面两张截图、实际上传的 EXE/INI、最后认可的双窗口效果图，以及逐文件读取的 dev1 源码 ZIP。

旧代码入口、白名单和本地 Setup 缺项详见 HANDOFF_V2_AUDIT.md。涉及外部程序新版本/CLI/自动启动/许可和当前游戏效果的内容，是给 Codex 的核验任务，不是本次已经证实的事实。

AGENTS.md 的项目级使用方式参考 OpenAI 官方文档入口：https://developers.openai.com/codex/guides/agents-md ，当前重定向至 https://learn.chatgpt.com/docs/agent-configuration/agents-md 。该官方说明称 Codex 在开始工作前读取 AGENTS.md，且支持项目层级指导。

ReShade/ww_unlockfps 上游具体源码行为沿用基线作为检索线索，不据此替代对用户本地二进制的核对；本次网页获取部分上游源码失败，不能声称完成新的上游源码复核。
