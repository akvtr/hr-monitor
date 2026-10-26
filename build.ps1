param([switch]$Test, [switch]$Diagnostic)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
Push-Location $root
try { $sdk = & (Join-Path $root 'tools\resolve-build-sdk.ps1') }
finally { Pop-Location }
$compiler = $sdk.Compiler
$dotnet = $sdk.DotNetPath
$metadata = & (Join-Path $root 'tools\resolve-windows-metadata.ps1')
if (!$metadata -or !(Test-Path $compiler)) { throw '.NET SDK and Windows 10/11 SDK are required to build.' }
# Development builds always replace the files directly under artifacts.
$out = Join-Path $root 'artifacts'
New-Item -ItemType Directory -Force $out | Out-Null
$iconPath = Join-Path $out 'AppIcon.ico'
& (Join-Path $root 'tools\generate-app-icon.ps1') -OutputPath $iconPath
if (!(Test-Path $iconPath)) { throw 'Application icon generation failed.' }
$refs = @('mscorlib.dll','System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Xaml.dll','System.Xml.dll','System.Runtime.Serialization.dll','System.Runtime.WindowsRuntime.dll','System.Web.Extensions.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$refs += '/reference:' + $metadata.FullName
$facades = @('System.Runtime','System.Runtime.InteropServices.WindowsRuntime','System.ObjectModel')
foreach ($name in $facades) {
    $dll = Get-ChildItem (Join-Path $env:WINDIR "Microsoft.NET\assembly\GAC_MSIL\$name") -Recurse -Filter "$name.dll" | Select-Object -First 1
    if ($dll) { $refs += '/reference:' + $dll.FullName }
}
$source = Get-ChildItem (Join-Path $root 'HeartRateMonitor') -Filter '*.cs' -Recurse | ForEach-Object FullName
$common = @('/nologo','/nostdlib+','/langversion:latest','/nullable:disable','/platform:x64','/optimize+')
$manifestPath = Join-Path $root 'HeartRateMonitor\app.manifest'
& $dotnet $compiler @common @refs '/target:winexe' "/out:$out\HeartRateMonitor.exe" "/win32manifest:$manifestPath" "/win32icon:$iconPath" "/resource:$root\HeartRateMonitor\Theme.xaml,HeartRateMonitor.Theme.xaml" "/resource:$iconPath,HeartRateMonitor.AppIcon.ico" @source
if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
Copy-Item (Join-Path $root 'HeartRateMonitor\App.config') (Join-Path $out 'HeartRateMonitor.exe.config') -Force
if ($Test) {
    & (Join-Path $root 'tests\build-sdk.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'SDK discovery tests failed.' }
    & (Join-Path $root 'tests\build-metadata.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Windows metadata discovery tests failed.' }
    $testSources = @(Get-ChildItem (Join-Path $root 'tests') -Filter 'Tests*.cs' | Sort-Object Name | ForEach-Object FullName)
    $testSources += Join-Path $root 'tests\BluetoothDiagnostic.Queries.cs'
    & $dotnet $compiler @common @refs '/target:exe' "/out:$out\HeartRateMonitor.Tests.exe" "/reference:$out\HeartRateMonitor.exe" @testSources
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
    Copy-Item (Join-Path $root 'HeartRateMonitor\App.config') (Join-Path $out 'HeartRateMonitor.Tests.exe.config') -Force
    & (Join-Path $out 'HeartRateMonitor.Tests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
if ($Diagnostic) {
    $diagnosticSources = @(Get-ChildItem (Join-Path $root 'tests') -Filter 'BluetoothDiagnostic*.cs' | Sort-Object Name | ForEach-Object FullName)
    & $dotnet $compiler @common @refs '/target:exe' "/out:$out\BluetoothDiagnostic.exe" "/reference:$out\HeartRateMonitor.exe" @diagnosticSources
    if ($LASTEXITCODE -ne 0) { throw 'Diagnostic build failed.' }
    Copy-Item (Join-Path $root 'HeartRateMonitor\App.config') (Join-Path $out 'BluetoothDiagnostic.exe.config') -Force
    & (Join-Path $out 'BluetoothDiagnostic.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Bluetooth diagnostic failed.' }
}
Copy-Item -LiteralPath (Join-Path $root 'README.md'),(Join-Path $root 'README.en.md') -Destination $out -Force
$packageFiles = @('HeartRateMonitor.exe','HeartRateMonitor.exe.config','README.md','README.en.md') | ForEach-Object { Join-Path $out $_ }
Compress-Archive -LiteralPath $packageFiles -DestinationPath (Join-Path $out 'HeartRateMonitor-win-x64.zip') -Force
Write-Host "Built: $out\HeartRateMonitor.exe"
