[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\..\Assets\Audio\Sfx')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Original deterministic synthesis for the era-medium re-voicings of the four immersive cues
# (docs/dev/era-theming-assets-brief.md, Workstream B: timbre only, never triggering). Telegram is
# a relay/telegraph-key tick plus a small bell, Fax is a thermal-print chirp plus a handshake
# warble, Email is a soft FM chime. No recordings, samples, random input, or emulated game audio
# are used; every cue keeps its base master's duration, peak, and melodic contour so the era skin
# changes only how the cue is voiced, never what it means.
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

        # A quiet second and third harmonic keep the support tones warm instead of bare.
        $voice = [Math]::Sin($phase)
        $voice += $Warmth * [Math]::Sin((2.0 * $phase) + 0.31)
        $voice += (0.28 * $Warmth) * [Math]::Sin((3.0 * $phase) + 0.79)
        $Samples[$i] += $Amplitude * $envelope * $voice
    }
}

function Add-FmVoice {
    param(
        [Parameter(Mandatory)][double[]]$Samples,
        [Parameter(Mandatory)][double]$Start,
        [Parameter(Mandatory)][double]$Duration,
        [Parameter(Mandatory)][double]$Frequency,
        [double]$Amplitude = 0.15,
        [double]$ModRatio = 2.00,
        [double]$ModIndex = 1.20,
        [double]$Decay = 3.0,
        [double]$Attack = 0.0025,
        [double]$Release = 0.065
    )

    $startSample = [Math]::Max(0, [int][Math]::Floor($Start * $script:SampleRate))
    $endSample = [Math]::Min(
        $Samples.Length,
        [int][Math]::Ceiling(($Start + $Duration) * $script:SampleRate))

    for ($i = $startSample; $i -lt $endSample; $i++) {
        $time = ($i / $script:SampleRate) - $Start
        $remaining = $Duration - $time
        $position = $time / $Duration

        $attackEnvelope = if ($Attack -le 0 -or $time -ge $Attack) {
            1.0
        }
        else {
            [Math]::Sin(0.5 * [Math]::PI * $time / $Attack)
        }

        $releaseEnvelope = if ($Release -le 0 -or $remaining -ge $Release) {
            1.0
        }
        else {
            [Math]::Sin(0.5 * [Math]::PI * $remaining / $Release)
        }

        $envelope = $attackEnvelope * $releaseEnvelope * [Math]::Exp(-$Decay * $position)
        $carrier = 2.0 * [Math]::PI * $Frequency * $time
        $modulator = 2.0 * [Math]::PI * $Frequency * $ModRatio * $time
        $modulation = $ModIndex * [Math]::Exp(-5.2 * $position) * [Math]::Sin($modulator)

        # A decaying FM partial plus a quiet octave: small telegram bell when the index is high,
        # soft email chime when it is low. Original synthesis in both cases.
        $voice = [Math]::Sin($carrier + $modulation)
        $voice += 0.10 * [Math]::Sin((2.0 * $carrier) + 0.41)
        $Samples[$i] += $Amplitude * $envelope * $voice
    }
}

function Add-Relay {
    param(
        [Parameter(Mandatory)][double[]]$Samples,
        [Parameter(Mandatory)][double]$Start,
        [double]$Duration = 0.035,
        [double]$Amplitude = 0.15,
        [double]$Resonance = 1480.0,
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

        # Telegraph-key strike: a fast decaying metal resonance under seeded contact noise.
        $envelope = [Math]::Exp(-52.0 * $time)
        $metal = [Math]::Sin(2.0 * [Math]::PI * $Resonance * $time)
        $metal += 0.20 * [Math]::Sin(2.0 * [Math]::PI * ($Resonance * 1.47) * $time)
        $Samples[$i] += $Amplitude * $envelope * ((0.58 * $smoothedNoise) + (0.42 * $metal))
    }
}

function Add-ThermalChirp {
    param(
        [Parameter(Mandatory)][double[]]$Samples,
        [Parameter(Mandatory)][double]$Start,
        [Parameter(Mandatory)][double]$Duration,
        [double]$Amplitude = 0.18,
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
        $position = $time / $Duration
        $state = [uint64](
            (($state * [uint64]22695477) + [uint64]1) % [uint64]4294967296)
        $white = ($state / 2147483648.0) - 1.0

        # Thermal print-head zip: the smoothing pole opens across the chirp so the band rises,
        # and a fast line-step flutter keeps it mechanical rather than airy noise.
        $smoothing = 0.34 - (0.26 * $position)
        $smoothedNoise += $smoothing * ($white - $smoothedNoise)
        $trill = 0.72 + (0.28 * [Math]::Sin(2.0 * [Math]::PI * 95.0 * $time))
        $envelope = [Math]::Pow([Math]::Sin([Math]::PI * [Math]::Min(1.0, $position)), 0.65)
        $Samples[$i] += $Amplitude * $envelope * $trill * $smoothedNoise
    }
}

