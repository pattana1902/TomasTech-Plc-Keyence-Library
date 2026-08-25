# Changelog

All notable changes to this project are documented in this file.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project uses [Semantic Versioning](https://semver.org/).

## [1.2.1] - 2026-08-25

### Fixed
- CI workflow failed on every run: the test project lived outside the git repository (a sibling folder, not a subfolder), so it was never committed/pushed and the checked-out repo on CI couldn't find it. Moved the test project to `tests/TomasTech Plc Keyence.Tests/` inside the repo and fixed the `.slnx` and `ProjectReference` paths accordingly.
- CI workflow restored/built/tested via the `.slnx` solution file, which requires a newer SDK than the `8.0.x` band the workflow pinned. Switched to restoring/building/testing individual `.csproj` files directly, which works on any SDK that supports the target framework.
- Bumped `actions/checkout`, `actions/setup-dotnet`, and `actions/upload-artifact` to their latest majors to clear the Node.js 20 deprecation warning.

## [1.2.0] - 2026-08-25

### Fixed
- **`PlcAddress.BaseAddress` no longer corrupts unrecognized device-type prefixes.** Previously it rebuilt the wire address from `WordType.ToString()`, so any prefix not already in the `PlcWordType` enum (e.g. `EM`) silently became the literal device name `"Unknown{offset}"` on the wire — which the PLC then rejected with a bare `"PLC Error: E1"` that gave no indication the real cause was an unrecognized prefix. `BaseAddress` now always reflects the exact prefix the caller typed, so this class of bug cannot recur for any future device-type area, known or not.

### Added
- `PlcWordType` now includes `EM` (Extended Data Memory), `W`, `R`, `CR`, `TN`, `CN`, `T`, `C`, `AT`, `CM`, covering the full set of Keyence KV-series memory areas instead of just the original 7.
- Full unit test suite: pure-parsing coverage for `PlcAddress.Parse`, plus loopback-socket integration tests for `KeyenceTcpClient` (read/write words, bool, Int32/Float word-order handling, ASCII string encode/decode, error responses, and a regression test proving the EM fix reaches the wire correctly).

### Changed
- Fixed a nullable-reference warning on the `netstandard2.0` target build.

## [1.1.0] - 2026-03-05

- Read multiple address support (`RDS`) and related updates. (No changelog was kept prior to 1.2.0 — see git history for details.)

## [1.0.0]

- Initial release: `KeyenceTcpClient` with Keyence Upper Link (ASCII) protocol support — word/bool/Int32/Float/ASCII string read and write, `PlcAddress` parsing with `.U/.S/.D/.H/.L/.B` suffixes.
