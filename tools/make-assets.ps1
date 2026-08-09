# Squint - copied-link safety checker for Windows.
# Copyright (C) 2026 milkmade
# SPDX-License-Identifier: GPL-3.0-or-later

# Generates the status PNGs and the tray/app .ico for Squint.
# Re-run any time you want to tweak the look; overwrites src/Squint/Assets.

Add-Type -AssemblyName System.Drawing

$AssetDir = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\src\Squint\Assets'))
New-Item -ItemType Directory -Force -Path $AssetDir | Out-Null

$SM = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$RoundCap = [System.Drawing.Drawing2D.LineCap]::Round
$RoundJoin = [System.Drawing.Drawing2D.LineJoin]::Round

function New-Bitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    return $bmp
}

function New-Gfx($bmp) {
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = $SM
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    return $g
}

function C([string]$hex) {
    return [System.Drawing.ColorTranslator]::FromHtml($hex)
}

# Draws a filled circle with a soft top-left -> bottom-right gradient.
function Draw-Disc($g, [double]$s, [string]$light, [string]$dark) {
    # NB: inside New-Object Type(...) the comma binds tighter than arithmetic,
    # so every computed argument has to be parenthesised on its own.
    $rect = New-Object System.Drawing.RectangleF([float](4*$s), [float](4*$s), [float](120*$s), [float](120*$s))
    $br = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, (C $light), (C $dark), 55.0)
    $g.FillEllipse($br, $rect)
    $br.Dispose()
}

# Thick round-capped polyline, used for check / X / exclamation strokes.
function Draw-Stroke($g, [double]$s, [string]$hex, [double]$width, [double[]]$pts) {
    $pen = New-Object System.Drawing.Pen((C $hex), [float]($width*$s))
    $pen.StartCap = $RoundCap; $pen.EndCap = $RoundCap; $pen.LineJoin = $RoundJoin
    for ($i = 0; $i -lt $pts.Length - 2; $i += 2) {
        $g.DrawLine($pen, [float]($pts[$i]*$s), [float]($pts[$i+1]*$s), [float]($pts[$i+2]*$s), [float]($pts[$i+3]*$s))
    }
    $pen.Dispose()
}

function Draw-Dot($g, [double]$s, [string]$hex, [double]$cx, [double]$cy, [double]$r) {
    $br = New-Object System.Drawing.SolidBrush((C $hex))
    $g.FillEllipse($br, [float](($cx-$r)*$s), [float](($cy-$r)*$s), [float](2*$r*$s), [float](2*$r*$s))
    $br.Dispose()
}

function Save-Png($bmp, [string]$name) {
    $path = Join-Path $AssetDir $name
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Host "  wrote $name ($($bmp.Width)x$($bmp.Height))"
    return $path
}

# ---------------------------------------------------------------- verified
$size = 256; $s = $size / 128.0
$bmp = New-Bitmap $size; $g = New-Gfx $bmp
Draw-Disc $g $s '#34D399' '#15A34A'
Draw-Stroke $g $s '#FFFFFF' 13 @(39,66, 56,84, 92,44)
$g.Dispose(); Save-Png $bmp 'verified.png' | Out-Null; $bmp.Dispose()

# ---------------------------------------------------------------- caution
$bmp = New-Bitmap $size; $g = New-Gfx $bmp
$tri = @(
    (New-Object System.Drawing.PointF([float](64*$s), [float](21*$s))),
    (New-Object System.Drawing.PointF([float](111*$s), [float](101*$s))),
    (New-Object System.Drawing.PointF([float](17*$s), [float](101*$s)))
)
$rect = New-Object System.Drawing.RectangleF([float](4*$s), [float](4*$s), [float](120*$s), [float](120*$s))
$br = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, (C '#FBBF24'), (C '#D97706'), 55.0)
$g.FillPolygon($br, $tri)
$pen = New-Object System.Drawing.Pen($br, [float](13*$s))
$pen.LineJoin = $RoundJoin
$g.DrawPolygon($pen, $tri)
$pen.Dispose(); $br.Dispose()
Draw-Stroke $g $s '#FFFFFF' 12 @(64,55, 64,79)
Draw-Dot $g $s '#FFFFFF' 64 92 6.5
$g.Dispose(); Save-Png $bmp 'caution.png' | Out-Null; $bmp.Dispose()

