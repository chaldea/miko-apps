# A geometric F mark, drawn locally without fonts or third-party artwork.
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
$appRoot = Split-Path $PSScriptRoot -Parent
$files = @(
    Get-ChildItem "$appRoot/MikoApp.FBox.Android/Resources" -Recurse -Filter *.png
    Get-ChildItem "$appRoot/MikoApp.FBox.iOS/Assets.xcassets" -Recurse -Filter *.png
)
foreach ($file in $files) {
    $original = [Drawing.Image]::FromFile($file.FullName)
    $size = $original.Width
    $original.Dispose()
    $bitmap = [Drawing.Bitmap]::new($size, $size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $foreground = $file.BaseName -eq "appicon_foreground"
        $background = $file.BaseName -eq "appicon_background"
        $graphics.Clear($(if ($foreground) { [Drawing.Color]::Transparent } else { [Drawing.Color]::FromArgb(16, 107, 172) }))
        if (!$background) {
            $graphics.ScaleTransform($size / 100.0, $size / 100.0)
            $graphics.FillRectangle([Drawing.Brushes]::White, 32, 26, 12, 49)
            $graphics.FillRectangle([Drawing.Brushes]::White, 32, 26, 40, 12)
            $graphics.FillRectangle([Drawing.Brushes]::White, 32, 46, 31, 11)
        }
        $bitmap.Save($file.FullName, [Drawing.Imaging.ImageFormat]::Png)
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
}
Write-Output "Generated $($files.Count) FBox icon assets."
