# v1.1.1 version-only update

Base: bed0bba, isolated branch agent/materials-v1.1.1. Earlier uncommitted RC drafts were preserved on agent/materials in commit6769194 before switching branches.

Only self-owned version strings were updated in Directory.Build.props and src: Version/InformationalVersion1.1.1, AssemblyVersion/FileVersion/app.manifest1.1.1.0, visible window/banner/trayv1.1.1, startup log1.1.1. No layout, functionality, third-party component versions, README, workflows or updater changed.

Parent reports the new user-approved addon at input/addon-update-20260923/renodx-mfgunlock.addon64,903680bytes,SHA2565D9184D5D690FF5CC1771C07A69331E4A596F8A94FDE5867A5AFC713650F800C. This version-only agent did not modify or test that binary. Parent handles addon packaging and release integration.

No builds or tests were run, as requested. A text search confirmed old targeted version strings no longer occur in Directory.Build.props/src; this is a version-string audit only.
