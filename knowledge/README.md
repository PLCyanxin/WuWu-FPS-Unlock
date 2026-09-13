# Agent 知识与隔离索引

本轮用户要求：子 agent 自己维护知识库、Git 隔离工作区和独立上下文，尽量复用已有 agent。

|角色|持久上下文|Git 分支|隔离工作区|
|---|---|---|---|
|启动 / WPF组件测试|launch/README.md、findings.md、tasks.md|agent/launch|.agent-worktrees/launch|
|部署 / 清除|deployment/README.md、ADDON_CONFIG_AUDIT.md|agent/deployment|.agent-worktrees/deployment|
|材料 / 运行库 / 候选映射|materials/README.md|agent/materials|.agent-worktrees/materials|
|整合 / 本地ReShade / 交付|integration/README.md|main|工程根|

共享大二进制和SDK可只读引用主工作区；源代码、日志、测试产物分别写各自worktree。子agent提交后由整合者cherry-pick，不能在主目录并行改文件。后续优先followup已有agent，读取知识库和待办；上下文丢失后依知识库恢复。禁止把聊天记忆当成测试证据。

首次隔离前的审计进度已保存为 Git checkpoint 9d2a66e，不伪称最初就已隔离。所有仓库/分支均本地，未上传材料或代码。
