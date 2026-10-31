# Heart Rate Monitor 0.23.0

A Windows desktop application that receives heart-rate notifications from standard BLE Heart Rate Service sensors, stores timestamped readings in a local SQLite database, and visualizes real-time and historical trends using interactive charts. It supports VRChat OSC output, CSV export, automated sleep mode logging and analysis via OyasumiVR integration, Japanese and English user interfaces, Light/Dark/System themes, and task tray residency. No external account or cloud service is required.

[日本語](README.md)

---

## Table of Contents

- [Requirements and Quick Start](#requirements-and-quick-start)
  - [System Requirements](#system-requirements)
  - [First Run and Basic Steps](#first-run-and-basic-steps)
  - [Connection and Reconnection Rules](#connection-and-reconnection-rules)
- [Charts, History, and CSV Export](#charts-history-and-csv-export)
  - [Live View](#live-view)
  - [Browsing History](#browsing-history)
  - [Date/Time Navigation and Monthly Calendar](#datetime-navigation-and-monthly-calendar)
  - [Analysis Interval Selection](#analysis-interval-selection)
  - [CSV Export Specifications](#csv-export-specifications)
- [Settings and OSC Transmission](#settings-and-osc-transmission)
  - [Settings Organization](#settings-organization)
  - [VRChat OSC Output Configuration](#vrchat-osc-output-configuration)
  - [One-Shot Transmission Test](#one-shot-transmission-test)
  - [Disconnection and Silence Timeout (Fallbacks)](#disconnection-and-silence-timeout-fallbacks)
- [Data Storage and Retention](#data-storage-and-retention)
  - [File Layout](#file-layout)
  - [Startup Safeguards and Settings Recovery](#startup-safeguards-and-settings-recovery)
  - [Data Retention Policy (Automatic Cleanup) and Warnings](#data-retention-policy-automatic-cleanup-and-warnings)
  - [Diagnostic Logging and Backup Procedures](#diagnostic-logging-and-backup-procedures)
- [OyasumiVR Sleep Mode Integration and Analysis](#oyasumivr-sleep-mode-integration-and-analysis)
  - [Integration Overview and Setup](#integration-overview-and-setup)
  - [Sleep Mode Detection and Disconnection Handling](#sleep-mode-detection-and-disconnection-handling)
  - [Sleep Dashboard and Analysis Statistics](#sleep-dashboard-and-analysis-statistics)
  - [Sleep Rhythm and Records List (Deleting Labels)](#sleep-rhythm-and-records-list-deleting-labels)
- [Troubleshooting and Limitations](#troubleshooting-and-limitations)
- [Development, Build, and Verification](#development-build-and-verification)
  - [Tech Stack and Build Prerequisites](#tech-stack-and-build-prerequisites)
  - [Build Commands and Artifacts](#build-commands-and-artifacts)
  - [Automated Tests and UI Smoke Testing](#automated-tests-and-ui-smoke-testing)
  - [Architecture and Reference Implementation](#architecture-and-reference-implementation)

---

## Requirements and Quick Start

### System Requirements

- **Operating System**: 64-bit Windows 10 or Windows 11
- **Runtime**: .NET Framework 4.8
- **Bluetooth**: Bluetooth adapter with Bluetooth LE (Low Energy) support
- **Heart-Rate Sensor**: Sensors supporting standard Bluetooth LE Heart Rate Service (UUID `0x180D`) and Heart Rate Measurement notifications (UUID `0x2A37`), such as the COOSPO HW706. Device compatibility should be verified with the actual physical hardware.

### First Run and Basic Steps

Extract the ZIP package and run `HeartRateMonitor.exe`. When built from source code, the executable is at `artifacts/HeartRateMonitor.exe`. Keep `HeartRateMonitor.exe.config` beside it.

1. **Prepare Sensor**: Turn on Windows Bluetooth, wear the sensor, and switch it on.
2. **Automatic Connection**: Discovery starts automatically on launch. The previously verified successful device is tried first (attempting direct connection even if absent from discovery advertisements), followed by devices advertising or registered in Windows with the standard heart-rate service. No manufacturer or model is favored; a name alone does not qualify an unverified device.
3. **View Readings & Record**: View incoming readings and the chart on **Live**. Samples are automatically saved to SQLite. Use **History** to explore past recordings and export displayed ranges to CSV.
4. **Pause and Resume**: **Stop** cancels discovery, connection, and automatic reconnection until **Connect** resumes them. **Details** contains manual selection, Scan, Reconnect, and Copy log.
5. **Tray Residency and Clean Exit**: Closing hides to the tray by default, keeping recording and OSC active. Double-click the tray icon, choose **Open window**, or run the EXE again to restore it. Choose **Exit** from the tray to drain the recording queue and quit.

> **Language Switch**: The initial UI is Japanese. Choose **設定 → 一般 → 表示言語 → English → 保存して適用** (Save & apply) to switch.

### Connection and Reconnection Rules

- **Direct Fallback for Verified Devices**: If automatic discovery fails, the app attempts a direct connection using saved verified device information. After disconnection, it keeps targeting the same sensor.
- **Safe Stop on User Pause or Permission Denial**: Discovery permission denial or cancellation through **Stop** halts without attempting direct connection fallback; an unverified saved address never qualifies.
- **Silence and Disconnection Thresholds**: The Live number clears to “—” after 5 seconds without a fresh sample. Twenty seconds without notifications ends the connection; automatic reconnection retries when enabled. Discovery listens for advertisements for about 8 seconds, with 5 seconds between retry cycles. Permission denial stops automatic retries.

---

## Charts, History, and CSV Export

### Live View

- **Graph-Centric Presentation**: Charts have no received-reading tables. Hover to inspect nearby samples or aggregated buckets.
- **Time Span Selection**: Presets (10, 30, 60 minutes) and a slider select 1 minute to 1 hour (slider minimum is 1 minute).
- **Real-Time Updates**: Keeps the newest time at the right edge. Use `Ctrl`+wheel to zoom.
- **Summary Statistics**: Summaries and the dashed mean line use valid samples in the displayed period, excluding 0 bpm and explicit lack of contact.
- **In-Memory Retention**: Live uses readings retained in memory during the current session (up to 1 hour); it does not reload past database records.

### Browsing History

- **Time Span Range**: Presets and a slider select 1 minute to all retained data (**All**). History starts at 10 minutes.
- **Two-Line Time Axis**: The History time axis shows the time and date (`yyyy/MM/dd`) on two lines.
- **Aggregated Whiskers**: When readings are aggregated for a wider view, the chart shows a mean trace with thin minimum-to-maximum whiskers behind it. Hover to inspect nearby samples or buckets.
- **Mouse Wheel Panning**: Scroll up on the main History chart to move earlier, or down to move later, by 10% of the visible time span per wheel notch (the overview chart has been removed, and page scrolling does not occur). Use `Ctrl`+wheel to zoom around the cursor.
- **Window Navigation**: Previous/Next move by one window width; **Latest recordings** refreshes and moves to the newest data.

### Date/Time Navigation and Monthly Calendar

- **Layout**: At the top of History, the monthly calendar and date/time form share the available width. The jump button spans both input fields below them.
- **Monthly Calendar**: Offers previous month, next month, and **This month** navigation. Recorded days have a highlight and a **•**; hover for valid sample counts. Select any day, including empty days, to view that local calendar day, from midnight to the following midnight. Days with daylight-saving transitions need not last 24 hours. Tab into the calendar and use arrows and Enter. Counts cover only the displayed month's valid heart-rate samples.
- **Find by Date / Time**: Accepts a date (`yyyy/MM/dd` or `yyyy-MM-dd`) and start time (`HH:mm` or `HH:mm:ss`). Choose **Go to date / time** or press Enter to display a range starting there with the current time span, including dates more than a year ago. Times use the PC's local time zone. Nonexistent daylight-saving times show an error; repeated times select the earlier occurrence.

### Analysis Interval Selection

- Drag horizontally in a main chart to analyze duration, mean, minimum, maximum, and count.
- Statistics use original valid samples (excluding 0 bpm and lack of contact) in the half-open range `[from, to)` (including the start and excluding the end).
- Click, `Esc`, right-click, or changing the visible range clears the selection.

### CSV Export Specifications

History provides two CSV export actions, both using the same original readings and column schema in timestamp order:

1. **Export range to CSV**: Exports the finalized displayed range.
2. **Export interval to CSV**: Exports the analysis interval selected by dragging on the chart (enabled only while an interval is selected; captured when the save dialog opens).

- **Format and Specifications**:
  - **Encoding**: UTF-8 with BOM.
  - **Included Data**: Both actions export original measurements in the half-open range `[from, to)`, including 0 bpm and no-contact samples.
  - **Columns**:
    ```csv
    utc_time,local_time,bpm,contact,rr_ms,energy_kj,device,session
    ```
  - `utc_time`: ISO 8601 UTC timestamp (e.g. `2026-10-31T05:30:00.0000000+00:00`).
  - `local_time`: Current local time zone formatted as invariant Gregorian `yyyy-MM-dd HH:mm:ss.fff`, independently of OS calendar and display settings.
  - `bpm`: Heart rate (integer).
  - `contact`: `1`, `0`, or empty when unknown.
  - `rr_ms`: RR intervals in milliseconds.
  - `energy_kj`: Energy in kJ.
  - `device`: Device identifier string.
  - `session`: Session identifier.
- **Protection**: Exporting over database files (`heartrate.db`, `-wal`, `-shm`, `-journal`, etc.) is rejected. Atomic file writing prevents partial overwriting of the destination file (cleanup of temporary files can itself fail, so it is not guaranteed that no temporary file remains anywhere). There is no CSV import.

---

## Settings and OSC Transmission

### Settings Organization

Choose a category on the left of Settings to replace the form on the right. Each category fits without scrolling. Switching categories or visiting Live and History preserves unsaved inputs; saving keeps the currently selected category.

1. **General**: Theme (Light / Dark / System), Display Language (日本語 / English), and Keep running in the tray when closed checkbox. *The toolbar sun/moon button switches Light/Dark and saves immediately; select System in Settings to follow Windows again.*
2. **Heart-rate sensor**: Automatic connection on startup and reconnection on disconnect.
3. **Data storage**: Data retention period (Unlimited, 1, 7, 30, 90, 365 days).
4. **OyasumiVR integration**: Enable integration, OSCQuery port, and Sleep day boundary hour (0–23).
5. **Destination & parameters**: Destination IP address, UDP port, Int parameter name, and Float parameter name.
6. **Values & timeout behavior**: Float bounds (minimum/maximum bpm), disconnection/timeout mode, timeout interval, and fallbacks (Int and Float).
7. **Transmission & test**: Transmission status, last sent values, and one-shot test transmission.

### VRChat OSC Output Configuration

OSC is enabled by default, targeting UDP `127.0.0.1:9000`. Configure matching avatar parameters and enable OSC in VRChat.

- **Destination & Port**: The destination accepts an IP address (IPv4 or IPv6), not a hostname. Port must be 1–65535.
- **Int32 Parameter**:
  - Default: `/avatar/parameters/VRCOSC/Heartrate/Value`
  - Value: Current heart rate, clamped to 0–255 bpm.
- **Float32 Parameter**:
  - Default: `/avatar/parameters/HRPercent`
  - Value: Linear conversion mapped to 0.0–1.0.
  - Formula: `clamp((bpm - minimum) / (maximum - minimum), 0.0, 1.0)`
  - Default bounds: 0 to 200 bpm (bounds must satisfy `0 <= minimum < maximum <= 65535`).
- **Naming Rules**:
  - Names without a leading slash receive `/avatar/parameters/`; full addresses must also be under that prefix.
  - Use distinct ASCII names without spaces, consecutive slashes `//`, or wildcards `#*,?[]{}`.

> [!NOTE]
> Zero bpm and explicit lack of contact are stored in the database but are not sent as live OSC values.

### One-Shot Transmission Test

Under **Transmission & test**, you can send a test packet once using an entered heart rate (default 80, range 1–65535) via **Send test**.

- **Prerequisite**: OSC must be **enabled and saved first**.
- Sends both parameters once using saved settings.
- **Isolation from Recordings**: Creates no stored reading and does not count as a received notification (does not reset the silence timer). Valid live measurements resume normal output automatically.
- **UDP Delivery Limitations**: UDP success means the OS accepted the datagrams; verify reception and application in VRChat's OSC debug display.

### Disconnection and Silence Timeout (Fallbacks)

Settings offers two disconnection / receive-timeout modes, applied via **Save & apply**:

1. **Keep last value**:
   - Missing notifications send no replacement, leaving the receiver's last value unchanged.
2. **Send fallback values on timeout** (default):
   - After the configured interval (1–3600 seconds, default 5), send configured Int (0–255, default 0) and Float (0.0–1.0, default 0.0) fallback values once.
   - **Bypasses Conversion and Clamping**: Fallbacks are sent directly without bpm clamping or heart-rate conversion.
   - Applies from app startup before notifications arrive, and while manually stopped. Each notification (including 0 bpm or lack of contact) resets the silence timer.
   - Timing uses monotonic elapsed time independent of system clock changes.
   - Identified in transmission status as “Timeout fallback sent via UDP” and creates no stored readings. Valid measurements resume normal output automatically.
   - No fallbacks are sent while OSC is disabled or after app exit.

- **Migration and Application Rules**:
  - Older settings without this option use the default (fallback mode, 5 seconds). Explicitly saved modes and intervals are preserved. Older settings without fallback values continue to send both values as zero.
  - If the interval has already expired when a setting is applied, fallbacks are sent after applying it.
  - Changing fallback values during silence sends the new values once; saving unchanged settings does not resend them.
  - One-shot tests do not count as received notifications and do not reset the timer; timeout fallbacks can replace test values.

---

## Data Storage and Retention

### File Layout

The normal directory is `%LOCALAPPDATA%\HeartRateMonitor\`:

- **`heartrate.db`**: SQLite database containing measurement records (`readings`), sleep intervals (`sleep_intervals`), and management metadata (`sleep_management`). SQLite uses WAL and `FULL` synchronization.
- **`settings.json`**: Preferences and previous verified device metadata.
- **`settings.json.bak`**: Previous settings file retained during atomic saves.
- **`connection.log`**: Up to 250 diagnostic lines from this launch, including connection, recording, OSC errors, and startup failures (UTF-8 with BOM).

### Startup Safeguards and Settings Recovery

If settings cannot be loaded, the app displays a warning and starts in safe mode with **OSC disabled**.

- Automatic settings saves on sensor connection are suspended until **Save & apply** succeeds, preserving the original `settings.json` and `settings.json.bak`.
- Toolbar theme changes apply only in memory during this period.
- The first explicit recovery save preserves the existing backup; normal automatic saves then resume.
- **Manual Backup Recovery**: Exit the app, copy both files elsewhere for safekeeping, copy a valid `settings.json.bak` to `settings.json` (do not rename away the backup), and restart.

### Data Retention Policy (Automatic Cleanup) and Warnings

Set retention under **Settings → Data storage**:

- **Options**: **Unlimited** (default), **1 day**, **7 days**, **30 days**, **90 days**, or **365 days**. Retention defaults to Unlimited when migrating from settings without a retention field.
- **Automatic Cleanup Timing**: With finite retention, readings older than the selected number of 24-hour days relative to the current time are automatically deleted at startup, after policy changes, and during checks every minute while running. **Existing readings are included.**
- **Sleep Label Cutoff**: Sleep labels ending at or before the cutoff time are permanently deleted too. Intervals crossing the cutoff keep their original timestamps.
- **Active Policy Notice**: The active policy appears on Live and History.

> [!WARNING]
> **Irreversible Deletion Notice**:
> Deletion is **irreversible: deleted readings and sleep labels cannot be recovered or exported. Returning to Unlimited does not restore them.** Export needed readings before shortening retention. Unlimited storage grows over time; monitor free space. Note that deletion does not guarantee immediate reduction of database file size.

### Diagnostic Logging and Backup Procedures

- **Diagnostic Logs**: Recording and OSC errors reach the log independently of UI updates, including during shutdown. If the log cannot be saved to disk, the in-memory copy retains the notification and write failure, which can be copied via **Copy log** in Details before exit (in-memory logs clear at exit).
- **Backup Procedure**: Exit from the tray before backing up the entire `%LOCALAPPDATA%\HeartRateMonitor\` folder. Sleep labels and readings reside in the same database. CSV is for export, not for restore.
- **Restore Procedure**: Exit, preserve the current folder separately, and replace it with the backup. The restored retention policy still applies at startup. There is no built-in backup/restore UI.
- **Shutdown Queue Flushing**: Shutdown waits for accepted readings to be saved and for any CSV export in progress to finish (large exports can delay exit). A crash or power loss can lose queued readings.

---

## OyasumiVR Sleep Mode Integration and Analysis

### Integration Overview and Setup

Run [OyasumiVR](https://github.com/Raphiiko/OyasumiVR) on the same PC and enable its OSC server. Integration is enabled by default under **Settings → OyasumiVR integration**.

- **Port Setup**: The default OSCQuery port `0` automatically discovers local listeners owned by OyasumiVR. If discovery fails, enter the **OSCQuery (HTTP) port** shown in OyasumiVR and save (this is different from its OSC UDP port). The host is always `127.0.0.1`. VRChat need not be running, and OSC forwarding destinations need no changes.
- **Polling State**: Every 2 seconds, the app reads the [official sleep-mode address](https://github.com/Raphiiko/OyasumiVR/blob/develop/src-ui/app/services/osc-control/methods/sleep-mode.osc-method.ts), `/OyasumiVR/SleepMode`, through OSCQuery. While enabled, it records “Sleep · OyasumiVR” intervals on the history chart. It only reads OyasumiVR's state and does not alter it. Labels represent sleep mode, not sleep inferred from heart rate.

### Sleep Mode Detection and Disconnection Handling

- **Interval Boundaries**: An interval starts at the first confirmed enabled observation and ends when mode is confirmed disabled.
- **No Inferred Time**: If communication fails, the saved interval stops at the last confirmed observation; **unknown time is never filled in as sleep**.
- **Interval Splitting**: A polling gap longer than 10 seconds (e.g. during PC suspension), an app restart, a changed integration endpoint, or disabling integration splits intervals. Polling resumes automatically after recovery. Connection status appears in Settings and History; errors are logged to `connection.log`.
- **Heart-Rate Independence**: Sleep labels remain selectable and visible in History even outside heart-rate recording times. Readings, statistics, and sample CSV columns are unchanged. Sleep labels are not included in CSV. Manual note/interval editing features were removed; existing manual annotation tables are preserved but never displayed or converted into sleep labels.

### Sleep Dashboard and Analysis Statistics

The **Sleep** sidebar page shows average sleeping heart rate, average sleep duration, and typical sleep hours for 7 days, 30 days, or a custom range (up to 366 days). The overview, sleep rhythm, and selected interval's heart-rate chart fit on one screen without page scrolling.

- **Sleep Day Boundary Hour**:
  - Dates identify the day on which the sleep-day boundary begins. The default is midnight to midnight; choose and save any hour from **00:00 to 23:00** under **Settings → OyasumiVR integration → Sleep day starts at**. Previously saved start times are preserved.
  - When the horizontal scale crosses a date boundary, an emphasized vertical line marks midnight. The sleep rhythm labels it as the next day, and the lower heart-rate chart labels the new date.
- **Metric Definitions**:
  - **Average Sleep Duration**: Sum of observed time per day divided by the **number of days with records** (intervals crossing boundaries count only within the range, overlaps are counted once, missing days are omitted instead of counted as zero; ongoing and excluded intervals are omitted from averages).
  - **Average Sleeping Heart Rate**: Mean of valid recorded samples within included intervals, without weighting by sampling interval. Missing heart-rate data displays “—”; separate counts show days with sleep and heart-rate data.
  - **Typical Sleep Hours**: Median start and end times of each day's longest continuous record. Use the timeline alongside these values when gaps split a night's records or when naps are present.

### Sleep Rhythm and Records List (Deleting Labels)

Switch between **Rhythm** and **Records** in the upper card.

- **Sleep Rhythm**: Use the arrows to page the daily timeline 7 days at a time; averages always cover the entire selected summary period. Daily bars show only observed intervals; communication gaps are never filled.
- **Records List**: Lists sleep labels from the selected period, newest first, with start/end times, duration, and states such as recording or excluded from averages.
- **Linked Heart-Rate Chart**: Select a timeline interval or list row to display its heart-rate chart below (focus with Tab, navigate with arrow keys, Home, or End). The caption shows the interval's start, end, and average heart rate. Intervals without measurements show an empty chart and “No heart-rate data”.
- **Deleting Sleep Labels**:
  - In Records, select a label and choose **Delete label…** to review its interval. Choose Cancel or press `Esc` to abort.
  - **Irreversible Deletion & Reading Preservation**: **Label deletion cannot be undone; the original heart-rate readings remain.** Deletion updates the sleep summary, rhythm, and History labels.
  - Recording labels cannot be deleted. There is no exclude/include action; existing exclusion states are preserved.
  - With finite retention, sleep labels ending at or before the cutoff are permanently deleted too. Intervals crossing the cutoff keep their original timestamps. **Deletion is irreversible.**

---

## Troubleshooting and Limitations

### Sensor Missing or Connection Issues

- Check power, battery, skin contact/fit according to the manufacturer's instructions, distance, and Windows Bluetooth (applies to both optical and electrode sensors).
- Ensure the sensor is not currently connected to another app or phone.
- Use **Details** on the connection bar to run **Scan**, manually select sensors, or reconnect.
- “Connected · awaiting data” means the connection is established but no heart-rate notification has arrived yet. Distinguish it from “Receiving heart rate”. Copy logs from Details before restarting.

### OSC Not Showing in VRChat

- Check saved destination IP/port, parameter names/types, VRChat OSC settings, and VRChat's OSC debug display.
- UDP success means the OS accepted the datagrams; it does not guarantee delivery into VRChat or application to the avatar.

### Storage or Settings Errors

- Check available disk space and folder permissions for `%LOCALAPPDATA%\HeartRateMonitor\`.
- Failed settings loading displays a warning and disables OSC; correct settings and click **Save & apply**.

### Known Limitations

- **System Tray Menu Appearance**: While the WPF window supports Light, Dark, and System themes, the standard WinForms tray menu uses default OS styling and does not explicitly follow the app palette.
- **Hardware Verification Scope**: Sensor compatibility, sleep recovery, sustained recording, and delivery into VRChat require hardware verification; automated tests do not establish those results.

---

## Development, Build, and Verification

### Tech Stack and Build Prerequisites

- **Platform**: x64 Windows 10 / 11, .NET Framework 4.8
- **Core APIs**: WPF, Windows WinRT BLE APIs, and OS-provided `winsqlite3.dll` (parameterized queries, WAL mode). No NuGet restore is used.
- **Tooling**: .NET SDK (Roslyn) and Windows SDK metadata.
- **Compiler Resolution**: The compiler comes from the SDK selected by `dotnet` on PATH (`tools/resolve-build-sdk.ps1`). SDK locations are not hardcoded; prerelease names and `global.json` selection follow the .NET CLI.
- **Windows SDK Resolution**: `tools/resolve-windows-metadata.ps1` compares installed versions numerically and uses the newest union metadata. Facade metadata is excluded, and the legacy layout directly under the UnionMetadata root is supported.

### Build Commands and Artifacts

Execute builds from the repository root using PowerShell. Always include `-Test` for development builds:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test
```

- **Output Artifacts**:
  - Development builds overwrite files directly under `artifacts/`:
  - `artifacts/HeartRateMonitor.exe` (main application)
  - `artifacts/HeartRateMonitor.exe.config` (runtime configuration)
  - `artifacts/README.md` and `artifacts/README.en.md` (packaged documentation)
  - `artifacts/HeartRateMonitor-win-x64.zip` (standalone ZIP package)
- **Build Precautions**: Exit the application from the tray before replacing a running EXE.
- **Artifact Conventions**:
  - Keep current development build outputs directly under `artifacts/` (do not create `artifacts/dev-*` directories).
  - Store logs and verification files in `artifacts/logs/`, and repository maintenance backups in `artifacts/backups/`.
  - Builds and verification do not finalize the product version or automatically create version directories.
  - Reserve `artifacts/versions/version-X.Y.Z/` for finalized snapshots only when explicitly instructed to finalize a version. The existing `artifacts/versions/version-0.0.0/` is for unversioned legacy artifacts. The product version remains `0.23.0`.

### Automated Tests and UI Smoke Testing

The dependency-free runner in `tests/Tests.cs` calls domain suites in `tests/Tests.*.cs`.

- **Test Suite Coverage**: Tests cover parsing, connection control, OSC and test isolation, settings, SQLite/CSV, retention, history, and analysis.
- **Scripted Build Checks**:
  - `build.ps1 -Test` runs `tests/build-sdk.ps1` (checks SDK discovery and failures using isolated SDK listings) and `tests/build-metadata.ps1` (checks Windows SDK selection).
- **Hardware Bluetooth Diagnostic**:
  - When `-Diagnostic` is included, `BluetoothDiagnostic.exe` is built and run.
  - The diagnostic attempts both device and heart-rate service enumeration, even if one query fails. An enumeration error or cancellation returns exit code 1; a successfully enumerated empty list is not a failure (exit code 0). Hardware-free regression tests also cover query success and failure handling. An abnormal diagnostic exit stops the build as a failure.

#### UI Automation and Smoke Testing

Additional checks on an interactive Windows desktop:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\ui-smoke.ps1
.\artifacts\HeartRateMonitor.exe --data-dir .\artifacts\logs\verification\render --smoke-test
```

- `tests/ui-smoke.ps1`: Seeds a temporary database labeled `ISOLATED UI TEST` and exercises tab switching, time sliders, calendar controls, sleep view layouts, settings persistence, language switching, and tray residency.
- `--smoke-test`: Checks Japanese/English and Light/Dark/System pages, captures PNGs, and writes `smoke-checks.txt`.
- **Data Isolation**: Smoke mode saves test settings, so it **requires** `--data-dir <path>` pointing to an isolated directory. Smoke mode rejects an omitted data directory or the normal profile directory (`%LOCALAPPDATA%\HeartRateMonitor`). A missing `--data-dir` value, duplicate `--data-dir` option, or unknown argument also causes a startup error.

### Architecture and Reference Implementation

- **Component Separation**:
  - `BluetoothMonitor`, `GattAccess`, `AutoConnector`: BLE communication, discovery, and reconnection.
  - `RecordingEngine`: Recording coordination.
  - `Store` and partial files: Persistence using Windows `winsqlite3.dll`, history/sleep management, and CSV export.
  - `OscSender`: VRChat OSC parameter transmission.
  - `MainWindow.*`: UI presentation and interaction.
- **Reference Notice**:
  - `Beko.BluetoothHeartRateOSC` is a behavioral reference only; this implementation is independent and does not copy its source.
