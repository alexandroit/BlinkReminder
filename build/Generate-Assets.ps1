[CmdletBinding()]
param([string]$SourcePng = '', [string]$DarkSourcePng = '')
. "$PSScriptRoot/Common.ps1"
Assert-Windows
Add-Type -AssemblyName System.Drawing
$destination = Join-Path $RepositoryRoot 'assets/generated'
New-Item $destination -ItemType Directory -Force | Out-Null
[xml]$placeholder = Get-Content "$RepositoryRoot/assets/branding/placeholder.svg" -Raw

function Write-Image {
    param([int]$Size, [string]$Path, [bool]$Dark = $false)
    $bitmap = [Drawing.Bitmap]::new($Size, $Size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([Drawing.Color]::Transparent)
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $sourcePath = if ($Dark -and $DarkSourcePng) { $DarkSourcePng } else { $SourcePng }
        if ($sourcePath) {
            $source = [Drawing.Image]::FromFile((Resolve-Path $sourcePath))
            try {
                if ($source.Width -ne $source.Height) { throw 'The source PNG must be square, with transparent margins already included.' }
                $graphics.DrawImage($source, 0, 0, $Size, $Size)
            } finally { $source.Dispose() }
        } else {
            # This deliberately small renderer accepts only our neutral circle SVG.
            foreach ($circle in $placeholder.svg.circle) {
                $color = [Drawing.ColorTranslator]::FromHtml([string]$circle.fill)
                if ($Dark -and $circle.fill -eq '#334155') { $color = [Drawing.Color]::FromArgb(15, 23, 42) }
                $brush = [Drawing.SolidBrush]::new($color)
                try {
                    $scale = $Size / 256.0
                    $graphics.FillEllipse($brush, [single](([double]$circle.cx - [double]$circle.r) * $scale), [single](([double]$circle.cy - [double]$circle.r) * $scale), [single]([double]$circle.r * 2 * $scale), [single]([double]$circle.r * 2 * $scale))
                } finally { $brush.Dispose() }
            }
        }
        $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
}

Write-Image 50 "$destination/StoreLogo.png"
Write-Image 44 "$destination/Square44x44Logo.png"
Write-Image 150 "$destination/Square150x150Logo.png"
Write-Image 256 "$destination/icon-light.png"
Write-Image 256 "$destination/icon-dark.png" $true

# PNG-compressed ICO frames are supported by the required Windows 11 platform.
foreach ($variant in @('app', 'tray-dark')) {
    $sizes = @(16, 20, 24, 32, 40, 48, 64, 256)
    $frames = [Collections.Generic.List[byte[]]]::new()
    foreach ($size in $sizes) {
        $temp = Join-Path ([IO.Path]::GetTempPath()) "blink-icon-$([Guid]::NewGuid()).png"
        try { Write-Image $size $temp ($variant -eq 'tray-dark'); $frames.Add([IO.File]::ReadAllBytes($temp)) }
        finally { Remove-Item $temp -ErrorAction SilentlyContinue }
    }
    $stream = [IO.File]::Create("$destination/$variant.ico")
    $writer = [IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
        $offset = 6 + 16 * $sizes.Count
        for ($i = 0; $i -lt $sizes.Count; $i++) {
            $sizeByte = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
            $writer.Write([byte]$sizeByte); $writer.Write([byte]$sizeByte)
            $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
            $offset += $frames[$i].Length
        }
        foreach ($frame in $frames) { $writer.Write($frame) }
    } finally { $writer.Dispose(); $stream.Dispose() }
}
