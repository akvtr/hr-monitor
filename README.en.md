# Heart Rate Monitor 0.22.0

A Windows desktop app that receives standard BLE heart-rate notifications, stores readings in SQLite, and displays live and historical charts. It supports VRChat OSC output, CSV export, Japanese/English UI, Light/Dark/System themes, and tray residency. No external account is required.

[日本語](README.md)

## Requirements and first run

Use 64-bit Windows 10/11 with .NET Framework 4.8 and a Bluetooth LE adapter. Sensors must expose Heart Rate Service (`0x180D`) and Heart Rate Measurement notifications (`0x2A37`), such as the COOSPO HW706. Each physical device requires compatibility verification.

Extract the ZIP and run `HeartRateMonitor.exe`; after a source build, run `artifacts/HeartRateMonitor.exe`. Keep `HeartRateMonitor.exe.config` beside it.

1. Enable Bluetooth, wear the sensor, and switch it on.
2. Discovery starts automatically. The previously verified successful device is tried first, even if absent from discovery, followed by devices advertising or registered in Windows with the standard heart-rate service. No manufacturer or model is favored; a name alone does not qualify a device.
3. View incoming readings on **Live**. Samples are saved automatically. Use **History** to explore recordings and export the displayed range.
4. **Stop** cancels discovery, connection, and automatic reconnection until **Connect** resumes them. **Details** contains manual selection, Scan, Reconnect, and Copy log.
5. Closing keeps recording and OSC running in the tray by default. Double-click the icon, choose **Open window**, or run the EXE again to restore it. Choose **Exit** from the tray to drain the recording queue and quit.

The initial UI is Japanese. Choose **設定 → 一般 → 表示言語 → English → 保存して適用** to switch.

If automatic discovery fails, the app can still attempt a direct connection using a previously verified device identity. After disconnection, it keeps targeting the same sensor. Discovery permission denial or cancellation through Stop prevents this fallback; an unverified saved address never qualifies.

## Charts, history, and CSV

Charts have no received-reading tables. Summaries and the dashed mean line use valid samples in the displayed period, excluding zero bpm and explicit lack of contact. Hover to inspect nearby samples or aggregated buckets.
When readings are aggregated for a wider view, the chart shows a mean trace with thin minimum-to-maximum whiskers behind it.
The History time axis shows the time and date (`yyyy/MM/dd`) on two lines.

- Presets and a slider select one minute to one hour on Live, or one minute to all retained data on History. History starts at ten minutes. Live uses readings retained in memory during this launch; it does not reload past records.
- Use `Ctrl`+wheel to zoom. History zooms around the cursor; Live keeps the newest time at the right edge.
- Scroll up on the main History chart to move earlier, or down to move later, by 10% of the visible time span per wheel notch. The overview chart has been removed. Previous/Next move by one window; **Latest recordings** refreshes and moves to the newest data.
- Drag horizontally in a main chart to analyze duration, mean, minimum, maximum, and count. Click, Escape, or right-click to clear; changing the visible range also clears it. Statistics use original valid samples, including the start and excluding the end.
- The monthly calendar offers previous/next month and **This month** navigation. Recorded days have a highlight and a **•**; hover for valid sample counts. Select any day, including empty days, to view that entire day. Tab into the calendar and use arrows and Enter. Counts cover only the displayed month.
- At the top of History, the monthly calendar and date/time form share the available width. The jump button spans both input fields below them.
- **Find by date / time** accepts a date (`yyyy/MM/dd` or `yyyy-MM-dd`) and start time (`HH:mm` or `HH:mm:ss`). Choose **Go to date / time** or press Enter to display a range starting there with the current time span, including dates more than a year ago. Times use the PC's local time zone. Nonexistent daylight-saving times show an error; repeated times select the earlier occurrence.

