[CmdletBinding()]
param(
    [string]$RepoRoot = "",
    [string]$OutputDirectory = "",
    [string]$PublishDirectory = "",
    [switch]$AllowIssues
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Split-Path -Parent $scriptRoot
}
$RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $RepoRoot "docs\art-audit"
}
elseif (-not [IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $RepoRoot $OutputDirectory
}

if (-not [string]::IsNullOrWhiteSpace($PublishDirectory) -and
    -not [IO.Path]::IsPathRooted($PublishDirectory)) {
    $PublishDirectory = Join-Path $RepoRoot $PublishDirectory
}

$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null

Add-Type -AssemblyName System.Drawing

$Utf8NoBom = New-Object Text.UTF8Encoding($false)
$ImageExtensions = [Collections.Generic.HashSet[string]]::new(
    [string[]]@(".jpg", ".jpeg", ".png"),
    [StringComparer]::OrdinalIgnoreCase)

function Get-RelativePath {
    param(
        [Parameter(Mandatory)][string]$BasePath,
        [Parameter(Mandatory)][string]$Path
    )

    $baseUri = [Uri]((Resolve-Path -LiteralPath $BasePath).Path.TrimEnd("\") + "\")
    $pathUri = [Uri]([IO.Path]::GetFullPath($Path))
    return [Uri]::UnescapeDataString($baseUri.MakeRelativeUri($pathUri).ToString()).Replace("\", "/")
}

function Convert-ToRepoPath {
    param([Parameter(Mandatory)][string]$Path)

    return (Get-RelativePath -BasePath $RepoRoot -Path $Path)
}

function Write-Utf8Lines {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Lines
    )

    [IO.File]::WriteAllLines($Path, $Lines, $Utf8NoBom)
}

function Write-Utf8Text {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Text
    )

    [IO.File]::WriteAllText($Path, $Text, $Utf8NoBom)
}

function Read-Json {
    param([Parameter(Mandatory)][string]$Path)

    return Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json
}

function Get-ObjectProperty {
    param(
        [AllowNull()][object]$Object,
        [Parameter(Mandatory)][string]$Name,
        [AllowNull()][object]$Default = $null
    )

    if ($null -eq $Object) {
        return $Default
    }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $Default
    }
    return $property.Value
}

function Get-Slug {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Value)

    $slug = $Value.ToLowerInvariant() -replace "[^a-z0-9]+", "-"
    return $slug.Trim("-")
}

function Get-EnumNames {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$EnumName
    )

    $inside = $false
    $names = [Collections.Generic.List[string]]::new()
    foreach ($line in Get-Content -LiteralPath $Path) {
        if (-not $inside -and $line -match ("^\s*public enum " + [Regex]::Escape($EnumName) + "\s*$")) {
            $inside = $true
            continue
        }
        if ($inside -and $line -match "^\s*}\s*$") {
            break
        }
        if ($inside -and $line -match "^\s*([A-Za-z][A-Za-z0-9_]*)\s*,?\s*(?://.*)?$") {
            $names.Add($Matches[1])
        }
    }
    return @($names)
}

function Get-HammingDistance {
    param(
        [Parameter(Mandatory)][string]$Left,
        [Parameter(Mandatory)][string]$Right
    )

    if ($Left.Length -ne $Right.Length) {
        return [int]::MaxValue
    }
    $distance = 0
    for ($index = 0; $index -lt $Left.Length; $index++) {
        if ($Left[$index] -ne $Right[$index]) {
            $distance++
        }
    }
    return $distance
}

function Get-ImageInfo {
    param([Parameter(Mandatory)][string]$Path)

    $result = [ordered]@{
        Width = 0
        Height = 0
        AspectRatio = ""
        Format = [IO.Path]::GetExtension($Path).TrimStart(".").ToLowerInvariant()
        DecodedFormat = ""
        FormatMatchesExtension = $false
        FileSizeBytes = (Get-Item -LiteralPath $Path).Length
        HasAlpha = $false
        DecodeStatus = "UNREADABLE"
        Sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()
        PerceptualHash = ""
        GrayRange = 0
    }

    $image = $null
    $sample = $null
    $graphics = $null
    try {
        $image = [Drawing.Image]::FromFile($Path)
        $rawFormat = $image.RawFormat.Guid
        $result.DecodedFormat = if ($rawFormat -eq [Drawing.Imaging.ImageFormat]::Png.Guid) { "png" }
            elseif ($rawFormat -eq [Drawing.Imaging.ImageFormat]::Jpeg.Guid) { "jpg" }
            elseif ($rawFormat -eq [Drawing.Imaging.ImageFormat]::Gif.Guid) { "gif" }
            elseif ($rawFormat -eq [Drawing.Imaging.ImageFormat]::Bmp.Guid) { "bmp" }
            elseif ($rawFormat -eq [Drawing.Imaging.ImageFormat]::Tiff.Guid) { "tiff" }
            else { $rawFormat.ToString() }
        $normalizedExtension = if ($result.Format -eq "jpeg") { "jpg" } else { $result.Format }
        $result.FormatMatchesExtension = $normalizedExtension -eq $result.DecodedFormat
        $result.Width = $image.Width
        $result.Height = $image.Height
        $result.AspectRatio = ($image.Width / [double]$image.Height).ToString("0.0000", [Globalization.CultureInfo]::InvariantCulture)
        $result.HasAlpha = (($image.Flags -band [int][Drawing.Imaging.ImageFlags]::HasAlpha) -ne 0) -or
            ($image.PixelFormat.ToString().IndexOf("Alpha", [StringComparison]::OrdinalIgnoreCase) -ge 0)

        $sample = New-Object Drawing.Bitmap 9, 8, ([Drawing.Imaging.PixelFormat]::Format24bppRgb)
        $graphics = [Drawing.Graphics]::FromImage($sample)
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.DrawImage($image, 0, 0, 9, 8)

        $bits = New-Object Text.StringBuilder 64
        $minimum = 255
        $maximum = 0
        for ($y = 0; $y -lt 8; $y++) {
            for ($x = 0; $x -lt 9; $x++) {
                $pixel = $sample.GetPixel($x, $y)
                $gray = [int][Math]::Round(($pixel.R * 0.299) + ($pixel.G * 0.587) + ($pixel.B * 0.114))
                if ($gray -lt $minimum) { $minimum = $gray }
                if ($gray -gt $maximum) { $maximum = $gray }
                if ($x -lt 8) {
                    $nextPixel = $sample.GetPixel($x + 1, $y)
                    $nextGray = [int][Math]::Round(($nextPixel.R * 0.299) + ($nextPixel.G * 0.587) + ($nextPixel.B * 0.114))
                    [void]$bits.Append($(if ($gray -gt $nextGray) { "1" } else { "0" }))
                }
            }
        }
        $result.PerceptualHash = $bits.ToString()
        $result.GrayRange = $maximum - $minimum
        $result.DecodeStatus = "OK"
    }
    catch {
        $result.DecodeStatus = "ERROR: " + $_.Exception.GetType().Name
    }
    finally {
        if ($null -ne $graphics) { $graphics.Dispose() }
        if ($null -ne $sample) { $sample.Dispose() }
        if ($null -ne $image) { $image.Dispose() }
    }

    return [pscustomobject]$result
}

$PhysicalColumns = @(
    "Path", "Category", "SourceExists", "DistExists", "PublishExists",
    "Width", "Height", "AspectRatio", "Format", "DecodedFormat", "FormatMatchesExtension", "FileSizeBytes", "HasAlpha",
    "DecodeStatus", "Sha256", "PerceptualHash", "ExactDuplicateGroup",
    "NearDuplicateGroup", "ReferenceCount", "ShippingStatus", "DistPackageStatus",
    "PublishPackageStatus", "OrphanStatus", "SuspectedPlaceholderStatus",
    "PlaceholderReason", "ProvenanceStatus", "Notes"
)

$ContentColumns = @(
    "ContentId", "StableId", "ContentType", "ContentSubtype", "SourceKind",
    "SeasonId", "SeasonNumber", "UniverseYearOrDate", "RaceOrEventId",
    "DriverId", "TeamId", "StoryThreadId", "Title", "ContentProvenance",
    "ExpectedAssetId", "ConfiguredAssetPath", "ResolvedPhysicalFile",
    "CurrentFallbackPath", "UiSurfaces", "Ships", "ReachableInUi",
    "ImageWidth", "ImageHeight", "AspectRatio", "FileFormat", "FileSizeBytes",
    "HasAlpha", "DecodeStatus", "BuildCopyStatus", "PublishPackageStatus",
    "Sha256", "PerceptualHash", "ExactReuseCount", "NearDuplicateReuseCount",
    "ExactDuplicateGroup", "NearDuplicateGroup", "PlaceholderDetectionReason",
    "CurrentAuditStatus", "Severity", "RecommendedAction", "NewArtRequired",
    "Notes", "SourceFile", "SourceLocator"
)

function New-OrderedRow {
    param(
        [Parameter(Mandatory)][string[]]$Columns,
        [Parameter(Mandatory)][hashtable]$Values
    )

    $ordered = [ordered]@{}
    foreach ($column in $Columns) {
        $ordered[$column] = if ($Values.ContainsKey($column)) { $Values[$column] } else { "" }
    }
    return [pscustomobject]$ordered
}

function Get-AssetCategory {
    param([Parameter(Mandatory)][string]$LogicalPath)

    switch -Regex ($LogicalPath) {
        "^data/ams2/era-art/" { return "era-art" }
        "^data/ams2/history-art/" { return "history-art" }
        "^data/ams2/track-art/" { return "track-art" }
        "^data/ams2/portraits/" { return "portraits" }
        "^data/ams2/cars/" { return "cars" }
        "^data/ams2/smgp/rounds/" { return "smgp-rounds" }
        "^data/ams2/smgp/teams/" { return "smgp-teams" }
        "^src/Companion\.App/Assets/TrackBanners/" { return "track-banners" }
        "^src/Companion\.App/Assets/Era/" { return "era-textures" }
        default { return "other" }
    }
}

