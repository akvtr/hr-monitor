# Repository Guidelines

## Project Structure & Architecture

`HeartRateMonitor/` contains the C# WPF application: `BluetoothMonitor`, `GattAccess`, and `AutoConnector` handle BLE; `RecordingEngine` coordinates recording; `Store` uses Windows `winsqlite3.dll`; `OscSender` sends VRChat parameters. Keep these responsibilities separate from `MainWindow.*` and chart controls. Shared colors live in `Theme.xaml`; icon assets/generation live in `AppIcon.xaml` and `tools/`. `tests/` contains assertions, UI automation, and Bluetooth diagnostics. Maintain Japanese `README.md` and English `README.en.md` together.

## Build, Test & Run

Requires x64 Windows 10/11, .NET Framework 4.8, .NET SDK Roslyn, and Windows SDK metadata. No NuGet dependencies.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\ui-smoke.ps1
.\artifacts\HeartRateMonitor.exe
```

Always include `-Test` when building. Run UI automation on an interactive desktop for UI changes. For isolated rendering checks, launch with `--data-dir <isolated-directory> --smoke-test`.

Always build the current development source into `artifacts/HeartRateMonitor.exe`, overwriting the previous development build. Do not create `artifacts/dev-*` or other per-build directories, and do not retain separate intermediate builds.

Keep current development build outputs directly under `artifacts/`. Store logs and verification files in `artifacts/logs/`, and repository maintenance backups in `artifacts/backups/`. Store finalized version snapshots in `artifacts/versions/version-X.Y.Z/` only when explicitly instructed by the user. Do not create version snapshot directories automatically after implementation, builds, or tests. Reserve existing `artifacts/versions/version-0.0.0/` for unversioned legacy artifacts. Generated files are not source; reorganizing them does not change product versions.

## Coding Style & Testing

Follow existing C# style: four-space indentation, PascalCase types/methods/properties, camelCase locals/private fields, and matching filenames. No formatter/linter is configured. Extend the dependency-free `tests/Tests.cs` runner using `Test…` methods and descriptive `Check`/`Throws` assertions. Validate parsing, conversion, persistence, reconnection, retention, and changed behavior; no coverage percentage is configured. Use temporary or isolated data, never the normal user database. Document physical-sensor and VRChat verification limitations.

## Product & Data Requirements

- Support standard BLE Heart Rate Service sensors, including HW706, without manufacturer preference. Auto-connect on startup: try the previously verified sensor even if undiscovered, then service-qualified candidates. Names alone do not qualify. Stop suspends discovery/reconnection until resumed.
- Persist timestamped readings; provide Live/History charts and CSV, never received-reading tables. Never fabricate normal readings; label and isolate demo/test data.
- Default OSC: Int `VRCOSC/Heartrate/Value`, Float `HRPercent`. Keep destination, names, and conversion configurable. Report connection/storage/OSC errors truthfully; UDP success does not prove delivery. One-shot tests must not create readings.
- Support Japanese/English, Light/Dark/System themes, shared theme resources, and saved preferences. Keep persistent options in Settings; retain the toolbar theme shortcut. Avoid pill badges and stretched status containers; align connection buttons with shared grid columns. Preserve tray residency with explicit Exit, and History's initial ten-minute window with a one-minute slider minimum.
- Retain readings indefinitely by default, including migrated settings. Settings alone offers Unlimited/1/7/30/90/365 days. Apply finite cleanup at startup, policy changes, and at least every minute. Disclose irreversible deletion in both languages and documentation; show active retention on Live/History.
- Beko.BluetoothHeartRateOSC is a behavioral reference, not source to copy.

## Versioning, Commits & Pull Requests

Current version: `0.21.0`. Do not finalize or bump the product version until the user explicitly instructs you to do so. Implementation, builds, tests, and verification alone do not authorize version finalization or creation of `artifacts/versions/version-X.Y.Z/`. When a version change is explicitly requested, use SemVer: before 1.0, MINOR for compatible features and PATCH for fixes; afterward, MAJOR for breaking changes, MINOR for compatible features, and PATCH for fixes. Allow `-beta.N`/`-rc.N`. Update `AppVersion.cs`, manifest identity, visible title, and both README version references together. Product/informational versions use `X.Y.Z` with any prerelease suffix; assembly/file/manifest versions use numeric `X.Y.Z.0`.

Commit frequently as work progresses: one logical fix or one feature addition per commit. Complete and verify each logical change, then commit it before starting an unrelated change; do not wait for an entire multi-change request to finish. Keep implementation, relevant tests, and documentation for the same change together so each commit is coherent and independently reviewable. Stage only files or hunks belonging to that change, and leave unrelated user changes untouched. Follow Conventional Commits (`fix: ...`, `feat: ...`, `docs: ...`, etc.) with concise imperative subjects. Local commits do not authorize pushing, version finalization, or release creation.

PRs should explain behavior, link relevant issues, report verification and hardware limits, and include screenshots for visual changes.

- Write GitHub release notes using only change-description bullet lists under applicable top-level headings: `Added`, `Changed`, `Fixed`, and `Removed` (or `Deleted`). Omit categories with no entries; start each bullet with the corresponding verb (for example, `- Added ...`). Do not include test results or counts, verification notes, checksums (SHA), installation instructions, or other supplementary sections or prose.
