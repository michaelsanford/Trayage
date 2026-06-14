#requires -Version 7.0
<#
.SYNOPSIS
    Generates every Trayage raster icon asset from a single glyph definition.

.DESCRIPTION
    Trayage's mark is a "merging branch" (a git trunk with one branch diverging and
    merging back into the mainline) — a nod to triaging pull-request / code activity.
    This script is the SINGLE SOURCE OF TRUTH for that glyph: the coordinates live here
    once and every output is rendered from them with GDI+ (System.Drawing), so there is
    no dependency on ImageMagick or Inkscape.

    Outputs:
      src/Trayage.App/Assets/trayage.ico            App / .exe icon (graphite tile + amber glyph)
      src/Trayage.App/Assets/trayage-disconnected.ico  Tray state: grey   (no provider connected)
      src/Trayage.App/Assets/trayage-caughtup.ico       Tray state: green  (connected, all read)
      src/Trayage.App/Assets/trayage-unread.ico         Tray state: amber  (unread items waiting)
      assets/oauth/trayage-oauth-{512,256,128}.png    Full-colour tile for the OAuth app logo

    Tray-state icons are transparent, single-colour glyphs (legible when Windows
    desaturates them); the app/exe and OAuth tiles are the full graphite-and-amber badge.

    Re-run this whenever the glyph changes. The colours mirror docs/styles.css.
#>
[CmdletBinding()]
param()

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
    Amber       = [System.Drawing.ColorTranslator]::FromHtml('#f1b24a')  # accent / unread
    AmberBright = [System.Drawing.ColorTranslator]::FromHtml('#ffd587')  # merge-node highlight
    Grey        = [System.Drawing.ColorTranslator]::FromHtml('#9aa3b5')  # disconnected
    Green       = [System.Drawing.ColorTranslator]::FromHtml('#41c463')  # caught up
}