# ---------------------------------------------------------------- suspect
$bmp = New-Bitmap $size; $g = New-Gfx $bmp
Draw-Disc $g $s '#F87171' '#DC2626'
Draw-Stroke $g $s '#FFFFFF' 13 @(46,46, 82,82)
Draw-Stroke $g $s '#FFFFFF' 13 @(82,46, 46,82)
$g.Dispose(); Save-Png $bmp 'suspect.png' | Out-Null; $bmp.Dispose()

# ---------------------------------------------------------------- processing
$bmp = New-Bitmap $size; $g = New-Gfx $bmp
Draw-Disc $g $s '#60A5FA' '#2563EB'
Draw-Dot $g $s '#FFFFFF' 42 64 8
Draw-Dot $g $s '#FFFFFF' 64 64 8
Draw-Dot $g $s '#FFFFFF' 86 64 8
$g.Dispose(); Save-Png $bmp 'processing.png' | Out-Null; $bmp.Dispose()

# ---------------------------------------------------------------- app / tray glyph
# A squinting eye. -Paused closes it, which is the whole point: an open eye in the tray means
# Squint is watching the clipboard, a closed grey one means it is running but switched off.
function New-AppGlyph([int]$size, [switch]$Paused) {
    $s = $size / 128.0
    $bmp = New-Bitmap $size; $g = New-Gfx $bmp
    $rect = New-Object System.Drawing.RectangleF([float](2*$s), [float](2*$s), [float](124*$s), [float](124*$s))

    if ($Paused) { $c1 = '#8A94A6'; $c2 = '#5B6472'; $glyph = '#E8ECF2' }
    else         { $c1 = '#4F46E5'; $c2 = '#1D4ED8'; $glyph = '#FFFFFF' }

    $br = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, (C $c1), (C $c2), 55.0)
    # rounded square via a thick round-joined stroke over a slightly inset fill
    $inner = New-Object System.Drawing.RectangleF([float](14*$s), [float](14*$s), [float](100*$s), [float](100*$s))
    $g.FillRectangle($br, $inner)
    $pen = New-Object System.Drawing.Pen($br, [float](24*$s))
    $pen.LineJoin = $RoundJoin
    $g.DrawRectangle($pen, [float](14*$s), [float](14*$s), [float](100*$s), [float](100*$s))
    $pen.Dispose()

    if ($Paused) {
        # Closed lid: one thick curve, with a couple of short lashes so it doesn't read as a dash.
        $wp = New-Object System.Drawing.Pen((C $glyph), [float](10*$s))
        $wp.StartCap = $RoundCap; $wp.EndCap = $RoundCap
        $lid = New-Object System.Drawing.Drawing2D.GraphicsPath
        $lid.AddBezier([float](28*$s), [float](58*$s), [float](46*$s), [float](82*$s),
                       [float](82*$s), [float](82*$s), [float](100*$s), [float](58*$s))
        $g.DrawPath($wp, $lid)
        $lid.Dispose()

        $lash = New-Object System.Drawing.Pen((C $glyph), [float](8*$s))
        $lash.StartCap = $RoundCap; $lash.EndCap = $RoundCap
        $g.DrawLine($lash, [float](40*$s), [float](76*$s), [float](34*$s), [float](88*$s))
        $g.DrawLine($lash, [float](64*$s), [float](81*$s), [float](64*$s), [float](94*$s))
        $g.DrawLine($lash, [float](88*$s), [float](76*$s), [float](94*$s), [float](88*$s))
        $lash.Dispose(); $wp.Dispose()
    }
    else {
        # Open but narrowed - a squint, not a wide cartoon eye. Two beziers meeting at the corners.
        $white = New-Object System.Drawing.SolidBrush((C $glyph))
        $eye = New-Object System.Drawing.Drawing2D.GraphicsPath
        $eye.AddBezier([float](24*$s), [float](64*$s), [float](46*$s), [float](42*$s),
                       [float](82*$s), [float](42*$s), [float](104*$s), [float](64*$s))
        $eye.AddBezier([float](104*$s), [float](64*$s), [float](82*$s), [float](86*$s),
                       [float](46*$s), [float](86*$s), [float](24*$s), [float](64*$s))
        $eye.CloseFigure()
        $g.FillPath($white, $eye)
        $eye.Dispose(); $white.Dispose()

        # Iris in the background colour, so the eye reads as a hole rather than a blob.
        $iris = New-Object System.Drawing.SolidBrush((C $c2))
        $g.FillEllipse($iris, [float](51*$s), [float](51*$s), [float](26*$s), [float](26*$s))
        $iris.Dispose()
    }

    $br.Dispose(); $g.Dispose()
    return $bmp
}

