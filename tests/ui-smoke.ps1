# Run on an interactive Windows desktop after build.ps1. Uses an isolated database.
param([string]$AppPath = '', [switch]$LayoutOnly)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class WindowVisibility { [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr handle); [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam); }'
if (!$AppPath) { $AppPath = Join-Path (Split-Path $PSScriptRoot) 'artifacts\HeartRateMonitor.exe' }
$dataPath = Join-Path $env:TEMP ('hr-ui-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $dataPath | Out-Null
[void][Reflection.Assembly]::LoadFrom($AppPath)
$store = New-Object HeartRateMonitor.Store((Join-Path $dataPath 'heartrate.db'))
try {
    $base = [DateTimeOffset]::UtcNow.AddHours(-2).ToUnixTimeMilliseconds()
    for ($i=0; $i -lt 120; $i++) {
        $row = New-Object HeartRateMonitor.Reading
        $row.UnixMs = $base + $i * 60000; $row.Bpm = 70 + $i % 25; $row.Device = 'ISOLATED UI TEST'; $row.Session = 'test'
        $store.Add($row)
    }
} finally { $store.Dispose() }
$appProcess = Start-Process -FilePath $appPath -ArgumentList @('--data-dir', ('"' + $dataPath + '"')) -WindowStyle Hidden -PassThru
try {
    $window = $null
    $deadline = [datetime]::UtcNow.AddSeconds(10)
    $processCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $appProcess.Id)
    $windowCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)
    $appWindowCondition = New-Object System.Windows.Automation.AndCondition($processCondition, $windowCondition)
    $readyCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'MainTabs')
    do {
        Start-Sleep -Milliseconds 100
        $appProcess.Refresh()
        foreach ($candidate in [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $appWindowCondition)) {
            if ($candidate.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $readyCondition)) { $window = $candidate; break }
        }
    } while (!$window -and !$appProcess.HasExited -and [datetime]::UtcNow -lt $deadline)
    if (!$window) { throw 'Application window was not created.' }
    # MainWindowHandle can refer to a tray helper HWND; use the actual WPF window.
    $windowHandle = [IntPtr]$window.Current.NativeWindowHandle
    function Find-Control([string]$automationId) {
        $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)
        $controlDeadline = [datetime]::UtcNow.AddSeconds(3)
        do {
            $element = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
            if ($element) { return $element }
            Start-Sleep -Milliseconds 50
        } while ([datetime]::UtcNow -lt $controlDeadline)
        if (!$element) { throw "Control not found: $automationId" }
    }
    if (!$LayoutOnly) {
    $stop = Find-Control 'StopConnection'
    $start = Find-Control 'AutoConnect'
    if (!$stop.Current.IsEnabled -or $start.Current.IsEnabled) { throw 'Startup auto-connect did not start.' }
    Write-Output 'PASS startup automatically begins discovery'
    $stop.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $deadline = [datetime]::UtcNow.AddSeconds(5)
    while (!$start.Current.IsEnabled -and [datetime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
    if (!$start.Current.IsEnabled -or $stop.Current.IsEnabled) { throw 'Stop did not cancel discovery.' }
    Write-Output 'PASS stop button cancels real Bluetooth discovery'
    Start-Sleep -Milliseconds 1200
    if (!$start.Current.IsEnabled) { throw 'Discovery restarted without an explicit request.' }
    Write-Output 'PASS user pause remains stopped'
    $start.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 300
    if (!$stop.Current.IsEnabled -or $start.Current.IsEnabled) { throw 'Restart did not begin discovery.' }
    Write-Output 'PASS automatic discovery can be restarted'
    foreach ($spanIndex in @(2,1,0)) {
        $liveSpan = Find-Control ('LiveScale' + $spanIndex)
        $liveSelection = $liveSpan.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        $liveSelection.Select()
        if (!$liveSelection.Current.IsSelected) { throw 'Live time span was not selected.' }
    }
    if (!(Find-Control 'LiveHeartRate')) { throw 'Current heart rate missing from live view.' }
    Write-Output 'PASS graph-focused live view retains current heart rate and all three time spans'
    $liveSlider = (Find-Control 'LiveSpanSlider').GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)
    $liveSlider.SetValue(0)
    Start-Sleep -Milliseconds 200
    if ((Find-Control 'LiveScale0').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) { throw 'Custom live span retained a stale preset selection.' }
    if ((Find-Control 'LiveSpanSliderValue').Current.Name -notmatch '1') { throw 'Live span label was not updated.' }
    (Find-Control 'LiveScale0').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    (Find-Control 'Tab1').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 600
    if (!(Find-Control 'HistorySelection').Current.IsEnabled) { throw 'History overview move handle is disabled with recordings.' }
    $latestRange = (Find-Control 'HistoryVisibleRange').Current.Name
    (Find-Control 'HistoryPrevious').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 350
    if ((Find-Control 'HistoryVisibleRange').Current.Name -eq $latestRange) { throw 'Previous did not move the selected range.' }
    Write-Output 'PASS overview range can navigate old recordings'
    foreach ($spanIndex in @(1,2,3,4,0)) {
        $span = Find-Control ('HistoryScale' + $spanIndex)
        $selection = $span.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        $selection.Select()
        Start-Sleep -Milliseconds 350
        if ($spanIndex -lt 3 -and !$selection.Current.IsSelected) { throw ('History time span was not selected: ' + $spanIndex) }
        if ($spanIndex -ge 3 -and !(Find-Control 'HistoryScale5').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) { throw 'Long presets should clamp to All when fewer recordings are available.' }
    }
    Write-Output 'PASS time spans select directly and clamp to available recordings'
    $spanSlider = (Find-Control 'HistorySpanSlider').GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)
    $spanSlider.SetValue(0)
    Start-Sleep -Milliseconds 300
    if ((Find-Control 'HistoryScale0').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) { throw 'Custom history span retained a stale preset selection.' }
    $spanSlider.SetValue($spanSlider.Current.Maximum)
    Start-Sleep -Milliseconds 300
    if (!(Find-Control 'HistoryScale5').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) { throw 'History slider did not reach all recordings.' }
    (Find-Control 'HistoryScale0').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Write-Output 'PASS live and history sliders synchronize custom spans, presets and all recordings'
    (Find-Control 'HistoryLatest').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 400
    if ((Find-Control 'HistoryNext').Current.IsEnabled) { throw 'Next should be disabled at the latest window.' }
    if (!(Find-Control 'HistoryPrevious').Current.IsEnabled) { throw 'Previous should be available with two hours of data.' }
    for ($step = 0; $step -lt 16 -and (Find-Control 'HistoryPrevious').Current.IsEnabled; $step++) {
        (Find-Control 'HistoryPrevious').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Start-Sleep -Milliseconds 150
    }
    if ((Find-Control 'HistoryPrevious').Current.IsEnabled) { throw 'Previous should be disabled at the oldest window.' }
    Write-Output 'PASS previous and next reflect the displayed recording bounds'
    } else {
        (Find-Control 'StopConnection').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        (Find-Control 'Tab1').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Start-Sleep -Milliseconds 400
    }
    function Assert-HistoryFits {
        $viewport = $window
        $bounds = $viewport.Current.BoundingRectangle
        $scrollCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ScrollBar)
        foreach ($scrollbar in $viewport.FindAll([System.Windows.Automation.TreeScope]::Descendants, $scrollCondition)) {
            if (!$scrollbar.Current.IsOffscreen) { throw 'History contains a visible scrollbar.' }
        }
        foreach ($removed in @('HistoryExactTime','HistoryJumpDate','HistoryJump')) {
            $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $removed)
            if ($viewport.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)) { throw ('Date/time jump remains: ' + $removed) }
        }
        foreach ($id in @('HistorySelection','HistoryScale0','HistoryScale5','HistorySpanSlider','HistoryVisibleRange','HistorySummary','HistoryPrevious','HistoryNext','HistoryLatest','HistoryExport','HistoryRetentionNotice')) {
            $control = Find-Control $id
            $rect = $control.Current.BoundingRectangle
            if ($rect.IsEmpty -or $rect.Width -le 0 -or $rect.Height -le 0 -or $rect.Left -lt $bounds.Left - 1 -or $rect.Top -lt $bounds.Top - 1 -or $rect.Right -gt $bounds.Right + 1 -or $rect.Bottom -gt $bounds.Bottom + 1) { throw ('History control does not fit: ' + $id + ' ' + $rect + ' within ' + $bounds) }
        }
    }
    Assert-HistoryFits
    $transform = $window.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern)
    $transform.Resize(1020,760)
    Start-Sleep -Milliseconds 250
    Assert-HistoryFits
    (Find-Control 'ChooseSensor').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 250
    Assert-HistoryFits
    (Find-Control 'ChooseSensor').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $transform.Resize(1200,880)
    Write-Output 'PASS history fits without scrollbars or date/time jump, including compact layout and connection details'
    $dataGridCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::DataGrid)
    if ($window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $dataGridCondition)) { throw 'A received readings table remains visible.' }
    Write-Output 'PASS history has no received readings table'
    (Find-Control 'Tab2').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    function Select-Retention([string]$label) {
        $retention = Find-Control 'RetentionChoice'
        $retention.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
        $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $label)
        $option = $retention.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if (!$option) { throw "Retention option not found: $label" }
        $option.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    }
    $contentsBounds = (Find-Control 'SettingsCategory0').Current.BoundingRectangle
    for ($category = 0; $category -lt 7; $category++) {
        $categoryControl = Find-Control ('SettingsCategory' + $category)
        $categoryControl.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Start-Sleep -Milliseconds 100
        if (!$categoryControl.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) { throw "Settings category selection did not navigate: $category" }
        if ((Find-Control 'SettingsCategory0').Current.BoundingRectangle.Top -ne $contentsBounds.Top) { throw 'Settings contents moved with the form.' }
    }
    (Find-Control 'SettingsCategory2').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Write-Output 'PASS all seven settings categories switch content and stay fixed'
    $selectedRetention = (Find-Control 'RetentionChoice').GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
    if ($selectedRetention[0].Current.Name -ne '無期限') { throw 'Retention should default to unlimited.' }
    Select-Retention '7日'
    if ((Find-Control 'RetentionHelp').Current.Name -notmatch '復元') { throw 'Finite retention needs an explicit deletion warning.' }
    (Find-Control 'Tab1').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    if ((Find-Control 'HistoryRetentionNotice').Current.Name -notmatch '無期限') { throw 'Unsaved retention must not change the active policy.' }
    (Find-Control 'Tab2').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    (Find-Control 'SaveSettings').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 250
    if ((Get-Content -Raw -Encoding UTF8 (Join-Path $dataPath 'settings.json') | ConvertFrom-Json).RetentionDays -ne 7) { throw 'Finite retention was not persisted.' }
    if (!(Find-Control 'Tab2').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) { throw 'Save should remain on Settings.' }
    (Find-Control 'Tab1').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    if ((Find-Control 'HistoryRetentionNotice').Current.Name -notmatch '7日') { throw 'History retention notice was not updated.' }
    (Find-Control 'Tab2').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Write-Output 'PASS retention defaults to unlimited, warns before deletion, and persists after save'
    (Find-Control 'SettingsCategory6').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    if (!(Find-Control 'TestOsc')) { throw 'One-shot OSC test was not moved to Settings.' }
    Write-Output 'PASS Settings retains the isolated one-shot OSC test control'
    (Find-Control 'SettingsCategory0').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $language = Find-Control 'LanguageChoice'
    $language.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    $englishCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'English')
    $english = $language.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $englishCondition)
    $english.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    (Find-Control 'SaveSettings').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 400
    if ((Find-Control 'SaveSettings').Current.Name -ne 'Save & apply') { throw 'English UI was not applied immediately.' }
    if ((Get-Content -Raw -Encoding UTF8 (Join-Path $dataPath 'settings.json') | ConvertFrom-Json).Language -ne 'en') { throw 'English language not persisted.' }
    Write-Output 'PASS English applies immediately and persists'
    (Find-Control 'SettingsCategory2').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $selectedRetention = (Find-Control 'RetentionChoice').GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
    if ($selectedRetention[0].Current.Name -ne '7 days') { throw 'Retention selection was lost during language change.' }
    Select-Retention 'Unlimited'
    (Find-Control 'SaveSettings').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 250
    if ((Get-Content -Raw -Encoding UTF8 (Join-Path $dataPath 'settings.json') | ConvertFrom-Json).RetentionDays -ne 0) { throw 'Unlimited retention was not persisted.' }
    (Find-Control 'Tab1').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    if ((Find-Control 'HistoryRetentionNotice').Current.Name -notmatch 'unlimited') { throw 'English history retention notice was not updated.' }
    Write-Output 'PASS retention survives language change and can be returned to unlimited'
    $transform.Resize(1020,760)
    Start-Sleep -Milliseconds 250
    Assert-HistoryFits
    Write-Output 'PASS English history fits the compact window'
    [void][WindowVisibility]::PostMessage($windowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 400
    $appProcess.Refresh()
    if ($appProcess.HasExited -or [WindowVisibility]::IsWindowVisible($windowHandle)) { throw 'Close did not hide the window to the tray.' }
    Write-Output 'PASS close keeps application resident in tray'
    $second = Start-Process -FilePath $appPath -ArgumentList @('--data-dir', ('"' + $dataPath + '/"')) -WindowStyle Hidden -PassThru
    if (!$second.WaitForExit(5000) -or $second.ExitCode -ne 0) { throw 'Second launch did not signal the existing instance.' }
    Start-Sleep -Milliseconds 300
    $appProcess.Refresh()
    if (![WindowVisibility]::IsWindowVisible($windowHandle)) { throw 'Second launch did not restore the tray window.' }
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($windowHandle)
    Write-Output 'PASS launching again restores the same tray instance'
} finally {
    if (!$appProcess.HasExited) {
        (Find-Control 'Tab2').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        (Find-Control 'SettingsCategory1').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        # Exit is now a tray action. Disable close-to-tray in this isolated profile
        # and close the actual WPF window instead of looking for a removed button.
        $trayNameJa = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, '閉じるボタンでタスクトレイに格納')
        $trayNameEn = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Keep running in the tray when closed')
        $trayCondition = New-Object System.Windows.Automation.OrCondition($trayNameJa, $trayNameEn)
        $trayChoice = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $trayCondition)
        if (!$trayChoice) { throw 'Close-to-tray setting was not found for test cleanup.' }
        $toggle = $trayChoice.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
        if ($toggle.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::Off) { $toggle.Toggle() }
        $save = Find-Control 'SaveSettings'
        $previousSaveId = $save.GetRuntimeId() -join ':'
        $save.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        # Invoke can return before WPF runs the handler. Wait for the rebuilt form
        # and persisted setting before closing, otherwise Close may still hide to tray.
        $deadline = [datetime]::UtcNow.AddSeconds(8)
        $saved = $false
        do {
            Start-Sleep -Milliseconds 50
            $currentSaveId = (Find-Control 'SaveSettings').GetRuntimeId() -join ':'
            $profile = Get-Content -Raw -Encoding UTF8 (Join-Path $dataPath 'settings.json') | ConvertFrom-Json
            $saved = $currentSaveId -ne $previousSaveId -and !$profile.CloseToTray
        } while (!$saved -and [datetime]::UtcNow -lt $deadline)
        if (!$saved) { throw 'Close-to-tray change was not saved and applied before cleanup.' }
        $windowHandle = [IntPtr]$window.Current.NativeWindowHandle
        if (![WindowVisibility]::PostMessage($windowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)) { throw 'Could not request test window closure.' }
        if (!$appProcess.WaitForExit(8000)) { throw "Test process did not exit cleanly: $($appProcess.Id)" }
    }
}
if ($appProcess.ExitCode -ne 0) { throw "Application failed: $($appProcess.ExitCode)" }
Write-Output "PASS clean exit during discovery. Isolated data: $dataPath"
