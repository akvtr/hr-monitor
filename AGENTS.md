# Project instructions

- Build a Windows desktop heart-rate monitor for standard BLE Heart Rate Service sensors, including the COOSPO HW706.
- Receive Bluetooth Low Energy Heart Rate Service notifications.
- Persist timestamped readings in SQLite and provide live and historical charts and CSV export. Do not display received-reading tables on either page.
- Send VRChat OSC parameters: Int `VRCOSC/Heartrate/Value` and Float `HRPercent` by default. Parameter names, destination, and percent conversion must be configurable.
- Keep the Japanese UI clear, support reconnection, and report connection/storage/OSC errors.
- Never fabricate readings in normal operation. Any demo data must be explicitly labeled and isolated.
- Keep Bluetooth, persistence, OSC, and UI responsibilities separate. Validate protocol parsing, conversion, and persistence with meaningful tests.
- Document setup, operation, configuration, and hardware-dependent verification limitations.
- Use WPF on .NET Framework 4.8, Windows WinRT BLE APIs, and the OS-provided winsqlite3.dll. Build without NuGet via `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test`; always run this test build whenever building.
- The build requires .NET SDK (Roslyn) and Windows SDK metadata. Runtime artifacts live under `artifacts/` and are not source files.
- Keep the latest verified build and its ZIP directly under `artifacts/`; retain release snapshots under `artifacts/version-X.Y.Z/` and put verification files inside the corresponding version folder. Default builds must maintain this layout. Use `version-0.0.0/` only for legacy artifacts without an assigned product version. Organizing build artifacts alone does not change the product version.
- Keep test data in temporary or explicitly isolated data directories. Do not use the user's normal database for verification.
- Automatically discover and connect on startup by default. Prefer the previous verified successful device, even when absent from discovery, then devices advertising or registered with the standard Heart Rate Service. Do not favor a manufacturer or model; names alone do not qualify a device for automatic connection. Explicit stop must cancel discovery/reconnection until the user resumes.
- Maintain Light, Dark, and System themes for all app-owned controls; store appearance preferences. Use shared theme resources instead of hardcoded UI colors.
- Avoid pill-shaped badges and stretched status containers. Align the connection bar's button rows using shared grid columns.
- Beko.BluetoothHeartRateOSC is a behavioral reference, not a source to copy. Preserve the independent implementation.
- Provide Japanese and English UI. Keep appearance, language, and persistent options exclusively in Settings.
- Support tray residency with an explicit Exit action, truthful OSC transmission status and isolated one-shot tests, and a history time-window slider starting at a ten-minute scale.

## Versioning

- Use Semantic Versioning as "MAJOR.MINOR.PATCH"; the current product version is "0.19.7".
- Before "1.0.0", increment MINOR for backward-compatible features and PATCH for backward-compatible fixes. After "1.0.0", increment MAJOR for breaking changes, MINOR for compatible features, and PATCH for compatible fixes.
- Use "-beta.N" or "-rc.N" for prereleases. Keep product and informational versions as "X.Y.Z" (including any prerelease suffix); use numeric "X.Y.Z.0" for assembly/file versions and the "app.manifest" identity.
- When changing the version, update "AppVersion" in "HeartRateMonitor/AppVersion.cs", the assembly identity in "HeartRateMonitor/app.manifest", the visible window title, and both README version references together.

- Retain readings indefinitely by default, including when migrating settings without a retention preference. Keep retention controls exclusively in Settings: Unlimited, 1, 7, 30, 90, or 365 days. When finite retention is selected, delete expired rows on startup, after applying a changed policy, and at least once per minute during operation. Disclose automatic deletion and its irreversibility in Japanese and English UI and documentation; show the active retention policy on Live and History.