In History, **Export range to CSV** exports the finalized displayed range. **Export interval to CSV** exports the analysis interval selected by dragging on the chart and is enabled only while an interval is selected. Escape, right-click, or changing the displayed range clears the selection. The interval is captured when the save dialog opens. Both actions use the same original readings and columns, including zero bpm and no-contact samples, in timestamp order. Encoding is UTF-8 with BOM. Columns are `utc_time,local_time,bpm,contact,rr_ms,energy_kj,device,session`. `local_time` uses the machine's current local time zone and the Gregorian format `yyyy-MM-dd HH:mm:ss.fff`, independently of OS calendar and display settings. The interval includes the start and excludes the end. Contact is `1`, `0`, or empty when unknown. There is no CSV import.

## Settings and OSC

Choose a category on the left of Settings to replace the form on the right. Each category fits without scrolling. Categories cover General (theme, language, and tray behavior), Heart-rate sensor (automatic connection and reconnection), Data storage, OyasumiVR integration, and OSC transmission. OSC transmission contains Destination & parameters, Values & timeout behavior, and Transmission & test. Use **Save & apply** to persist edits. Switching categories or visiting Live and History preserves unsaved inputs; saving keeps the current category selected. The toolbar sun/moon button switches Light/Dark and saves immediately; select System in Settings to follow Windows again.

OSC is enabled by default, targeting UDP `127.0.0.1:9000`:

- Int32: `/avatar/parameters/VRCOSC/Heartrate/Value`, clamped to 0–255 bpm.
- Float32: `/avatar/parameters/HRPercent`, calculated as `clamp((bpm - minimum) / (maximum - minimum), 0, 1)`. Default bounds are 0–200 bpm.

Configure matching avatar parameters and enable OSC in VRChat. Destination IP, port, names, and bounds are configurable. Hostnames are not accepted. Names without a leading slash receive `/avatar/parameters/`; full addresses must also be under that prefix. Use distinct ASCII names without spaces or wildcards. Ports must be 1–65535; bounds must satisfy `0 <= minimum < maximum <= 65535`.

The transmission section shows destination, send time, values, and errors. **Send test** sends both parameters once using saved settings and an entered heart rate (initially 80, range 1–65535); it creates no stored reading. OSC must be enabled and saved first. A subsequent valid live sample replaces test values. UDP success means the OS accepted the datagrams; verify reception and application in VRChat's OSC debug display.

Zero bpm and explicit lack of contact are stored but not sent as live values. Settings offers two disconnection / receive-timeout modes:

- **Keep last value**: missing notifications send no replacement, leaving the receiver's last value unchanged.
- **Send fallback values on timeout** (default): after the configured interval (1–3600 seconds, default 5), send the configured Int and Float fallbacks once. Set Int to 0–255 and Float to 0.0–1.0 independently; both default to zero. Fallbacks are sent directly without bpm clamping or heart-rate conversion. The interval starts at app startup or the last notification, including zero-bpm/no-contact notifications, and also applies while manually stopped. Timeout transmissions are identified in the transmission status and create no stored readings. Valid measurements resume normal output automatically.

Older settings without this option also use the default. Explicitly saved modes and intervals are preserved.

Use **Save & apply** to apply the mode, interval, and fallback values. Older settings without fallback values continue to send both values as zero, preserving the saved mode and interval. No fallbacks are sent while OSC is disabled or after the app exits. Timing uses monotonic elapsed time. If the interval has already expired when a setting is applied, fallbacks are sent after applying it. Changing fallback values during silence sends the new values once; saving unchanged settings does not resend them. One-shot tests do not count as received notifications; timeout fallbacks can replace test values. UDP success does not guarantee receipt or application.

The Live number clears after five seconds without a fresh sample. Twenty seconds without notifications ends the connection; automatic reconnection retries when enabled. Discovery listens for advertisements for about eight seconds, with five seconds between retry cycles. Permission denial stops automatic retries.

## Storage and retention

