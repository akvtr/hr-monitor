# Heart Rate Monitor 0.19.7

A Windows desktop app that receives standard BLE heart-rate notifications, stores readings in SQLite, and displays live and historical charts. It supports VRChat OSC output, CSV export, Japanese/English UI, Light/Dark/System themes, and tray residency. No external account is required.

[日本語](README.md) · [Detailed documentation (Japanese)](docs/README.md)

## Requirements and first run

Use 64-bit Windows 10/11 with .NET Framework 4.8 and a Bluetooth LE adapter. Sensors must expose Heart Rate Service (`0x180D`) and Heart Rate Measurement notifications (`0x2A37`), such as the COOSPO HW706. Each physical device requires compatibility verification.

Extract the ZIP and run `HeartRateMonitor.exe`; after a source build, run `artifacts/HeartRateMonitor.exe`. Keep `HeartRateMonitor.exe.config` beside it.

1. Enable Bluetooth, wear the sensor, and switch it on.
2. Discovery starts automatically. The previously verified successful device is tried first, even if absent from discovery, followed by devices advertising or registered in Windows with the standard heart-rate service. No manufacturer or model is favored; a name alone does not qualify a device.
3. View incoming readings on **Live**. Samples are saved automatically. Use **History** to explore recordings and export the displayed range.
4. **Stop** cancels discovery, connection, and automatic reconnection until **Connect** resumes them. **Details** contains manual selection, Scan, Reconnect, and Copy log.
5. Closing keeps recording and OSC running in the tray by default. Double-click the icon, choose **Open window**, or run the EXE again to restore it. Choose **Exit** from the tray to drain the recording queue and quit.

The initial UI is Japanese. Choose **設定 → 表示言語 → English → 保存して適用** to switch.

## Charts, history, and CSV

Charts have no received-reading tables. Summaries and the dashed mean line use valid samples in the displayed period, excluding zero bpm and explicit lack of contact. Hover to inspect nearby samples or aggregated buckets.

- Presets and a slider select one minute to one hour on Live, or one minute to all retained data on History. History starts at ten minutes. Live uses readings retained in memory during this launch; it does not reload past records.
- Use `Ctrl`+wheel to zoom. History zooms around the cursor; Live keeps the newest time at the right edge.
- Drag the History overview selection to pan, or its edges to resize. Click outside to recenter. Tab between move/start/end handles; arrows adjust by one minute, Shift+arrows by ten minutes, and Home/End reach the bounds. Previous/Next move by one window; **Latest recordings** refreshes and moves to the newest data.
- Drag horizontally in a main chart to analyze duration, mean, minimum, maximum, and count. Click, Escape, or right-click to clear; changing the visible range also clears it. Statistics use original valid samples, including the start and excluding the end.
- **Recording rhythm** covers 365 local days including today. Shading represents valid sample count: 0, 1–599, 600–3,599, 3,600–10,799, or 10,800+. It does not measure wear duration or heart-rate intensity. Hover for counts; click a day, or Tab into the calendar and use arrows and Enter. Older retained data remains accessible through the overview.

CSV exports **the finalized displayed range**, independently of analysis selection. It includes original readings, including zero bpm and no-contact samples, in timestamp order. Encoding is UTF-8 with BOM. Columns are `utc_time,local_time,bpm,contact,rr_ms,energy_kj,device,session`. The interval includes the start and excludes the end. Contact is `1`, `0`, or empty when unknown. There is no CSV import.

## Settings and OSC

Choose a category on the left of Settings to replace the form on the right. Each category fits without scrolling. Categories cover appearance/language, connection/tray behavior, retention, OSC destination, value conversion, receive timeout, and transmission/testing. Use **Save & apply** to persist edits. Switching categories or visiting Live and History preserves unsaved inputs; saving keeps the current category selected. The toolbar sun/moon button switches Light/Dark and saves immediately; select System in Settings to follow Windows again.

OSC is enabled by default, targeting UDP `127.0.0.1:9000`:

- Int32: `/avatar/parameters/VRCOSC/Heartrate/Value`, clamped to 0–255 bpm.
- Float32: `/avatar/parameters/HRPercent`, calculated as `clamp((bpm - minimum) / (maximum - minimum), 0, 1)`. Default bounds are 0–200 bpm.

Configure matching avatar parameters and enable OSC in VRChat. Destination IP, port, names, and bounds are configurable. Hostnames are not accepted. Names without a leading slash receive `/avatar/parameters/`; full addresses must also be under that prefix. Use distinct ASCII names without spaces or wildcards. Ports must be 1–65535; bounds must satisfy `0 <= minimum < maximum <= 65535`.

