# Generates src/Lcd207.Gui/app.ico: a dark case front with a glowing landscape LCD
# showing a mountain/sun picture. Run from anywhere: pwsh tools/make-icon.ps1
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
$out = Join-Path $PSScriptRoot '..\src\Lcd207.Gui\app.ico'

function New-RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    $p
}

function C([string]$hex, [int]$a = 255) {
    $c = [System.Drawing.ColorTranslator]::FromHtml($hex)
    [System.Drawing.Color]::FromArgb($a, $c)
}

function Render([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.ScaleTransform($size / 256.0, $size / 256.0)
    $small = $size -le 24

    # Case body
    $body = New-RoundRect 16 28 224 200 28
    $bodyBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new(0, 28)), ([System.Drawing.PointF]::new(0, 228)), (C '#3A3F48'), (C '#15171B')
    $g.FillPath($bodyBrush, $body)
    $g.DrawPath((New-Object System.Drawing.Pen (C '#5A616C'), ($(if ($small) { 10 } else { 4 }))), $body)

    # Screen glow
    if (-not $small) {
        for ($i = 4; $i -ge 1; $i--) {
            $glow = New-RoundRect (36 - 3 * $i) (50 - 3 * $i) (184 + 6 * $i) (120 + 6 * $i) (10 + 3 * $i)
            $g.FillPath((New-Object System.Drawing.SolidBrush (C '#3A96DD' (18 * (5 - $i)))), $glow)
        }
    }

    # Screen (landscape, ~2:1 like the mounted panel) with sky gradient
    $sx = 36; $sy = 50; $sw = 184; $sh = 120
    if ($small) { $sx = 30; $sy = 46; $sw = 196; $sh = 136 }
    $screen = New-RoundRect $sx $sy $sw $sh 10
    $sky = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new(0, $sy)), ([System.Drawing.PointF]::new(0, $sy + $sh)), (C '#5B3FD9'), (C '#2FB8F0')
    $g.FillPath($sky, $screen)
    $g.SetClip($screen)

    # Sun
    $sunR = $sh * 0.17
    $g.FillEllipse((New-Object System.Drawing.SolidBrush (C '#FFD45A')), ($sx + $sw * 0.70), ($sy + $sh * 0.18), (2 * $sunR), (2 * $sunR))

    # Mountains
    $back = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new($sx, $sy + $sh * 0.80),
        [System.Drawing.PointF]::new($sx + $sw * 0.30, $sy + $sh * 0.38),
        [System.Drawing.PointF]::new($sx + $sw * 0.55, $sy + $sh * 0.70),
        [System.Drawing.PointF]::new($sx + $sw * 0.78, $sy + $sh * 0.45),
        [System.Drawing.PointF]::new($sx + $sw, $sy + $sh * 0.75),
        [System.Drawing.PointF]::new($sx + $sw, $sy + $sh),
        [System.Drawing.PointF]::new($sx, $sy + $sh))
    $g.FillPolygon((New-Object System.Drawing.SolidBrush (C '#1F6E8C')), $back)
    $front = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new($sx, $sy + $sh * 0.92),
        [System.Drawing.PointF]::new($sx + $sw * 0.20, $sy + $sh * 0.68),
        [System.Drawing.PointF]::new($sx + $sw * 0.45, $sy + $sh * 0.95),
        [System.Drawing.PointF]::new($sx + $sw * 0.68, $sy + $sh * 0.62),
        [System.Drawing.PointF]::new($sx + $sw, $sy + $sh * 0.98),
        [System.Drawing.PointF]::new($sx + $sw, $sy + $sh),
        [System.Drawing.PointF]::new($sx, $sy + $sh))
    $g.FillPolygon((New-Object System.Drawing.SolidBrush (C '#0F3B52')), $front)
    $g.ResetClip()
    $g.DrawPath((New-Object System.Drawing.Pen (C '#9FD8FF'), ($(if ($small) { 6 } else { 3 }))), $screen)

    # Front-panel details: vent slots + power LED
    if ($size -ge 48) {
        $vent = New-Object System.Drawing.SolidBrush (C '#0C0D10')
        foreach ($i in 0..4) { $g.FillPath($vent, (New-RoundRect (44 + $i * 26) 190 18 18 4)) }
    }
    $ledY = $(if ($small) { 196 } else { 192 })
    $ledR = $(if ($small) { 14 } else { 7 })
    $g.FillEllipse((New-Object System.Drawing.SolidBrush (C '#3AE0FF')), (204 - $ledR), $ledY, (2 * $ledR), (2 * $ledR))

    $g.Dispose()
    $bmp
}

# Assemble a PNG-compressed ICO
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
    $b = Render $s
    $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $b.Dispose()
    , $ms.ToArray()
}
$fs = [System.IO.File]::Create($out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256))
    $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$pngs[$i].Length); $w.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Dispose()
Write-Host "Wrote $((Resolve-Path $out).Path)"
