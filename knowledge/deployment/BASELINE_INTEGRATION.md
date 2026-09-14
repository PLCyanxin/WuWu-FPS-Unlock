# Current game presence baseline

At 2026-09-14 17:54:25 +08:00, read-only bounded scanning of the user-selected game root found ten same-name material DLL paths. Every found file was hashed; current-game-baseline.json preserves the exact relative/absolute mapping, length and SHA-256. Eight currently absent names are explicitly reference-only, never future required targets. GameExe identity is copied from the existing runtime receipt for the same root; no filename or target directory was invented. No game files were written.

Core integration:
- GameFileBaselineStore.CaptureAsync(root, shipping) records existing whitelisted files only.
- MergeAndSave(baselinePath, observed) verifies the per-installation identity, atomically saves under an exclusive store lock, retains all prior required paths and original hash evidence, and only adds newly observed paths. It never shrinks an existing baseline after a deletion scan.
- Check(baseline, root, shipping) returns missing exact absolute paths. Hashes are baseline evidence, not a requirement to keep pre-repair bytes: official repair/update can change DLL content.
- RequirePresent(...) throws with the required first sentence: 检测到文件缺失，请在鸣潮官方启动器启动一次游戏完成游戏文件修复
- Parent must import the current ten-file snapshot to each installation's persistent baseline before deploy preflight; then call RequirePresent before the installer or any writes. Do not rebuild an empty baseline after a cleanup.
- Current desktop history's 0-target deployment followed cleanup without official repair (user corrected cause). This baseline prevents repeating that silent addon-only deployment. It does not create absent files or guess replacement locations.
- Existing deployment still searches all 18 names; names never observed in baseline remain optional, as requested.

Validation: five new fake-directory regressions, complete suite 110 passed / 0 failed. Tests cover only-current capture, exact missing-file instruction, no recreation, persistence that never shrinks, append-only observations, legitimate repaired hash changes and different-installation refusal. Baseline state lives in test directories only.
