# Generates src/BrowserMultiview/Assets/app.ico: two side-by-side panes on a blue-violet rounded tile.
# Each size is drawn separately (not downscaled) so small sizes stay crisp. The .ico stores PNG entries.
param(
    [string]$Out = (Join-Path $PSScriptRoot "..\src\BrowserMultiview\Assets\app.ico"),
    [string]$PreviewPng = ""
)

Add-Type -AssemblyName System.Drawing

function New-RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [Math]::Min($r * 2, [Math]::Min($w, $h))
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = $size / 256.0
    $small = $size -le 32

    # Tile: blue -> violet diagonal gradient
    $inset = [Math]::Max(0.5, 8 * $s)
    $tile = New-RoundedRect $inset $inset ($size - 2 * $inset) ($size - 2 * $inset) (56 * $s)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF $size, $size),
        [System.Drawing.Color]::FromArgb(255, 0x3D, 0x86, 0xFF), [System.Drawing.Color]::FromArgb(255, 0x7B, 0x4D, 0xFF))
    $g.FillPath($grad, $tile)

    # Two panes
    $pad = if ($small) { 52 * $s } else { 48 * $s }
    $gap = if ($small) { 20 * $s } else { 16 * $s }
    $top = if ($small) { 64 * $s } else { 62 * $s }
    $bottom = $size - $top
    $paneW = ($size - 2 * $pad - $gap) / 2
    $paneH = $bottom - $top
    $radius = if ($small) { 10 * $s } else { 14 * $s }

    foreach ($i in 0, 1) {
        $x = $pad + $i * ($paneW + $gap)
        $alpha = if ($i -eq 0) { 250 } else { 215 }
        $pane = New-RoundedRect $x $top $paneW $paneH $radius
        $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb($alpha, 255, 255, 255))), $pane)

        if (-not $small) {
            # Title strip + two "content lines" per pane
            $barH = 22 * $s
            $g.SetClip($pane)
            $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(70, 0x3D, 0x4E, 0xC8))), $x, $top, $paneW, $barH)
            $g.ResetClip()
            $lineBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(60, 0x3D, 0x4E, 0xC8))
            $lx = $x + 14 * $s; $lw = $paneW - 28 * $s; $lh = 9 * $s
            foreach ($row in 0, 1, 2) {
                $w = if ($row -eq 2) { $lw * 0.6 } else { $lw }
                $ly = $top + $barH + (18 + $row * 22) * $s
                $g.FillPath($lineBrush, (New-RoundedRect $lx $ly $w $lh ($lh / 2)))
            }
        }
    }

    $g.Dispose()
    return $bmp
}

# Classic 32-bit DIB icon entry: BITMAPINFOHEADER (height doubled for the AND mask), bottom-up BGRA
# pixels, then an all-zero 1-bpp AND mask (alpha does the masking). Older readers only accept PNG at 256.
function ConvertTo-IconDib([System.Drawing.Bitmap]$bmp) {
    $size = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $ms
    $w.Write([uint32]40); $w.Write([int32]$size); $w.Write([int32]($size * 2))
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]0); $w.Write([uint32]0)
    $w.Write([int32]0); $w.Write([int32]0); $w.Write([uint32]0); $w.Write([uint32]0)
    for ($y = $size - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $size; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
        }
    }
    $maskRow = [int]([Math]::Ceiling($size / 32.0) * 4)
    $w.Write((New-Object byte[] ($maskRow * $size)))
    $w.Flush()
    return , $ms.ToArray()
}

$sizes = 256, 48, 32, 16
$images = foreach ($size in $sizes) {
    $bmp = New-IconBitmap $size
    if ($PreviewPng -and $size -eq 256) { $bmp.Save($PreviewPng, [System.Drawing.Imaging.ImageFormat]::Png) }
    if ($size -ge 256) {
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $data = $ms.ToArray()
    } else {
        $data = ConvertTo-IconDib $bmp
    }
    $bmp.Dispose()
    , $data
}
$pngs = $images

# ICO container: ICONDIR + one ICONDIRENTRY per image, then the payloads (PNG for 256, DIB for the rest).
New-Item -ItemType Directory -Force -Path (Split-Path $Out) | Out-Null
$fs = [System.IO.File]::Create($Out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }   # 0 means 256
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($png in $pngs) { $w.Write($png) }
$w.Dispose()
"Wrote $Out ($((Get-Item $Out).Length) bytes)"