The normal directory is `%LOCALAPPDATA%\HeartRateMonitor\`:

- `heartrate.db`: UTC timestamps, original bpm, device/session, optional contact, RR intervals, and energy.
- `settings.json`: preferences and previous verified device. `settings.json.bak` retains the previous file when replaced.
- `connection.log`: up to 250 diagnostic lines from this launch, including connection, recording, and OSC errors and startup settings/retention failures. They may contain Japanese and Windows error text.

Recording and OSC errors reach the log independently of UI updates, including during shutdown. If the log cannot be saved, the in-memory copy retains the notification and write failure. Copy needed logs before exiting; in-memory logs are lost at exit.

Retention defaults to **Unlimited**, including migration from settings without a retention field. Settings offers **Unlimited, 1, 7, 30, 90, or 365 days**. The active policy appears on Live and History. Finite retention deletes readings older than the selected number of 24-hour days relative to the current time: at startup, after policy changes, and during checks every minute while running. Existing readings are included. **Deletion is irreversible: deleted readings cannot be recovered or exported. Returning to Unlimited does not restore them.** Export needed readings before shortening retention. Unlimited storage grows over time; monitor free space.

Exit from the tray before backing up the entire data directory. To restore, exit, preserve the current folder separately, and replace it with the backup. The restored retention policy still applies at startup. There is no built-in backup/restore UI. SQLite uses WAL and FULL synchronization. Normal exit drains the queue; a crash or power loss can lose queued readings. Deletion does not guarantee immediate reduction of file size.

### Automatic OyasumiVR sleep labels

Run OyasumiVR on the same PC and enable its OSC server. Integration is enabled by default under **Settings → OyasumiVR integration**. The default OSCQuery port, `0`, automatically discovers local listeners owned by OyasumiVR. If discovery fails, enter the **OSCQuery (HTTP) port** shown in OyasumiVR and save. This is different from its OSC UDP port. The host is always `127.0.0.1`. VRChat need not be running, and OSC forwarding destinations need no changes.

Every 2 seconds, the app reads the [official sleep-mode address](https://github.com/Raphiiko/OyasumiVR/blob/develop/src-ui/app/services/osc-control/methods/sleep-mode.osc-method.ts), `/OyasumiVR/SleepMode`, through OSCQuery. While enabled, it records **Sleep · OyasumiVR** intervals on the history chart. It only reads OyasumiVR's state. Labels represent sleep mode, not sleep inferred from heart rate.

An interval starts at the first confirmed enabled observation and ends when mode is confirmed disabled. If communication fails, the saved interval stops at the last confirmed observation; unknown time is never filled in as sleep. A polling gap longer than 10 seconds (for example during PC suspension), an app restart, a changed integration endpoint, or disabling integration splits intervals too. Polling resumes automatically after recovery. Connection status appears in Settings and History; errors are also logged.

Sleep labels remain selectable and visible in History even outside heart-rate recording times. Readings, statistics, and sample CSV columns are unchanged. Sleep labels are not included in CSV and are stored in the same database for full-directory backup. Manual note/interval creation, editing, and search have been removed. Existing manual annotation tables are preserved but never displayed or converted into sleep labels.

The **Sleep** sidebar page shows average sleeping heart rate, average sleep duration, and typical sleep hours for 7 days, 30 days, or a custom range of up to 366 days. The overview, sleep rhythm, and selected interval's heart-rate chart fit on one screen without page scrolling. Use the arrows to page the daily timeline seven days at a time; averages always cover the entire selected summary period. Daily horizontal bars show only observed intervals; communication gaps are never filled. Dates identify the day on which the sleep-day boundary begins. The default is noon to the following noon; change it under **Settings → OyasumiVR integration → Sleep day starts at**.

Average sleep duration is the sum of observed time per day divided by the number of days with records. Intervals crossing the selected boundaries count only within the range, and overlaps are counted once. Missing days are omitted instead of counted as zero; ongoing and excluded intervals are omitted from averages. Sleeping heart rate is the mean of valid recorded samples within included intervals, without weighting by sampling interval. Missing heart-rate data displays “—”. Separate counts show how many days contributed sleep and heart-rate data. Typical sleep hours are the median start and end times of each day's longest continuous record. Use the timeline alongside these values when gaps split a night's records or when naps are present.

Click an interval in the upper sleep timeline to display its heart-rate chart below. You can also focus the timeline with Tab and select intervals using arrow keys, Home, or End. The chart caption shows the interval's start, end, and average heart rate. Intervals without measurements show an empty chart and “No heart-rate data”. There is no record list, manual deletion, or exclude/include action. Existing exclusion states are preserved.

With finite retention, sleep labels ending at or before the cutoff are permanently deleted too. Intervals crossing the cutoff keep their original timestamps. **Deletion is irreversible.**

Shutdown waits for accepted readings to be saved and for any CSV export in progress to finish. Large exports can therefore delay exit.

## Troubleshooting and limitations

If a sensor is missing, check power, battery, contact, distance, Windows Bluetooth, and connections from other apps or phones. Use Details to select sensors whose service was not discovered. Connection and waiting for the first notification are distinct states. Copy logs before restarting.

For OSC, check saved destination, names/types, VRChat settings, and its debug display. For storage/deletion errors, check free space and permissions. Failed settings loading displays a warning and disables OSC; correct settings before enabling it again.

The WPF window supports Light/Dark/System. The current standard WinForms tray menu does not explicitly follow the app palette. Sensor compatibility, sleep recovery, sustained recording, and delivery into VRChat require hardware verification; automated tests do not establish those results.

## Build and verification

The app uses WPF on .NET Framework 4.8, Windows WinRT BLE APIs, and OS-provided `winsqlite3.dll`. Install the .NET SDK (Roslyn) and Windows SDK metadata. No NuGet restore is used. Always build with tests:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test
```

