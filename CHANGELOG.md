# Changelog

## [2.2.1] - 2026-10-10
Shipped with OmniDebugger 2.2.1.

### Changed
- The generator moved out of the OmniDebugger repository into its own. The generator itself is unchanged.
- `build.sh` takes the OmniDebugger package to copy the DLL into, as an argument or through `OMNIDEBUGGER_PATH`;
  without one it only builds.
- The tests compile against OmniDebugger from the `external/OmniDebugger` submodule, or from `OMNIDEBUGGER_PATH`.

### Added
- GitHub Actions: *Tests* runs the tests on every push and pull request to `master` and `develop`. *Release* checks a
  `release/X.Y.Z` or `hotfix/X.Y.Z` pull request into `master` (version, CHANGELOG section, description) and, once
  it is merged, tags the merge commit `vX.Y.Z` and publishes the release with the pull request's description as its
  notes and the freshly built DLL, checked to carry that version, attached.

## [2.2.0] - 2026-10-04
Shipped with OmniDebugger 2.2.0.

### Added
- `[DebugOptions(memberName)]` on a command property or parameter: the generated command reads its options from that
  member — a field, property or parameterless method yielding `IEnumerable<T>`, or a `Func` returning one. Checked at
  compile time: `OMNI014` (member not found), `OMNI015` (member cannot be read), `OMNI016` (options do not fit the
  argument).
- The DLL carries its own version, set by `<Version>` in its project, and stamps it into the `[GeneratedCode]`
  attribute of the code it writes.

## [2.0.0] - 2026-09-30
Shipped with OmniDebugger 2.0.0.

### Added
- An incremental generator that turns `[DebugCommand]` members into registration code at compile time: direct calls,
  no reflection, nothing for the IL2CPP linker to strip. Reads `[DebugTags]`, `[DebugIcon]` and `[DebugRange]`.
- Public and internal members, and commands declared on base classes.
- Compile errors for members that cannot be commands: `OMNI001`–`OMNI013`.
