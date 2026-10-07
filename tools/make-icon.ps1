<#
.SYNOPSIS
  Рисует иконку приложения Grapher и сохраняет её в assets\grapher.ico.

.DESCRIPTION
  Иконка — график в осях на синем фоне. Каждый размер (16…256 пикселей) рисуется отдельно,
  с толщинами линий под этот размер, поэтому мелкие значки остаются чёткими.
  Запускать нужно только если хочется изменить рисунок: готовый файл лежит в репозитории.

.PARAMETER PreviewPath
  Необязательный путь к PNG, куда сохранить все размеры рядом (для просмотра результата).
#>
param(
    [string]$PreviewPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-RoundedRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # Все координаты заданы для холста 256×256 и масштабируются.
    $s = $size / 256.0
    $small = $size -le 24
    $tiny = $size -le 16

    # Фон: скруглённый квадрат с градиентом.
    $inset = if ($small) { 0 } else { 6 * $s }
    $side = $size - 2 * $inset
    $rect = New-Object System.Drawing.RectangleF($inset, $inset, $side, $side)
    $back = New-RoundedRect $inset $inset $side $side ($side * 0.2)
    $gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect,
        [System.Drawing.Color]::FromArgb(255, 52, 136, 238), [System.Drawing.Color]::FromArgb(255, 18, 76, 176), 90.0)
    $g.FillPath($gradient, $back)

    # Поле графика.
    $left = 62 * $s; $bottom = 196 * $s; $right = 208 * $s; $top = 52 * $s
    if ($small) { $left = 56 * $s; $bottom = 204 * $s; $right = 218 * $s; $top = 40 * $s }

    # Сетка — только на крупных значках.
    if ($size -ge 48) {
        $gridPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(70, 255, 255, 255), [single][Math]::Max(1, 3 * $s))
        foreach ($k in 1..3) {
            $x = $left + ($right - $left) * $k / 3.0
            $y = $bottom - ($bottom - $top) * $k / 3.0
            $g.DrawLine($gridPen, [single]$x, [single]$top, [single]$x, [single]$bottom)
            $g.DrawLine($gridPen, [single]$left, [single]$y, [single]$right, [single]$y)
        }
        $gridPen.Dispose()
    }

    # Оси.
    $axisWidth = if ($tiny) { 1.6 } elseif ($small) { 2.0 } else { [Math]::Max(2.5, 13 * $s) }
    $axisPen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, [single]$axisWidth)
    $axisPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $axisPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $axisPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $axisTop = $top - 14 * $s; $axisRight = $right + 14 * $s
    $g.DrawLines($axisPen, [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF([single]$left, [single]$axisTop)),
        (New-Object System.Drawing.PointF([single]$left, [single]$bottom)),
        (New-Object System.Drawing.PointF([single]$axisRight, [single]$bottom))))

    # Кривая: плавно растущая линия через точки.
    $points = @(
        @(0.06, 0.10), @(0.36, 0.52), @(0.66, 0.74), @(0.96, 0.92)
    ) | ForEach-Object {
        New-Object System.Drawing.PointF([single]($left + ($right - $left) * $_[0]), [single]($bottom - ($bottom - $top) * $_[1]))
    }
    $curveWidth = if ($tiny) { 2.0 } elseif ($small) { 2.6 } else { [Math]::Max(3, 17 * $s) }
    $curveColor = [System.Drawing.Color]::FromArgb(255, 255, 205, 64)
    $curvePen = New-Object System.Drawing.Pen($curveColor, [single]$curveWidth)
    $curvePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $curvePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawCurve($curvePen, [System.Drawing.PointF[]]$points, [single]0.45)

    # Точки данных — на значках от 32 пикселей.
    if ($size -ge 32) {
        $r = [Math]::Max(2.6, 13 * $s)
        $ring = New-Object System.Drawing.Pen($curveColor, [single][Math]::Max(1.4, 6 * $s))
        foreach ($p in $points) {
            $g.FillEllipse([System.Drawing.Brushes]::White, [single]($p.X - $r), [single]($p.Y - $r), [single](2 * $r), [single](2 * $r))
            $g.DrawEllipse($ring, [single]($p.X - $r), [single]($p.Y - $r), [single](2 * $r), [single](2 * $r))
        }
        $ring.Dispose()
    }

    $curvePen.Dispose(); $axisPen.Dispose(); $gradient.Dispose(); $back.Dispose(); $g.Dispose()
    return $bmp
}

