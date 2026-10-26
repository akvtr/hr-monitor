$ErrorActionPreference = 'Stop'
$resolver = Join-Path (Split-Path $PSScriptRoot) 'tools\resolve-build-sdk.ps1'
$directory = Join-Path $env:TEMP ('hr-build-sdk-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $directory | Out-Null
$sdkRoot = Join-Path $directory 'SDK path with spaces'
$fakeHost = Join-Path $directory 'dotnet-fixture.ps1'
$configuration = Join-Path $directory 'configuration.xml'
@'
param([string]$Operation)
$fixture = Import-Clixml (Join-Path $PSScriptRoot 'configuration.xml')
if ($Operation -eq '--version') { Write-Output $fixture.Version; exit $fixture.VersionExit }
if ($Operation -eq '--list-sdks') { Write-Output $fixture.SdkLines; exit $fixture.ListExit }
throw "Unexpected fixture command: $Operation"
'@ | Set-Content -LiteralPath $fakeHost -Encoding UTF8
$count = 0
function Check([bool]$Condition, [string]$Name) {
    if (!$Condition) { throw $Name }
    $script:count++
    Write-Output "PASS $Name"
}
function Reject([scriptblock]$Action, [string]$Message, [string]$Name) {
    try { & $Action }
    catch {
        if (!$_.Exception.Message.Contains($Message)) { throw }
        Check $true $Name
        return
    }
    throw "Expected rejection: $Name"
}
$stable = '9.0.101'
$preview = '10.0.100-preview.7.12345.1'
foreach ($version in @($stable, $preview)) {
    $path = Join-Path $sdkRoot "$version\Roslyn\bincore\csc.dll"
    New-Item -ItemType Directory -Path (Split-Path $path) -Force | Out-Null
    # This empty file is a discovery fixture and is never executed.
    New-Item -ItemType File -Path $path | Out-Null
}
$fixture = @{ Version = $stable; VersionExit = 0; ListExit = 0; SdkLines = @("$preview [$sdkRoot]", "$stable [$sdkRoot]") }
$fixture | Export-Clixml -LiteralPath $configuration
$selected = & $resolver -DotNetPath $fakeHost
Check ($selected.DotNetPath -eq $fakeHost -and $selected.Compiler -eq (Join-Path $sdkRoot "$stable\Roslyn\bincore\csc.dll")) 'SDK discovery follows CLI selection and supports paths with spaces'
$fixture.Version = $preview; $fixture | Export-Clixml -LiteralPath $configuration
$selected = & $resolver -DotNetPath $fakeHost
Check ($selected.Compiler -eq (Join-Path $sdkRoot "$preview\Roslyn\bincore\csc.dll")) 'SDK discovery preserves the complete prerelease version without System.Version parsing'
$fixture.Version = '8.0.999'; $fixture | Export-Clixml -LiteralPath $configuration
Reject { & $resolver -DotNetPath $fakeHost } 'not found in the installed SDK list' 'SDK discovery rejects an unlisted selected SDK'
$fixture.SdkLines = @("8.0.999 [$sdkRoot]"); $fixture | Export-Clixml -LiteralPath $configuration
Reject { & $resolver -DotNetPath $fakeHost } 'no Roslyn compiler' 'SDK discovery rejects a missing compiler before building'
$fixture.VersionExit = 2; $fixture | Export-Clixml -LiteralPath $configuration
Reject { & $resolver -DotNetPath $fakeHost } 'selection command failed' 'SDK discovery propagates selection command failure'
$fixture.VersionExit = 0; $fixture.ListExit = 3; $fixture | Export-Clixml -LiteralPath $configuration
Reject { & $resolver -DotNetPath $fakeHost } 'listing command failed' 'SDK discovery propagates listing command failure'
$fixture.Version = ''; $fixture.ListExit = 0; $fixture | Export-Clixml -LiteralPath $configuration
Reject { & $resolver -DotNetPath $fakeHost } 'did not report an SDK version' 'SDK discovery rejects an empty version response'
Write-Output "All $count SDK discovery checks passed. Test artifacts: $directory"
exit 0
