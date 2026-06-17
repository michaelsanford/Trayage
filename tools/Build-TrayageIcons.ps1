#requires -Version 7.0
<#
.SYNOPSIS
    Generates every Trayage raster icon asset from a single glyph definition.

.DESCRIPTION
    Trayage's mark is three descending priority bars — a sorted queue, the act of triage
    distilled to its essence. The top bar is accented amber to mark "act on this first".
    State is shown by the tint of the bars (and whether the top bar is accented):

      blue, accented top    new activity waiting (unread)        — blue bars + amber top
      blue, no accent       connected, all caught up             — plain blue bars
      grey                  nothing connected / configured       — grey bars
      red                   configured but unreachable / error   — red bars

    This script is the SINGLE SOURCE OF TRUTH for that glyph: the geometry lives here once
    and every output is rendered from it with GDI+ (System.Drawing), so there is no
    dependency on ImageMagick or Inkscape.

    Outputs:
      src/Trayage.App/Assets/trayage.ico               App / .exe icon (graphite tile + accented bars)
      src/Trayage.App/Assets/trayage-caughtup.ico       State: plain blue bars (connected, all read)
      src/Trayage.App/Assets/trayage-unread.ico         State: blue bars + amber top (unread waiting)
      src/Trayage.App/Assets/trayage-disconnected.ico   State: grey bars (nothing connected)
      src/Trayage.App/Assets/trayage-error.ico          State: red bars (configured but unreachable)
      assets/oauth/trayage-oauth-{512,256,128}.png      Full-colour tile for the OAuth app logo

    State icons are transparent (no tile); the app/exe and OAuth tiles sit on the graphite
    badge. Pass -Preview to also drop a contact sheet of every variant in tools/preview/
    for eyeballing.

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
# Brand palette (matches docs/styles.css)
# ---------------------------------------------------------------------------
$Color = @{
    GraphiteHi  = [System.Drawing.ColorTranslator]::FromHtml('#2b3340')  # tile top-left
    GraphiteLo  = [System.Drawing.ColorTranslator]::FromHtml('#161b23')  # tile bottom-right
    Border      = [System.Drawing.ColorTranslator]::FromHtml('#343d4d')  # tile edge
    AmberBright = [System.Drawing.ColorTranslator]::FromHtml('#ffd587')  # accented top bar
    Signal      = [System.Drawing.ColorTranslator]::FromHtml('#e5484d')  # error
    SignalSoft  = [System.Drawing.ColorTranslator]::FromHtml('#ff8b8e')  # error accent
    Grey        = [System.Drawing.ColorTranslator]::FromHtml('#9aa3b5')  # disconnected
    GreySoft    = [System.Drawing.ColorTranslator]::FromHtml('#cfd6e4')  # disconnected accent
    BlueHi      = [System.Drawing.ColorTranslator]::FromHtml('#5b97f7')  # bars (lit)
}

# ---------------------------------------------------------------------------
# Glyph geometry, in normalised [0,1] fractions of the canvas: three left-aligned,
# round-capped horizontal bars of descending length, evenly spaced to fill the frame.
# ---------------------------------------------------------------------------
$Geom = @{
    BarLeft   = 0.20            # left edge of every bar (cap tip sits a little left of this)
    BarThick  = 0.135           # bar thickness (also the round-cap diameter)
    BarRights = @(0.84, 0.64, 0.46)  # right edge of each bar, top -> bottom (descending)
    BarCys    = @(0.30, 0.50, 0.70)  # vertical centre of each bar, top -> bottom
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

# Draws the triage mark: three descending priority bars, top one optionally accented.
function Add-Bars {
    param(
        [System.Drawing.Graphics]$G,
        [int]$Size,
        [System.Drawing.Color]$Bar,
        [System.Drawing.Color]$Accent,
        [bool]$AccentTop
    )
    $px = { param($f) [single]($f * $Size) }
    $x0 = & $px $Geom.BarLeft
    $th = [single]($Geom.BarThick * $Size)
    for ($i = 0; $i -lt 3; $i++) {
        $col = if ($i -eq 0 -and $AccentTop) { $Accent } else { $Bar }
        $pen = [System.Drawing.Pen]::new($col, $th)
        try {
            $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
            $pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
            $y = & $px $Geom.BarCys[$i]
            $G.DrawLine($pen, $x0, $y, (& $px $Geom.BarRights[$i]), $y)
        }
        finally { $pen.Dispose() }
    }
}

# Renders one icon variant at a pixel size, returning a Bitmap.
function New-GlyphBitmap {
    param(
        [int]$Size,
        [hashtable]$Variant  # Tile, Bar, Accent, AccentTop
    )

    $bmp = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
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

        Add-Bars -G $g -Size $Size -Bar $Variant.Bar -Accent $Variant.Accent -AccentTop $Variant.AccentTop
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
# Icon variants — bars tinted per state; the top bar is accented amber when there is
# something to act on (app badge and unread). Connected/caught-up, disconnected and
# error are distinguished by tint alone.
# ---------------------------------------------------------------------------
$Variants = @{
    # App / OAuth — accented bars on the graphite tile.
    App          = @{ Tile = $true;  Bar = $Color.BlueHi; Accent = $Color.AmberBright; AccentTop = $true }
    # Connected, all caught up — plain blue bars.
    CaughtUp     = @{ Tile = $false; Bar = $Color.BlueHi; Accent = $Color.AmberBright; AccentTop = $false }
    # Unread waiting — blue bars + amber top.
    Unread       = @{ Tile = $false; Bar = $Color.BlueHi; Accent = $Color.AmberBright; AccentTop = $true }
    # Nothing connected / configured — grey bars.
    Disconnected = @{ Tile = $false; Bar = $Color.Grey;   Accent = $Color.GreySoft;    AccentTop = $false }
    # Configured but unreachable / error — red bars.
    Error        = @{ Tile = $false; Bar = $Color.Signal; Accent = $Color.SignalSoft;  AccentTop = $false }
}

# ---------------------------------------------------------------------------
# Output locations
# ---------------------------------------------------------------------------
$repo      = Split-Path $PSScriptRoot -Parent
$appAssets = Join-Path $repo 'src/Trayage.App/Assets'
$oauthDir  = Join-Path $repo 'assets/oauth'
New-Item -ItemType Directory -Force -Path $appAssets, $oauthDir | Out-Null

$tileSizes  = @(16, 20, 24, 32, 48, 64, 128, 256)
$stateSizes = @(16, 20, 24, 32, 48, 64, 256)

Write-Host 'Generating Trayage icons...'

# App / .exe icon — the full badge.
Build-Ico -Sizes $tileSizes -Path (Join-Path $appAssets 'trayage.ico') -Variant $Variants.App

# State icons — transparent, tinted per state.
Build-Ico -Sizes $stateSizes -Path (Join-Path $appAssets 'trayage-caughtup.ico')     -Variant $Variants.CaughtUp
Build-Ico -Sizes $stateSizes -Path (Join-Path $appAssets 'trayage-unread.ico')       -Variant $Variants.Unread
Build-Ico -Sizes $stateSizes -Path (Join-Path $appAssets 'trayage-disconnected.ico') -Variant $Variants.Disconnected
Build-Ico -Sizes $stateSizes -Path (Join-Path $appAssets 'trayage-error.ico')        -Variant $Variants.Error

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
