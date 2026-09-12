[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$targetPath = Join-Path $repositoryRoot 'src/goat-shooooting.SampleGame/goat-shooooting.ico'
$bitmap = [System.Drawing.Bitmap]::new(256, 256, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$stream = [System.IO.MemoryStream]::new()

try {
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::FromArgb(7, 16, 31))

    $trianglePen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(85, 214, 190), 20)
    $trianglePen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    try {
        $points = [System.Drawing.Point[]]@(
            [System.Drawing.Point]::new(128, 32),
            [System.Drawing.Point]::new(216, 192),
            [System.Drawing.Point]::new(40, 192),
            [System.Drawing.Point]::new(128, 32)
        )
        $graphics.DrawLines($trianglePen, $points)
    }
    finally {
        $trianglePen.Dispose()
    }

    $markerBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 235, 84))
    try {
        $graphics.FillEllipse($markerBrush, 104, 120, 48, 48)
    }
    finally {
        $markerBrush.Dispose()
    }

    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $png = $stream.ToArray()
    $file = [System.IO.File]::Open($targetPath, [System.IO.FileMode]::Create)
    $writer = [System.IO.BinaryWriter]::new($file)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]1)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$png.Length)
        $writer.Write([uint32]22)
        $writer.Write($png)
    }
    finally {
        $writer.Dispose()
        $file.Dispose()
    }
}
finally {
    $stream.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}

Write-Host "Generated $targetPath"
