# 2026-09-14 launch integrity read-only diagnosis

Reviewed the existing runtime logs and receipt under artifacts/desktop-runtime-20260914-172011/data, plus read-only hashes of exactly the receipt's current game files and the selected ReShade.ini. Did not write the game, start processes or alter cleanup rules.

Evidence:
- 17:38:03 onward repeatedly logged a generic Preflight deployment/configuration mismatch.
- 17:39:26 cleanup explicitly logged that [RenoDX.MFGUnlock] ForceMultiplier had changed and was retained. This is a concrete runtime-setting mismatch that the old IsIntactAsync treated as a launch blocker. The writer cannot be attributed to the addon versus a user from this evidence alone, and an exact 17:38 failed-state hash snapshot was not retained.
- 17:39:34 new deployment matched 0 DLL targets, skipping all 18 material names after cleanup. Only ReShade and addon were installed again. No path was guessed or missing vendor file recreated; the game automatically replenishing those files is not established.
- The current 17:39:46 receipt tracks only dxgi.dll and renodx-mfgunlock.addon64. Read-only actual SHA-256 values match their recorded values (0cee63f9...814b94f7 and 64184bb3...af0a0da). The current five recorded INI keys also match; DynamicTargetFPS=160 and added overlay/runtime keys are additional user/runtime configuration. This current post-redeployment state cannot retroactively prove all binary hashes at 17:38.

Implementation:
- DeploymentFiles.InspectIntegrityAsync(receipt) returns detailed file/config differences, expected/actual values, paths, and Blocking/Warning severity.
- Files absent, modified, unreadable, unverified, outside boundaries or an incomplete receipt block launch.
- INI runtime value changes, missing/disabled addon settings, early-load changes or unparseable INI are surfaced as MFG configuration warnings; they do not block independent FPS/game startup or automatically overwrite settings. No change author is inferred.
- IsIntactAsync remains strict for post-deployment verification. Launch callers use CanLaunch and log every difference; ConfigurationMatches=false must not be presented as MFG enabled.
- Skipped vendor names are always listed as warnings. A receipt with 18 skipped names is not evidence of complete DLSS/Streamline deployment.
- Hash-based cleanup and source ownership rules are unchanged.

Validation: native Windows Core regression suite 105 passed / 0 failed, including four new tests for runtime ForceMultiplier changes without rewrite, payload corruption details, missing payload without recreation, and extra runtime keys/skipped-material reporting. Log: integrity-diagnostics-tests.log.
