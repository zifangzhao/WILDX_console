param([string]$OutputDirectory = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$entries = @()
foreach ($size in @(16, 24, 32, 48, 64, 128, 256)) {
    $large = New-Object Drawing.Bitmap ($size * 4), ($size * 4)
    $g = [Drawing.Graphics]::FromImage($large)
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.ScaleTransform(($size / 16.0), ($size / 16.0))
    $shape = New-Object Drawing.Drawing2D.GraphicsPath
    $shape.AddArc(2, 2, 26, 26, 180, 90)
    $shape.AddArc(36, 2, 26, 26, 270, 90)
    $shape.AddArc(36, 36, 26, 26, 0, 90)
    $shape.AddArc(2, 36, 26, 26, 90, 90)
    $shape.CloseFigure()
    $base = New-Object Drawing.SolidBrush ([Drawing.ColorTranslator]::FromHtml('#111e2f'))
    $edge = New-Object Drawing.Pen ([Drawing.ColorTranslator]::FromHtml('#31526b')), 2
    $wave = New-Object Drawing.Pen ([Drawing.ColorTranslator]::FromHtml('#6ee7e0')), 5
    $wave.StartCap = $wave.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $wave.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
    $g.FillPath($base, $shape)
    $g.DrawPath($edge, $shape)
    $points = [Drawing.PointF[]]@(
        [Drawing.PointF]::new(9,34), [Drawing.PointF]::new(17,34),
        [Drawing.PointF]::new(23,21), [Drawing.PointF]::new(30,44),
        [Drawing.PointF]::new(38,21), [Drawing.PointF]::new(45,34),
        [Drawing.PointF]::new(55,34))
    $g.DrawLines($wave, $points)
    $small = New-Object Drawing.Bitmap $size, $size
    $resizer = [Drawing.Graphics]::FromImage($small)
    $resizer.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $resizer.DrawImage($large, 0, 0, $size, $size)
    $stream = New-Object IO.MemoryStream
    if ($size -eq 256) {
        $small.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
    } else {
        # Classic 32-bit DIB entries support legacy Windows icon/resource tools.
        $dib = New-Object IO.BinaryWriter $stream
        $maskStride = [int]([Math]::Ceiling($size / 32.0) * 4)
        $dib.Write([uint32]40); $dib.Write([int32]$size); $dib.Write([int32]($size * 2))
        $dib.Write([uint16]1); $dib.Write([uint16]32); $dib.Write([uint32]0)
        $dib.Write([uint32]($size * $size * 4 + $maskStride * $size))
        for ($i = 0; $i -lt 4; $i++) { $dib.Write([uint32]0) }
        for ($y = $size - 1; $y -ge 0; $y--) {
            for ($x = 0; $x -lt $size; $x++) {
                $color = $small.GetPixel($x, $y)
                $dib.Write([byte]$color.B); $dib.Write([byte]$color.G)
                $dib.Write([byte]$color.R); $dib.Write([byte]$color.A)
            }
        }
        for ($y = $size - 1; $y -ge 0; $y--) {
            $mask = New-Object byte[] $maskStride
            for ($x = 0; $x -lt $size; $x++) {
                if ($small.GetPixel($x, $y).A -eq 0) {
                    $byteIndex = [int][Math]::Floor($x / 8.0)
                    $mask[$byteIndex] = $mask[$byteIndex] -bor (128 -shr ($x % 8))
                }
            }
            $dib.Write([byte[]]$mask)
        }
        $dib.Flush()
    }
    $entries += @{ Size = $size; Bytes = $stream.ToArray() }
    if ($size -eq 256) { $small.Save((Join-Path $OutputDirectory 'wildx-wave.png'), [Drawing.Imaging.ImageFormat]::Png) }
    $stream.Dispose(); $resizer.Dispose(); $small.Dispose()
    $wave.Dispose(); $edge.Dispose(); $base.Dispose(); $shape.Dispose(); $g.Dispose(); $large.Dispose()
}
$file = [IO.File]::Create((Join-Path $OutputDirectory 'wildx-wave.ico'))
$writer = New-Object IO.BinaryWriter $file
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$entries.Count)
    $offset = 6 + 16 * $entries.Count
    foreach ($entry in $entries) {
        $encodedSize = if ($entry.Size -eq 256) { 0 } else { $entry.Size }
        $writer.Write([byte]$encodedSize); $writer.Write([byte]$encodedSize)
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$entry.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $entry.Bytes.Length
    }
    foreach ($entry in $entries) { $writer.Write([byte[]]$entry.Bytes) }
} finally { $writer.Dispose(); $file.Dispose() }
Write-Output "Built 7 icon sizes (16-256 px): $(Join-Path $OutputDirectory 'wildx-wave.ico')"
