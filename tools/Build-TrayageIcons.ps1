#requires -Version 7.0
<#
.SYNOPSIS
    Generates every Trayage raster icon asset from a single glyph definition.

.DESCRIPTION
    Trayage's mark is a blue inbox tray carrying an upward chevron (^) — the tray you
    triage code activity out of. State is shown by a symbol rising above the tray and a
    tint on the tray itself:

      (none)      connected, all caught up      — full-colour blue tray
      rising sun  new activity waiting (unread)  — blue tray + amber sun
      question    nothing connected / configured — grey-tinted tray + "?"
      cross (X)   configured but unreachable / error — red-tinted tray + "X"

    This script is the SINGLE SOURCE OF TRUTH for that glyph: the geometry lives here
    once and every output is rendered from it with GDI+ (System.Drawing), so there is no
    dependency on ImageMagick or Inkscape.

    Outputs:
      src/Trayage.App/Assets/trayage.ico               App / .exe icon (graphite tile + full brand: tray + sun)
      src/Trayage.App/Assets/trayage-caughtup.ico       Tray state: blue tray (connected, all read)
      src/Trayage.App/Assets/trayage-unread.ico         Tray state: blue tray + rising sun (unread waiting)
      src/Trayage.App/Assets/trayage-disconnected.ico   Tray state: grey tray + "?" (nothing connected)
      src/Trayage.App/Assets/trayage-error.ico          Tray state: red tray + "X" (configured but unreachable)
      assets/oauth/trayage-oauth-{512,256,128}.png      Full-colour tile for the OAuth app logo

    Tray-state icons are transparent (no tile); the app/exe and OAuth tiles sit on the
    graphite badge. Pass -Preview to also drop a contact sheet of every variant in
    tools/preview/ for eyeballing.

    Re-run this whenever the glyph changes. The colours mirror docs/styles.css.
