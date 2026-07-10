# Re-derives the mechanical columns of docs/dev/season-coverage.md from the repo:
#   powershell -File tools/coverage_matrix.ps1
# Emits CSV, one row per pack. Judgment columns (ratings source, wet-research state, notes)
# live in the doc itself — this script covers everything countable so a session never has to
# re-read 20 packs to know where the content stands.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo

function WetSlots($slots) {
    if ($null -eq $slots) { return $false }
    foreach ($s in $slots) { if ($s -ne 'Clear') { return $true } }
    return $false
}

# Era corpora resolve by YEAR RANGE (data/rules/news/*.json eras[].fromYear..toYear), not by
# decade-in-filename — 2020 belongs to the 2010s file's [2010-2029] era.
$newsEras = @()
foreach ($f in (Get-ChildItem data/rules/news/*.json)) {
    $j = Get-Content $f.FullName -Raw | ConvertFrom-Json
    foreach ($e in $j.eras) {
        $newsEras += [pscustomobject]@{ Key = $e.key; From = $e.fromYear; To = $e.toYear; File = $f.Name; Size = $f.Length }
    }
}

$rows = @()
foreach ($dir in (Get-ChildItem packs -Directory | Sort-Object Name)) {
    $pack = Get-Content "$($dir.FullName)\pack.json" -Raw | ConvertFrom-Json
    $season = Get-Content "$($dir.FullName)\season.json" -Raw | ConvertFrom-Json
    $drivers = (Get-Content "$($dir.FullName)\drivers.json" -Raw | ConvertFrom-Json).drivers
    $entries = (Get-Content "$($dir.FullName)\entries.json" -Raw | ConvertFrom-Json).entries

    $rounds = @($season.rounds)
    $weekendAuthored = 0; $wetRounds = 0; $aiOverrideRounds = 0
    $altRounds = 0; $placeholderNoAlt = 0
    foreach ($r in $rounds) {
        $wk = $r.weekend
        if ($null -ne $wk -and $null -ne $wk.practice.durationMinutes -and $null -ne $wk.practice.weatherSlots -and
            $null -ne $wk.qualifying.durationMinutes -and $null -ne $wk.qualifying.weatherSlots) { $weekendAuthored++ }
        $wet = $false
        if ($null -ne $wk) {
            if (WetSlots $wk.practice.weatherSlots) { $wet = $true }
            if (WetSlots $wk.qualifying.weatherSlots) { $wet = $true }
            foreach ($race in @($wk.races)) { if (WetSlots $race.weatherSlots) { $wet = $true } }
        }
        if (-not $wet -and $null -ne $r.setupGuide -and $null -ne $r.setupGuide.session) {
            if (WetSlots $r.setupGuide.session.weatherSlots) { $wet = $true }
        }
        if ($wet) { $wetRounds++ }
        if ($null -ne $r.aiOverrides -and @($r.aiOverrides.PSObject.Properties).Count -gt 0) { $aiOverrideRounds++ }
        if ($r.track.isPlaceholder -and $null -eq $r.track.alternate) { $placeholderNoAlt++ }
        if ($null -ne $r.track.alternate) { $altRounds++ }
    }

    $carBlocks = @($drivers | Where-Object { $null -ne $_.car }).Count
    $formCount = if ($null -eq $season.driverForm) { 0 } else { @($season.driverForm.PSObject.Properties).Count }
    $historyPointers = @($rounds | Where-Object { $null -ne $_.history }).Count

    $year = $season.year
    $histFile = "data\history\$year.json"
    $factRounds = 0; $circuitRounds = 0; $histRounds = 0
    if (Test-Path $histFile) {
        $hist = Get-Content $histFile -Raw | ConvertFrom-Json
        $histRounds = @($hist.rounds).Count
        $factRounds = @($hist.rounds | Where-Object { $null -ne $_.circuit -and @($_.circuit.facts).Count -gt 0 }).Count
        $circuitRounds = @($hist.rounds | Where-Object { $null -ne $_.circuit -and $null -ne $_.circuit.layoutId -and (Test-Path "data\ams2\circuits\$($_.circuit.layoutId).json") }).Count
    }

    $era = $newsEras | Where-Object { $year -ge $_.From -and $year -le $_.To } | Select-Object -First 1
    $eraArt = Test-Path "dist\data\ams2\era-art\$year.jpg"

    $rows += [pscustomobject]@{
        pack = $dir.Name; year = $year; class = $season.ams2Class
        skinSeason = $pack.skinSeason
        entries = @($entries).Count; drivers = @($drivers).Count
        carBlocks = $carBlocks; form = $formCount
        aiOvrRounds = $aiOverrideRounds; rounds = $rounds.Count
        weekend = $weekendAuthored; refuel = $season.refuellingAllowed
        wetRounds = $wetRounds
        histRounds = $histRounds; factRounds = $factRounds; circuitRounds = $circuitRounds
        histPointers = $historyPointers
        altRounds = $altRounds; placeholderNoAlt = $placeholderNoAlt
        newsEra = if ($era) { $era.Key } else { 'NONE' }
        eraArt = $eraArt
    }
}
$rows | ConvertTo-Csv -NoTypeInformation
