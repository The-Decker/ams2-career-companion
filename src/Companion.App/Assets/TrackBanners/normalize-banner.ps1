param(
    [Parameter(Mandatory = $true)]
    [string] $InputPath,

    [Parameter(Mandatory = $true)]
    [string] $OutputPath,

    [ValidateRange(0.0, 1.0)]
    [double] $VerticalBias = 0.78
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$inputImage = [System.Drawing.Bitmap]::FromFile($InputPath)
try {
    $targetWidth = 1920
    $targetHeight = 440
    $cropHeight = [int] [Math]::Round($inputImage.Width * $targetHeight / $targetWidth)
    if ($cropHeight -gt $inputImage.Height) {
        throw "Source image is too narrow for the 1920x440 banner contract: $($inputImage.Width)x$($inputImage.Height)."
    }

    $cropY = [int] [Math]::Round(($inputImage.Height - $cropHeight) * $VerticalBias)
    $destination = [System.Drawing.Bitmap]::new($targetWidth, $targetHeight)
    try {
        $destination.SetResolution(96, 96)
        $graphics = [System.Drawing.Graphics]::FromImage($destination)
        try {
            # Pixel-art masters should remain crisp after the small normalization scale.
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
            $graphics.DrawImage(
                $inputImage,
                [System.Drawing.Rectangle]::new(0, 0, $targetWidth, $targetHeight),
                [System.Drawing.Rectangle]::new(0, $cropY, $inputImage.Width, $cropHeight),
                [System.Drawing.GraphicsUnit]::Pixel)
        }
        finally {
            $graphics.Dispose()
        }

        $directory = Split-Path -Parent $OutputPath
        if ($directory) {
            New-Item -ItemType Directory -Force -Path $directory | Out-Null
        }

        $jpeg = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() |
            Where-Object MimeType -eq 'image/jpeg'
        $encoderParameters = [System.Drawing.Imaging.EncoderParameters]::new(1)
        try {
            $encoderParameters.Param[0] = [System.Drawing.Imaging.EncoderParameter]::new(
                [System.Drawing.Imaging.Encoder]::Quality,
                92L)
            $destination.Save($OutputPath, $jpeg, $encoderParameters)
        }
        finally {
            $encoderParameters.Dispose()
        }
    }
    finally {
        $destination.Dispose()
    }
}
finally {
    $inputImage.Dispose()
}

$result = [System.Drawing.Image]::FromFile($OutputPath)
try {
    if ($result.Width -ne 1920 -or $result.Height -ne 440) {
        throw "Normalized banner has invalid dimensions: $($result.Width)x$($result.Height)."
    }
}
finally {
    $result.Dispose()
}