# ---------------------------------------------------------------------------
# Glyph geometry, in normalised [0,1] fractions of the canvas. The branch (node
# B, bottom-right) rises and curves left to merge into the mainline top (node A);
# node C is the trunk base. Tuned so nodes + strokes never clip at the edges.
# ---------------------------------------------------------------------------
$Geom = @{
    TrunkX  = 0.32
    TopY    = 0.20   # node A — merge target
    BaseY   = 0.80   # node C — trunk base
    BranchX = 0.72   # node B — feature branch tip
    # Bezier control points carrying node B up and into node A.
    C1      = @(0.72, 0.40)
    C2      = @(0.54, 0.20)
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

# Renders the glyph (and optional tile) at a given pixel size, returning a Bitmap.
function New-GlyphBitmap {
    param(
        [int]$Size,
        [switch]$Tile,                 # draw the graphite badge behind the glyph
        [System.Drawing.Color]$Glyph,  # stroke / node colour
        [System.Drawing.Color]$NodeAccent = [System.Drawing.Color]::Empty  # optional bright merge node
    )

    $bmp = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.Clear([System.Drawing.Color]::Transparent)

        if ($Tile) {
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

        # Resolve normalised geometry to pixels.
        $ax = [single]($Geom.TrunkX  * $Size); $ay = [single]($Geom.TopY  * $Size)   # node A (top / merge)
        $cx = [single]($Geom.TrunkX  * $Size); $cy = [single]($Geom.BaseY * $Size)   # node C (trunk base)
        $bx = [single]($Geom.BranchX * $Size); $by = [single]($Geom.BaseY * $Size)   # node B (branch tip)
        $c1x = [single]($Geom.C1[0] * $Size);  $c1y = [single]($Geom.C1[1] * $Size)
        $c2x = [single]($Geom.C2[0] * $Size);  $c2y = [single]($Geom.C2[1] * $Size)

        $stroke = [single]([math]::Max(2.0, 0.105 * $Size))
        $nodeR  = [single]($stroke * 1.05)

        $pen = [System.Drawing.Pen]::new($Glyph, $stroke)
        try {
            $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
            $pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
            $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
            $g.DrawLine($pen, $ax, $ay, $cx, $cy)                       # trunk
            $g.DrawBezier($pen, $bx, $by, $c1x, $c1y, $c2x, $c2y, $ax, $ay)  # branch -> merge
        }
        finally { $pen.Dispose() }

        $accent = if ($NodeAccent -eq [System.Drawing.Color]::Empty) { $Glyph } else { $NodeAccent }
        $nodes = @(
            @{ X = $ax; Y = $ay; C = $accent },  # merge node (highlighted on the tile)
            @{ X = $cx; Y = $cy; C = $Glyph },
            @{ X = $bx; Y = $by; C = $Glyph }
        )
        foreach ($n in $nodes) {
            $brush = [System.Drawing.SolidBrush]::new($n.C)
            try { $g.FillEllipse($brush, [single]($n.X - $nodeR), [single]($n.Y - $nodeR), [single](2 * $nodeR), [single](2 * $nodeR)) }
            finally { $brush.Dispose() }
        }
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
    param([int[]]$Sizes, [string]$Path, [switch]$Tile, [System.Drawing.Color]$Glyph, [System.Drawing.Color]$NodeAccent = [System.Drawing.Color]::Empty)
    $frames = foreach ($s in $Sizes) {
        $bmp = New-GlyphBitmap -Size $s -Tile:$Tile -Glyph $Glyph -NodeAccent $NodeAccent
        try { @{ Size = $s; Bytes = (Get-PngBytes -Bitmap $bmp) } }
        finally { $bmp.Dispose() }
    }
    Save-Ico -Frames $frames -Path $Path
    Write-Host ("  {0}  ({1} sizes)" -f (Split-Path $Path -Leaf), $Sizes.Count)
}

function Build-Png {
    param([int]$Size, [string]$Path, [switch]$Tile, [System.Drawing.Color]$Glyph, [System.Drawing.Color]$NodeAccent = [System.Drawing.Color]::Empty)
    $bmp = New-GlyphBitmap -Size $Size -Tile:$Tile -Glyph $Glyph -NodeAccent $NodeAccent
    try { $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png) }
    finally { $bmp.Dispose() }
    Write-Host ("  {0}  ({1}px)" -f (Split-Path $Path -Leaf), $Size)
}

# ---------------------------------------------------------------------------
# Output locations
# ---------------------------------------------------------------------------
$repo      = Split-Path $PSScriptRoot -Parent
$appAssets = Join-Path $repo 'src/Trayage.App/Assets'
$oauthDir  = Join-Path $repo 'assets/oauth'
New-Item -ItemType Directory -Force -Path $appAssets, $oauthDir | Out-Null

$tileSizes  = @(16, 20, 24, 32, 48, 64, 128, 256)
$traySizes  = @(16, 20, 24, 32, 48, 64, 256)

Write-Host 'Generating Trayage icons...'

# App / .exe icon — the full badge.
Build-Ico -Sizes $tileSizes -Path (Join-Path $appAssets 'trayage.ico') -Tile -Glyph $Color.Amber -NodeAccent $Color.AmberBright

# Tray state icons — transparent mono glyphs, coloured by state.
Build-Ico -Sizes $traySizes -Path (Join-Path $appAssets 'trayage-disconnected.ico') -Glyph $Color.Grey
Build-Ico -Sizes $traySizes -Path (Join-Path $appAssets 'trayage-caughtup.ico')     -Glyph $Color.Green
Build-Ico -Sizes $traySizes -Path (Join-Path $appAssets 'trayage-unread.ico')       -Glyph $Color.Amber

# OAuth app tiles — large full-colour badges (e.g. GitHub OAuth App logo upload).
foreach ($s in 512, 256, 128) {
    Build-Png -Size $s -Path (Join-Path $oauthDir ("trayage-oauth-{0}.png" -f $s)) -Tile -Glyph $Color.Amber -NodeAccent $Color.AmberBright
}

Write-Host 'Done.'
