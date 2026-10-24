# Heart Rate Monitor 0.19.7

Live, History, and Settings use icons and short labels in a slim, 112-unit left sidebar. Items have no surrounding border; a soft background and a thin left indicator identify the selected page. CSV export is available in History. Settings is one continuous vertical form: Appearance & language, Connection & tray, Data storage, then OSC settings, separated by dividers. The form scrolls within its tab, while Save & apply stays at the bottom right. Switching tabs preserves unsaved inputs and scroll position; saving keeps Settings selected and restores its scroll position.

History's **Recording rhythm** calendar covers the past 365 days including today. Rectangular cells tile the available width. Each cell is one Windows local day; shading shows valid sample count (excluding zero bpm and explicit lack of contact): 0 / 1–599 / 600–3,599 / 3,600–10,799 / 10,800+. Cells use teal in Light and blue in Dark; empty days are gray. Shading does not indicate heart-rate intensity or wear duration. Hover for the date and exact count; click a day to view its history, or Tab into the calendar, move with arrow keys, and press Enter. Active days and total samples appear to the right of the heading. Counts refresh when opening History, choosing Latest recordings, and every minute while History is visible. Retention-deleted samples are excluded. Older retained recordings remain accessible through the existing overview.

The date/time jump has been removed. History fits the chart, overview, CSV export and retention notice on screen without horizontal or vertical scrolling. When space is limited, including the minimum window size, the entire page scales down to fit. Long errors and export paths are truncated; hover over them to read the full text.

Time span now has a slider alongside the preset buttons on Live and History. Move left for shorter windows and right for longer windows; the current duration stays visible. The minimum is one minute, with a maximum of one hour on Live or all recordings on History. Arrow keys also adjust the slider. Its logarithmic scale keeps short windows easy to select even in long archives. Use a preset to return to an exact duration such as ten minutes.

A Windows desktop app for receiving heart-rate notifications from standard BLE Heart Rate Service sensors, including the COOSPO HW706, saving samples in SQLite, exploring recordings, and sending two OSC parameters to VRChat. Built independently; Beko.BluetoothHeartRateOSC is a behavioral reference.

## Getting started

Run `artifacts\HeartRateMonitor.exe` on 64-bit Windows 10/11 with .NET Framework 4.8 and a Bluetooth LE adapter. Wear and switch on the sensor. Discovery starts automatically. Other apps or phones connected to the sensor may prevent this app from connecting.

Automatic connection first tries the previously verified sensor, even when it is absent from discovery, then sensors advertising the standard Heart Rate Service or registered with a heart-rate service in Windows. If the previous sensor is unavailable, it proceeds to other candidates. No manufacturer or model is favored. A name alone does not qualify a device; use Details to select a sensor whose service is not discovered. Compatibility with each physical sensor still requires hardware verification.

Choose the **設定 tab → 表示言語 → English → 保存して適用** to switch to English immediately. Language, theme, automatic connection, reconnection, and tray preferences are all in Settings.

The connection panel distinguishes discovery, detection, connection, waiting for the first sample, and actual reception. Permission errors, sharing conflicts, unreachable devices, and timeouts have recovery guidance. Use **Reconnect** or **Copy log** in this panel. Technical logs may contain Japanese diagnostic strings and Windows error details.

## Live and OSC

Live focuses on a full-width heart-rate chart. The current rate, minimum/average/maximum, and last sample time appear above it, alongside direct 10-minute, 30-minute, and 1-hour buttons. Recording and OSC continue in the background; their separate cards are removed from Live. OSC status and the one-shot test are in Settings. Both live and history charts show a horizontal dashed line and a value label for the average of valid samples in the displayed window, matching the summary. No average line is shown without data. Hover over either chart to see the nearest sample or bucket timestamp, average, minimum, and maximum. No received-sample table is shown. Samples are saved automatically. No fresh sample for five seconds clears the current number. After twenty seconds without notifications the connection is retried, if enabled.

History has a thin overview chart beneath the main plot. It starts at ten minutes. Drag either edge to zoom (minimum one minute), or drag the middle/top grab marker to pan; the main plot follows immediately. Clicking outside the selection recenters it. Presets cover 10 min, 30 min, 1 hour, 6 hours, 24 hours, and All, clamped to available recordings. Short recordings retain a ten-minute overview domain. Tab between the move/start/end handles; arrows adjust by one minute, Shift+arrows by ten minutes, and Home/End reach the boundaries. During dragging, bounded cached aggregates of real recordings supply a preview. Detailed buckets, exact statistics and the average line are refined after release. CSV exports original samples from the finalized selected range. Recordings are retained indefinitely by default. Finite retention can be selected in Settings; expired recordings are deleted automatically and cannot be recovered or exported.

Default UDP destination: `127.0.0.1:9000`.

| Type | Default parameter | Conversion |
| --- | --- | --- |
| Int32 | `VRCOSC/Heartrate/Value` | Heart rate, clamped to 0–255 |
| Float32 | `HRPercent` | `clamp((bpm - minimum) / (maximum - minimum), 0, 1)` |

Parameter names are prefixed with `/avatar/parameters/`. Full addresses are also accepted. The initial float range is 0–200 bpm. Configure matching avatar parameters and enable OSC in VRChat.

The **Transmission & test** section in Settings shows the configured destination, last transmission time, actual values and send errors. **Send test** transmits both values once using the saved settings and the entered test heart rate (initially 80). It does not create a stored sample. It is disabled when OSC is disabled. A subsequent live sample replaces the test values.

UDP send success means the operating system accepted the outgoing datagrams; it does not confirm that VRChat received or applied them. Check VRChat's OSC debug display. Zero bpm or explicit lack of sensor contact is stored but not transmitted.

