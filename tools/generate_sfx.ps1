[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\src\Companion.App\Assets\Audio\Sfx')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:SampleRate = 48000
$script:BitsPerSample = 16

function New-SampleBuffer {
    param([Parameter(Mandatory)][double]$DurationSeconds)

    $sampleCount = [int][Math]::Round(
        $DurationSeconds * $script:SampleRate,
        [MidpointRounding]::AwayFromZero)
    return ,([double[]]::new($sampleCount))
}

function Add-Tone {
    param(
        [Parameter(Mandatory)][double[]]$Samples,
        [Parameter(Mandatory)][double]$Start,
        [Parameter(Mandatory)][double]$Duration,
        [Parameter(Mandatory)][double]$Frequency,
        [double]$FrequencyEnd = [double]::NaN,
        [double]$Amplitude = 0.2,
        [double]$Attack = 0.008,
        [double]$Release = 0.08,
        [double]$Decay = 1.8,
        [double]$Warmth = 0.16
    )

    if ([double]::IsNaN($FrequencyEnd)) {
        $FrequencyEnd = $Frequency
    }

    $startSample = [Math]::Max(0, [int][Math]::Floor($Start * $script:SampleRate))
    $endSample = [Math]::Min(
        $Samples.Length,
        [int][Math]::Ceiling(($Start + $Duration) * $script:SampleRate))
    $sweepPerSecond = ($FrequencyEnd - $Frequency) / $Duration

    for ($i = $startSample; $i -lt $endSample; $i++) {
        $time = ($i / $script:SampleRate) - $Start
        $remaining = $Duration - $time
        $position = $time / $Duration

        $attackEnvelope = if ($Attack -le 0 -or $time -ge $Attack) {
            1.0
        }
        else {
            [Math]::Pow([Math]::Sin(0.5 * [Math]::PI * $time / $Attack), 2)
        }

        $releaseEnvelope = if ($Release -le 0 -or $remaining -ge $Release) {
            1.0
        }
        else {
            [Math]::Pow([Math]::Sin(0.5 * [Math]::PI * $remaining / $Release), 2)
        }

        $envelope = $attackEnvelope * $releaseEnvelope * [Math]::Exp(-$Decay * $position)
        $phase = 2.0 * [Math]::PI * (
            ($Frequency * $time) + (0.5 * $sweepPerSecond * $time * $time))

        # A quiet second and third harmonic make the tones read as warm electromechanical
        # resonances rather than bare test oscillators.
        $voice = [Math]::Sin($phase)
        $voice += $Warmth * [Math]::Sin((2.0 * $phase) + 0.31)
        $voice += (0.28 * $Warmth) * [Math]::Sin((3.0 * $phase) + 0.79)
        $Samples[$i] += $Amplitude * $envelope * $voice
    }
}

function Add-FmChime {
    param(
        [Parameter(Mandatory)][double[]]$Samples,
        [Parameter(Mandatory)][double]$Start,
        [Parameter(Mandatory)][double]$Duration,
        [Parameter(Mandatory)][double]$Frequency,
        [double]$FrequencyEnd = [double]::NaN,
        [double]$Amplitude = 0.16,
        [double]$Attack = 0.003,
        [double]$Release = 0.10,
        [double]$Decay = 3.0,
        [double]$ModRatio = 2.01,
        [double]$ModIndex = 1.25
    )

    if ([double]::IsNaN($FrequencyEnd)) {
        $FrequencyEnd = $Frequency
    }

    $startSample = [Math]::Max(0, [int][Math]::Floor($Start * $script:SampleRate))
    $endSample = [Math]::Min(
        $Samples.Length,
        [int][Math]::Ceiling(($Start + $Duration) * $script:SampleRate))
    $sweepPerSecond = ($FrequencyEnd - $Frequency) / $Duration

    for ($i = $startSample; $i -lt $endSample; $i++) {
        $time = ($i / $script:SampleRate) - $Start
        $remaining = $Duration - $time
        $position = $time / $Duration

        $attackEnvelope = if ($Attack -le 0 -or $time -ge $Attack) {
            1.0
        }
        else {
            [Math]::Pow([Math]::Sin(0.5 * [Math]::PI * $time / $Attack), 2)
        }
        $releaseEnvelope = if ($Release -le 0 -or $remaining -ge $Release) {
            1.0
        }
        else {
            [Math]::Pow([Math]::Sin(0.5 * [Math]::PI * $remaining / $Release), 2)
        }

        $envelope = $attackEnvelope * $releaseEnvelope * [Math]::Exp(-$Decay * $position)
        $carrierPhase = 2.0 * [Math]::PI * (
            ($Frequency * $time) + (0.5 * $sweepPerSecond * $time * $time))
        $modulatorPhase = 2.0 * [Math]::PI * $Frequency * $ModRatio * $time
        $modulation = $ModIndex * [Math]::Exp(-4.5 * $position) * [Math]::Sin($modulatorPhase)

        # A decaying FM bell plus a quiet octave gives an original late-90s desktop chime
        # character without sampling or recreating any operating-system sound.
        $voice = [Math]::Sin($carrierPhase + $modulation)
        $voice += 0.12 * [Math]::Sin((2.0 * $carrierPhase) + 0.37)
        $Samples[$i] += $Amplitude * $envelope * $voice
    }
}

function Add-Relay {
    param(
        [Parameter(Mandatory)][double[]]$Samples,
        [Parameter(Mandatory)][double]$Start,
        [double]$Duration = 0.055,
        [double]$Amplitude = 0.18,
        [double]$Resonance = 760.0,
        [uint64]$Seed = 1
    )

    $startSample = [Math]::Max(0, [int][Math]::Floor($Start * $script:SampleRate))
    $endSample = [Math]::Min(
        $Samples.Length,
        [int][Math]::Ceiling(($Start + $Duration) * $script:SampleRate))
    $state = if ($Seed -eq 0) { [uint64]1 } else { $Seed }
    $smoothedNoise = 0.0

    for ($i = $startSample; $i -lt $endSample; $i++) {
        $time = ($i / $script:SampleRate) - $Start
        $state = [uint64](
            (($state * [uint64]1664525) + [uint64]1013904223) % [uint64]4294967296)
        $white = ($state / 2147483648.0) - 1.0
        $smoothedNoise += 0.28 * ($white - $smoothedNoise)

        $envelope = [Math]::Exp(-48.0 * $time)
        $metal = [Math]::Sin(2.0 * [Math]::PI * $Resonance * $time)
        $metal += 0.20 * [Math]::Sin(2.0 * [Math]::PI * ($Resonance * 1.47) * $time)
        $Samples[$i] += $Amplitude * $envelope * ((0.58 * $smoothedNoise) + (0.42 * $metal))
    }
}

function Add-Thump {
    param(
        [Parameter(Mandatory)][double[]]$Samples,
        [Parameter(Mandatory)][double]$Start,
        [double]$Amplitude = 0.17,
        [double]$Frequency = 175.0,
        [double]$FrequencyEnd = 95.0,
        [double]$Duration = 0.14
    )

    Add-Tone -Samples $Samples -Start $Start -Duration $Duration `
        -Frequency $Frequency -FrequencyEnd $FrequencyEnd -Amplitude $Amplitude `
        -Attack 0.002 -Release 0.07 -Decay 4.2 -Warmth 0.08
}

function Add-PaperSweep {
    param(
        [Parameter(Mandatory)][double[]]$Samples,
        [Parameter(Mandatory)][double]$Start,
        [Parameter(Mandatory)][double]$Duration,
        [double]$Amplitude = 0.09,
        [uint64]$Seed = 1
    )

    $startSample = [Math]::Max(0, [int][Math]::Floor($Start * $script:SampleRate))
    $endSample = [Math]::Min(
        $Samples.Length,
        [int][Math]::Ceiling(($Start + $Duration) * $script:SampleRate))
    $state = if ($Seed -eq 0) { [uint64]1 } else { $Seed }
    $smoothA = 0.0
    $smoothB = 0.0

    for ($i = $startSample; $i -lt $endSample; $i++) {
        $time = ($i / $script:SampleRate) - $Start
        $position = $time / $Duration
        $state = [uint64](
            (($state * [uint64]22695477) + [uint64]1) % [uint64]4294967296)
        $white = ($state / 2147483648.0) - 1.0

        # Two gentle one-pole stages keep the paper texture out of the piercing band.
        $smoothing = 0.055 + (0.10 * [Math]::Sin([Math]::PI * $position))
        $smoothA += $smoothing * ($white - $smoothA)
        $smoothB += 0.20 * ($smoothA - $smoothB)
        $envelope = [Math]::Pow([Math]::Sin([Math]::PI * $position), 1.4)
        $flutter = 0.82 + (0.18 * [Math]::Sin(2.0 * [Math]::PI * 13.0 * $time))
        $Samples[$i] += $Amplitude * $envelope * $flutter * $smoothB
    }
}

function Normalize-Samples {
    param(
        [Parameter(Mandatory)][double[]]$Samples,
        [Parameter(Mandatory)][double]$TargetPeakDbfs
    )

    $mean = 0.0
    foreach ($sample in $Samples) {
        $mean += $sample
    }
    $mean /= $Samples.Length

    $fadeSamples = [int](0.003 * $script:SampleRate)
    $peak = 0.0
    for ($i = 0; $i -lt $Samples.Length; $i++) {
        $value = $Samples[$i] - $mean
        if ($i -lt $fadeSamples) {
            $value *= [Math]::Sin(0.5 * [Math]::PI * $i / $fadeSamples)
        }
        $distanceFromEnd = $Samples.Length - 1 - $i
        if ($distanceFromEnd -lt $fadeSamples) {
            $value *= [Math]::Sin(0.5 * [Math]::PI * $distanceFromEnd / $fadeSamples)
        }
        $Samples[$i] = $value
        $peak = [Math]::Max($peak, [Math]::Abs($value))
    }

    if ($peak -le 0.0) {
        throw 'Cannot normalize a silent cue.'
    }

    $targetPeak = [Math]::Pow(10.0, $TargetPeakDbfs / 20.0)
    $scale = $targetPeak / $peak
    for ($i = 0; $i -lt $Samples.Length; $i++) {
        $Samples[$i] *= $scale
    }
}

function Write-MonoPcmWav {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][double[]]$Samples
    )

    $dataLength = $Samples.Length * 2
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
    $writer = [IO.BinaryWriter]::new($stream, [Text.Encoding]::ASCII, $false)

    $peakInteger = 0
    $sumSquares = 0.0
    try {
        $writer.Write([Text.Encoding]::ASCII.GetBytes('RIFF'))
        $writer.Write([int](36 + $dataLength))
        $writer.Write([Text.Encoding]::ASCII.GetBytes('WAVE'))
        $writer.Write([Text.Encoding]::ASCII.GetBytes('fmt '))
        $writer.Write([int]16)
        $writer.Write([int16]1)
        $writer.Write([int16]1)
        $writer.Write([int]$script:SampleRate)
        $writer.Write([int]($script:SampleRate * 2))
        $writer.Write([int16]2)
        $writer.Write([int16]$script:BitsPerSample)
        $writer.Write([Text.Encoding]::ASCII.GetBytes('data'))
        $writer.Write([int]$dataLength)

        foreach ($sample in $Samples) {
            $integer = [int][Math]::Round(
                $sample * 32767.0,
                [MidpointRounding]::AwayFromZero)
            $integer = [Math]::Max(-32768, [Math]::Min(32767, $integer))
            $writer.Write([int16]$integer)
            $peakInteger = [Math]::Max($peakInteger, [Math]::Abs($integer))
            $normalized = $integer / 32768.0
            $sumSquares += $normalized * $normalized
        }
    }
    finally {
        $writer.Dispose()
    }

    $peakDbfs = 20.0 * [Math]::Log10($peakInteger / 32768.0)
    $rms = [Math]::Sqrt($sumSquares / $Samples.Length)
    $rmsDbfs = if ($rms -gt 0.0) { 20.0 * [Math]::Log10($rms) } else { [double]::NegativeInfinity }
    $file = Get-Item -LiteralPath $Path

    return [pscustomobject]@{
        File = $file.Name
        DurationSeconds = [Math]::Round($Samples.Length / $script:SampleRate, 3)
        Bytes = $file.Length
        PeakDbfs = [Math]::Round($peakDbfs, 2)
        RmsDbfs = [Math]::Round($rmsDbfs, 2)
    }
}

