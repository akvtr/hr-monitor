# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

These instructions apply to the entire repository. Keep this file in English.

`README.en.md` / `README.md` are the detailed behavior specification (thresholds, formulas, CSV schema, retention, sleep metrics).

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

Other commands:

```powershell
# Re-run the already-built tests without recompiling
.\artifacts\HeartRateMonitor.Tests.exe

# Rendering checks across languages and themes; writes PNGs and smoke-checks.txt to the data directory
.\artifacts\HeartRateMonitor.exe --data-dir .\artifacts\logs\verification\render --smoke-test

# Also build and run the Bluetooth diagnostic (touches the real adapter)
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test -Diagnostic
```

- UI verification code lives in `HeartRateMonitor/MainWindow.SmokeTest.cs`, other `*SmokeTest.cs` files, and `tests/ui-smoke.ps1`.
- Launch manually with `.\artifacts\HeartRateMonitor.exe`. For isolated rendering checks, pass `--data-dir <isolated-directory> --smoke-test`.
- Use temporary or isolated test data, never the normal user database.
- Documentation-only changes do not require builds or UI automation. Check referenced paths, commands, and consistency with the implementation instead.
- Review the diff and run `git diff --check` before committing. Report completed verification and any physical-sensor or VRChat verification limitations accurately.
- **No single-test filter exists.** `HeartRateMonitor.Tests.exe` takes no arguments and `Tests.Main` calls every suite in sequence. To focus on one suite, temporarily comment out the other calls in `tests/Tests.cs` and restore them before committing.
- The first failed `Check` throws and ends the run, so later suites do not execute. Failures print the temporary artifacts folder.
- Exit a running copy from the tray before building; a running `artifacts/HeartRateMonitor.exe` cannot be replaced. Instances are keyed per data directory, so a `--data-dir` instance can run beside the normal one, but it still locks the EXE.

### Testing Seams

Tests run without hardware. They exercise logic through injected delegates and internal constructors (for example the `AutoConnector` delay, the `RecordingEngine` retention interval, and the `ReceptionTimer` timestamp source) and use a per-run temporary folder. Prefer adding a seam of this kind over waiting on real time or real devices.

## Build Model

There is no `.csproj` or NuGet. `build.ps1` invokes Roslyn directly, so these things differ from a typical .NET project:

- Every `HeartRateMonitor/**/*.cs` file is compiled automatically. New assembly references and embedded resources must be added to `build.ps1`.
- WPF XAML is not compiled, and windows have no markup. The UI is built in C# (`MainWindow.BuildUi` and the `MainWindow.*` partials). `Theme.xaml` is embedded and loaded at runtime with `XamlReader`; `AppIcon.xaml` is consumed at build time by `tools/generate-app-icon.ps1`.
- Tests compile from `tests/Tests*.cs` and reference the built EXE, reaching internals through `InternalsVisibleTo("HeartRateMonitor.Tests")`. **A new `Test...` method only runs once it is called from `Tests.Main`**; nothing discovers tests automatically.
- `*SmokeTest.cs` partials ship inside the main EXE and are activated by `--smoke-test`. `tests/ui-smoke.ps1` loads the EXE by reflection and uses `Store` and `Reading` to seed data, so those types must stay public.

## Build Artifacts

- Always build current development source into `artifacts/HeartRateMonitor.exe`, overwriting the previous development build.
- Keep development outputs directly under `artifacts/`. Do not create `artifacts/dev-*`, other per-build directories, or separately retained intermediate builds.
- Store logs and verification files in `artifacts/logs/`, and repository maintenance backups in `artifacts/backups/`.
- Save finalized version snapshots in `artifacts/versions/version-X.Y.Z/` only when explicitly instructed by the user. Never create them automatically after implementation, builds, or tests.
- Reserve existing `artifacts/versions/version-0.0.0/` for unversioned legacy artifacts.
- Generated files are not source. Reorganizing them does not change product versions.

## Architecture

`MainWindow` is the composition root: it owns every long-lived component and wires them together with events (`MainWindow.Events.cs`).

### Reading pipeline