# Кадр значка в формате BMP (32 бита с альфа-каналом): его понимают все версии Windows.
function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $w = $bmp.Width; $h = $bmp.Height
    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)
    $maskStride = [int]([Math]::Floor(($w + 31) / 32) * 4)
    $writer.Write([int]40); $writer.Write([int]$w); $writer.Write([int]($h * 2))
    $writer.Write([int16]1); $writer.Write([int16]32); $writer.Write([int]0)
    $writer.Write([int]($w * $h * 4 + $maskStride * $h))
    $writer.Write([int]0); $writer.Write([int]0); $writer.Write([int]0); $writer.Write([int]0)
    for ($y = $h - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $w; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $writer.Write([byte]$c.B); $writer.Write([byte]$c.G); $writer.Write([byte]$c.R); $writer.Write([byte]$c.A)
        }
    }
    $writer.Write((New-Object byte[] ($maskStride * $h)))
    $writer.Flush()
    return ,$stream.ToArray()
}

function Get-PngBytes([System.Drawing.Bitmap]$bmp) {
    $stream = New-Object System.IO.MemoryStream
    $bmp.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    return ,$stream.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$bitmaps = @{}
$frames = @()
foreach ($size in $sizes) {
    $bmp = New-IconBitmap $size
    $bitmaps[$size] = $bmp
    # Кадр 256×256 хранится в PNG (так принято), остальные — в BMP.
    $data = if ($size -ge 256) { Get-PngBytes $bmp } else { Get-DibBytes $bmp }
    $frames += ,@($size, $data)
}

$root = Split-Path $PSScriptRoot -Parent
$assets = Join-Path $root 'assets'
New-Item -ItemType Directory -Force $assets | Out-Null
$icoPath = Join-Path $assets 'grapher.ico'

$file = [System.IO.File]::Create($icoPath)
$out = New-Object System.IO.BinaryWriter($file)
$out.Write([int16]0); $out.Write([int16]1); $out.Write([int16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($frame in $frames) {
    $size = $frame[0]; $data = $frame[1]
    $dimension = if ($size -ge 256) { 0 } else { $size }
    $out.Write([byte]$dimension); $out.Write([byte]$dimension); $out.Write([byte]0); $out.Write([byte]0)
    $out.Write([int16]1); $out.Write([int16]32)
    $out.Write([int]$data.Length); $out.Write([int]$offset)
    $offset += $data.Length
}
foreach ($frame in $frames) { $out.Write([byte[]]$frame[1]) }
$out.Flush(); $file.Dispose()
Write-Host "Иконка сохранена: $icoPath ($($frames.Count) размеров)"

if ($PreviewPath) {
    # Лист со всеми размерами на светлом и тёмном фоне.
    $sheet = New-Object System.Drawing.Bitmap(760, 300)
    $g = [System.Drawing.Graphics]::FromImage($sheet)
    $g.Clear([System.Drawing.Color]::FromArgb(243, 243, 243))
    $g.FillRectangle((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(32, 32, 32))), 0, 150, 760, 150)
    $x = 12
    foreach ($size in ($sizes | Sort-Object -Descending)) {
        if ($size -eq 256) {
            $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $g.DrawImage($bitmaps[$size], 12, 10, 128, 128); $g.DrawImage($bitmaps[$size], 12, 160, 128, 128)
            $x = 160
        } else {
            $g.DrawImageUnscaled($bitmaps[$size], $x, 40); $g.DrawImageUnscaled($bitmaps[$size], $x, 190)
            $x += $size + 18
        }
    }
    $g.Dispose()
    $sheet.Save($PreviewPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $big = $bitmaps[256]
    $big.Save(($PreviewPath -replace '\.png$', '_256.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Host "Просмотр: $PreviewPath"
}
