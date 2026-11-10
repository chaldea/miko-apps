param(
    [string]$Source = "FBox/artifacts/verification",
    [string]$Output = "FBox/artifacts/contact"
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
$files = @(Get-ChildItem -LiteralPath $Source -Filter *.png | Sort-Object Name)
$directory = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Output))
[IO.Directory]::CreateDirectory($directory) | Out-Null
$font = [Drawing.Font]::new("Arial", 10)
try {
    for ($offset = 0; $offset -lt $files.Count; $offset += 12) {
        $bitmap = [Drawing.Bitmap]::new(840, 1380)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([Drawing.Color]::FromArgb(221, 227, 233))
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            for ($i = 0; $i -lt 12 -and $offset + $i -lt $files.Count; $i++) {
                $file = $files[$offset + $i]
                $image = [Drawing.Image]::FromFile($file.FullName)
                try {
                    $x = ($i % 4) * 210 + 7
                    $y = [Math]::Floor($i / 4) * 460 + 27
                    $scale = [Math]::Min(195.0 / $image.Width, 422.0 / $image.Height)
                    $graphics.DrawImage($image, [Drawing.RectangleF]::new($x, $y, $image.Width * $scale, $image.Height * $scale))
                    $graphics.DrawString($file.BaseName, $font, [Drawing.Brushes]::Black, [float]$x, [float]($y - 21))
                } finally { $image.Dispose() }
            }
            $bitmap.Save("$Output-$([int]($offset / 12) + 1).jpg", [Drawing.Imaging.ImageFormat]::Jpeg)
        } finally { $graphics.Dispose(); $bitmap.Dispose() }
    }
} finally { $font.Dispose() }
Write-Output "Created contact sheets for $($files.Count) captures."
