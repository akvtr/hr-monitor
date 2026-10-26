$ErrorActionPreference = 'Stop'
$resolver = Join-Path (Split-Path $PSScriptRoot) 'tools\resolve-windows-metadata.ps1'
$directory = Join-Path $env:TEMP ('hr-metadata-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $directory | Out-Null
$count = 0
function Check([bool]$Condition, [string]$Name) {
    if (!$Condition) { throw $Name }
    $script:count++
    Write-Output "PASS $Name"
}
function Reject([string]$Root, [string]$Name) {
    try { & $resolver -MetadataRoot $Root | Out-Null }
    catch { Check $true $Name; return }
    throw "Expected rejection: $Name"
}
foreach ($relative in @('10.0.9999.0\Windows.winmd', '10.0.26100.0\Windows.winmd',
    '10.0.99999.0\Facade\Windows.winmd', 'Facade\Windows.winmd', 'unversioned\Windows.winmd', 'Windows.winmd')) {
    $path = Join-Path $directory $relative
    New-Item -ItemType Directory -Path (Split-Path $path) -Force | Out-Null
    # Discovery fixtures are empty and never passed to the compiler.
    New-Item -ItemType File -Path $path | Out-Null
}
$selected = & $resolver -MetadataRoot $directory
Check ($selected.FullName -eq (Join-Path $directory '10.0.26100.0\Windows.winmd')) 'Windows metadata uses numeric SDK versions and ignores facade-only or unversioned directories'
$legacy = Join-Path $directory 'legacy'
New-Item -ItemType Directory -Path $legacy | Out-Null
New-Item -ItemType File -Path (Join-Path $legacy 'Windows.winmd') | Out-Null
Check ((& $resolver -MetadataRoot $legacy).FullName -eq (Join-Path $legacy 'Windows.winmd')) 'Windows metadata supports the legacy root layout'
Reject (Join-Path $directory 'missing') 'Windows metadata reports a missing SDK directory'
$empty = Join-Path $directory 'empty'
New-Item -ItemType Directory -Path $empty | Out-Null
Reject $empty 'Windows metadata reports an SDK directory without union metadata'
Write-Output "All $count Windows metadata checks passed. Test artifacts: $directory"
exit 0
