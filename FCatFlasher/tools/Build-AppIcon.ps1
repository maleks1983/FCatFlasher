param(
    [string]$Source = "Assets\FCatFlasher-icon-source.png",
    [string]$PngOutput = "Assets\FCatFlasher-icon.png",
    [string]$IcoOutput = "Assets\FCatFlasher.ico"
)

Add-Type -AssemblyName System.Drawing

$sourceImage = [System.Drawing.Bitmap]::new($Source)
$transparent = [System.Drawing.Bitmap]::new(
    $sourceImage.Width,
    $sourceImage.Height,
    [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

for ($y = 0; $y -lt $sourceImage.Height; $y++) {
    for ($x = 0; $x -lt $sourceImage.Width; $x++) {
        $pixel = $sourceImage.GetPixel($x, $y)
        $other = [Math]::Max([int]$pixel.R, [int]$pixel.B)
        $keyStrength = [int]$pixel.G - $other

        if ($pixel.G -gt 150 -and $keyStrength -gt 45) {
            $alpha = [Math]::Max(0, [Math]::Min(255, [int](255 * (85 - $keyStrength) / 40)))
            $green = [Math]::Min([int]$pixel.G, $other)
            $cleanPixel = [System.Drawing.Color]::FromArgb($alpha, $pixel.R, $green, $pixel.B)
        }
        else {
            $cleanPixel = [System.Drawing.Color]::FromArgb(255, $pixel.R, $pixel.G, $pixel.B)
        }

        $transparent.SetPixel($x, $y, $cleanPixel)
    }
}

$transparent.Save($PngOutput, [System.Drawing.Imaging.ImageFormat]::Png)
$sourceImage.Dispose()

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = foreach ($size in $sizes) {
    $resized = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($resized)
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.DrawImage($transparent, 0, 0, $size, $size)
    $graphics.Dispose()

    $stream = [System.IO.MemoryStream]::new()
    $resized.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $resized.Dispose()
    ,$stream.ToArray()
    $stream.Dispose()
}

$transparent.Dispose()
$file = [System.IO.File]::Create($IcoOutput)
$writer = [System.IO.BinaryWriter]::new($file)
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$frames.Count)

$offset = 6 + (16 * $frames.Count)
for ($index = 0; $index -lt $frames.Count; $index++) {
    $size = $sizes[$index]
    $writer.Write([byte]($(if ($size -eq 256) { 0 } else { $size })))
    $writer.Write([byte]($(if ($size -eq 256) { 0 } else { $size })))
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$frames[$index].Length)
    $writer.Write([uint32]$offset)
    $offset += $frames[$index].Length
}

foreach ($frame in $frames) {
    $writer.Write($frame)
}

$writer.Dispose()
$file.Dispose()