```
BluetoothMonitor.Received -> RecordingEngine.Accept -> bounded queue -> worker: Store.Add, then OSC send
                                                                     -> Processed event -> MainWindow (via Ui)
```

- `RecordingEngine` runs one worker task that serializes readings, one-shot OSC tests, retention pruning, and the OSC silence timeout. Add work of this kind as a new `WorkKind` rather than a new thread.
- A storage failure does not stop the OSC send, and observer exceptions are isolated in `NotifyObservers`.
- Engine and monitor events fire off the UI thread. Handlers must marshal through `MainWindow.Ui(...)`, which also drops work once closing has begun.
- Silence is measured by the monotonic `ReceptionTimer`, never by wall-clock time. Stored timestamps are UTC Unix milliseconds.

### Connection

- `AutoConnector` holds the discovery, candidate, and retry policy over the `IHeartRateDiscovery` interface so it can be tested without WinRT. `BluetoothMonitor` implements that interface; `GattAccess` handles GATT.
- `ConnectionUpdate.Classify` picks user advice by matching exception **message text**, including Japanese fragments (`タイムアウト`, `アダプター`, `20秒`) and WinRT status names. Changing those messages in the connection code silently changes the advice shown.

### Persistence

- `Store` is a partial class over hand-written `winsqlite3.dll` P/Invoke (`Store.Native.cs`). There is no migration framework: the constructor applies idempotent `CREATE ... IF NOT EXISTS` schema statements on every open, so schema changes must be safe against existing user databases.
- A `Store` is not shared across owners. The engine worker and `OyasumiSleepMonitor` each hold their own connection, and `HistoryRepository` opens one per operation so background reads never share a UI-owned `Store`. Use `ReadSnapshot` when several queries must see one consistent view.
- "Valid" readings mean `bpm > 0` and contact not explicitly false (`ValidReadingCondition`; `MainWindow.Received` and `RecordingEngine.SendReading` apply the same rule in C#). All readings are stored and exported to CSV, but statistics, charts, and OSC use only valid ones.
- Time ranges are half-open `[from, to)` throughout.

### Settings

- `Settings` is a mutable POCO serialized by `JavaScriptSerializer`. Fields absent from older files keep their property initializers, so **the initializer is the migration default**.
- Pass `Copy()` snapshots across threads. The apply path is `MainWindow.Settings.cs`: validate, save (`AtomicFile`), then `engine.Configure` and `sleepMonitor.Configure`, then `BuildUi()`.
- A new field usually needs a `Validate` rule and, if it affects timeout output, a comparison in `RecordingEngine.CheckOscTimeout` so the fallback re-arms.
- `settingsLoadFailed` puts the app in a safe mode (OSC disabled, automatic saves suspended) that preserves the original file and its backup. New code that saves settings outside the explicit Save action must honor it.

### Sleep integration

`OyasumiSleepMonitor` polls OyasumiVR over OSCQuery on its own background task and records intervals through `SleepIntervalTracker`. Discovery, the state reader, and the interval are constructor-injected for tests. Any failure or configuration change closes the open interval, because unobserved time must never be recorded as sleep. Heart-rate readings and sleep labels are independent data.

### UI conventions

- Text is inline at the call site as `Words.T("日本語", "English")`; there are no resource files. Changing language rebuilds the UI with `BuildUi()`, so view state that must survive cannot live only in controls. Some connection status strings written to the log are Japanese only.
- Reference colors with `SetResourceReference` and keys from `Theme.xaml`. `HeartChart` and `SleepRhythmChart` draw in `OnRender`, so they must be invalidated on theme change; add any new custom-drawn chart to `MainWindow.ApplyThemeTransition`.

### Shutdown

There are two shutdown paths: `MainWindow.OnClosing` (async) and `MainWindow.EndSession` (blocking, for Windows session end). New background disk work must be tracked as a `Task` and awaited in **both**, as `historyExport` and `sleepLabelWrite` are, so that exit does not abandon it.

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

## Local Files

`TODO.md` is a gitignored, Japanese, local feature backlog; consult it when asked what to work on next. `.agents/`, `.dotnet-home/`, and `artifacts/` are also gitignored.
