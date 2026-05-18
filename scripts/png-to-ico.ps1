param(
    [Parameter(Mandatory = $true)][string]$Source,
    [Parameter(Mandatory = $true)][string]$Destination,
    [int[]]$Sizes = @(16, 32, 48, 64, 128, 256)
)

Add-Type -AssemblyName System.Drawing

$src = [System.Drawing.Image]::FromFile((Resolve-Path $Source).Path)
try {
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)

    $bw.Write([uint16]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]$Sizes.Count)

    $dataOffset = 6 + (16 * $Sizes.Count)
    $blobs = @()

    foreach ($s in $Sizes) {
        $bmp = New-Object System.Drawing.Bitmap $s, $s
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $g.DrawImage($src, 0, 0, $s, $s)
        $g.Dispose()

        $pngStream = New-Object System.IO.MemoryStream
        $bmp.Save($pngStream, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        $bytes = $pngStream.ToArray()
        $pngStream.Dispose()

        $dim = if ($s -ge 256) { [byte]0 } else { [byte]$s }
        $bw.Write([byte]$dim)
        $bw.Write([byte]$dim)
        $bw.Write([byte]0)
        $bw.Write([byte]0)
        $bw.Write([uint16]1)
        $bw.Write([uint16]32)
        $bw.Write([uint32]$bytes.Length)
        $bw.Write([uint32]$dataOffset)

        $dataOffset += $bytes.Length
        $blobs += , $bytes
    }

    foreach ($b in $blobs) { $bw.Write($b) }
    $bw.Flush()

    [System.IO.File]::WriteAllBytes((Join-Path (Get-Location) $Destination), $ms.ToArray())
    $ms.Dispose()
    Write-Host "Wrote $Destination ($($Sizes -join ','))"
}
finally {
    $src.Dispose()
}
