[CmdletBinding()]
param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\..\Assets\Audio\Sfx\seat-confirm.wav')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Original deterministic FM synthesis for the SMGP seat-lock interaction. No recordings,
# samples, random input, or emulated game audio are used. The compact C-G-C arpeggio and bright
# decaying modulators evoke a 16-bit console confirmation while remaining a new composition.
$sampleRate = 48000
$duration = 0.480
$frameCount = [int]($sampleRate * $duration)
$samples = [double[]]::new($frameCount)

function Add-FmVoice {
    param(
        [double[]]$Buffer,
        [double]$Start,
        [double]$VoiceDuration,
        [double]$Frequency,
        [double]$Amplitude,
        [double]$ModRatio,
        [double]$ModIndex,
        [double]$Decay
    )

    $startFrame = [int][Math]::Floor($Start * $sampleRate)
    $endFrame = [Math]::Min($Buffer.Length, [int][Math]::Ceiling(($Start + $VoiceDuration) * $sampleRate))
    for ($frame = $startFrame; $frame -lt $endFrame; $frame++) {
        $time = ($frame / $sampleRate) - $Start
        $position = $time / $VoiceDuration
        $remaining = $VoiceDuration - $time
        $attack = [Math]::Min(1.0, $time / 0.0025)
        $release = [Math]::Min(1.0, $remaining / 0.065)
        $envelope = [Math]::Sin(0.5 * [Math]::PI * $attack)
        $envelope *= [Math]::Sin(0.5 * [Math]::PI * $release)
        $envelope *= [Math]::Exp(-$Decay * $position)

        $carrier = 2.0 * [Math]::PI * $Frequency * $time
        $modulator = 2.0 * [Math]::PI * $Frequency * $ModRatio * $time
        $modulation = $ModIndex * [Math]::Exp(-5.2 * $position) * [Math]::Sin($modulator)
        $voice = [Math]::Sin($carrier + $modulation)
        $voice += 0.10 * [Math]::Sin((2.0 * $carrier) + 0.41)
        $Buffer[$frame] += $Amplitude * $envelope * $voice
    }
}

Add-FmVoice $samples 0.004 0.205 523.25 0.17 2.00 2.30 3.5
Add-FmVoice $samples 0.082 0.225 783.99 0.16 3.00 1.65 3.2
Add-FmVoice $samples 0.168 0.292 1046.50 0.18 2.00 1.20 2.6
Add-FmVoice $samples 0.178 0.255 261.63 0.055 2.00 0.55 2.5

# Remove DC, apply click-safe 3 ms edge fades, then normalize to -10.0 dBFS.
$mean = ($samples | Measure-Object -Average).Average
$fadeFrames = [int](0.003 * $sampleRate)
$peak = 0.0
for ($frame = 0; $frame -lt $samples.Length; $frame++) {
    $value = $samples[$frame] - $mean
    if ($frame -lt $fadeFrames) {
        $value *= [Math]::Sin(0.5 * [Math]::PI * $frame / $fadeFrames)
    }
    $fromEnd = $samples.Length - 1 - $frame
    if ($fromEnd -lt $fadeFrames) {
        $value *= [Math]::Sin(0.5 * [Math]::PI * $fromEnd / $fadeFrames)
    }
    $samples[$frame] = $value
    $peak = [Math]::Max($peak, [Math]::Abs($value))
}

$scale = [Math]::Pow(10.0, -10.0 / 20.0) / $peak
for ($frame = 0; $frame -lt $samples.Length; $frame++) {
    $samples[$frame] *= $scale
}

$OutputPath = [IO.Path]::GetFullPath($OutputPath)
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($OutputPath))
$stream = [IO.File]::Open($OutputPath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
$writer = [IO.BinaryWriter]::new($stream, [Text.Encoding]::ASCII, $false)
try {
    $dataLength = $samples.Length * 2
    $writer.Write([Text.Encoding]::ASCII.GetBytes('RIFF'))
    $writer.Write([int](36 + $dataLength))
    $writer.Write([Text.Encoding]::ASCII.GetBytes('WAVE'))
    $writer.Write([Text.Encoding]::ASCII.GetBytes('fmt '))
    $writer.Write([int]16)
    $writer.Write([int16]1)
    $writer.Write([int16]1)
    $writer.Write([int]$sampleRate)
    $writer.Write([int]($sampleRate * 2))
    $writer.Write([int16]2)
    $writer.Write([int16]16)
    $writer.Write([Text.Encoding]::ASCII.GetBytes('data'))
    $writer.Write([int]$dataLength)
    foreach ($sample in $samples) {
        $integer = [int][Math]::Round($sample * 32767.0, [MidpointRounding]::AwayFromZero)
        $integer = [Math]::Max(-32768, [Math]::Min(32767, $integer))
        $writer.Write([int16]$integer)
    }
}
finally {
    $writer.Dispose()
}

Get-Item -LiteralPath $OutputPath | Select-Object Name, Length