The compiler comes from the SDK selected by the `dotnet` on PATH when run in the repository. SDK locations are not hardcoded; prerelease names and `global.json` selection follow the .NET CLI.

Windows SDK metadata selection compares installed versions numerically and uses the newest union metadata. Facade metadata is excluded, and the legacy layout directly under the UnionMetadata root is also supported.

Development builds overwrite `artifacts/HeartRateMonitor.exe` and place the configuration, both READMEs, and ZIP directly under `artifacts/`. The ZIP includes the EXE, configuration, and both READMEs. Exit from the tray before replacing a running EXE.

Keep current development build outputs directly under `artifacts/`. Store logs and verification files in `artifacts/logs/`, and repository maintenance backups in `artifacts/backups/`. Builds and verification do not finalize the product version or automatically create version directories. Reserve `artifacts/versions/version-X.Y.Z/` for finalized snapshots only when explicitly instructed to finalize a version. The existing `artifacts/versions/version-0.0.0/` is for unversioned legacy artifacts.

Additional checks on an interactive Windows desktop:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\ui-smoke.ps1
.\artifacts\HeartRateMonitor.exe --data-dir .\artifacts\logs\verification\render --smoke-test
```

The UI script seeds a temporary database labeled `ISOLATED UI TEST`. Rendering smoke mode checks Japanese/English and Light/Dark/System pages, captures PNGs, and writes `smoke-checks.txt`. It saves test settings: always use an isolated `--data-dir`. Smoke mode rejects an omitted data directory or the normal profile directory. A missing `--data-dir` value, duplicate data-directory option, or unknown argument also causes a startup error. These are procedures, not a claim they were run during this documentation update.

The dependency-free runner in `tests/Tests.cs` calls the domain suites in `tests/Tests.*.cs`. `build.ps1 -Test` compiles and runs them together; `BluetoothDiagnostic.cs` remains a separate hardware diagnostic.

`build.ps1 -Test` also runs `tests/build-sdk.ps1`, which checks SDK discovery and failures using isolated SDK listings, and `tests/build-metadata.ps1`, which checks Windows SDK selection. When `-Diagnostic` is included, an abnormal hardware diagnostic exit also stops the build as a failure.

The Bluetooth diagnostic attempts both device and heart-rate service enumeration, even if one query fails. An enumeration error or cancellation returns exit code 1; a successfully enumerated empty list is not a failure. Hardware-free regression tests also cover the query success and failure handling.

Tests cover parsing, connection control, OSC and test isolation, settings, SQLite/CSV, retention, history, and analysis.

Beko.BluetoothHeartRateOSC is a behavioral reference; this implementation is independent.