#>
[CmdletBinding()]
param(
    # Also render a contact sheet of every variant to tools/preview/ for visual review.
    [switch]$Preview
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

# ---------------------------------------------------------------------------
# Brand palette (matches docs/styles.css, plus the tray blues introduced here)
# ---------------------------------------------------------------------------
$Color = @{
    GraphiteHi  = [System.Drawing.ColorTranslator]::FromHtml('#2b3340')  # tile top-left
    GraphiteLo  = [System.Drawing.ColorTranslator]::FromHtml('#161b23')  # tile bottom-right
    Border      = [System.Drawing.ColorTranslator]::FromHtml('#343d4d')  # tile edge
    Amber       = [System.Drawing.ColorTranslator]::FromHtml('#f1b24a')  # accent / chevron / sun
    AmberBright = [System.Drawing.ColorTranslator]::FromHtml('#ffd587')  # sun core highlight
    Signal      = [System.Drawing.ColorTranslator]::FromHtml('#e5484d')  # error
    SignalDark  = [System.Drawing.ColorTranslator]::FromHtml('#9e2b2f')  # error opening / shade
    SignalSoft  = [System.Drawing.ColorTranslator]::FromHtml('#ff8b8e')  # error highlight
    Grey        = [System.Drawing.ColorTranslator]::FromHtml('#9aa3b5')  # disconnected
    GreyDark    = [System.Drawing.ColorTranslator]::FromHtml('#5c6577')  # disconnected shade
    GreySoft    = [System.Drawing.ColorTranslator]::FromHtml('#cfd6e4')  # disconnected highlight
    BlueHi      = [System.Drawing.ColorTranslator]::FromHtml('#5b97f7')  # tray front (lit)
    BlueLo      = [System.Drawing.ColorTranslator]::FromHtml('#2f63c8')  # tray opening / shade
}

# ---------------------------------------------------------------------------
# Glyph geometry, in normalised [0,1] fractions of the canvas. The tray sits in
# the lower ~60% so a status symbol can rise above it in the upper third. Tuned
# so nothing clips at the edges at any size.
# ---------------------------------------------------------------------------
$Geom = @{
    # Inbox tray — a letter-tray drawn in slight perspective: a trapezoidal front
    # panel (wider at the open top, narrowing to the base) topped by an elliptical mouth.
    TrayTopY     = 0.50   # top edge of the front panel (mouth centre line)
    TrayBaseY    = 0.86   # bottom edge of the front panel
    TrayTopHalf  = 0.34   # half-width at the top
    TrayBaseHalf = 0.27   # half-width at the base
    MouthRy      = 0.085  # vertical radius of the opening ellipse
    MouthInset   = 0.78   # inner-lip ellipse as a fraction of the mouth (depth cue)
    # Upward chevron on the front panel.
    ChevApexY    = 0.62
    ChevFootY    = 0.73
    ChevHalf     = 0.115
    # Status symbol, centred above the tray.
    SymCx        = 0.50
    SymCy        = 0.24
    SymR         = 0.155  # nominal radius the symbol fits within
    # Vertical centroid of the whole composition (top sun-ray tip ~0.07 .. tray base 0.86).
    # The per-variant Scale (below) grows the glyph about this point so it fills the canvas
    # without drifting — see New-GlyphBitmap.
    CompCenterY  = 0.466
}

function New-RoundedRectPath {
    param([single]$X, [single]$Y, [single]$W, [single]$H, [single]$R)
    $d = [single](2 * $R)
    $p = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $p.AddArc($X, $Y, $d, $d, 180, 90)
    $p.AddArc([single]($X + $W - $d), $Y, $d, $d, 270, 90)
    $p.AddArc([single]($X + $W - $d), [single]($Y + $H - $d), $d, $d, 0, 90)
    $p.AddArc($X, [single]($Y + $H - $d), $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

# Draws the inbox tray (perspective trapezoid + elliptical mouth + chevron).
function Add-Tray {
    param(
        [System.Drawing.Graphics]$G,
        [int]$Size,
        [System.Drawing.Color]$Front,    # lit front panel
        [System.Drawing.Color]$Shade,    # opening / shaded tone
        [System.Drawing.Color]$Chevron
    )

    $px = { param($f) [single]($f * $Size) }

    $topY  = & $px $Geom.TrayTopY
    $baseY = & $px $Geom.TrayBaseY
    $topL  = & $px (0.5 - $Geom.TrayTopHalf)
    $topR  = & $px (0.5 + $Geom.TrayTopHalf)
    $baseL = & $px (0.5 - $Geom.TrayBaseHalf)
    $baseR = & $px (0.5 + $Geom.TrayBaseHalf)

    # Front panel (lit) — trapezoid with softened bottom corners via a path.
    $panel = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $panel.AddPolygon([System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new($topL,  $topY)
        [System.Drawing.PointF]::new($topR,  $topY)
        [System.Drawing.PointF]::new($baseR, $baseY)
        [System.Drawing.PointF]::new($baseL, $baseY)
    ))
    try {
        $brush = [System.Drawing.SolidBrush]::new($Front)
        try { $G.FillPath($brush, $panel) } finally { $brush.Dispose() }
    }
    finally { $panel.Dispose() }

    # Tray mouth — full ellipse in the shaded tone (the opening), then an inner lip
    # in the front tone to read as depth.
    $mRy = & $px $Geom.MouthRy
    $mW  = [single]($topR - $topL)
    $mX  = $topL
    $mY  = [single]($topY - $mRy)
    $shadeBrush = [System.Drawing.SolidBrush]::new($Shade)
    try { $G.FillEllipse($shadeBrush, $mX, $mY, $mW, [single](2 * $mRy)) }
    finally { $shadeBrush.Dispose() }

    # Inner lip (depth cue) — only at >=32px; below that it collapses to sub-pixel mush.
    if ($Size -ge 32) {
        $inset = $Geom.MouthInset
        $iW    = [single]($mW * $inset)
        $iRy   = [single]($mRy * $inset)
        $iX    = [single]($mX + ($mW - $iW) / 2)
        $iY    = [single]($topY - $iRy)
        $frontBrush = [System.Drawing.SolidBrush]::new($Front)
        try { $G.FillEllipse($frontBrush, $iX, $iY, $iW, [single](2 * $iRy)) }
        finally { $frontBrush.Dispose() }
    }

    # Upward chevron on the front panel.
    $stroke = [single]([math]::Max(2.0, 0.085 * $Size))
    $apexX  = & $px $Geom.SymCx
    $apexY  = & $px $Geom.ChevApexY
    $footY  = & $px $Geom.ChevFootY
    $halfW  = & $px $Geom.ChevHalf
    $pen = [System.Drawing.Pen]::new($Chevron, $stroke)
    try {
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $G.DrawLines($pen, [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new([single]($apexX - $halfW), $footY)
            [System.Drawing.PointF]::new($apexX, $apexY)
            [System.Drawing.PointF]::new([single]($apexX + $halfW), $footY)
        ))
    }
    finally { $pen.Dispose() }
}

# Draws a rising sun above the tray (filled core + radiating rays). At small sizes the rays
# turn to mush, so below 32px we drop them and draw a single, larger core disc instead — it
# still reads as "something has risen above the tray" without the noise.
function Add-Sun {
    param([System.Drawing.Graphics]$G, [int]$Size, [System.Drawing.Color]$Core, [System.Drawing.Color]$Rays)
    $cx = [single]($Geom.SymCx * $Size)
    $cy = [single]($Geom.SymCy * $Size)
    $small = $Size -lt 32
    $r  = if ($small) { [single](0.15 * $Size) } else { [single](0.11 * $Size) }

    if (-not $small) {
        $stroke = [single]([math]::Max(1.5, 0.036 * $Size))
        $rInner = [single]($r * 1.2)
        $rOuter = [single]($r * 1.5)
        $pen = [System.Drawing.Pen]::new($Rays, $stroke)
        try {
            $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
            $pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
            foreach ($deg in 0, 45, 90, 135, 180, 225, 270, 315) {
                $rad = [single]($deg * [math]::PI / 180)
                $sx = [single]($cx + $rInner * [math]::Cos($rad))
                $sy = [single]($cy + $rInner * [math]::Sin($rad))
                $ex = [single]($cx + $rOuter * [math]::Cos($rad))
                $ey = [single]($cy + $rOuter * [math]::Sin($rad))
                $G.DrawLine($pen, $sx, $sy, $ex, $ey)
            }
        }
        finally { $pen.Dispose() }
    }

    $brush = [System.Drawing.SolidBrush]::new($Core)
    try { $G.FillEllipse($brush, [single]($cx - $r), [single]($cy - $r), [single](2 * $r), [single](2 * $r)) }
    finally { $brush.Dispose() }
}

# Draws a centred "?" above the tray.
function Add-Question {
    param([System.Drawing.Graphics]$G, [int]$Size, [System.Drawing.Color]$Ink)
    $cx = [single]($Geom.SymCx * $Size)
    $cy = [single]($Geom.SymCy * $Size)
    $em = [single](0.36 * $Size)
    $font = [System.Drawing.Font]::new('Segoe UI', $em, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $fmt  = [System.Drawing.StringFormat]::new()
    $fmt.Alignment     = [System.Drawing.StringAlignment]::Center
    $fmt.LineAlignment = [System.Drawing.StringAlignment]::Center
    $brush = [System.Drawing.SolidBrush]::new($Ink)
    try {
        $rect = [System.Drawing.RectangleF]::new([single]($cx - $em), [single]($cy - $em), [single](2 * $em), [single](2 * $em))
        $G.DrawString('?', $font, $brush, $rect, $fmt)
    }
    finally { $brush.Dispose(); $fmt.Dispose(); $font.Dispose() }
}

# Draws a centred "X" above the tray.
function Add-Cross {
    param([System.Drawing.Graphics]$G, [int]$Size, [System.Drawing.Color]$Ink)
    $cx = [single]($Geom.SymCx * $Size)
    $cy = [single]($Geom.SymCy * $Size)
    $h  = [single](0.10 * $Size)
    $stroke = [single]([math]::Max(2.0, 0.05 * $Size))
    $pen = [System.Drawing.Pen]::new($Ink, $stroke)
    try {
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
        $G.DrawLine($pen, [single]($cx - $h), [single]($cy - $h), [single]($cx + $h), [single]($cy + $h))
        $G.DrawLine($pen, [single]($cx + $h), [single]($cy - $h), [single]($cx - $h), [single]($cy + $h))
    }
    finally { $pen.Dispose() }
}

# Renders one icon variant at a pixel size, returning a Bitmap.
function New-GlyphBitmap {
    param(
        [int]$Size,
        [hashtable]$Variant  # Tile, Front, Shade, Chevron, Symbol ('None'|'Sun'|'Question'|'Cross'), SymInk, SymCore
    )

    $bmp = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.TextRenderingHint  = [System.Drawing.Text.TextRenderingHint]::AntiAlias
        $g.Clear([System.Drawing.Color]::Transparent)

        if ($Variant.Tile) {
            $inset  = [single]([math]::Max(0.5, $Size / 256.0))
            $radius = [single](0.22 * $Size)
            $rect   = [System.Drawing.RectangleF]::new(0, 0, $Size, $Size)
            $path   = New-RoundedRectPath -X $inset -Y $inset -W ([single]($Size - 2 * $inset)) -H ([single]($Size - 2 * $inset)) -R $radius
            try {
                $grad = [System.Drawing.Drawing2D.LinearGradientBrush]::new($rect, $Color.GraphiteHi, $Color.GraphiteLo, [single]135)
                try { $g.FillPath($grad, $path) } finally { $grad.Dispose() }
                $bw  = [single]([math]::Max(1.0, $Size / 128.0))
                $bpen = [System.Drawing.Pen]::new($Color.Border, $bw)
                try { $g.DrawPath($bpen, $path) } finally { $bpen.Dispose() }
            }
            finally { $path.Dispose() }
        }

        # Grow the glyph (tray + symbol) about the composition centroid so it fills the
        # canvas. The tile, drawn above, stays full-bleed. Pen widths scale with the
        # transform, so strokes get proportionally bolder too.
        $scale = if ($Variant.ContainsKey('Scale')) { [single]$Variant.Scale } else { [single]1.0 }
        $st = $g.Save()
        $cx = [single](0.5 * $Size)
        $cy = [single]($Geom.CompCenterY * $Size)
        $g.TranslateTransform($cx, $cy)
        $g.ScaleTransform($scale, $scale)
        $g.TranslateTransform([single](-$cx), [single](-$cy))

        Add-Tray -G $g -Size $Size -Front $Variant.Front -Shade $Variant.Shade -Chevron $Variant.Chevron

        switch ($Variant.Symbol) {
            'Sun'      { Add-Sun      -G $g -Size $Size -Core $Variant.SymCore -Rays $Variant.SymInk }
            'Question' { Add-Question -G $g -Size $Size -Ink $Variant.SymInk }
            'Cross'    { Add-Cross    -G $g -Size $Size -Ink $Variant.SymInk }
        }

        $g.Restore($st)
    }
    finally { $g.Dispose() }

    return $bmp
}

function Get-PngBytes {
    param([System.Drawing.Bitmap]$Bitmap)
    $ms = [System.IO.MemoryStream]::new()
    try {
        $Bitmap.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        return , $ms.ToArray()
    }
    finally { $ms.Dispose() }
}

# Packs PNG frames into a multi-resolution .ico (PNG-compressed entries; supported
# by the Windows WIC/WPF icon decoder on Windows 10+).
function Save-Ico {
    param([hashtable[]]$Frames, [string]$Path)  # each frame: @{ Size = int; Bytes = byte[] }

    $ms = [System.IO.MemoryStream]::new()
    $bw = [System.IO.BinaryWriter]::new($ms)
    try {
        $bw.Write([uint16]0)               # reserved
        $bw.Write([uint16]1)               # type: icon
        $bw.Write([uint16]$Frames.Count)
        $offset = 6 + (16 * $Frames.Count)
        foreach ($f in $Frames) {
            $dim = if ($f.Size -ge 256) { 0 } else { $f.Size }
            $bw.Write([byte]$dim)           # width  (0 => 256)
            $bw.Write([byte]$dim)           # height (0 => 256)
            $bw.Write([byte]0)              # palette count
            $bw.Write([byte]0)              # reserved
            $bw.Write([uint16]1)            # colour planes
            $bw.Write([uint16]32)           # bits per pixel
            $bw.Write([uint32]$f.Bytes.Length)
            $bw.Write([uint32]$offset)
            $offset += $f.Bytes.Length
        }
        foreach ($f in $Frames) { $bw.Write($f.Bytes) }
        $bw.Flush()
        [System.IO.File]::WriteAllBytes($Path, $ms.ToArray())
    }
    finally { $bw.Dispose(); $ms.Dispose() }
}

function Build-Ico {
    param([int[]]$Sizes, [string]$Path, [hashtable]$Variant)
    $frames = foreach ($s in $Sizes) {
        $bmp = New-GlyphBitmap -Size $s -Variant $Variant
        try { @{ Size = $s; Bytes = (Get-PngBytes -Bitmap $bmp) } }
        finally { $bmp.Dispose() }
    }
    Save-Ico -Frames $frames -Path $Path
    Write-Host ("  {0}  ({1} sizes)" -f (Split-Path $Path -Leaf), $Sizes.Count)
}

function Build-Png {
    param([int]$Size, [string]$Path, [hashtable]$Variant)
    $bmp = New-GlyphBitmap -Size $Size -Variant $Variant
    try { $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png) }
    finally { $bmp.Dispose() }
    Write-Host ("  {0}  ({1}px)" -f (Split-Path $Path -Leaf), $Size)
}

# ---------------------------------------------------------------------------
# Icon variants
# ---------------------------------------------------------------------------
# Scale grows the glyph about the composition centroid (see New-GlyphBitmap). Tiled
# variants stay modest (1.08) to keep tile padding; transparent tray icons fill the frame
# (1.14) since they have no tile to frame them. Tuned so nothing clips at any size.
$Variants = @{
    # App / OAuth — full brand on the graphite tile (tray + chevron + rising sun).
    App = @{
        Tile = $true; Front = $Color.BlueHi; Shade = $Color.BlueLo; Chevron = $Color.AmberBright
        Symbol = 'Sun'; SymInk = $Color.Amber; SymCore = $Color.AmberBright; Scale = 1.08
    }
    # Connected, all caught up — plain blue tray, no symbol.
    CaughtUp = @{
        Tile = $false; Front = $Color.BlueHi; Shade = $Color.BlueLo; Chevron = $Color.AmberBright
        Symbol = 'None'; Scale = 1.14
    }
    # Unread waiting — blue tray + rising sun.
    Unread = @{
        Tile = $false; Front = $Color.BlueHi; Shade = $Color.BlueLo; Chevron = $Color.AmberBright
        Symbol = 'Sun'; SymInk = $Color.Amber; SymCore = $Color.AmberBright; Scale = 1.14
    }
    # Nothing connected / configured — grey-tinted tray + "?".
    Disconnected = @{
        Tile = $false; Front = $Color.Grey; Shade = $Color.GreyDark; Chevron = $Color.GreySoft
        Symbol = 'Question'; SymInk = $Color.GreySoft; Scale = 1.14
    }
    # Configured but unreachable / error — red-tinted tray + "X".
    Error = @{
        Tile = $false; Front = $Color.Signal; Shade = $Color.SignalDark; Chevron = $Color.SignalSoft
        Symbol = 'Cross'; SymInk = $Color.SignalSoft; Scale = 1.14
    }
}

# ---------------------------------------------------------------------------
# Output locations
# ---------------------------------------------------------------------------
$repo      = Split-Path $PSScriptRoot -Parent
$appAssets = Join-Path $repo 'src/Trayage.App/Assets'
$oauthDir  = Join-Path $repo 'assets/oauth'
New-Item -ItemType Directory -Force -Path $appAssets, $oauthDir | Out-Null

$tileSizes = @(16, 20, 24, 32, 48, 64, 128, 256)
$traySizes = @(16, 20, 24, 32, 48, 64, 256)

Write-Host 'Generating Trayage icons...'

# App / .exe icon — the full badge.
Build-Ico -Sizes $tileSizes -Path (Join-Path $appAssets 'trayage.ico') -Variant $Variants.App

# Tray state icons — transparent, tinted per state.
Build-Ico -Sizes $traySizes -Path (Join-Path $appAssets 'trayage-caughtup.ico')     -Variant $Variants.CaughtUp
Build-Ico -Sizes $traySizes -Path (Join-Path $appAssets 'trayage-unread.ico')       -Variant $Variants.Unread
Build-Ico -Sizes $traySizes -Path (Join-Path $appAssets 'trayage-disconnected.ico') -Variant $Variants.Disconnected
Build-Ico -Sizes $traySizes -Path (Join-Path $appAssets 'trayage-error.ico')        -Variant $Variants.Error

# OAuth app tiles — large full-colour badges (e.g. GitHub OAuth App logo upload).
foreach ($s in 512, 256, 128) {
    Build-Png -Size $s -Path (Join-Path $oauthDir ("trayage-oauth-{0}.png" -f $s)) -Variant $Variants.App
}

# ---------------------------------------------------------------------------
# Optional contact sheet for visual review.
# ---------------------------------------------------------------------------
if ($Preview) {
    $previewDir = Join-Path $PSScriptRoot 'preview'
    New-Item -ItemType Directory -Force -Path $previewDir | Out-Null
    $order = 'App', 'CaughtUp', 'Unread', 'Disconnected', 'Error'
    $cell = 128; $pad = 16; $smalls = @(16, 20, 24, 32)
    $cols = $order.Count
    $smallStripH = ($smalls | Measure-Object -Maximum).Maximum + 14
    $sheetW = $cols * ($cell + $pad) + $pad
    $sheetH = ($cell + $pad) + 24 + $smallStripH + $pad
    $sheet = [System.Drawing.Bitmap]::new($sheetW, $sheetH, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $sg = [System.Drawing.Graphics]::FromImage($sheet)
    try {
        $sg.Clear([System.Drawing.ColorTranslator]::FromHtml('#0c0e12'))
        $sg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $labelFont = [System.Drawing.Font]::new('Segoe UI', 11, [System.Drawing.GraphicsUnit]::Pixel)
        $labelBrush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#9aa3b5'))
        $x = $pad
        foreach ($name in $order) {
            # Big render.
            $bmp = New-GlyphBitmap -Size $cell -Variant $Variants[$name]
            try { $sg.DrawImage($bmp, $x, $pad, $cell, $cell) } finally { $bmp.Dispose() }
            $sg.DrawString($name, $labelFont, $labelBrush, [single]$x, [single]($pad + $cell + 4))
            # Actual-size small renders (true tray sizes), laid out left-to-right.
            $sx = $x; $sy = $pad + $cell + 24
            foreach ($s in $smalls) {
                $sm = New-GlyphBitmap -Size $s -Variant $Variants[$name]
                try { $sg.DrawImage($sm, [int]$sx, [int]$sy, $s, $s) } finally { $sm.Dispose() }
                $sx += $s + 6
            }
            $x += $cell + $pad
        }
        $labelFont.Dispose(); $labelBrush.Dispose()
        $sheetPath = Join-Path $previewDir 'contact-sheet.png'
        $sheet.Save($sheetPath, [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Host ("  preview: {0}" -f $sheetPath)
    }
    finally { $sg.Dispose(); $sheet.Dispose() }
}

Write-Host 'Done.'
