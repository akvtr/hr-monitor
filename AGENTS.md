# AGENTS.md

These instructions apply to the entire repository. Keep this file in English.

## Working Process

- Before editing, inspect the relevant implementation, tests, documentation, and `git status`. Preserve existing user changes and include only your own changes in each commit.
- Complete and verify each independent change, then commit it before starting unrelated work. Keep one fix, addition, or removal per commit, together with its supporting tests and documentation. Do not combine independent changes just because they share a commit type.
- Update Japanese `README.md` and English `README.en.md` together when changing user-facing behavior or instructions.
- Implementation, builds, tests, and local commits do not authorize pushing, version finalization, or release creation. Follow the user's instructions for each delivery step.

## Project Structure and Responsibilities

- `HeartRateMonitor/`: C# WPF application.
  - `BluetoothMonitor`, `GattAccess`, and `AutoConnector`: BLE communication, discovery, and reconnection.
  - `RecordingEngine`: recording coordination.
  - `Store` and its partial files: persistence using Windows `winsqlite3.dll`.
  - `OscSender`: VRChat OSC parameter transmission.
  - `MainWindow.*` and chart controls: UI. Keep communication, recording, and persistence responsibilities in their respective components.
  - `Theme.xaml`: shared colors and theme resources.
  - `AppIcon.xaml`: application icon definition.
- `tests/`: dependency-free tests, UI automation, and Bluetooth diagnostics.
- `tools/`: development scripts, including icon generation.

## Implementation and Verification

### Environment and Coding Style

- Target x64 Windows 10/11 and .NET Framework 4.8. Building requires .NET SDK Roslyn and Windows SDK metadata. There are no NuGet dependencies.
- Follow existing C# style: four-space indentation, PascalCase types/methods/properties, camelCase locals/private fields, and filenames matching their types or responsibilities.
- No formatter or linter is configured. Avoid unrelated formatting changes.
- Extend the existing runner in `tests/Tests.cs` and `tests/Tests*.cs` with `Test...` methods and descriptive `Check` / `Throws` assertions.
- Verify changed behavior, including parsing, conversion, persistence, reconnection, and retention where relevant. No coverage percentage is required.

### Verification Commands

Run from the repository root. Always include `-Test` when building.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test
```

For UI changes, also run UI automation on an interactive desktop.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\ui-smoke.ps1
```

- UI verification code lives in `HeartRateMonitor/MainWindow.SmokeTest.cs`, other `*SmokeTest.cs` files, and `tests/ui-smoke.ps1`.
- Launch manually with `.\artifacts\HeartRateMonitor.exe`. For isolated rendering checks, pass `--data-dir <isolated-directory> --smoke-test`.
- Use temporary or isolated test data, never the normal user database.
- Documentation-only changes do not require builds or UI automation. Check referenced paths, commands, and consistency with the implementation instead.
- Review the diff and run `git diff --check` before committing. Report completed verification and any physical-sensor or VRChat verification limitations accurately.

## Build Artifacts

- Always build current development source into `artifacts/HeartRateMonitor.exe`, overwriting the previous development build.
- Keep development outputs directly under `artifacts/`. Do not create `artifacts/dev-*`, other per-build directories, or separately retained intermediate builds.
- Store logs and verification files in `artifacts/logs/`, and repository maintenance backups in `artifacts/backups/`.
- Save finalized version snapshots in `artifacts/versions/version-X.Y.Z/` only when explicitly instructed by the user. Never create them automatically after implementation, builds, or tests.
- Reserve existing `artifacts/versions/version-0.0.0/` for unversioned legacy artifacts.
- Generated files are not source. Reorganizing them does not change product versions.

## Product Requirements

### Sensors, Recording, and OSC

- Support standard BLE Heart Rate Service sensors, including HW706, without manufacturer preference.
- Auto-connect on startup: first try the previously verified sensor even if undiscovered, then service-qualified candidates. Names alone do not qualify a sensor.
- Stop suspends discovery and reconnection until resumed.
- Persist timestamped readings. Provide Live/History charts and CSV export, never received-reading tables.
- Never fabricate normal readings. Label and isolate demo/test data. One-shot transmission tests must not create readings.
- Default OSC parameters are Int `VRCOSC/Heartrate/Value` and Float `HRPercent`. Keep destination, parameter names, and conversion configurable.
- Report connection, storage, and OSC errors truthfully. UDP send success does not prove delivery.
- Use `Beko.BluetoothHeartRateOSC` as a behavioral reference only; do not copy its source.

### UI and Settings

- Preserve Japanese/English support, Light/Dark/System themes, shared theme resources, and saved preferences.
- Keep persistent options in Settings and retain the toolbar theme shortcut.
- Avoid pill badges and stretched status containers. Align connection buttons with shared grid columns.
- Preserve tray residency with explicit Exit.
- Preserve History's initial ten-minute window and one-minute slider minimum.

### Data Retention

- Retain readings indefinitely by default, including when migrating existing settings.
- Offer retention choices only in Settings: Unlimited / 1 / 7 / 30 / 90 / 365 days.
- With finite retention enabled, delete expired data at startup, on policy changes, and at least every minute.
- Disclose irreversible deletion in both Japanese and English UI and documentation.
- Show active retention on Live and History.

## Versioning, Commits, and Publishing

### Version Changes

- Read the current product version from `HeartRateMonitor/AppVersion.cs`. Do not finalize or bump it without explicit user instruction.
- When instructed to change the version, follow SemVer: before 1.0, MINOR for compatible features and PATCH for fixes; afterward, MAJOR for breaking changes, MINOR for compatible features, and PATCH for fixes.
- Prerelease suffixes such as `-beta.N` and `-rc.N` are allowed.
- Update `AppVersion.cs`, the identity in `HeartRateMonitor/app.manifest`, the visible title, and both README version references together.
- Product/informational versions use `X.Y.Z` with any prerelease suffix. Assembly/file/manifest versions use numeric `X.Y.Z.0`.

### Commits and Pull Requests

- Follow Conventional Commits: `<type>[optional scope][!]: <description>`. Use concise imperative subjects, such as `fix: handle empty input` or `feat(osc): add destination setting`.
- Mark breaking changes with `!` after the type/scope or a `BREAKING CHANGE:` footer.
- PRs should explain resulting behavior, link relevant issues, report verification and hardware limitations, and include screenshots for visual changes.

### GitHub Release Notes

- Use only change-description bullet lists under applicable top-level headings: `Added`, `Changed`, `Fixed`, and `Removed` (or `Deleted`). Omit empty categories.
- Start each bullet with the corresponding verb, for example `- Added ...`.
- Do not include test results or counts, verification notes, SHA/checksums, installation instructions, or other supplementary sections or prose.
