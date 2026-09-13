# Launch agent knowledge

Owner: launch. Isolated branch: agent/launch. Workspace: .agent-worktrees/launch. Resume by reading this file, findings.md and tasks.md before editing. Keep source changes separate from deployment changes in AppViewModel.

The original binary and INI remain untouched in input/unlocker. Never execute them without the user's one-time concrete game-launch approval. No game or unlocker execution occurred during this audit.

Evidence is in the main workspace artifacts/unlocker-audit/decompiled/ww_unlockfps.decompiled.cs (generated from the exact binary with ILSpy 9.1, not a downloaded source commit). No decompiled assembly/binary is committed. The bundle extraction script is main workspace .tools/extract-unlocker.py.
