param([string]$OutputPath = (Join-Path $PSScriptRoot '..\artifacts\AppIcon.ico'))
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$iconSource = Join-Path $PSScriptRoot '..\HeartRateMonitor\AppIcon.xaml'
$iconStream = [System.IO.File]::OpenRead($iconSource)
try { $dictionary = [System.Windows.Markup.XamlReader]::Load($iconStream) }
finally { $iconStream.Dispose() }
$drawing = $dictionary['AppIconDrawing']
if (!$drawing) { throw 'AppIconDrawing was not found in AppIcon.xaml.' }

$sizes = @(256, 128, 64, 48, 32, 24, 16)
$pngFrames = New-Object 'System.Collections.Generic.List[byte[]]'
foreach ($size in $sizes) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    $context = $visual.RenderOpen()
    $scale = $size / 256.0
    $context.PushTransform([System.Windows.Media.ScaleTransform]::new($scale, $scale))
    $context.DrawImage($drawing, [System.Windows.Rect]::new(0, 0, 256, 256))
    $context.Pop()
    $context.Close()

    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $pngStream = New-Object System.IO.MemoryStream
    try { $encoder.Save($pngStream); $pngFrames.Add($pngStream.ToArray()) }
    finally { $pngStream.Dispose() }
}

$iconStream = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($iconStream)
try {
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $size = $sizes[$i]
        $dimension = if ($size -eq 256) { [byte]0 } else { [byte]$size }
        $writer.Write($dimension)
        $writer.Write($dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]32)
        $writer.Write([UInt32]$pngFrames[$i].Length)
        $writer.Write([UInt32]$offset)
        $offset += $pngFrames[$i].Length
    }
    foreach ($frame in $pngFrames) { $writer.Write($frame) }
    $writer.Flush()
    $target = [System.IO.Path]::GetFullPath($OutputPath)
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($target)) | Out-Null
    [System.IO.File]::WriteAllBytes($target, $iconStream.ToArray())
} finally { $writer.Dispose(); $iconStream.Dispose() }
Write-Host "Generated: $target"
