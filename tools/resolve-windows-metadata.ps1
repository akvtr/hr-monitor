param([string]$MetadataRoot = 'C:\Program Files (x86)\Windows Kits\10\UnionMetadata')
$ErrorActionPreference = 'Stop'

if (!(Test-Path -LiteralPath $MetadataRoot -PathType Container)) { throw 'Windows SDK metadata directory was not found.' }
$candidates = @(
    foreach ($directory in Get-ChildItem -LiteralPath $MetadataRoot -Directory) {
        $version = $null
        if (![version]::TryParse($directory.Name, [ref]$version)) { continue }
        $path = Join-Path $directory.FullName 'Windows.winmd'
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            [pscustomobject]@{ Version = $version; Path = $path }
        }
    }
)
$selected = $candidates | Sort-Object Version -Descending | Select-Object -First 1
if ($selected) { return Get-Item -LiteralPath $selected.Path }
# Older SDK installations can expose the union metadata directly at the root.
$legacy = Join-Path $MetadataRoot 'Windows.winmd'
if (Test-Path -LiteralPath $legacy -PathType Leaf) { return Get-Item -LiteralPath $legacy }
throw 'Windows SDK union metadata was not found. Install the Windows SDK.'
