param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\Assets')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$outputPath = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($outputPath) | Out-Null

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$images = New-Object System.Collections.Generic.List[object]

foreach ($size in $sizes) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $margin = [Math]::Max(1, [int][Math]::Round($size * 0.035))
    $bounds = New-Object System.Drawing.Rectangle $margin, $margin, ($size - 2 * $margin), ($size - 2 * $margin)
    $radius = [Math]::Max(2, [int][Math]::Round($size * 0.22))
    $diameter = [Math]::Min($radius * 2, [Math]::Min($bounds.Width, $bounds.Height))
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $arc = New-Object System.Drawing.Rectangle $bounds.X, $bounds.Y, $diameter, $diameter
    $path.AddArc($arc, 180, 90)
    $arc.X = $bounds.Right - $diameter
    $path.AddArc($arc, 270, 90)
    $arc.Y = $bounds.Bottom - $diameter
    $path.AddArc($arc, 0, 90)
    $arc.X = $bounds.Left
    $path.AddArc($arc, 90, 90)
    $path.CloseFigure()

    $startColor = [System.Drawing.Color]::FromArgb(36, 107, 253)
    $endColor = [System.Drawing.Color]::FromArgb(13, 148, 136)
    $gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush $bounds, $startColor, $endColor, ([System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
    $graphics.FillPath($gradient, $path)

    $penWidth = [Math]::Max(1.4, $size * 0.075)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), $penWidth
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

    $headsetRect = New-Object System.Drawing.RectangleF ($size * 0.23), ($size * 0.20), ($size * 0.54), ($size * 0.52)
    $graphics.DrawArc($pen, $headsetRect, 205, 130)
    $graphics.DrawLine($pen, [single]($size * 0.23), [single]($size * 0.45), [single]($size * 0.23), [single]($size * 0.70))
    $graphics.DrawLine($pen, [single]($size * 0.77), [single]($size * 0.45), [single]($size * 0.77), [single]($size * 0.70))
    $graphics.DrawLine($pen, [single]($size * 0.23), [single]($size * 0.70), [single]($size * 0.34), [single]($size * 0.70))
    $graphics.DrawLine($pen, [single]($size * 0.66), [single]($size * 0.70), [single]($size * 0.77), [single]($size * 0.70))

    if ($size -eq 256) {
        $bitmap.Save((Join-Path $outputPath 'BluetoothAudioRelay-256.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    }

    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $images.Add([pscustomobject]@{ Size = $size; Data = $stream.ToArray() })

    $stream.Dispose()
    $pen.Dispose()
    $gradient.Dispose()
    $path.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}

$iconPath = Join-Path $outputPath 'BluetoothAudioRelay.ico'
$fileStream = [System.IO.File]::Create($iconPath)
$writer = New-Object System.IO.BinaryWriter $fileStream

$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$images.Count)

$offset = 6 + (16 * $images.Count)
foreach ($image in $images) {
    $dimension = if ($image.Size -ge 256) { 0 } else { $image.Size }
    $writer.Write([byte]$dimension)
    $writer.Write([byte]$dimension)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$image.Data.Length)
    $writer.Write([uint32]$offset)
    $offset += $image.Data.Length
}

foreach ($image in $images) {
    $writer.Write($image.Data)
}

$writer.Dispose()
$fileStream.Dispose()

Get-Item -LiteralPath $iconPath, (Join-Path $outputPath 'BluetoothAudioRelay-256.png') |
    Select-Object FullName, Length, LastWriteTime