$cueSpecs = @(
    [pscustomobject]@{ Name = 'navigate';          Duration = 0.090; PeakDbfs = -13.0 }
    [pscustomobject]@{ Name = 'commit';            Duration = 0.320; PeakDbfs = -10.5 }
    [pscustomobject]@{ Name = 'back';              Duration = 0.280; PeakDbfs = -11.0 }
    [pscustomobject]@{ Name = 'warning';           Duration = 0.520; PeakDbfs = -10.0 }
    [pscustomobject]@{ Name = 'destructive';       Duration = 0.720; PeakDbfs =  -9.5 }
    [pscustomobject]@{ Name = 'skill-unlock';      Duration = 0.900; PeakDbfs =  -9.5 }
    [pscustomobject]@{ Name = 'bucket-pickup';     Duration = 0.140; PeakDbfs = -12.5 }
    [pscustomobject]@{ Name = 'bucket-place';      Duration = 0.220; PeakDbfs = -11.5 }
)

$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$reports = [Collections.Generic.List[object]]::new()

foreach ($cue in $cueSpecs) {
    $samples = New-SampleBuffer -DurationSeconds $cue.Duration

    switch ($cue.Name) {
        'navigate' {
            # A quiet two-stage mouse mechanism: close, soft body resonance, then a smaller
            # release tick. Deterministic relay noise gives tactile texture without a recording.
            Add-Relay -Samples $samples -Start 0.003 -Duration 0.040 -Amplitude 0.20 `
                -Resonance 820 -Seed 131
            Add-Tone -Samples $samples -Start 0.005 -Duration 0.072 -Frequency 330 `
                -FrequencyEnd 225 -Amplitude 0.048 -Attack 0.001 -Release 0.042 -Decay 6.2 -Warmth 0.05
            Add-Relay -Samples $samples -Start 0.029 -Duration 0.045 -Amplitude 0.105 `
                -Resonance 640 -Seed 137
        }
        'commit' {
            Add-FmChime -Samples $samples -Start 0.004 -Duration 0.235 -Frequency 1046.50 `
                -Amplitude 0.14 -Release 0.105 -Decay 3.1 -ModRatio 2.00 -ModIndex 1.05
            Add-FmChime -Samples $samples -Start 0.075 -Duration 0.225 -Frequency 1567.98 `
                -Amplitude 0.12 -Release 0.120 -Decay 2.8 -ModRatio 1.50 -ModIndex 0.75
            Add-Tone -Samples $samples -Start 0.010 -Duration 0.245 -Frequency 523.25 `
                -Amplitude 0.035 -Attack 0.004 -Release 0.130 -Decay 2.5 -Warmth 0.03
        }
        'back' {
            Add-FmChime -Samples $samples -Start 0.004 -Duration 0.190 -Frequency 783.99 `
                -Amplitude 0.13 -Release 0.085 -Decay 3.3 -ModRatio 2.01 -ModIndex 0.95
            Add-FmChime -Samples $samples -Start 0.070 -Duration 0.195 -Frequency 523.25 `
                -Amplitude 0.14 -Release 0.100 -Decay 3.0 -ModRatio 2.50 -ModIndex 0.75
        }
        'success' {
            Add-Relay -Samples $samples -Start 0.006 -Duration 0.048 -Amplitude 0.11 -Resonance 705 -Seed 401
            Add-Tone -Samples $samples -Start 0.030 -Duration 0.230 -Frequency 293.66 -Amplitude 0.14 -Decay 2.3
            Add-Tone -Samples $samples -Start 0.120 -Duration 0.250 -Frequency 440.00 -Amplitude 0.13 -Decay 2.1
            Add-Tone -Samples $samples -Start 0.225 -Duration 0.270 -Frequency 587.33 -Amplitude 0.12 `
                -Release 0.130 -Decay 1.8
        }
        'warning' {
            Add-FmChime -Samples $samples -Start 0.008 -Duration 0.235 -Frequency 880.00 `
                -Amplitude 0.15 -Release 0.105 -Decay 3.0 -ModRatio 2.50 -ModIndex 1.20
            Add-FmChime -Samples $samples -Start 0.235 -Duration 0.255 -Frequency 659.25 `
                -Amplitude 0.16 -Release 0.125 -Decay 2.7 -ModRatio 2.00 -ModIndex 1.10
            Add-Tone -Samples $samples -Start 0.240 -Duration 0.230 -Frequency 329.63 `
                -Amplitude 0.04 -Attack 0.003 -Release 0.120 -Decay 2.4 -Warmth 0.04
        }
        'error' {
            Add-Relay -Samples $samples -Start 0.004 -Duration 0.060 -Amplitude 0.16 -Resonance 560 -Seed 601
            Add-Thump -Samples $samples -Start 0.006 -Amplitude 0.12 -Frequency 185 -FrequencyEnd 110 -Duration 0.18
            Add-Tone -Samples $samples -Start 0.025 -Duration 0.205 -Frequency 329.63 -Amplitude 0.15 -Decay 2.4
            Add-Tone -Samples $samples -Start 0.145 -Duration 0.220 -Frequency 261.63 -Amplitude 0.15 -Decay 2.3
            Add-Tone -Samples $samples -Start 0.275 -Duration 0.235 -Frequency 220.00 -Amplitude 0.16 `
                -Release 0.120 -Decay 2.2
        }
        'destructive' {
            Add-FmChime -Samples $samples -Start 0.006 -Duration 0.245 -Frequency 659.25 `
                -Amplitude 0.14 -Release 0.115 -Decay 2.9 -ModRatio 2.50 -ModIndex 1.35
            Add-FmChime -Samples $samples -Start 0.190 -Duration 0.255 -Frequency 523.25 `
                -Amplitude 0.15 -Release 0.125 -Decay 2.7 -ModRatio 2.00 -ModIndex 1.25
            Add-FmChime -Samples $samples -Start 0.380 -Duration 0.305 -Frequency 392.00 `
                -FrequencyEnd 369.99 -Amplitude 0.16 -Release 0.155 -Decay 2.4 -ModRatio 2.50 -ModIndex 1.15
            Add-Tone -Samples $samples -Start 0.390 -Duration 0.275 -Frequency 196.00 `
                -Amplitude 0.045 -Attack 0.003 -Release 0.150 -Decay 2.1 -Warmth 0.04
        }
        'dispatch-arrival' {
            Add-PaperSweep -Samples $samples -Start 0.004 -Duration 0.310 -Amplitude 0.15 -Seed 809
            Add-Relay -Samples $samples -Start 0.035 -Duration 0.045 -Amplitude 0.12 -Resonance 710 -Seed 811
            Add-Relay -Samples $samples -Start 0.105 -Duration 0.045 -Amplitude 0.12 -Resonance 760 -Seed 821
            Add-Relay -Samples $samples -Start 0.175 -Duration 0.045 -Amplitude 0.12 -Resonance 815 -Seed 823
            Add-Tone -Samples $samples -Start 0.275 -Duration 0.260 -Frequency 293.66 -Amplitude 0.12 -Decay 2.1
            Add-Tone -Samples $samples -Start 0.400 -Duration 0.320 -Frequency 440.00 -Amplitude 0.13 `
                -Release 0.150 -Decay 1.8
        }
        'grid-lock' {
            Add-Thump -Samples $samples -Start 0.003 -Amplitude 0.18 -Frequency 180 -FrequencyEnd 98 -Duration 0.18
            Add-Relay -Samples $samples -Start 0.005 -Duration 0.070 -Amplitude 0.20 -Resonance 540 -Seed 907
            Add-Relay -Samples $samples -Start 0.125 -Duration 0.060 -Amplitude 0.17 -Resonance 670 -Seed 911
            Add-Tone -Samples $samples -Start 0.165 -Duration 0.370 -Frequency 146.83 -Amplitude 0.13 `
                -Release 0.180 -Decay 1.7
            Add-Tone -Samples $samples -Start 0.180 -Duration 0.350 -Frequency 220.00 -Amplitude 0.10 `
                -Release 0.180 -Decay 1.7
        }
        'skill-unlock' {
            Add-FmChime -Samples $samples -Start 0.010 -Duration 0.280 -Frequency 523.25 `
                -Amplitude 0.115 -Release 0.125 -Decay 2.8 -ModRatio 2.00 -ModIndex 1.05
            Add-FmChime -Samples $samples -Start 0.145 -Duration 0.300 -Frequency 659.25 `
                -Amplitude 0.115 -Release 0.140 -Decay 2.6 -ModRatio 2.50 -ModIndex 0.95
            Add-FmChime -Samples $samples -Start 0.295 -Duration 0.335 -Frequency 783.99 `
                -Amplitude 0.115 -Release 0.155 -Decay 2.4 -ModRatio 2.00 -ModIndex 0.85
            Add-FmChime -Samples $samples -Start 0.465 -Duration 0.405 -Frequency 1046.50 `
                -Amplitude 0.13 -Release 0.195 -Decay 2.0 -ModRatio 1.50 -ModIndex 0.70
            Add-Tone -Samples $samples -Start 0.480 -Duration 0.365 -Frequency 523.25 `
                -Amplitude 0.045 -Attack 0.004 -Release 0.190 -Decay 1.8 -Warmth 0.03
        }
        'bucket-pickup' {
            Add-FmChime -Samples $samples -Start 0.003 -Duration 0.120 -Frequency 659.25 `
                -FrequencyEnd 1046.50 -Amplitude 0.16 -Release 0.055 -Decay 4.3 -ModRatio 2.01 -ModIndex 0.90
            Add-Tone -Samples $samples -Start 0.006 -Duration 0.100 -Frequency 329.63 -FrequencyEnd 523.25 `
                -Amplitude 0.035 -Attack 0.002 -Release 0.050 -Decay 4.2 -Warmth 0.02
        }
        'bucket-place' {
            Add-FmChime -Samples $samples -Start 0.004 -Duration 0.155 -Frequency 1046.50 `
                -FrequencyEnd 783.99 -Amplitude 0.12 -Release 0.070 -Decay 3.7 -ModRatio 2.00 -ModIndex 0.75
            Add-FmChime -Samples $samples -Start 0.055 -Duration 0.145 -Frequency 523.25 `
                -Amplitude 0.15 -Release 0.080 -Decay 3.5 -ModRatio 2.50 -ModIndex 0.65
            Add-Tone -Samples $samples -Start 0.060 -Duration 0.130 -Frequency 261.63 `
                -Amplitude 0.035 -Attack 0.002 -Release 0.075 -Decay 3.3 -Warmth 0.03
        }
        'level-up' {
            Add-Relay -Samples $samples -Start 0.005 -Duration 0.055 -Amplitude 0.11 -Resonance 690 -Seed 1103
            Add-Tone -Samples $samples -Start 0.025 -Duration 0.280 -Frequency 293.66 -Amplitude 0.12 -Decay 2.2
            Add-Tone -Samples $samples -Start 0.160 -Duration 0.310 -Frequency 369.99 -Amplitude 0.12 -Decay 2.0
            Add-Tone -Samples $samples -Start 0.305 -Duration 0.350 -Frequency 440.00 -Amplitude 0.12 -Decay 1.8
            Add-Tone -Samples $samples -Start 0.485 -Duration 0.495 -Frequency 587.33 -Amplitude 0.13 `
                -Release 0.230 -Decay 1.4
            Add-Tone -Samples $samples -Start 0.510 -Duration 0.460 -Frequency 293.66 -Amplitude 0.055 `
                -Release 0.220 -Decay 1.2
        }
        'promotion' {
            Add-Relay -Samples $samples -Start 0.005 -Duration 0.055 -Amplitude 0.12 -Resonance 620 -Seed 1201
            Add-Tone -Samples $samples -Start 0.025 -Duration 0.270 -Frequency 220.00 -Amplitude 0.11 -Decay 2.3
            Add-Tone -Samples $samples -Start 0.145 -Duration 0.290 -Frequency 293.66 -Amplitude 0.12 -Decay 2.1
            Add-Tone -Samples $samples -Start 0.280 -Duration 0.320 -Frequency 369.99 -Amplitude 0.12 -Decay 1.9
            Add-Tone -Samples $samples -Start 0.430 -Duration 0.350 -Frequency 440.00 -Amplitude 0.12 -Decay 1.7
            Add-Tone -Samples $samples -Start 0.610 -Duration 0.610 -Frequency 587.33 -Amplitude 0.13 `
                -Release 0.280 -Decay 1.2
            Add-Tone -Samples $samples -Start 0.635 -Duration 0.570 -Frequency 293.66 -Amplitude 0.055 `
                -Release 0.270 -Decay 1.1
            Add-Tone -Samples $samples -Start 0.650 -Duration 0.540 -Frequency 440.00 -Amplitude 0.045 `
                -Release 0.260 -Decay 1.1
        }
        'demotion' {
            Add-Relay -Samples $samples -Start 0.006 -Duration 0.055 -Amplitude 0.10 -Resonance 600 -Seed 1301
            Add-Tone -Samples $samples -Start 0.025 -Duration 0.300 -Frequency 587.33 -Amplitude 0.11 -Decay 2.2
            Add-Tone -Samples $samples -Start 0.175 -Duration 0.310 -Frequency 440.00 -Amplitude 0.12 -Decay 2.1
            Add-Tone -Samples $samples -Start 0.330 -Duration 0.340 -Frequency 349.23 -Amplitude 0.12 -Decay 1.9
            Add-Tone -Samples $samples -Start 0.500 -Duration 0.500 -Frequency 293.66 -FrequencyEnd 277.18 `
                -Amplitude 0.13 -Release 0.250 -Decay 1.5
            Add-Relay -Samples $samples -Start 0.790 -Duration 0.075 -Amplitude 0.11 -Resonance 430 -Seed 1303
        }
        'title-won' {
            Add-Relay -Samples $samples -Start 0.006 -Duration 0.060 -Amplitude 0.12 -Resonance 650 -Seed 1409
            Add-Tone -Samples $samples -Start 0.030 -Duration 0.300 -Frequency 293.66 -Amplitude 0.11 -Decay 2.2
            Add-Tone -Samples $samples -Start 0.165 -Duration 0.330 -Frequency 369.99 -Amplitude 0.11 -Decay 2.0
            Add-Tone -Samples $samples -Start 0.310 -Duration 0.370 -Frequency 440.00 -Amplitude 0.11 -Decay 1.8
            Add-Tone -Samples $samples -Start 0.470 -Duration 0.400 -Frequency 493.88 -Amplitude 0.11 -Decay 1.6
            Add-Tone -Samples $samples -Start 0.660 -Duration 1.080 -Frequency 587.33 -Amplitude 0.12 `
                -Release 0.450 -Decay 0.9
            Add-Tone -Samples $samples -Start 0.690 -Duration 1.020 -Frequency 293.66 -Amplitude 0.055 `
                -Release 0.430 -Decay 0.8
            Add-Tone -Samples $samples -Start 0.700 -Duration 1.000 -Frequency 369.99 -Amplitude 0.045 `
                -Release 0.420 -Decay 0.8
            Add-Tone -Samples $samples -Start 0.710 -Duration 0.980 -Frequency 440.00 -Amplitude 0.045 `
                -Release 0.410 -Decay 0.8
            Add-Relay -Samples $samples -Start 1.180 -Duration 0.050 -Amplitude 0.055 -Resonance 880 -Seed 1423
        }
        'season-transition' {
            Add-PaperSweep -Samples $samples -Start 0.005 -Duration 0.410 -Amplitude 0.16 -Seed 1511
            Add-Relay -Samples $samples -Start 0.030 -Duration 0.050 -Amplitude 0.10 -Resonance 650 -Seed 1513
            Add-Relay -Samples $samples -Start 0.285 -Duration 0.055 -Amplitude 0.11 -Resonance 720 -Seed 1523
            Add-Tone -Samples $samples -Start 0.305 -Duration 0.330 -Frequency 220.00 -Amplitude 0.11 -Decay 2.0
            Add-Tone -Samples $samples -Start 0.455 -Duration 0.390 -Frequency 293.66 -Amplitude 0.12 -Decay 1.7
            Add-Tone -Samples $samples -Start 0.650 -Duration 0.500 -Frequency 440.00 -Amplitude 0.11 `
                -Release 0.240 -Decay 1.3
        }
        default {
            throw "No synthesis recipe exists for cue '$($cue.Name)'."
        }
    }

    Normalize-Samples -Samples $samples -TargetPeakDbfs $cue.PeakDbfs
    $path = Join-Path $OutputDirectory ($cue.Name + '.wav')
    $report = Write-MonoPcmWav -Path $path -Samples $samples
    $reports.Add($report)
}

Write-Host "Generated $($reports.Count) deterministic Pitwall 98 cues in $OutputDirectory"
$reports | Format-Table File, DurationSeconds, Bytes, PeakDbfs, RmsDbfs -AutoSize