# Classic BMP (BITMAPINFOHEADER + BGRA + AND mask) frame for an .ico.
# PNG-compressed frames are legal since Vista, but GDI+ and several older code paths decode them
# to garbage, so small sizes ship as BMP and only 128/256 use PNG.
function Get-IcoBmpFrame([System.Drawing.Bitmap]$bmp) {
    $w = $bmp.Width; $h = $bmp.Height
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)

    # BITMAPINFOHEADER. Height is doubled: the XOR (colour) and AND (mask) bitmaps are stacked.
    $bw.Write([UInt32]40)
    $bw.Write([Int32]$w)
    $bw.Write([Int32]($h * 2))
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]0)                 # BI_RGB
    $bw.Write([UInt32]($w * $h * 4))
    $bw.Write([Int32]0); $bw.Write([Int32]0)
    $bw.Write([UInt32]0); $bw.Write([UInt32]0)

    # XOR bitmap, bottom-up, BGRA.
    for ($y = $h - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $w; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $bw.Write([Byte]$c.B); $bw.Write([Byte]$c.G); $bw.Write([Byte]$c.R); $bw.Write([Byte]$c.A)
        }
    }

    # AND mask: 1bpp, rows padded to 4 bytes. Left all-zero - the alpha channel does the masking.
    $rowBytes = [int]([Math]::Ceiling($w / 32.0) * 4)
    $blank = New-Object Byte[] $rowBytes
    for ($y = 0; $y -lt $h; $y++) { $bw.Write($blank) }

    $bw.Flush()
    $bytes = $ms.ToArray()
    $bw.Dispose(); $ms.Dispose()
    return , $bytes
}

# Builds the .ico container from pre-encoded frame blobs.
function Write-Ico($blobs, [int[]]$sizes, [string]$outPath) {
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $n = $blobs.Count
    $bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$n)

    $offset = 6 + (16 * $n)
    for ($i = 0; $i -lt $n; $i++) {
        $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
        $bw.Write([Byte]$dim); $bw.Write([Byte]$dim); $bw.Write([Byte]0); $bw.Write([Byte]0)
        $bw.Write([UInt16]1); $bw.Write([UInt16]32)
        $bw.Write([UInt32]$blobs[$i].Length); $bw.Write([UInt32]$offset)
        $offset += $blobs[$i].Length
    }

    foreach ($b in $blobs) { $bw.Write($b) }
    $bw.Flush()
    [System.IO.File]::WriteAllBytes($outPath, $ms.ToArray())
    $bw.Dispose(); $ms.Dispose()
}

$icoSizes = @(16, 24, 32, 48, 64, 128, 256)

foreach ($variant in @(@{ Name = 'app.ico'; Paused = $false }, @{ Name = 'app-paused.ico'; Paused = $true })) {
    $blobs = @()
    foreach ($sz in $icoSizes) {
        $b = if ($variant.Paused) { New-AppGlyph $sz -Paused } else { New-AppGlyph $sz }

        if ($sz -le 64) {
            $frame = Get-IcoBmpFrame $b
        }
        else {
            # 128/256 stay PNG: a 256px BMP frame is 256 KB, and every consumer of those sizes
            # is modern enough to decode PNG.
            $ms = New-Object System.IO.MemoryStream
            $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
            $frame = $ms.ToArray()
            $ms.Dispose()
        }

        # The leading comma matters: without it, += unrolls the byte[] into individual bytes
        # and the .ico comes out as noise.
        $blobs += , $frame
        $b.Dispose()
    }

    $path = Join-Path $AssetDir $variant.Name
    Write-Ico $blobs $icoSizes $path

    # Fail loudly rather than shipping an .ico Windows can't parse. GDI+ can't select the 256px
    # frame at all (it falls back to 128), so that one is only checked for loadability - the
    # shell reads it fine, and it's the small sizes that actually appear in the tray.
    foreach ($check in $icoSizes) {
        $probe = New-Object System.Drawing.Icon($path, $check, $check)
        if ($check -le 128 -and $probe.Width -ne $check) {
            throw "$($variant.Name): asked for ${check}px, got $($probe.Width)px"
        }
        $probe.Dispose()
    }

    Write-Host "  wrote $($variant.Name) ($($icoSizes -join ', ') - BMP to 64, PNG above, all verified)"
}

# A plain 256 PNG of the glyph too, for the settings window header.
$b = New-AppGlyph 256; Save-Png $b 'app.png' | Out-Null; $b.Dispose()

Write-Host "`nAssets written to $AssetDir"