The transmission section shows destination, send time, values, and errors. **Send test** sends both parameters once using saved settings and an entered heart rate (initially 80, range 1–65535); it creates no stored reading. OSC must be enabled and saved first. A subsequent valid live sample replaces test values. UDP success means the OS accepted the datagrams; verify reception and application in VRChat's OSC debug display.

Zero bpm and explicit lack of contact are stored but not sent as live values. Settings offers two disconnection / receive-timeout modes:

- **Keep last value**: missing notifications send no replacement, leaving the receiver's last value unchanged.
- **Send both values as zero on timeout** (default): after the configured interval (1–3600 seconds, default 5), send Int `0` and Float `0.0` once. The interval starts at app startup or the last notification, including zero-bpm/no-contact notifications, and also applies while manually stopped. Timeout transmissions are identified in the transmission status and create no stored readings. Valid measurements resume normal output automatically.

Older settings without this option also use the default. Explicitly saved modes and intervals are preserved.

Use **Save & apply** to apply the mode and interval. No zeros are sent while OSC is disabled or after the app exits. Timing uses monotonic elapsed time. If the interval has already expired when a setting is applied, zeros are sent after applying it. One-shot tests do not count as received notifications; timeout zeros can replace test values. UDP success does not guarantee receipt or application.

The Live number clears after five seconds without a fresh sample. Twenty seconds without notifications ends the connection; automatic reconnection retries when enabled. Discovery listens for advertisements for about eight seconds, with five seconds between retry cycles. Permission denial stops automatic retries.

## Storage and retention

The normal directory is `%LOCALAPPDATA%\HeartRateMonitor\`:

- `heartrate.db`: UTC timestamps, original bpm, device/session, optional contact, RR intervals, and energy.
- `settings.json`: preferences and previous verified device. `settings.json.bak` retains the previous file when replaced.
- `connection.log`: up to 250 diagnostic lines from this launch. Copy logs before restarting; they may contain Japanese and Windows error text.

Retention defaults to **Unlimited**, including migration from settings without a retention field. Settings offers **Unlimited, 1, 7, 30, 90, or 365 days**. The active policy appears on Live and History. Finite retention deletes readings older than the selected number of 24-hour days relative to the current time: at startup, after policy changes, and during checks every minute while running. Existing readings are included. **Deletion is irreversible: deleted readings cannot be recovered or exported. Returning to Unlimited does not restore them.** Export needed readings before shortening retention. Unlimited storage grows over time; monitor free space.

Exit from the tray before backing up the entire data directory. To restore, exit, preserve the current folder separately, and replace it with the backup. The restored retention policy still applies at startup. There is no built-in backup/restore UI. SQLite uses WAL and FULL synchronization. Normal exit drains the queue; a crash or power loss can lose queued readings. Deletion does not guarantee immediate reduction of file size.

## Troubleshooting and limitations

If a sensor is missing, check power, battery, contact, distance, Windows Bluetooth, and connections from other apps or phones. Use Details to select sensors whose service was not discovered. Connection and waiting for the first notification are distinct states. Copy logs before restarting.

For OSC, check saved destination, names/types, VRChat settings, and its debug display. For storage/deletion errors, check free space and permissions. Failed settings loading displays a warning and disables OSC; correct settings before enabling it again.

The WPF window supports Light/Dark/System. The current standard WinForms tray menu does not explicitly follow the app palette. Sensor compatibility, sleep recovery, sustained recording, and delivery into VRChat require hardware verification; automated tests do not establish those results.

## Build and verification

The app uses WPF on .NET Framework 4.8, Windows WinRT BLE APIs, and OS-provided `winsqlite3.dll`. Install the .NET SDK (Roslyn) and Windows SDK metadata. No NuGet restore is used. Always build with tests:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test
```

The default build writes a snapshot under `artifacts/version-X.Y.Z/`, then publishes the latest EXE, configuration, and ZIP directly under `artifacts/` after requested checks pass. The ZIP includes the EXE, configuration, and both READMEs; docs remain in the source repository. Keep verification output in the matching version folder. `version-0.0.0` is for unversioned legacy artifacts. Exit before replacing a running EXE.

Additional checks on an interactive Windows desktop:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\ui-smoke.ps1
.\artifacts\HeartRateMonitor.exe --data-dir .\artifacts\version-0.19.7\verification\render --smoke-test
```

The UI script seeds a temporary database labeled `ISOLATED UI TEST`. Rendering smoke mode checks Japanese/English and Light/Dark/System pages, captures PNGs, and writes `smoke-checks.txt`. It saves test settings: always use an isolated `--data-dir`. These are procedures, not a claim they were run during this documentation update.

Tests cover parsing, connection control, OSC and test isolation, settings, SQLite/CSV, retention, history, and analysis. See the [development guide (Japanese)](docs/development.md) for source responsibilities and hardware checks.

Beko.BluetoothHeartRateOSC is a behavioral reference; this implementation is independent.
