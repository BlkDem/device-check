<#
.SYNOPSIS
  Regenerates src/DeviceCheck.Agent.UI/Assets/agent.ico from the site logo geometry (web/public/favicon.svg).
  Run only when the logo changes; the .ico is committed.
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$out = Join-Path (Split-Path -Parent $PSScriptRoot) 'src/DeviceCheck.Agent.UI/Assets/agent.ico'
New-Item -ItemType Directory -Force -Path (Split-Path $out) | Out-Null

function New-Frame([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $k = $size / 32.0
    $pt = { param($x, $y) New-Object System.Drawing.PointF ($x * $k), ($y * $k) }
    $cyan = [System.Drawing.Color]::FromArgb(255, 0x19, 0xE6, 0xFF)
    $magenta = [System.Drawing.Color]::FromArgb(255, 0xFF, 0x2E, 0xC4)
    $w = [Math]::Max(1.0, 1.9 * $k)

    # octagon
    $oct = [System.Drawing.PointF[]]@((& $pt 9 1.5), (& $pt 23 1.5), (& $pt 30.5 9), (& $pt 30.5 23), (& $pt 23 30.5), (& $pt 9 30.5), (& $pt 1.5 23), (& $pt 1.5 9))
    $g.FillPolygon((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0x0B, 0x1B, 0x24))), $oct)
    $g.DrawPolygon((New-Object System.Drawing.Pen $cyan, $w), $oct)

    # crosshair ticks (skipped at 16 px: they turn into noise)
    if ($size -ge 24) {
        $pm = New-Object System.Drawing.Pen $magenta, $w
        $g.DrawLine($pm, (& $pt 16 5), (& $pt 16 9)); $g.DrawLine($pm, (& $pt 16 23), (& $pt 16 27))
        $g.DrawLine($pm, (& $pt 5 16), (& $pt 9 16)); $g.DrawLine($pm, (& $pt 23 16), (& $pt 27 16))
    }

    # check mark
    $pc = New-Object System.Drawing.Pen $cyan, ([Math]::Max(1.6, 2.8 * $k))
    $pc.StartCap = 'Square'; $pc.EndCap = 'Square'; $pc.LineJoin = 'Miter'
    $g.DrawLines($pc, [System.Drawing.PointF[]]@((& $pt 11 16.2), (& $pt 14.5 19.7), (& $pt 21 12.6)))
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$frames = $sizes | ForEach-Object { ,(New-Frame $_) }

# ICO container with PNG-compressed entries (supported since Windows Vista)
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $frames[$i].Length
    $bw.Write([byte]($s % 256)); $bw.Write([byte]($s % 256)); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$len); $bw.Write([UInt32]$offset)
    $offset += $len
}
foreach ($f in $frames) { $bw.Write($f) }
$bw.Close()
"written $out ($((Get-Item $out).Length) bytes)"

# MSIX / Microsoft Store logos (agent/msix/Assets): same mark, PNG.
$assets = Join-Path (Split-Path -Parent $PSScriptRoot) 'msix/Assets'
New-Item -ItemType Directory -Force -Path $assets | Out-Null
foreach ($l in @(@{ n = 'StoreLogo'; s = 50 }, @{ n = 'Square44x44Logo'; s = 44 }, @{ n = 'Square150x150Logo'; s = 150 }, @{ n = 'Square44x44Logo.targetsize-256'; s = 256 })) {
    [IO.File]::WriteAllBytes((Join-Path $assets "$($l.n).png"), (New-Frame $l.s))
}
# Wide tile: the mark centred on a transparent 310×150 canvas.
$wide = New-Object System.Drawing.Bitmap 310, 150
$g = [System.Drawing.Graphics]::FromImage($wide)
$mark = [System.Drawing.Image]::FromStream((New-Object System.IO.MemoryStream (, (New-Frame 120))))
$g.DrawImage($mark, 95, 15, 120, 120)
$g.Dispose()
$wide.Save((Join-Path $assets 'Wide310x150Logo.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$wide.Dispose()
"written MSIX logos to $assets"