function Get-ProvenanceStatus {
    param(
        [Parameter(Mandatory)][string]$LogicalPath,
        [Parameter(Mandatory)][string]$Category
    )

    $baseName = [IO.Path]::GetFileNameWithoutExtension($LogicalPath)
    if ($Category -eq "era-art" -and $baseName -in @("telegram", "fax", "email")) {
        return "DOCUMENTED_ORIGINAL_GENERATED"
    }
    if ($Category -eq "track-art" -or $Category -eq "history-art") {
        return "USER_DROP_IN_REVIEW"
    }
    return "PROVENANCE_REVIEW"
}

function Get-LogicalPathsFromFolder {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$LogicalPrefix
    )

    if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
        return @()
    }
    $items = [Collections.Generic.List[string]]::new()
    foreach ($file in Get-ChildItem -Recurse -File -LiteralPath $Root) {
        if (-not $ImageExtensions.Contains($file.Extension)) {
            continue
        }
        $relative = Get-RelativePath -BasePath $Root -Path $file.FullName
        $items.Add(($LogicalPrefix.TrimEnd("/") + "/" + $relative).Replace("//", "/"))
    }
    return @($items)
}

$LooseFolders = @(
    "era-art",
    "history-art",
    "track-art",
    "portraits",
    "cars",
    "smgp/rounds",
    "smgp/teams"
)

$logicalPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($folder in $LooseFolders) {
    $sourceFolder = Join-Path $RepoRoot ("data\ams2\" + $folder.Replace("/", "\"))
    foreach ($logical in Get-LogicalPathsFromFolder -Root $sourceFolder -LogicalPrefix ("data/ams2/" + $folder)) {
        [void]$logicalPaths.Add($logical)
    }

    $distFolder = Join-Path $RepoRoot ("dist\data\ams2\" + $folder.Replace("/", "\"))
    foreach ($logical in Get-LogicalPathsFromFolder -Root $distFolder -LogicalPrefix ("data/ams2/" + $folder)) {
        [void]$logicalPaths.Add($logical)
    }

    if (-not [string]::IsNullOrWhiteSpace($PublishDirectory)) {
        $publishFolder = Join-Path $PublishDirectory ("data\ams2\" + $folder.Replace("/", "\"))
        foreach ($logical in Get-LogicalPathsFromFolder -Root $publishFolder -LogicalPrefix ("data/ams2/" + $folder)) {
            [void]$logicalPaths.Add($logical)
        }
    }
}

$EmbeddedFolders = @(
    "src/Companion.App/Assets/TrackBanners",
    "src/Companion.App/Assets/Era"
)
foreach ($folder in $EmbeddedFolders) {
    $absolute = Join-Path $RepoRoot $folder.Replace("/", "\")
    foreach ($logical in Get-LogicalPathsFromFolder -Root $absolute -LogicalPrefix $folder) {
        [void]$logicalPaths.Add($logical)
    }
}

$physicalRows = [Collections.Generic.List[object]]::new()
$assetByLogicalPath = @{}
foreach ($logicalPath in @($logicalPaths) | Sort-Object) {
    $category = Get-AssetCategory -LogicalPath $logicalPath
    $sourcePath = Join-Path $RepoRoot $logicalPath.Replace("/", "\")
    $distPath = if ($logicalPath.StartsWith("data/ams2/", [StringComparison]::Ordinal)) {
        Join-Path $RepoRoot ("dist\" + $logicalPath.Replace("/", "\"))
    }
    else { "" }
    $publishPath = if (-not [string]::IsNullOrWhiteSpace($PublishDirectory) -and
        $logicalPath.StartsWith("data/ams2/", [StringComparison]::Ordinal)) {
        Join-Path $PublishDirectory $logicalPath.Replace("/", "\")
    }
    else { "" }

    $sourceExists = Test-Path -LiteralPath $sourcePath -PathType Leaf
    $distExists = $distPath.Length -gt 0 -and (Test-Path -LiteralPath $distPath -PathType Leaf)
    $publishExists = $publishPath.Length -gt 0 -and (Test-Path -LiteralPath $publishPath -PathType Leaf)
    $canonicalPath = if ($sourceExists) { $sourcePath }
        elseif ($distExists) { $distPath }
        elseif ($publishExists) { $publishPath }
        else { "" }

    if ($canonicalPath.Length -eq 0) {
        continue
    }
    $info = Get-ImageInfo -Path $canonicalPath
    $filename = [IO.Path]::GetFileNameWithoutExtension($logicalPath)
    $placeholderTokens = @(
        "placeholder", "default", "fallback", "generic", "temporary", "temp", "sample",
        "demo", "todo", "tbd", "missing", "coming-soon", "unknown", "no-image", "blank",
        "test-image"
    )
    $placeholderReason = ""
    foreach ($token in $placeholderTokens) {
        if (($filename.IndexOf($token, [StringComparison]::OrdinalIgnoreCase) -ge 0)) {
            $placeholderReason = "Filename contains '$token'"
            break
        }
    }
    if ($placeholderReason.Length -eq 0 -and $info.DecodeStatus -eq "OK" -and
        -not $info.HasAlpha -and $info.GrayRange -le 2) {
        $placeholderReason = "Near-uniform luminance in 9x8 audit sample"
    }

    $distStatus = if ($distPath.Length -eq 0) { "NOT_APPLICABLE" }
        elseif (-not $distExists) { "MISSING_FROM_DIST" }
        elseif (-not $sourceExists) { "DIST_ONLY" }
        elseif ((Get-FileHash -Algorithm SHA256 -LiteralPath $distPath).Hash -ieq $info.Sha256) { "MATCHES_SOURCE" }
        else { "DIFFERS_FROM_SOURCE" }
    $publishStatus = if ([string]::IsNullOrWhiteSpace($PublishDirectory)) { "NOT_CHECKED" }
        elseif ($logicalPath.StartsWith("src/Companion.App/Assets/", [StringComparison]::Ordinal)) {
            if (Test-Path -LiteralPath (Join-Path $PublishDirectory "AMS2CareerCompanion.exe") -PathType Leaf) {
                "EMBEDDED_RESOURCE_BUILD_PRESENT"
            }
            else { "PUBLISH_EXE_MISSING" }
        }
        elseif (-not $publishExists) { "MISSING_FROM_PUBLISH" }
        elseif (-not $sourceExists) { "PUBLISH_ONLY" }
        elseif ((Get-FileHash -Algorithm SHA256 -LiteralPath $publishPath).Hash -ieq $info.Sha256) { "MATCHES_SOURCE" }
        else { "DIFFERS_FROM_SOURCE" }

    $shippingStatus = if ($logicalPath.StartsWith("src/Companion.App/Assets/", [StringComparison]::Ordinal)) {
        "EMBEDDED_RESOURCE_DECLARED"
    }
    elseif ($sourceExists) { "LOOSE_COPY_DECLARED" }
    else { "DIST_ONLY_NOT_IN_SOURCE" }

    $row = New-OrderedRow -Columns $PhysicalColumns -Values @{
        Path = $logicalPath
        Category = $category
        SourceExists = $sourceExists
        DistExists = $distExists
        PublishExists = $publishExists
        Width = $info.Width
        Height = $info.Height
        AspectRatio = $info.AspectRatio
        Format = $info.Format
        DecodedFormat = $info.DecodedFormat
        FormatMatchesExtension = $info.FormatMatchesExtension
        FileSizeBytes = $info.FileSizeBytes
        HasAlpha = $info.HasAlpha
        DecodeStatus = $info.DecodeStatus
        Sha256 = $info.Sha256
        PerceptualHash = $info.PerceptualHash
        ReferenceCount = 0
        ShippingStatus = $shippingStatus
        DistPackageStatus = $distStatus
        PublishPackageStatus = $publishStatus
        OrphanStatus = "PENDING"
        SuspectedPlaceholderStatus = $(if ($placeholderReason.Length -gt 0) { "SUSPECTED_PLACEHOLDER" } else { "NO_SIGNAL" })
        PlaceholderReason = $placeholderReason
        ProvenanceStatus = Get-ProvenanceStatus -LogicalPath $logicalPath -Category $category
        Notes = $(if (-not $sourceExists) { "Asset exists outside tracked source only." } else { "" })
    }
    $physicalRows.Add($row)
    $assetByLogicalPath[$logicalPath] = $row
}

# Exact duplicates are certain. Perceptual groups are suspected and only compare assets inside
# the same functional category to avoid grouping unrelated logos, portraits, and editorial art.
$exactIndex = 0
foreach ($group in $physicalRows | Where-Object { $_.DecodeStatus -eq "OK" } | Group-Object Sha256 |
    Where-Object Count -gt 1 | Sort-Object Name) {
    $exactIndex++
    $name = "EXACT-{0:d3}" -f $exactIndex
    foreach ($row in $group.Group) {
        $row.ExactDuplicateGroup = $name
    }
}

$parent = @{}
foreach ($row in $physicalRows) {
    $parent[$row.Path] = $row.Path
}
function Find-Root {
    param([Parameter(Mandatory)][string]$Key)
    $cursor = $Key
    while ($parent[$cursor] -ne $cursor) {
        $cursor = $parent[$cursor]
    }
    $root = $cursor
    $cursor = $Key
    while ($parent[$cursor] -ne $cursor) {
        $next = $parent[$cursor]
        $parent[$cursor] = $root
        $cursor = $next
    }
    return $root
}
function Join-Roots {
    param(
        [Parameter(Mandatory)][string]$Left,
        [Parameter(Mandatory)][string]$Right
    )
    $leftRoot = Find-Root -Key $Left
    $rightRoot = Find-Root -Key $Right
    if ($leftRoot -ne $rightRoot) {
        if ([StringComparer]::Ordinal.Compare($leftRoot, $rightRoot) -lt 0) {
            $parent[$rightRoot] = $leftRoot
        }
        else {
            $parent[$leftRoot] = $rightRoot
        }
    }
}

foreach ($categoryGroup in $physicalRows | Where-Object {
        $_.DecodeStatus -eq "OK" -and $_.PerceptualHash.Length -eq 64
    } | Group-Object Category) {
    $items = @($categoryGroup.Group | Sort-Object Path)
    for ($leftIndex = 0; $leftIndex -lt $items.Count; $leftIndex++) {
        for ($rightIndex = $leftIndex + 1; $rightIndex -lt $items.Count; $rightIndex++) {
            if ($items[$leftIndex].Sha256 -eq $items[$rightIndex].Sha256) {
                continue
            }
            $distance = Get-HammingDistance -Left $items[$leftIndex].PerceptualHash -Right $items[$rightIndex].PerceptualHash
            if ($distance -le 4) {
                Join-Roots -Left $items[$leftIndex].Path -Right $items[$rightIndex].Path
            }
        }
    }
}

$nearGroups = @{}
foreach ($row in $physicalRows) {
    $root = Find-Root -Key $row.Path
    if (-not $nearGroups.ContainsKey($root)) {
        $nearGroups[$root] = [Collections.Generic.List[object]]::new()
    }
    $nearGroups[$root].Add($row)
}
$nearIndex = 0
foreach ($group in $nearGroups.Values | Where-Object Count -gt 1 |
    Sort-Object { ($_.Path | Sort-Object | Select-Object -First 1) }) {
    $nearIndex++
    $name = "NEAR-{0:d3}" -f $nearIndex
    foreach ($row in $group) {
        $row.NearDuplicateGroup = $name
    }
}

function Resolve-LogicalAsset {
    param(
        [Parameter(Mandatory)][string]$Folder,
        [Parameter(Mandatory)][string]$Key
    )

    foreach ($extension in @(".jpg", ".jpeg", ".png")) {
        $candidate = ("data/ams2/" + $Folder.Trim("/") + "/" + $Key + $extension).Replace("//", "/")
        if ($assetByLogicalPath.ContainsKey($candidate)) {
            return $candidate
        }
    }
    return ""
}

$contentRows = [Collections.Generic.List[object]]::new()
function Add-ContentRow {
    param([Parameter(Mandatory)][hashtable]$Values)

    $row = New-OrderedRow -Columns $ContentColumns -Values $Values
    if ($row.ResolvedPhysicalFile.Length -gt 0 -and $assetByLogicalPath.ContainsKey($row.ResolvedPhysicalFile)) {
        $asset = $assetByLogicalPath[$row.ResolvedPhysicalFile]
        $asset.ReferenceCount = [int]$asset.ReferenceCount + 1
        $row.ImageWidth = $asset.Width
        $row.ImageHeight = $asset.Height
        $row.AspectRatio = $asset.AspectRatio
        $row.FileFormat = $(if ($asset.DecodedFormat.Length -gt 0) { $asset.DecodedFormat } else { $asset.Format })
        $row.FileSizeBytes = $asset.FileSizeBytes
        $row.HasAlpha = $asset.HasAlpha
        $row.DecodeStatus = $asset.DecodeStatus
        $row.BuildCopyStatus = $asset.ShippingStatus
        $row.PublishPackageStatus = $asset.PublishPackageStatus
        $row.Sha256 = $asset.Sha256
        $row.PerceptualHash = $asset.PerceptualHash
        $row.ExactDuplicateGroup = $asset.ExactDuplicateGroup
        $row.NearDuplicateGroup = $asset.NearDuplicateGroup
        $row.PlaceholderDetectionReason = $asset.PlaceholderReason
    }
    $contentRows.Add($row)
}

function Get-StatusForResolvedAsset {
    param(
        [Parameter(Mandatory)][string]$LogicalPath,
        [string]$BaseStatus = "APPROVED_UNIQUE"
    )

    if ($LogicalPath.Length -eq 0) {
        return "MISSING_FILE"
    }
    $asset = $assetByLogicalPath[$LogicalPath]
    $statuses = [Collections.Generic.List[string]]::new()
    $statuses.Add($BaseStatus)
    if ($asset.DecodeStatus -ne "OK") { $statuses.Add("CORRUPT_OR_UNREADABLE") }
    if ($asset.SuspectedPlaceholderStatus -eq "SUSPECTED_PLACEHOLDER") { $statuses.Add("SUSPECTED_PLACEHOLDER") }
    if ($asset.ProvenanceStatus -eq "PROVENANCE_REVIEW") { $statuses.Add("PROVENANCE_REVIEW") }
    if ($asset.PublishPackageStatus -eq "MISSING_FROM_PUBLISH") { $statuses.Add("SOURCE_ONLY_NOT_PACKAGED") }
    return ($statuses -join "|")
}

# Production News templates: these are the finite authored source records. Runtime article instances
# are not stored; one deterministic event can select one of these templates.
$newsroomDirectory = Join-Path $RepoRoot "data\rules\newsroom"
$newsroomTemplateCount = 0
$newsroomEventNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($file in Get-ChildItem -File -LiteralPath $newsroomDirectory -Filter "*.json" |
    Where-Object Name -ne "desks.json" | Sort-Object Name) {
    $json = Read-Json -Path $file.FullName
    $templates = @(Get-ObjectProperty -Object $json -Name "templates" -Default @())
    for ($index = 0; $index -lt $templates.Count; $index++) {
        $template = $templates[$index]
        $id = [string](Get-ObjectProperty -Object $template -Name "id" -Default "")
        $eventName = [string](Get-ObjectProperty -Object $template -Name "event" -Default "")
        [void]$newsroomEventNames.Add($eventName)
        $newsroomTemplateCount++
        $headline = [string](Get-ObjectProperty -Object $template -Name "headline" -Default $id)
        $provenance = if ($eventName -eq "historyHeld") { "verifiedHistorical" }
            elseif ($eventName -in @("smgpCanonDiverged", "smgpCanonHeld")) { "smgpFiction" }
            elseif ($eventName -eq "historyDiverged") { "hybrid" }
            else { "careerUniverse" }
        Add-ContentRow @{
            ContentId = "newsroom-template:$id"
            StableId = $id
            ContentType = "NEWS"
            ContentSubtype = "NEWSROOM_TEMPLATE"
            SourceKind = "TEMPLATE_BASED"
            SeasonId = "shared"
            RaceOrEventId = $eventName
            Title = $headline
            ContentProvenance = $provenance
            ExpectedAssetId = "newsroom:$id"
            ConfiguredAssetPath = ""
            ResolvedPhysicalFile = ""
            CurrentFallbackPath = "Explicit News placeholder frame and era texture; no article art key is projected"
            UiSurfaces = "News lead; featured cards; story list; bookmarks; search; article reader"
            Ships = $true
            ReachableInUi = "CONDITIONAL_ON_DETECTED_EVENT"
            DecodeStatus = "NO_ASSET_FIELD"
            BuildCopyStatus = "NOT_APPLICABLE"
            PublishPackageStatus = "NOT_APPLICABLE"
            CurrentAuditStatus = "MISSING_ASSET_FIELD|EXPLICIT_PLACEHOLDER|GENERIC_FALLBACK|UNIVERSAL_FALLBACK_OVERUSE"
            Severity = "P0"
            RecommendedAction = "Stage B: assign a deterministic context-aware editorial art pool by event, season, team, driver, venue, and stable story key."
            NewArtRequired = $true
            Notes = "NewsStoryProjection copies text/editorial metadata for NewsroomArticle but leaves TrackArtKey, DriverPortraitKey, TeamArtKey, and CarArtKey empty."
            SourceFile = Convert-ToRepoPath -Path $file.FullName
            SourceLocator = "templates[$index]"
        }
    }
}

# Legacy article grammar: ten body families in seven era corpora. These remain a production fallback
# when the richer newsroom corpus is absent or does not supersede the journal story.
$legacyDirectory = Join-Path $RepoRoot "data\rules\news"
$legacyBodyRows = 0
$legacyBodyFamilyNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($file in Get-ChildItem -File -LiteralPath $legacyDirectory -Filter "*.json" | Sort-Object Name) {
    $json = Read-Json -Path $file.FullName
    $eraKey = [IO.Path]::GetFileNameWithoutExtension($file.Name)
    foreach ($bodyProperty in $json.bodies.PSObject.Properties | Sort-Object Name) {
        $legacyBodyRows++
        [void]$legacyBodyFamilyNames.Add($bodyProperty.Name)
        $stableId = "legacy-news:${eraKey}:$($bodyProperty.Name)"
        Add-ContentRow @{
            ContentId = $stableId
            StableId = $stableId
            ContentType = "NEWS"
            ContentSubtype = "LEGACY_BODY_FAMILY"
            SourceKind = "TEMPLATE_BASED"
            SeasonId = "shared"
            RaceOrEventId = $bodyProperty.Name
            Title = "$eraKey - $($bodyProperty.Name)"
            ContentProvenance = $(if ($eraKey -eq "smgp") { "smgpFiction" } else { "careerUniverse" })
            ExpectedAssetId = "legacy:${eraKey}:$($bodyProperty.Name)"
            CurrentFallbackPath = "Explicit News placeholder frame and era document styling; journal projection carries no art key"
            UiSurfaces = "News story list; article reader; History archived articles"
            Ships = $true
            ReachableInUi = "FALLBACK_OR_LEGACY_CAREER"
            DecodeStatus = "NO_ASSET_FIELD"
            BuildCopyStatus = "NOT_APPLICABLE"
            PublishPackageStatus = "NOT_APPLICABLE"
            CurrentAuditStatus = "MISSING_ASSET_FIELD|EXPLICIT_PLACEHOLDER|GENERIC_FALLBACK|UNIVERSAL_FALLBACK_OVERUSE"
            Severity = "P0"
            RecommendedAction = "Stage B: provide deterministic art pools for the body family and use story facts to avoid unrelated reuse."
            NewArtRequired = $true
            Notes = "One row represents the art requirement for the procedural body family, not every prose variant."
            SourceFile = Convert-ToRepoPath -Path $file.FullName
            SourceLocator = "bodies.$($bodyProperty.Name)"
        }
    }
}

# Frozen fold-time headline bank. Nine keys are paired with legacy body families above; five
# headline-only families still produce shipping News rows and therefore need explicit art coverage.
$headlineBankPath = Join-Path $RepoRoot "data\rules\career-headline-templates.json"
$headlineBankJson = Read-Json -Path $headlineBankPath
$headlineFamilyNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$legacyArticleFamilyNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$headlineVariantCount = 0
$headlineOnlyFamilyCount = 0
foreach ($name in $legacyBodyFamilyNames) { [void]$legacyArticleFamilyNames.Add($name) }
foreach ($familyProperty in $headlineBankJson.templates.PSObject.Properties | Sort-Object Name) {
    $familyName = [string]$familyProperty.Name
    [void]$headlineFamilyNames.Add($familyName)
    [void]$legacyArticleFamilyNames.Add($familyName)
    foreach ($eraProperty in $familyProperty.Value.PSObject.Properties) {
        $headlineVariantCount += @($eraProperty.Value).Count
    }
    if ($legacyBodyFamilyNames.Contains($familyName)) { continue }

    $headlineOnlyFamilyCount++
    $stableId = "legacy-headline-only:$familyName"
    Add-ContentRow @{
        ContentId = $stableId
        StableId = $stableId
        ContentType = "NEWS"
        ContentSubtype = "LEGACY_HEADLINE_ONLY_FAMILY"
        SourceKind = "TEMPLATE_BASED"
        SeasonId = "shared"
        RaceOrEventId = $familyName
        Title = $familyName
        ContentProvenance = "careerUniverse"
        ExpectedAssetId = "legacy-headline:$familyName"
        CurrentFallbackPath = "Explicit News placeholder frame; the fold-time journal row has no art key"
        UiSurfaces = "News unified wire; headline-only journal row"
        Ships = $true
        ReachableInUi = "CONDITIONAL_ON_JOURNAL_OUTCOME"
        DecodeStatus = "NO_ASSET_FIELD"
        BuildCopyStatus = "NOT_APPLICABLE"
        PublishPackageStatus = "NOT_APPLICABLE"
        CurrentAuditStatus = "MISSING_ASSET_FIELD|EXPLICIT_PLACEHOLDER|GENERIC_FALLBACK|UNIVERSAL_FALLBACK_OVERUSE"
        Severity = "P0"
        RecommendedAction = "Stage B: provide deterministic era-aware editorial art for this headline-only family and persist a stable art selection."
        NewArtRequired = $true
        Notes = "This family has no legacy body-bank row, but its stored journal headline can still reach the unified News wire."
        SourceFile = Convert-ToRepoPath -Path $headlineBankPath
        SourceLocator = "templates.$familyName"
    }
}
# SMGP dispatch corpus: finite procedural families with runtime driver/team overlays. They currently
# reuse portraits and team photos; no article-specific editorial image exists.
$dispatchPath = Join-Path $RepoRoot "data\rules\smgp\dispatches.json"
$dispatchJson = Read-Json -Path $dispatchPath
$dispatchFamilyCount = 0
foreach ($property in $dispatchJson.templates.PSObject.Properties | Sort-Object Name) {
    $dispatchFamilyCount++
    $stableId = "smgp-dispatch:$($property.Name)"
    Add-ContentRow @{
        ContentId = $stableId
        StableId = $stableId
        ContentType = "NEWS"
        ContentSubtype = "SMGP_DISPATCH_FAMILY"
        SourceKind = "TEMPLATE_BASED"
        SeasonId = "shared"
        RaceOrEventId = $property.Name
        Title = $property.Name
        ContentProvenance = "smgpFiction"
        ExpectedAssetId = $stableId
        ConfiguredAssetPath = "dynamic: data/ams2/smgp/teams/<team>.jpg + data/ams2/portraits/<driver>.jpg"
        CurrentFallbackPath = "Semantic category block when dynamic team/driver keys are empty"
        UiSurfaces = "News lead; News cards; article reader; History latest dispatches"
        Ships = $true
        ReachableInUi = "CONDITIONAL_ON_SMGP_BEAT"
        DecodeStatus = "DYNAMIC_POOL"
        BuildCopyStatus = "LOOSE_COPY_DECLARED"
        PublishPackageStatus = $(if ([string]::IsNullOrWhiteSpace($PublishDirectory)) { "NOT_CHECKED" } else { "POOL_CHECKED_IN_PHYSICAL_INVENTORY" })
        CurrentAuditStatus = "GENERIC_FALLBACK|UNINTENTIONAL_REUSE"
        Severity = "P1"
        RecommendedAction = "Stage B: replace portrait/team-photo reuse with a deterministic event-, season-, team-, driver-, and venue-aware editorial pool."
        NewArtRequired = $true
        Notes = "$(@($property.Value).Count) authored prose variants share this art behavior."
        SourceFile = Convert-ToRepoPath -Path $dispatchPath
        SourceLocator = "templates.$($property.Name)"
    }
}

# Story threads are audited explicitly. Their rail is intentionally text-only today.
$threadTypes = Get-EnumNames -Path (Join-Path $RepoRoot "src\Companion.Core\Newsroom\StoryThreads.cs") -EnumName "StoryThreadType"
foreach ($threadType in $threadTypes) {
    Add-ContentRow @{
        ContentId = "story-thread:$threadType"
        StableId = "story-thread:$threadType"
        ContentType = "NEWS"
        ContentSubtype = "STORY_THREAD_FAMILY"
        SourceKind = "GENERATED"
        SeasonId = "shared"
        StoryThreadId = $threadType
        Title = $threadType
        ContentProvenance = "careerUniverse"
        ExpectedAssetId = ""
        ConfiguredAssetPath = ""
        CurrentFallbackPath = "Text-only thread rail by current design"
        UiSurfaces = "News story-thread rail"
        Ships = $true
        ReachableInUi = "CONDITIONAL_ON_THREAD_EVENTS"
        DecodeStatus = "INTENTIONALLY_IMAGELESS"
        BuildCopyStatus = "NOT_APPLICABLE"
        PublishPackageStatus = "NOT_APPLICABLE"
        CurrentAuditStatus = "INTENTIONALLY_IMAGELESS"
        Severity = "P4"
        RecommendedAction = "Owner review in Stage B: keep the compact thread rail text-only or add one deterministic thread cover per family."
        NewArtRequired = $false
        Notes = "The linked articles are separately inventoried; this row covers the thread container itself."
        SourceFile = "src/Companion.Core/Newsroom/StoryThreads.cs"
        SourceLocator = "StoryThreadType.$threadType"
    }
}

# SMGP season identity and introduction/retrospective requirement, one per campaign ordinal.
$smgpSeasonsPath = Join-Path $RepoRoot "data\rules\smgp\seasons.json"
$smgpSeasonsJson = Read-Json -Path $smgpSeasonsPath
foreach ($season in @($smgpSeasonsJson.seasons) | Sort-Object ordinal) {
    $ordinal = [int]$season.ordinal
    Add-ContentRow @{
        ContentId = "smgp-season-lore:$ordinal"
        StableId = "smgp-season-lore:$ordinal"
        ContentType = "HISTORY"
        ContentSubtype = "SMGP_SEASON_LORE"
        SourceKind = "AUTHORED"
        SeasonId = "ordinal:$ordinal"
        SeasonNumber = $ordinal
        UniverseYearOrDate = [string](1989 + $ordinal)
        Title = [string]$season.title
        ContentProvenance = "smgpFiction"
        ExpectedAssetId = "smgp-season:$ordinal"
        ConfiguredAssetPath = ""
        CurrentFallbackPath = "Text-only Briefing lore header and CampaignTimelineStrip slot"
        UiSurfaces = "Briefing season header; Rival screen; campaign timeline; future season preview"
        Ships = $true
        ReachableInUi = $true
        DecodeStatus = "NO_ASSET_FIELD"
        BuildCopyStatus = "NOT_APPLICABLE"
        PublishPackageStatus = "NOT_APPLICABLE"
        CurrentAuditStatus = "MISSING_ASSET_FIELD"
        Severity = "P1"
        RecommendedAction = "Stage B: create one season-specific editorial identity image with safe crops for preview and timeline use."
        NewArtRequired = $true
        Notes = "The authored overview, preseason, technical, safety, themes, timeline, arcs, hooks, contenders, and milestones share this season-level art requirement. Ordinal is the only source-defined season identity; every campaign year reuses production packId smgp-1."
        SourceFile = Convert-ToRepoPath -Path $smgpSeasonsPath
        SourceLocator = "seasons[ordinal=$ordinal]"
    }
}

# SMGP almanac, one canonical venue-history record per campaign venue.
$almanacPath = Join-Path $RepoRoot "data\rules\smgp\what-really-happened.json"
$almanacJson = Read-Json -Path $almanacPath
$almanacIndex = 0
foreach ($property in $almanacJson.races.PSObject.Properties) {
    $almanacIndex++
    $resolved = Resolve-LogicalAsset -Folder "smgp/rounds" -Key $almanacIndex.ToString([Globalization.CultureInfo]::InvariantCulture)
    $status = Get-StatusForResolvedAsset -LogicalPath $resolved
    Add-ContentRow @{
        ContentId = "smgp-almanac:$((Get-Slug -Value $property.Name))"
        StableId = "smgp-almanac:$((Get-Slug -Value $property.Name))"
        ContentType = "HISTORY"
        ContentSubtype = "SMGP_CANON_RACE"
        SourceKind = "AUTHORED"
        SeasonId = "shared"
        RaceOrEventId = "canonical-round:$almanacIndex"
        Title = [string](Get-ObjectProperty -Object $property.Value -Name "title" -Default $property.Name)
        ContentProvenance = "smgpFiction"
        ExpectedAssetId = "smgp-round:$almanacIndex"
        ConfiguredAssetPath = "data/ams2/smgp/rounds/$almanacIndex.(jpg|jpeg|png)"
        ResolvedPhysicalFile = $resolved
        CurrentFallbackPath = "Panel hides image if the round-keyed file is absent"
        UiSurfaces = "History SMGP almanac; PhotoWindow"
        Ships = $true
        ReachableInUi = "UNLOCKS_AFTER_VENUE_IS_RACED"
        CurrentAuditStatus = $status
        Severity = $(if ($resolved.Length -eq 0) { "P0" } else { "P3" })
        RecommendedAction = "Stage B: validate the venue against the art in a live shuffled-season career; retain only if context and provenance are approved."
        NewArtRequired = $false
        Notes = "The almanac is venue-keyed, while the current raster binding is round-number keyed. Exact season-2+ context needs live-career validation."
        SourceFile = Convert-ToRepoPath -Path $almanacPath
        SourceLocator = "races.$($property.Name)"
    }
}

# SMGP driver and team history profiles have finite stable IDs and dedicated current art.
$driverProfilesPath = Join-Path $RepoRoot "data\rules\smgp\driver-profiles.json"
$driverProfilesJson = Read-Json -Path $driverProfilesPath
foreach ($driver in @($driverProfilesJson.drivers) | Sort-Object driverId) {
    $driverId = [string]$driver.driverId
    $resolved = Resolve-LogicalAsset -Folder "portraits" -Key $driverId
    Add-ContentRow @{
        ContentId = "smgp-driver-history:$driverId"
        StableId = $driverId
        ContentType = "HISTORY"
        ContentSubtype = "SMGP_DRIVER_HISTORY"
        SourceKind = "AUTHORED"
        SeasonId = "shared"
        DriverId = $driverId
        Title = [string]$driver.name
        ContentProvenance = "smgpFiction"
        ExpectedAssetId = $driverId
        ConfiguredAssetPath = "data/ams2/portraits/$driverId.(jpg|jpeg|png)"
        ResolvedPhysicalFile = $resolved
        CurrentFallbackPath = "Portrait panel collapses when absent"
        UiSurfaces = "News cards; News reader; History events; History hero/rival; Paddock driver history"
        Ships = $true
        ReachableInUi = $true
        CurrentAuditStatus = Get-StatusForResolvedAsset -LogicalPath $resolved
        Severity = $(if ($resolved.Length -eq 0) { "P0" } else { "P3" })
        RecommendedAction = "Stage B: retain only after style/context/provenance review; do not treat a profile portrait as article-specific editorial art."
        NewArtRequired = $false
        Notes = "Dedicated driver portrait exists or is expected; article reuse is separately reported on the template mappings."
        SourceFile = Convert-ToRepoPath -Path $driverProfilesPath
        SourceLocator = "drivers[driverId=$driverId]"
    }
}

$teamProfilesPath = Join-Path $RepoRoot "data\rules\smgp\team-profiles.json"
$teamProfilesJson = Read-Json -Path $teamProfilesPath
foreach ($property in $teamProfilesJson.teams.PSObject.Properties | Sort-Object Name) {
    $teamId = $property.Name
    $teamKey = if ($teamId.StartsWith("team.", [StringComparison]::Ordinal)) { $teamId.Substring(5) } else { $teamId }
    $resolved = Resolve-LogicalAsset -Folder "smgp/teams" -Key $teamKey
    Add-ContentRow @{
        ContentId = "smgp-team-history:$teamId"
        StableId = $teamId
        ContentType = "HISTORY"
        ContentSubtype = "SMGP_TEAM_HISTORY"
        SourceKind = "AUTHORED"
        SeasonId = "shared"
        TeamId = $teamId
        Title = [string](Get-ObjectProperty -Object $property.Value -Name "name" -Default $teamKey)
        ContentProvenance = "smgpFiction"
        ExpectedAssetId = "team-photo:$teamKey"
        ConfiguredAssetPath = "data/ams2/smgp/teams/$teamKey.(jpg|jpeg|png)"
        ResolvedPhysicalFile = $resolved
        CurrentFallbackPath = "Team photo panel collapses when absent"
        UiSurfaces = "News cards; News reader; History dispatches; Paddock team history"
        Ships = $true
        ReachableInUi = $true
        CurrentAuditStatus = Get-StatusForResolvedAsset -LogicalPath $resolved
        Severity = $(if ($resolved.Length -eq 0) { "P0" } else { "P3" })
        RecommendedAction = "Stage B: retain only after style/context/provenance review; do not treat a team photo as article-specific editorial art."
        NewArtRequired = $false
        Notes = "Dedicated team photo exists or is expected; article reuse is separately reported on the template mappings."
        SourceFile = Convert-ToRepoPath -Path $teamProfilesPath
        SourceLocator = "teams.$teamId"
    }
}

# The current career archive exposes 16 race slots in each of 17 SMGP seasons. The view binds
# raster art by round number. From season 2 onward the calendar is shuffled, so the same key can
# depict a different venue. No shipping sample career database exists to enumerate exact swaps.
for ($seasonOrdinal = 1; $seasonOrdinal -le 17; $seasonOrdinal++) {
    for ($round = 1; $round -le 16; $round++) {
        $resolved = Resolve-LogicalAsset -Folder "smgp/rounds" -Key $round.ToString([Globalization.CultureInfo]::InvariantCulture)
        $status = if ($seasonOrdinal -eq 1) {
            Get-StatusForResolvedAsset -LogicalPath $resolved -BaseStatus "APPROVED_INTENTIONAL_REUSE"
        }
        else {
            $base = if ($resolved.Length -eq 0) { "MISSING_FILE" } else { "GENERIC_FALLBACK|UNKNOWN_REQUIRES_REVIEW" }
            if ($resolved.Length -gt 0 -and $assetByLogicalPath[$resolved].ProvenanceStatus -eq "PROVENANCE_REVIEW") {
                $base += "|PROVENANCE_REVIEW"
            }
            $base
        }
        Add-ContentRow @{
            ContentId = "smgp-race-history:s$($seasonOrdinal.ToString('00')):r$($round.ToString('00'))"
            StableId = "race:${seasonOrdinal}:$round"
            ContentType = "HISTORY"
            ContentSubtype = "SMGP_GENERATED_RACE_SLOT"
            SourceKind = "GENERATED"
            SeasonId = "ordinal:$seasonOrdinal"
            SeasonNumber = $seasonOrdinal
            UniverseYearOrDate = [string](1989 + $seasonOrdinal)
            RaceOrEventId = "round:$round"
            Title = "Season $seasonOrdinal race archive, round $round"
            ContentProvenance = "careerUniverse"
            ExpectedAssetId = "smgp-race:${seasonOrdinal}:$round"
            ConfiguredAssetPath = "data/ams2/smgp/rounds/$round.(jpg|jpeg|png)"
            ResolvedPhysicalFile = $resolved
            CurrentFallbackPath = "History race card semantic panel when the round image is absent"
            UiSurfaces = "History race archive card; History chronological event"
            Ships = $true
            ReachableInUi = "AFTER_RACE_IS_STORED"
            CurrentAuditStatus = $status
            Severity = $(if ($resolved.Length -eq 0) { "P0" } elseif ($seasonOrdinal -eq 1) { "P3" } else { "P2" })
            RecommendedAction = $(if ($seasonOrdinal -eq 1) {
                "Validate crop and provenance; retain as intentional round/venue art if approved."
            } else {
                "Stage B integration: bind by stable venue/track identity after comparing a live shuffled career. Existing art may be reusable after correct mapping."
            })
            NewArtRequired = $false
            Notes = $(if ($seasonOrdinal -eq 1) {
                "Season 1 uses the canonical round order."
            } else {
                "Exact mismatch is unknown without a concrete career seed/database. The system shuffles season-2+ calendars, while this UI binding uses only Round."
            })
            SourceFile = "src/Companion.App/Views/HistoryView.xaml"
            SourceLocator = "HistoryRaceTemplate round image binding"
        }
    }
}

# Generated career-history beat families use portraits rather than event-specific art.
$beatKinds = Get-EnumNames -Path (Join-Path $RepoRoot "src\Companion.Core\Smgp\SmgpCareerBeats.cs") -EnumName "SmgpBeatKind"
foreach ($kind in $beatKinds) {
    Add-ContentRow @{
        ContentId = "smgp-history-beat:$kind"
        StableId = "smgp-history-beat:$kind"
        ContentType = "HISTORY"
        ContentSubtype = "SMGP_CAREER_BEAT_FAMILY"
        SourceKind = "GENERATED"
        SeasonId = "shared"
        RaceOrEventId = $kind
        Title = $kind
        ContentProvenance = "careerUniverse"
        ExpectedAssetId = "dynamic:subject-portrait"
        ConfiguredAssetPath = "dynamic: data/ams2/portraits/<subjectDriverId>.(jpg|jpeg|png)"
        CurrentFallbackPath = "Text event card when SubjectPortraitKey is empty"
        UiSurfaces = "History chronological event; News/History dispatch context"
        Ships = $true
        ReachableInUi = "CONDITIONAL_ON_CAREER_BEAT"
        DecodeStatus = "DYNAMIC_POOL"
        BuildCopyStatus = "LOOSE_COPY_DECLARED"
        PublishPackageStatus = $(if ([string]::IsNullOrWhiteSpace($PublishDirectory)) { "NOT_CHECKED" } else { "POOL_CHECKED_IN_PHYSICAL_INVENTORY" })
        CurrentAuditStatus = "GENERIC_FALLBACK|UNINTENTIONAL_REUSE"
        Severity = "P3"
        RecommendedAction = "Stage B: decide which major beat families need contextual editorial art rather than a repeated driver portrait."
        NewArtRequired = $true
        Notes = "The family is finite, but occurrences are career-generated and identified by stable beat keys."
        SourceFile = "src/Companion.Core/Smgp/SmgpCareerBeats.cs"
        SourceLocator = "SmgpBeatKind.$kind"
    }
}

# Real-history season, race, and computed entity coverage. Season cards have an explicit optional
# history-art slot. Round/entity/subject surfaces are intentionally image-less or vector-first today.
$historyDirectory = Join-Path $RepoRoot "data\history"
$historyFiles = @(Get-ChildItem -File -LiteralPath $historyDirectory -Filter "*.json" |
    Where-Object BaseName -Match "^\d{4}$" | Sort-Object BaseName)
$historicalDrivers = @{}
$historicalTeams = @{}
$historicalCircuits = @{}
$historicalRoundCount = 0
foreach ($file in $historyFiles) {
    $season = Read-Json -Path $file.FullName
    $year = [int]$season.year
    $resolved = Resolve-LogicalAsset -Folder "history-art" -Key $year.ToString([Globalization.CultureInfo]::InvariantCulture)
    Add-ContentRow @{
        ContentId = "historical-season:$year"
        StableId = "historical-season:$year"
        ContentType = "HISTORY"
        ContentSubtype = "VERIFIED_HISTORICAL_SEASON"
        SourceKind = "SEEDED"
        SeasonId = "f1-$year"
        UniverseYearOrDate = $year
        Title = "$year - $($season.driversChampion.driver) / $($season.constructorsChampion.team)"
        ContentProvenance = "verifiedHistorical"
        ExpectedAssetId = "history-season:$year"
        ConfiguredAssetPath = "data/ams2/history-art/$year.(jpg|jpeg|png)"
        ResolvedPhysicalFile = $resolved
        CurrentFallbackPath = "History season panel omits the image"
        UiSurfaces = "History real-season card; What really happened panel; PhotoWindow"
        Ships = $true
        ReachableInUi = $true
        CurrentAuditStatus = $(if ($resolved.Length -eq 0) { "MISSING_FILE" } else { Get-StatusForResolvedAsset -LogicalPath $resolved })
        Severity = $(if ($resolved.Length -eq 0) { "P1" } else { "P3" })
        RecommendedAction = "Stage B: provide a sourced or clearly illustrated season-specific image with documented provenance and safe History crops."
        NewArtRequired = $true
        Notes = "All 60 verified seasons expose this explicit optional image slot. The tracked history-art folder currently contains no images."
        SourceFile = Convert-ToRepoPath -Path $file.FullName
        SourceLocator = "season root"
    }

    foreach ($roundRecord in @($season.rounds)) {
        $historicalRoundCount++
        $round = [int]$roundRecord.round
        $layoutId = [string]$roundRecord.circuit.layoutId
        if (-not $historicalCircuits.ContainsKey($layoutId)) {
            $historicalCircuits[$layoutId] = [string]$roundRecord.circuit.name
        }
        foreach ($result in @($roundRecord.results)) {
            if ($null -ne $result.driver -and -not $historicalDrivers.ContainsKey([string]$result.driver)) {
                $historicalDrivers[[string]$result.driver] = $true
            }
            if ($null -ne $result.team -and -not $historicalTeams.ContainsKey([string]$result.team)) {
                $historicalTeams[[string]$result.team] = $true
            }
        }
        Add-ContentRow @{
            ContentId = "historical-race:${year}:$round"
            StableId = "historical-race:${year}:$round"
            ContentType = "HISTORY"
            ContentSubtype = "VERIFIED_HISTORICAL_RACE"
            SourceKind = "SEEDED"
            SeasonId = "f1-$year"
            UniverseYearOrDate = $year
            RaceOrEventId = "round:$round"
            Title = [string]$roundRecord.name
            ContentProvenance = "verifiedHistorical"
            ExpectedAssetId = ""
            ConfiguredAssetPath = ""
            CurrentFallbackPath = "Vector circuit map and results table by current design"
            UiSurfaces = "History real-season round expander; circuit/entity archive"
            Ships = $true
            ReachableInUi = $true
            DecodeStatus = "INTENTIONALLY_IMAGELESS"
            BuildCopyStatus = "NOT_APPLICABLE"
            PublishPackageStatus = "NOT_APPLICABLE"
            CurrentAuditStatus = "INTENTIONALLY_IMAGELESS"
            Severity = "P4"
            RecommendedAction = "Owner review in Stage B: retain the vector-first race record or authorize race-specific editorial images."
            NewArtRequired = $false
            Notes = "No raster editorial slot exists. This is not counted as a broken reference."
            SourceFile = Convert-ToRepoPath -Path $file.FullName
            SourceLocator = "rounds[round=$round]"
        }
    }
}

foreach ($driverName in $historicalDrivers.Keys | Sort-Object) {
    Add-ContentRow @{
        ContentId = "history-driver:$((Get-Slug -Value $driverName))"
        StableId = "history-driver:$driverName"
        ContentType = "HISTORY"
        ContentSubtype = "COMPUTED_DRIVER_PROFILE"
        SourceKind = "GENERATED"
        SeasonId = "shared"
        DriverId = $driverName
        Title = $driverName
        ContentProvenance = "verifiedHistorical"
        CurrentFallbackPath = "Text/stat table by current design"
        UiSurfaces = "History driver encyclopedia; unified archive search"
        Ships = $true
        ReachableInUi = $true
        DecodeStatus = "INTENTIONALLY_IMAGELESS"
        BuildCopyStatus = "NOT_APPLICABLE"
        PublishPackageStatus = "NOT_APPLICABLE"
        CurrentAuditStatus = "INTENTIONALLY_IMAGELESS"
        Severity = "P4"
        RecommendedAction = "Owner review in Stage B: keep text/stat-first or add licensed/illustrated driver history art."
        NewArtRequired = $false
        Notes = "Computed from the 60 verified season files; no stable raster art field exists."
        SourceFile = "data/history/*.json"
        SourceLocator = "computed driver entity"
    }
}

foreach ($teamName in $historicalTeams.Keys | Sort-Object) {
    Add-ContentRow @{
        ContentId = "history-team:$((Get-Slug -Value $teamName))"
        StableId = "history-team:$teamName"
        ContentType = "HISTORY"
        ContentSubtype = "COMPUTED_TEAM_PROFILE"
        SourceKind = "GENERATED"
        SeasonId = "shared"
        TeamId = $teamName
        Title = $teamName
        ContentProvenance = "verifiedHistorical"
        CurrentFallbackPath = "Text/stat table by current design"
        UiSurfaces = "History team encyclopedia; unified archive search"
        Ships = $true
        ReachableInUi = $true
        DecodeStatus = "INTENTIONALLY_IMAGELESS"
        BuildCopyStatus = "NOT_APPLICABLE"
        PublishPackageStatus = "NOT_APPLICABLE"
        CurrentAuditStatus = "INTENTIONALLY_IMAGELESS"
        Severity = "P4"
        RecommendedAction = "Owner review in Stage B: keep text/stat-first or add licensed/illustrated team history art."
        NewArtRequired = $false
        Notes = "Computed from the 60 verified season files; no stable raster art field exists."
        SourceFile = "data/history/*.json"
        SourceLocator = "computed team entity"
    }
}

foreach ($layoutId in $historicalCircuits.Keys | Sort-Object) {
    Add-ContentRow @{
        ContentId = "history-circuit:$layoutId"
        StableId = "history-circuit:$layoutId"
        ContentType = "HISTORY"
        ContentSubtype = "COMPUTED_CIRCUIT_PROFILE"
        SourceKind = "GENERATED"
        SeasonId = "shared"
        RaceOrEventId = $layoutId
        Title = [string]$historicalCircuits[$layoutId]
        ContentProvenance = "verifiedHistorical"
        CurrentFallbackPath = "Vector circuit map and text/stat table by current design"
        UiSurfaces = "History circuit encyclopedia; unified archive search"
        Ships = $true
        ReachableInUi = $true
        DecodeStatus = "INTENTIONALLY_IMAGELESS"
        BuildCopyStatus = "NOT_APPLICABLE"
        PublishPackageStatus = "NOT_APPLICABLE"
        CurrentAuditStatus = "INTENTIONALLY_IMAGELESS"
        Severity = "P4"
        RecommendedAction = "Owner review in Stage B: retain vector maps or add sourced/illustrated circuit history art."
        NewArtRequired = $false
        Notes = "Computed from the 60 verified season files; no editorial raster field exists."
        SourceFile = "data/history/*.json"
        SourceLocator = "computed circuit entity"
    }
}

$erasPath = Join-Path $historyDirectory "eras.json"
$erasJson = Read-Json -Path $erasPath
foreach ($era in @($erasJson.eras) | Sort-Object fromYear) {
    $key = [string]$era.key
    $fromYear = [int]$era.fromYear
    $resolved = Resolve-LogicalAsset -Folder "era-art" -Key $fromYear.ToString([Globalization.CultureInfo]::InvariantCulture)
    if ($resolved.Length -eq 0) {
        $medium = if ($fromYear -le 1979) { "telegram" } elseif ($fromYear -le 1993) { "fax" } else { "email" }
        $resolved = Resolve-LogicalAsset -Folder "era-art" -Key $medium
    }
    $status = if ($resolved.Length -eq 0) { "MISSING_FILE" }
        else { "GENERIC_FALLBACK|UNINTENTIONAL_REUSE" + $(if ($assetByLogicalPath[$resolved].ProvenanceStatus -eq "PROVENANCE_REVIEW") { "|PROVENANCE_REVIEW" } else { "" }) }
    Add-ContentRow @{
        ContentId = "history-era:$key"
        StableId = $key
        ContentType = "HISTORY"
        ContentSubtype = "AUTHORED_ERA"
        SourceKind = "AUTHORED"
        SeasonId = "shared"
        UniverseYearOrDate = "$($era.fromYear)-$($era.toYear)"
        Title = [string]$era.name
        ContentProvenance = "verifiedHistorical"
        ExpectedAssetId = "history-era:$key"
        ConfiguredAssetPath = "dynamic era-art by YearsLabel/start year"
        ResolvedPhysicalFile = $resolved
        CurrentFallbackPath = "Era medium image shared across multiple archive eras"
        UiSurfaces = "History featured era; era browser"
        Ships = $true
        ReachableInUi = $true
        CurrentAuditStatus = $status
        Severity = $(if ($resolved.Length -eq 0) { "P1" } else { "P2" })
        RecommendedAction = "Stage B: create a dedicated era image keyed by era ID, with documented source/provenance and History-safe crop."
        NewArtRequired = $true
        Notes = "The current converter infers a year from display text and can reuse a generic medium image; there is no era-ID art registry."
        SourceFile = Convert-ToRepoPath -Path $erasPath
        SourceLocator = "eras[key=$key]"
    }
}

$subjectsPath = Join-Path $historyDirectory "subjects.json"
$subjectsJson = Read-Json -Path $subjectsPath
foreach ($subject in @($subjectsJson.subjects) | Sort-Object id) {
    Add-ContentRow @{
        ContentId = "history-subject:$($subject.id)"
        StableId = [string]$subject.id
        ContentType = "HISTORY"
        ContentSubtype = "AUTHORED_HISTORY_SUBJECT"
        SourceKind = "AUTHORED"
        SeasonId = "shared"
        UniverseYearOrDate = "$($subject.fromYear)-$($subject.toYear)"
        RaceOrEventId = [string]$subject.category
        Title = [string]$subject.title
        ContentProvenance = [string]$subject.provenance
        CurrentFallbackPath = "Text-only subject card by current design"
        UiSurfaces = "History subject browser; verified timeline; unified archive search"
        Ships = $true
        ReachableInUi = $true
        DecodeStatus = "INTENTIONALLY_IMAGELESS"
        BuildCopyStatus = "NOT_APPLICABLE"
        PublishPackageStatus = "NOT_APPLICABLE"
        CurrentAuditStatus = "INTENTIONALLY_IMAGELESS"
        Severity = "P4"
        RecommendedAction = "Owner review in Stage B: keep source-led text presentation or add clearly licensed editorial illustration."
        NewArtRequired = $false
        Notes = "No raster art field exists; this is not counted as broken."
        SourceFile = Convert-ToRepoPath -Path $subjectsPath
        SourceLocator = "subjects[id=$($subject.id)]"
    }
}

# Manifest references keep embedded track-banner masters from being misclassified as orphans.
$trackManifestPath = Join-Path $RepoRoot "src\Companion.App\Assets\TrackBanners\manifest.json"
if (Test-Path -LiteralPath $trackManifestPath -PathType Leaf) {
    $manifest = Read-Json -Path $trackManifestPath
    foreach ($property in $manifest.tracks.PSObject.Properties) {
        $value = [string]$property.Value
        $logical = "src/Companion.App/Assets/TrackBanners/" + $value.TrimStart("/", "\").Replace("\", "/")
        if ($assetByLogicalPath.ContainsKey($logical)) {
            $assetByLogicalPath[$logical].ReferenceCount = [int]$assetByLogicalPath[$logical].ReferenceCount + 1
        }
    }
}

# Dynamic conventions used outside a single finite content row still count as known references.
foreach ($row in $physicalRows) {
    if ([int]$row.ReferenceCount -eq 0) {
        $fileName = [IO.Path]::GetFileNameWithoutExtension([string]$row.Path)
        if ($row.Category -eq "portraits" -and $fileName.StartsWith("player.", [StringComparison]::Ordinal)) {
            $row.ReferenceCount = 1
            $row.Notes = (($row.Notes + " Dynamic player portrait convention.").Trim())
        }
        elseif ($row.Category -eq "cars" -and $fileName.StartsWith("driver.", [StringComparison]::Ordinal)) {
            $row.ReferenceCount = 1
            $row.Notes = (($row.Notes + " Dynamic driver car-art convention.").Trim())
        }
        elseif ($row.Category -eq "era-art") {
            $row.ReferenceCount = 1
            $row.Notes = (($row.Notes + " Dynamic year/medium resolver convention.").Trim())
        }
        elseif ($row.Category -eq "era-textures") {
            $row.ReferenceCount = 1
            $row.Notes = (($row.Notes + " Referenced by the News era-theme resource dictionaries.").Trim())
        }
    }
    $row.OrphanStatus = if ([int]$row.ReferenceCount -gt 0) { "REFERENCED" } else { "ORPHAN_ASSET" }
}

# Reuse counts are mapping counts, not physical-file counts.
$mappingByFile = $contentRows | Where-Object { $_.ResolvedPhysicalFile.Length -gt 0 } |
    Group-Object ResolvedPhysicalFile
foreach ($group in $mappingByFile) {
    foreach ($row in $group.Group) {
        $row.ExactReuseCount = $group.Count
    }
}
$mappingByNear = $contentRows | Where-Object { $_.NearDuplicateGroup.Length -gt 0 } |
    Group-Object NearDuplicateGroup
foreach ($group in $mappingByNear) {
    foreach ($row in $group.Group) {
        $row.NearDuplicateReuseCount = $group.Count
    }
}

$contentRowsSorted = @($contentRows | Sort-Object ContentType, ContentSubtype, SeasonNumber, UniverseYearOrDate, StableId)
$physicalRowsSorted = @($physicalRows | Sort-Object Category, Path)

$contentCsvPath = Join-Path $OutputDirectory "news_history_art_inventory.csv"
$contentJsonPath = Join-Path $OutputDirectory "news_history_art_inventory.json"
$physicalCsvPath = Join-Path $OutputDirectory "news_history_physical_asset_inventory.csv"
$summaryPath = Join-Path $OutputDirectory "news_history_art_audit_summary.json"

Write-Utf8Lines -Path $contentCsvPath -Lines @($contentRowsSorted | ConvertTo-Csv -NoTypeInformation)
Write-Utf8Text -Path $contentJsonPath -Text ($contentRowsSorted | ConvertTo-Json -Depth 8)
Write-Utf8Lines -Path $physicalCsvPath -Lines @($physicalRowsSorted | ConvertTo-Csv -NoTypeInformation)

$newsEventKinds = Get-EnumNames -Path (Join-Path $RepoRoot "src\Companion.Core\Newsroom\NewsEvent.cs") -EnumName "NewsEventKind"
$newsRows = @($contentRowsSorted | Where-Object ContentType -eq "NEWS")
$historyRows = @($contentRowsSorted | Where-Object ContentType -eq "HISTORY")
$missingFileRows = @($contentRowsSorted | Where-Object CurrentAuditStatus -Match "(^|\|)MISSING_FILE(\||$)")
$missingFieldRows = @($contentRowsSorted | Where-Object CurrentAuditStatus -Match "(^|\|)MISSING_ASSET_FIELD(\||$)")
$brokenRows = @($contentRowsSorted | Where-Object CurrentAuditStatus -Match "(BROKEN_URI|WRONG_CASE_PATH|CORRUPT_OR_UNREADABLE|SOURCE_ONLY_NOT_PACKAGED|PACKAGED_BUT_NOT_LOADABLE)")
$genericRows = @($contentRowsSorted | Where-Object CurrentAuditStatus -Match "(^|\|)GENERIC_FALLBACK(\||$)")
$explicitPlaceholderRows = @($contentRowsSorted | Where-Object CurrentAuditStatus -Match "(^|\|)EXPLICIT_PLACEHOLDER(\||$)")
$suspectedPlaceholderAssets = @($physicalRowsSorted | Where-Object SuspectedPlaceholderStatus -eq "SUSPECTED_PLACEHOLDER")
$packagingFailures = @($physicalRowsSorted | Where-Object {
    $_.PublishPackageStatus -in @("MISSING_FROM_PUBLISH", "DIFFERS_FROM_SOURCE", "PUBLISH_EXE_MISSING")
})
$provenanceAssets = @($physicalRowsSorted | Where-Object ProvenanceStatus -eq "PROVENANCE_REVIEW")
$formatMismatchAssets = @($physicalRowsSorted | Where-Object FormatMatchesExtension -eq $false)
$exactGroups = @($physicalRowsSorted | Where-Object ExactDuplicateGroup -ne "" |
    Select-Object -ExpandProperty ExactDuplicateGroup -Unique)
$nearGroupsCounted = @($physicalRowsSorted | Where-Object NearDuplicateGroup -ne "" |
    Select-Object -ExpandProperty NearDuplicateGroup -Unique)
$validUniqueHashes = @($physicalRowsSorted | Where-Object {
    $_.DecodeStatus -eq "OK" -and $_.OrphanStatus -eq "REFERENCED" -and
    $_.SuspectedPlaceholderStatus -ne "SUSPECTED_PLACEHOLDER" -and
    $_.PublishPackageStatus -notin @("MISSING_FROM_PUBLISH", "DIFFERS_FROM_SOURCE", "PUBLISH_EXE_MISSING")
} | Select-Object -ExpandProperty Sha256 -Unique)

$seasonMatrix = [Collections.Generic.List[object]]::new()
foreach ($season in @($smgpSeasonsJson.seasons) | Sort-Object ordinal) {
    $ordinal = [int]$season.ordinal
    $rows = @($contentRowsSorted | Where-Object SeasonNumber -eq $ordinal)
    $validMappings = @($rows | Where-Object {
        $_.ResolvedPhysicalFile.Length -gt 0 -and
        $_.CurrentAuditStatus -notmatch "(MISSING|BROKEN|CORRUPT|GENERIC_FALLBACK|UNKNOWN_REQUIRES_REVIEW|WRONG_)"
    })
    $uniqueHashes = @($rows | Where-Object Sha256 -ne "" | Select-Object -ExpandProperty Sha256 -Unique)
    $seasonMatrix.Add([pscustomobject][ordered]@{
        SeasonNumber = $ordinal
        SeasonId = "ordinal:$ordinal"
        ProductionPackId = "smgp-1"
        UniverseYear = 1989 + $ordinal
        Title = [string]$season.title
        NewsRecords = @($rows | Where-Object ContentType -eq "NEWS").Count
        AuthoredNewsRecords = @($rows | Where-Object {
            $_.ContentType -eq "NEWS" -and $_.SourceKind -in @("AUTHORED", "SEEDED")
        }).Count
        GeneratedNewsTemplates = 0
        HistoryEntries = @($rows | Where-Object ContentType -eq "HISTORY").Count
        UniqueExpectedArtSlots = @($rows | Where-Object ExpectedAssetId -ne "").Count
        ValidUniqueImages = $uniqueHashes.Count
        ApprovedIntentionalReuseMappings = @($rows | Where-Object CurrentAuditStatus -Match "APPROVED_INTENTIONAL_REUSE").Count
        GenericFallbackMappings = @($rows | Where-Object CurrentAuditStatus -Match "GENERIC_FALLBACK").Count
        ExplicitPlaceholderMappings = @($rows | Where-Object CurrentAuditStatus -Match "EXPLICIT_PLACEHOLDER").Count
        MissingFiles = @($rows | Where-Object CurrentAuditStatus -Match "MISSING_FILE").Count
        BrokenReferences = @($rows | Where-Object CurrentAuditStatus -Match "(BROKEN_URI|WRONG_CASE_PATH|CORRUPT_OR_UNREADABLE)").Count
        PublishFailures = @($rows | Where-Object PublishPackageStatus -Match "(MISSING|DIFFERS)").Count
        ResolutionOrAspectProblems = @($rows | Where-Object CurrentAuditStatus -Match "(LOW_RESOLUTION|INCORRECT_ASPECT_RATIO|POOR_UI_CROP)").Count
        ExactOrNearDuplicateMappings = @($rows | Where-Object {
            $_.ExactDuplicateGroup.Length -gt 0 -or $_.NearDuplicateGroup.Length -gt 0
        }).Count
        NewArtworkRequired = @($rows | Where-Object NewArtRequired -eq $true).Count
        CompleteCoveragePercent = if ($rows.Count -eq 0) { 0.0 } else {
            [Math]::Round(($validMappings.Count / [double]$rows.Count) * 100.0, 1)
        }
    })
}

$seasonBoundRows = @($contentRowsSorted | Where-Object { [int]$_.SeasonNumber -ge 1 -and [int]$_.SeasonNumber -le 17 })
$sharedOrNonSeasonRows = @($contentRowsSorted | Where-Object { [int]$_.SeasonNumber -lt 1 -or [int]$_.SeasonNumber -gt 17 })

$summary = [pscustomobject][ordered]@{
    SchemaVersion = 1
    MissionId = "SMGP-NEWS-HISTORY-ART-001"
    Mode = "AUDIT_ONLY"
    GeneratedUtc = "NOT_RECORDED_DETERMINISTIC_OUTPUT"
    Definitions = [pscustomobject][ordered]@{
        NewsRecord = "One finite production template/body/dispatch/thread art requirement. No runtime article instances are stored in the repository."
        ProceduralArticleFamily = "One NewsEventKind, one unique legacy body key, or one SMGP dispatch template family."
        MissingAsset = "A configured or explicitly expected shipping art slot whose file does not exist."
        BrokenReference = "A configured reference that is corrupt, wrong-case, URI-invalid, or excluded/unloadable after packaging. Missing files are reported separately."
        ValidUniqueAsset = "A unique SHA-256 image that decodes, has a known reference, has no placeholder signal, and is not missing/mismatched in a checked publish. This is not a provenance or editorial-quality approval."
    }
    Counts = [pscustomobject][ordered]@{
        NewsRecordsAudited = $newsRows.Count
        HistoryRecordsAudited = $historyRows.Count
        NewsroomTemplates = $newsroomTemplateCount
        NewsroomEventKinds = $newsEventKinds.Count
        NewsroomEventKindsWithTemplates = $newsroomEventNames.Count
        LegacyBodyMappings = $legacyBodyRows
        LegacyBodyFamilies = $legacyBodyFamilyNames.Count
        FrozenHeadlineFamilies = $headlineFamilyNames.Count
        FrozenHeadlineVariants = $headlineVariantCount
        LegacyHeadlineOnlyFamilies = $headlineOnlyFamilyCount
        LegacyArticleFamilies = $legacyArticleFamilyNames.Count
        SmgpDispatchFamilies = $dispatchFamilyCount
        StoryThreadFamilies = $threadTypes.Count
        ProceduralArticleFamilies = $newsEventKinds.Count + $legacyArticleFamilyNames.Count + $dispatchFamilyCount
        PhysicalArtFilesAudited = $physicalRowsSorted.Count
        ValidUniqueAssets = $validUniqueHashes.Count
        MissingFiles = $missingFileRows.Count
        MissingAssetFields = $missingFieldRows.Count
        BrokenReferences = $brokenRows.Count
        ExplicitPlaceholderMappings = $explicitPlaceholderRows.Count
        SuspectedPlaceholderAssets = $suspectedPlaceholderAssets.Count
        GenericFallbackMappings = $genericRows.Count
        ExactDuplicateGroups = $exactGroups.Count
        NearDuplicateGroups = $nearGroupsCounted.Count
        PackagingFailures = $packagingFailures.Count
        ProvenanceOrLicensingReviewAssets = $provenanceAssets.Count
        FormatExtensionMismatches = $formatMismatchAssets.Count
        NewsMappingsRequiringNewArt = @($newsRows | Where-Object NewArtRequired -eq $true).Count
        HistoryMappingsRequiringNewOrRevisedArt = @($historyRows | Where-Object NewArtRequired -eq $true).Count
        VerifiedHistorySeasons = $historyFiles.Count
        VerifiedHistoryRounds = $historicalRoundCount
        VerifiedHistoryDrivers = $historicalDrivers.Count
        VerifiedHistoryTeams = $historicalTeams.Count
        VerifiedHistoryCircuits = $historicalCircuits.Count
        SmgpSeasons = @($smgpSeasonsJson.seasons).Count
        SmgpRaceHistorySlots = 17 * 16
        SmgpCareerBeatFamilies = $beatKinds.Count
    }
    SeasonMatrix = @($seasonMatrix)
    RowReconciliation = [pscustomobject][ordered]@{
        AllContentRows = $contentRowsSorted.Count
        SeasonMatrixRows = $seasonBoundRows.Count
        SharedOrNonSeasonRows = $sharedOrNonSeasonRows.Count
        SeasonMatrixNewsRows = @($seasonBoundRows | Where-Object ContentType -eq "NEWS").Count
        SeasonMatrixHistoryRows = @($seasonBoundRows | Where-Object ContentType -eq "HISTORY").Count
        SharedOrNonSeasonNewsRows = @($sharedOrNonSeasonRows | Where-Object ContentType -eq "NEWS").Count
        SharedOrNonSeasonHistoryRows = @($sharedOrNonSeasonRows | Where-Object ContentType -eq "HISTORY").Count
    }
    OutputFiles = @(
        Convert-ToRepoPath -Path $contentCsvPath
        Convert-ToRepoPath -Path $contentJsonPath
        Convert-ToRepoPath -Path $physicalCsvPath
        Convert-ToRepoPath -Path $summaryPath
    )
}
Write-Utf8Text -Path $summaryPath -Text ($summary | ConvertTo-Json -Depth 8)

Write-Host ("News rows: {0}; History rows: {1}; Physical assets: {2}" -f
    $newsRows.Count, $historyRows.Count, $physicalRowsSorted.Count)
Write-Host ("Missing files: {0}; missing art fields: {1}; broken refs: {2}; packaging failures: {3}" -f
    $missingFileRows.Count, $missingFieldRows.Count, $brokenRows.Count, $packagingFailures.Count)
Write-Host ("Inventories written to {0}" -f $OutputDirectory)

$releaseBlockingCount = $missingFileRows.Count + $brokenRows.Count + $packagingFailures.Count
if (-not $AllowIssues -and $releaseBlockingCount -gt 0) {
    Write-Error ("Audit found {0} missing/broken/packaging issue mappings. See {1}." -f
        $releaseBlockingCount, $summaryPath)
    exit 1
}

exit 0
