# Embedded Dynamic addon planning tests

Run `dotnet run --project tests/EmbeddedDynamicAddon.Tests -c Release` on Windows.

The project compiles the production `EmbeddedDynamicAddon` helper directly and supplies a temporary-directory `AppPaths` adapter. Its embedded resource is plain inert text, never a real addon. Tests verify content fingerprints, cache reuse and repair, cancellation, destination containment, duplicate rejection, capture of existing target hashes, and absence of game-directory writes during preview.

These checks exercise resource extraction and plan creation only. They do not validate the companion binary, deployment execution, protocol-1 package compatibility, or in-game behavior.
