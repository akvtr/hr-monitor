param([switch]$Test, [switch]$Diagnostic, [string]$OutputDirectory = 'artifacts')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$sdk = Get-ChildItem 'C:\Program Files\dotnet\sdk' -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$compiler = Join-Path $sdk.FullName 'Roslyn\bincore\csc.dll'
$metadata = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\UnionMetadata' -Recurse -Filter Windows.winmd | Where-Object { $_.FullName -notmatch 'Facade' } | Sort-Object FullName -Descending | Select-Object -First 1
if (!$metadata -or !(Test-Path $compiler)) { throw '.NET SDK and Windows 10/11 SDK are required to build.' }
$versionSource = [IO.File]::ReadAllText((Join-Path $root 'HeartRateMonitor\AppVersion.cs'))
$versionPattern = 'internal const string Product = "([^"]+)";'
$versionMatch = [regex]::Match($versionSource, $versionPattern)
if (!$versionMatch.Success) { throw 'Product version was not found in AppVersion.cs.' }
$productVersion = $versionMatch.Groups[1].Value
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $root 'artifacts'))
$requestedOutput = [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
$defaultOutput = $requestedOutput.TrimEnd('\') -eq $artifactRoot.TrimEnd('\')
$publishLatest = $defaultOutput
$out = if ($defaultOutput) { Join-Path $artifactRoot ('version-' + $productVersion) } else { $requestedOutput }
$releaseNames = @('HeartRateMonitor.exe','HeartRateMonitor.exe.config','AppIcon.ico','HeartRateMonitor.Tests.exe','HeartRateMonitor.Tests.exe.config','HeartRateMonitor-win-x64.zip')
# Preserve the previous published release before replacing the latest files.
$previousExe = Join-Path $artifactRoot 'HeartRateMonitor.exe'
if ($publishLatest -and (Test-Path -LiteralPath $previousExe)) {
    $previousVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($previousExe).ProductVersion
    if ($previousVersion -match '^\d+\.\d+\.\d+(?:-(?:beta|rc)\.\d+)?$' -and $previousVersion -ne $productVersion) {
        $previousOutput = Join-Path $artifactRoot ('version-' + $previousVersion)
        New-Item -ItemType Directory -Force $previousOutput | Out-Null
        foreach ($name in $releaseNames) {
            $previousFile = Join-Path $artifactRoot $name
            if (Test-Path -LiteralPath $previousFile) { Copy-Item -LiteralPath $previousFile -Destination (Join-Path $previousOutput $name) -Force }
        }
    }
}
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
& dotnet $compiler @common @refs '/target:winexe' "/out:$out\HeartRateMonitor.exe" "/win32manifest:$manifestPath" "/win32icon:$iconPath" "/resource:$root\HeartRateMonitor\Theme.xaml,HeartRateMonitor.Theme.xaml" "/resource:$iconPath,HeartRateMonitor.AppIcon.ico" @source
if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
Copy-Item (Join-Path $root 'HeartRateMonitor\App.config') (Join-Path $out 'HeartRateMonitor.exe.config') -Force
if ($Test) {
    $testSources = @((Join-Path $root 'tests\Tests.cs'))
    & dotnet $compiler @common @refs '/target:exe' "/out:$out\HeartRateMonitor.Tests.exe" "/reference:$out\HeartRateMonitor.exe" @testSources
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
    Copy-Item (Join-Path $root 'HeartRateMonitor\App.config') (Join-Path $out 'HeartRateMonitor.Tests.exe.config') -Force
    & (Join-Path $out 'HeartRateMonitor.Tests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
if ($Diagnostic) {
    & dotnet $compiler @common @refs '/target:exe' "/out:$out\BluetoothDiagnostic.exe" "/reference:$out\HeartRateMonitor.exe" (Join-Path $root 'tests\BluetoothDiagnostic.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Diagnostic build failed.' }
    Copy-Item (Join-Path $root 'HeartRateMonitor\App.config') (Join-Path $out 'BluetoothDiagnostic.exe.config') -Force
    & (Join-Path $out 'BluetoothDiagnostic.exe')
}
if ($defaultOutput) {
    # Publish only after compilation and the requested checks have succeeded.
    Copy-Item -LiteralPath (Join-Path $root 'README.md'),(Join-Path $root 'README.en.md') -Destination $out -Force
    $packageFiles = @('HeartRateMonitor.exe','HeartRateMonitor.exe.config','README.md','README.en.md') | ForEach-Object { Join-Path $out $_ }
    Compress-Archive -LiteralPath $packageFiles -DestinationPath (Join-Path $out 'HeartRateMonitor-win-x64.zip') -Force
    if ($publishLatest) { foreach ($name in $releaseNames) {
        $releaseFile = Join-Path $out $name
        if (Test-Path -LiteralPath $releaseFile) { Copy-Item -LiteralPath $releaseFile -Destination (Join-Path $artifactRoot $name) -Force }
    }
    Write-Host "Latest: $artifactRoot\HeartRateMonitor.exe" }
}
Write-Host "Built: $out\HeartRateMonitor.exe"

