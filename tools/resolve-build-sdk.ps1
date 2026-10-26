param([string]$DotNetPath = (Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1 -ExpandProperty Source))
$ErrorActionPreference = 'Stop'

# Let the CLI resolve SDK policy, including global.json and prerelease versions.
$versionOutput = @(& $DotNetPath --version)
if ($LASTEXITCODE -ne 0) { throw 'The .NET SDK selection command failed.' }
$version = ($versionOutput | Where-Object { ![string]::IsNullOrWhiteSpace($_) } | Select-Object -Last 1)
if (!$version) { throw 'The .NET CLI did not report an SDK version.' }
$version = $version.Trim()
$installed = @(& $DotNetPath --list-sdks)
if ($LASTEXITCODE -ne 0) { throw 'The .NET SDK listing command failed.' }
$pattern = '^' + [regex]::Escape($version) + ' \[(.+)\]$'
foreach ($entry in $installed) {
    if ($entry -match $pattern) {
        $compiler = Join-Path $Matches[1] "$version\Roslyn\bincore\csc.dll"
        if (!(Test-Path -LiteralPath $compiler -PathType Leaf)) { throw "The selected .NET SDK has no Roslyn compiler: $compiler" }
        return [pscustomobject]@{ DotNetPath = $DotNetPath; Compiler = $compiler }
    }
}
throw "The selected .NET SDK was not found in the installed SDK list: $version"
