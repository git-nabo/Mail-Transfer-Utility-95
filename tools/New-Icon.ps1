<#
.SYNOPSIS
    Generates assets\app.ico - a Windows 95 style mail-transfer icon
    (two envelopes with a transfer arrow).

.DESCRIPTION
    Draws the icon on a 16x16 virtual grid then scales it up to each target size,
    so it stays genuinely pixel-art. PNG-compressed icon frames are supported by
    Windows Vista and later, so this works on Windows 10 and Windows 11.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\New-Icon.ps1
#>
[CmdletBinding()]
param(
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrEmpty($OutputPath)) {
    $scriptDir = $PSScriptRoot
    if ([string]::IsNullOrEmpty($scriptDir)) {
        $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    }
    if ([string]::IsNullOrEmpty($scriptDir)) {
        $scriptDir = (Get-Location).Path
    }
    $OutputPath = Join-Path (Split-Path -Parent $scriptDir) 'assets\app.ico'
}

Add-Type -AssemblyName System.Drawing

function New-Canvas([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None
    $g.Clear([System.Drawing.Color]::Transparent)
    return @($bmp, $g)
}

function New-MailIcon([int]$size) {
    $canvas = New-Canvas $size
    $bmp = $canvas[0]
    $g = $canvas[1]
    $u = [double]$size / 16.0

    $white = '#FFFFFF'
    $cream = '#ECE9D8'
    $gray  = '#808080'
    $dark  = '#404040'
    $navy  = '#000080'
    $green = '#00A000'
    $greenD = '#007000'

    function Cell([System.Drawing.Graphics]$gr, [double]$u, [int]$x, [int]$y, [int]$w, [int]$h, [string]$hex) {
        $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml($hex))
        $gr.FillRectangle($brush, [float]($x * $u), [float]($y * $u), [float]($w * $u), [float]($h * $u))
        $brush.Dispose()
    }

    # --- Back envelope (upper-left), cream body ---
    Cell $g $u 1 1 10 7 $cream
    # border
    Cell $g $u 1 1 10 1 $gray
    Cell $g $u 1 7 10 1 $gray
    Cell $g $u 1 1 1 7 $gray
    Cell $g $u 10 1 1 7 $gray
    # flap (navy V)
    for ($i = 0; $i -lt 5; $i++) {
        Cell $g $u (1 + $i) (1 + $i) 1 1 $navy
        Cell $g $u (10 - $i) (1 + $i) 1 1 $navy
    }

    # --- Front envelope (lower-right), white body ---
    Cell $g $u 5 7 10 7 $white
    Cell $g $u 5 7 10 1 $dark
    Cell $g $u 5 13 10 1 $dark
    Cell $g $u 5 7 1 7 $dark
    Cell $g $u 14 7 1 7 $dark
    for ($i = 0; $i -lt 5; $i++) {
        Cell $g $u (5 + $i) (7 + $i) 1 1 $navy
        Cell $g $u (14 - $i) (7 + $i) 1 1 $navy
    }

    # --- Transfer arrow (green) sweeping from the back to the front envelope ---
    Cell $g $u 11 4 1 1 $greenD
    Cell $g $u 12 5 1 1 $green
    Cell $g $u 13 6 1 1 $green
    # arrowhead
    Cell $g $u 13 4 1 1 $green
    Cell $g $u 13 5 1 1 $green
    Cell $g $u 12 6 1 1 $green

    $g.Dispose()
    return $bmp
}

function ConvertTo-PngBytes([System.Drawing.Bitmap]$bmp) {
    $ms = New-Object System.IO.MemoryStream
    try {
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        return ,([byte[]]$ms.ToArray())
    } finally {
        $ms.Dispose()
    }
}

$output = [System.IO.Path]::GetFullPath($OutputPath)
$outDir = [System.IO.Path]::GetDirectoryName($output)
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = @()
foreach ($s in $sizes) {
    $bmp = New-MailIcon $s
    $frames += ,@{ Size = $s; Data = (ConvertTo-PngBytes $bmp) }
    $bmp.Dispose()
}

$stream = [System.IO.File]::Create($output)
$writer = New-Object System.IO.BinaryWriter($stream)
try {
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]$frames.Count)

    $offset = 6 + (16 * $frames.Count)
    foreach ($f in $frames) {
        $w = $(if ($f.Size -ge 256) { 0 } else { $f.Size })
        $writer.Write([Byte]$w)
        $writer.Write([Byte]$w)
        $writer.Write([Byte]0)
        $writer.Write([Byte]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]32)
        $writer.Write([UInt32]$f.Data.Length)
        $writer.Write([UInt32]$offset)
        $offset += $f.Data.Length
    }
    foreach ($f in $frames) {
        $writer.Write($f.Data)
    }
} finally {
    $writer.Dispose()
    $stream.Dispose()
}

Write-Host ("Wrote {0} ({1} bytes, sizes: {2})" -f $output, (Get-Item $output).Length, ($sizes -join ', '))