## History and CSV

Choose a 10-minute, 30-minute, 1-hour, 6-hour, or 24-hour window directly with the time-span buttons. The current range appears above the chart; the first and latest recording times appear below it. Recordings are retained indefinitely by default; the active retention policy is shown beneath the chart and can be changed in Settings. Hover over the chart to see the timestamp, average, minimum, and maximum for the nearest bucket. Click or drag the timeline or use its arrow keys to move through your recordings. **Previous** and **Next** move by one window and are disabled at the recording bounds. **Latest recordings** refreshes the bounds and moves to the newest sample.

**Export range to CSV** exports every stored measurement in the displayed half-open interval, including invalid-contact samples excluded from the chart. Export is available after the selected range loads. CSV uses UTF-8 with BOM.

## Tray behavior

Closing the window keeps recording and OSC running by default. Double-click the tray icon, choose **Open window** from its context menu, or launch the EXE again to restore the existing instance. The tray menu also provides Connect, Stop, and Exit.

Use **Exit** in the tray menu to drain the recording queue and terminate. Disable **Keep running in the tray when closed** if the close button should exit instead.

## Data and building

Data lives in `%LOCALAPPDATA%\HeartRateMonitor\`:

- `heartrate.db`: SQLite samples with UTC timestamps, original heart rates, device/session, contact, and optional RR/energy data.
- `settings.json`: configuration, with the previous version saved as `.bak`.
- `connection.log`: the most recent 250 diagnostic lines from this launch.

Existing settings are retained. **Settings → Data storage → Retention period** offers **Unlimited (default), 1 day (24 hours), 7 days, 30 days, 90 days, and 1 year (365 days)**. Use **Save & apply** to apply changes. Finite retention deletes recordings older than the selected number of days relative to the current time, at startup, after changing the policy, and during checks every minute while the app runs. Existing recordings are included. Export anything needed before shortening the period: deleted recordings cannot be recovered or exported, and switching back to Unlimited does not restore them. Old settings without a retention field migrate to Unlimited. Storage use grows under Unlimited; monitor available space. Back up the entire folder after exiting the app. Old settings default to Japanese and tray residency.

Build without NuGet using the .NET SDK and Windows SDK:

Default builds keep a versioned snapshot in `artifacts\version-X.Y.Z\` and publish the latest EXE, configuration, and ZIP directly under `artifacts\` after successful checks. The ZIP contains the EXE, configuration, and both READMEs. Keep older releases and verification files inside their version folders. `version-0.0.0\` holds legacy builds without an assigned product version and their verification files. Exit the running app from the tray before replacing its EXE.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\ui-smoke.ps1
.\artifacts\HeartRateMonitor.exe --data-dir .\artifacts\version-0.19.7\verification\smoke --smoke-test
```

Automated checks cover notification parsing, discovery/reconnection, permissions, OSC datagrams, one-shot test isolation, language/tray settings, timeline bounds, SQLite, CSV, unlimited-retention migration, policy changes, periodic cleanup, long-term history and queue draining. UI automation uses an isolated temporary database labeled as test data. Rendering smoke tests generate twenty screenshots across two languages, two themes, and three tabs, including constrained History and the bottom of Settings. They also verify selected-tab styling, scrolling, unsaved edits across tab switches, and keeping Settings selected with its scroll position after saving; results are written to `smoke-checks.txt`. Always use a separate data directory for smoke tests.

Real HW706 reception, reconnection and delivery into VRChat still require verification with the physical sensor and VRChat running.


Use the sun/moon icon in the top window toolbar on any page to switch between light and dark. The theme is saved immediately; select Follow Windows in Settings to resume system matching. Theme colors and the icon animate for about 0.36 seconds without retaining screen images. Animation clocks are removed afterward. Windows animation and high-contrast preferences are respected.

Drag the blank or app-name area of the toolbar to move the window; double-click to maximize/restore. Settings is a standard tab alongside Live and History & CSV. Scroll its contents to edit options, then use the fixed Save & apply button at the bottom. Select another tab to switch views. Dark mode uses neutral gray surfaces. Recording continues while Settings is open.

Ctrl+wheel over a main chart zooms in (up) or out (down). History zooms around the cursor timestamp and synchronizes its overview; Live keeps the latest timestamp at the right edge. Live spans one minute to one hour; History spans one minute to all available recordings. The overview also accepts Ctrl+wheel. Without Ctrl, the wheel continues scrolling the page.
Drag horizontally in either main chart to highlight an analysis interval and display its duration, average, minimum, maximum, sample count and interval mean line. Click, Escape or right-click clears the interval. Statistics include the start and exclude the end. History queries original SQLite samples; Live uses original readings retained for the current live chart. Bucket averages are never averaged for interval statistics. Zero bpm and no-contact readings are excluded. Changing the visible range clears analysis. CSV continues exporting the displayed range, independently of the analysis selection.

Connection controls use a compact bar: device/status, Connect, Stop and Details. Details opens a popup with sensor selection, Reconnect and Copy log, preserving the window and chart sizes. Dismiss it with Close, Escape, or a click outside. Theme and Settings icons share a 16px size, stroke and button dimensions. The toolbar now receives pointer input directly and calls window dragging, with double-click maximize/restore and restore-on-drag from maximized state.
Chart time axes use larger clock labels with aligned time intervals. Views of twenty minutes or less include seconds. Selected start/end times are prominent in the analysis annotation and also shown beneath the axis. Durations use hours/minutes/seconds; sub-minute selections show milliseconds. Dates remain secondary, with both dates shown across midnight.