function Add-Warble {
    param(
        [Parameter(Mandatory)][double[]]$Samples,
        [Parameter(Mandatory)][double]$Start,
        [Parameter(Mandatory)][double]$Duration,
        [Parameter(Mandatory)][double]$Frequency,
        [double]$FrequencyEnd = [double]::NaN,
        [double]$Amplitude = 0.12,
        [double]$VibratoHz = 12.0,
        [double]$VibratoDepth = 0.012,
        [double]$Attack = 0.006,
        [double]$Release = 0.09,
        [double]$Decay = 1.6
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

        # Handshake warble: a slow sinusoidal frequency wobble around the carrier, evoking a fax
        # negotiation tone without reproducing any recording or ITU signal specification exactly.
        $phase = 2.0 * [Math]::PI * (
            ($Frequency * $time) + (0.5 * $sweepPerSecond * $time * $time))
        $phase += ($Frequency * $VibratoDepth / $VibratoHz) *
            [Math]::Sin(2.0 * [Math]::PI * $VibratoHz * $time)
        $voice = [Math]::Sin($phase)
        $voice += 0.14 * [Math]::Sin((2.0 * $phase) + 0.37)
        $Samples[$i] += $Amplitude * $envelope * $voice
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
    [pscustomobject]@{ Name = 'navigate-telegram';     Duration = 0.090; PeakDbfs = -13.0 }
    [pscustomobject]@{ Name = 'commit-telegram';       Duration = 0.320; PeakDbfs = -10.5 }
    [pscustomobject]@{ Name = 'seat-confirm-telegram'; Duration = 0.480; PeakDbfs = -10.0 }
    [pscustomobject]@{ Name = 'back-telegram';         Duration = 0.280; PeakDbfs = -11.0 }
    [pscustomobject]@{ Name = 'navigate-fax';          Duration = 0.090; PeakDbfs = -13.0 }
    [pscustomobject]@{ Name = 'commit-fax';            Duration = 0.320; PeakDbfs = -10.5 }
    [pscustomobject]@{ Name = 'seat-confirm-fax';      Duration = 0.480; PeakDbfs = -10.0 }
    [pscustomobject]@{ Name = 'back-fax';              Duration = 0.280; PeakDbfs = -11.0 }
    [pscustomobject]@{ Name = 'navigate-email';        Duration = 0.090; PeakDbfs = -13.0 }
    [pscustomobject]@{ Name = 'commit-email';          Duration = 0.320; PeakDbfs = -10.5 }
    [pscustomobject]@{ Name = 'seat-confirm-email';    Duration = 0.480; PeakDbfs = -10.0 }
    [pscustomobject]@{ Name = 'back-email';            Duration = 0.280; PeakDbfs = -11.0 }
)

$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$reports = [Collections.Generic.List[object]]::new()

foreach ($cue in $cueSpecs) {
    $samples = New-SampleBuffer -DurationSeconds $cue.Duration

    switch ($cue.Name) {
        'navigate-telegram' {
            # The base two-stage mouse mechanism re-voiced as a telegraph-key close and release.
            Add-Relay -Samples $samples -Start 0.003 -Duration 0.032 -Amplitude 0.20 `
                -Resonance 1480 -Seed 21011
            Add-FmVoice -Samples $samples -Start 0.005 -Duration 0.070 -Frequency 1760.00 `
                -Amplitude 0.045 -ModRatio 3.02 -ModIndex 1.30 -Decay 5.6
            Add-Relay -Samples $samples -Start 0.033 -Duration 0.028 -Amplitude 0.115 `
                -Resonance 1740 -Seed 21013
        }
        'commit-telegram' {
            # Keyed ticks under a small C6-G6 bell, the base commit contour.
            Add-Relay -Samples $samples -Start 0.004 -Duration 0.038 -Amplitude 0.15 `
                -Resonance 1380 -Seed 22021
            Add-FmVoice -Samples $samples -Start 0.007 -Duration 0.230 -Frequency 1046.50 `
                -Amplitude 0.135 -ModRatio 3.00 -ModIndex 1.55 -Decay 3.2
            Add-Relay -Samples $samples -Start 0.074 -Duration 0.034 -Amplitude 0.10 `
                -Resonance 1560 -Seed 22027
            Add-FmVoice -Samples $samples -Start 0.080 -Duration 0.222 -Frequency 1567.98 `
                -Amplitude 0.115 -ModRatio 2.00 -ModIndex 1.05 -Decay 2.9
            Add-Tone -Samples $samples -Start 0.012 -Duration 0.240 -Frequency 523.25 `
                -Amplitude 0.030 -Attack 0.004 -Release 0.120 -Decay 2.6 -Warmth 0.04
        }
        'seat-confirm-telegram' {
            # The base C-G-C lock-in arpeggio struck on a telegraph key with a small bell.
            Add-Relay -Samples $samples -Start 0.004 -Duration 0.036 -Amplitude 0.14 `
                -Resonance 1420 -Seed 23031
            Add-FmVoice -Samples $samples -Start 0.006 -Duration 0.200 -Frequency 523.25 `
                -Amplitude 0.15 -ModRatio 3.00 -ModIndex 1.70 -Decay 3.4
            Add-Relay -Samples $samples -Start 0.082 -Duration 0.034 -Amplitude 0.12 `
                -Resonance 1560 -Seed 23033
            Add-FmVoice -Samples $samples -Start 0.084 -Duration 0.220 -Frequency 783.99 `
                -Amplitude 0.14 -ModRatio 3.00 -ModIndex 1.40 -Decay 3.1
            Add-Relay -Samples $samples -Start 0.168 -Duration 0.036 -Amplitude 0.13 `
                -Resonance 1740 -Seed 23037
            Add-FmVoice -Samples $samples -Start 0.170 -Duration 0.285 -Frequency 1046.50 `
                -Amplitude 0.16 -ModRatio 2.00 -ModIndex 1.10 -Decay 2.6
            Add-Tone -Samples $samples -Start 0.180 -Duration 0.250 -Frequency 261.63 `
                -Amplitude 0.045 -Attack 0.004 -Release 0.140 -Decay 2.5 -Warmth 0.05
        }
        'back-telegram' {
            # Keyed ticks under a small G5-C5 falling bell, the base back contour.
            Add-Relay -Samples $samples -Start 0.004 -Duration 0.036 -Amplitude 0.14 `
                -Resonance 1560 -Seed 24041
            Add-FmVoice -Samples $samples -Start 0.006 -Duration 0.185 -Frequency 783.99 `
                -Amplitude 0.12 -ModRatio 3.00 -ModIndex 1.30 -Decay 3.3
            Add-Relay -Samples $samples -Start 0.070 -Duration 0.034 -Amplitude 0.11 `
                -Resonance 1380 -Seed 24043
            Add-FmVoice -Samples $samples -Start 0.074 -Duration 0.190 -Frequency 523.25 `
                -Amplitude 0.13 -ModRatio 2.00 -ModIndex 0.95 -Decay 3.0
        }
        'navigate-fax' {
            # One short thermal print-head chirp with a faint warble tail.
            Add-ThermalChirp -Samples $samples -Start 0.004 -Duration 0.050 -Amplitude 0.20 `
                -Seed 31051
            Add-Warble -Samples $samples -Start 0.012 -Duration 0.068 -Frequency 1250.00 `
                -FrequencyEnd 1150.00 -Amplitude 0.038 -VibratoHz 12.5 -VibratoDepth 0.010
        }
        'commit-fax' {
            # Print-head chirp, then a handshake warble rising to resolve like the base commit.
            Add-ThermalChirp -Samples $samples -Start 0.004 -Duration 0.065 -Amplitude 0.155 `
                -Seed 32061
            Add-Warble -Samples $samples -Start 0.078 -Duration 0.222 -Frequency 1100.00 `
                -FrequencyEnd 1650.00 -Amplitude 0.115 -VibratoHz 11.5 -VibratoDepth 0.014
            Add-Tone -Samples $samples -Start 0.085 -Duration 0.210 -Frequency 550.00 `
                -Amplitude 0.028 -Attack 0.005 -Release 0.115 -Decay 2.6 -Warmth 0.03
        }
        'seat-confirm-fax' {
            # Chirp, then a three-stage handshake warble arpeggio mirroring the base lock-in.
            Add-ThermalChirp -Samples $samples -Start 0.004 -Duration 0.070 -Amplitude 0.15 `
                -Seed 33071
            Add-Warble -Samples $samples -Start 0.062 -Duration 0.150 -Frequency 1100.00 `
                -Amplitude 0.10 -VibratoHz 12.0 -VibratoDepth 0.012
            Add-Warble -Samples $samples -Start 0.172 -Duration 0.160 -Frequency 1385.00 `
                -Amplitude 0.105 -VibratoHz 12.0 -VibratoDepth 0.012
            Add-Warble -Samples $samples -Start 0.292 -Duration 0.175 -Frequency 1650.00 `
                -Amplitude 0.115 -VibratoHz 11.0 -VibratoDepth 0.010
            Add-Tone -Samples $samples -Start 0.300 -Duration 0.165 -Frequency 275.00 `
                -Amplitude 0.035 -Attack 0.004 -Release 0.100 -Decay 2.4 -Warmth 0.03
        }
        'back-fax' {
            # Print-head chirp, then a handshake warble falling away like the base back.
            Add-ThermalChirp -Samples $samples -Start 0.004 -Duration 0.060 -Amplitude 0.145 `
                -Seed 34081
            Add-Warble -Samples $samples -Start 0.064 -Duration 0.200 -Frequency 1650.00 `
                -FrequencyEnd 1100.00 -Amplitude 0.115 -VibratoHz 12.5 -VibratoDepth 0.013
        }
        'navigate-email' {
            # A soft two-stage FM blip, the quietest of the era navigation voicings.
            Add-FmVoice -Samples $samples -Start 0.004 -Duration 0.078 -Frequency 1318.51 `
                -Amplitude 0.095 -ModRatio 2.00 -ModIndex 0.55 -Decay 5.8 `
                -Attack 0.004 -Release 0.045
            Add-FmVoice -Samples $samples -Start 0.042 -Duration 0.042 -Frequency 1975.53 `
                -Amplitude 0.038 -ModRatio 2.00 -ModIndex 0.40 -Decay 6.2
        }
        'commit-email' {
            # A soft FM C6-G6 chime, rounder than the base commit's brighter bell.
            Add-FmVoice -Samples $samples -Start 0.006 -Duration 0.232 -Frequency 1046.50 `
                -Amplitude 0.125 -ModRatio 2.00 -ModIndex 0.60 -Decay 3.1 `
                -Attack 0.006 -Release 0.110
            Add-FmVoice -Samples $samples -Start 0.078 -Duration 0.224 -Frequency 1567.98 `
                -Amplitude 0.105 -ModRatio 2.00 -ModIndex 0.45 -Decay 2.8 `
                -Attack 0.006 -Release 0.120
            Add-Tone -Samples $samples -Start 0.012 -Duration 0.240 -Frequency 523.25 `
                -Amplitude 0.028 -Attack 0.006 -Release 0.125 -Decay 2.5 -Warmth 0.03
        }
        'seat-confirm-email' {
            # The base C-G-C lock-in arpeggio as soft rounded FM chimes.
            Add-FmVoice -Samples $samples -Start 0.006 -Duration 0.200 -Frequency 523.25 `
                -Amplitude 0.145 -ModRatio 2.00 -ModIndex 0.75 -Decay 3.4
            Add-FmVoice -Samples $samples -Start 0.084 -Duration 0.220 -Frequency 783.99 `
                -Amplitude 0.135 -ModRatio 2.00 -ModIndex 0.65 -Decay 3.1
            Add-FmVoice -Samples $samples -Start 0.170 -Duration 0.285 -Frequency 1046.50 `
                -Amplitude 0.15 -ModRatio 1.50 -ModIndex 0.50 -Decay 2.6
            Add-Tone -Samples $samples -Start 0.180 -Duration 0.250 -Frequency 261.63 `
                -Amplitude 0.042 -Attack 0.005 -Release 0.140 -Decay 2.5 -Warmth 0.04
        }
        'back-email' {
            # A soft FM G5-C5 descent, the base back contour.
            Add-FmVoice -Samples $samples -Start 0.004 -Duration 0.185 -Frequency 783.99 `
                -Amplitude 0.115 -ModRatio 2.00 -ModIndex 0.60 -Decay 3.3
            Add-FmVoice -Samples $samples -Start 0.072 -Duration 0.192 -Frequency 523.25 `
                -Amplitude 0.125 -ModRatio 2.00 -ModIndex 0.50 -Decay 3.0
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

Write-Host "Generated $($reports.Count) deterministic era-medium SFX voicings in $OutputDirectory"
$reports | Format-Table File, DurationSeconds, Bytes, PeakDbfs, RmsDbfs -AutoSize
