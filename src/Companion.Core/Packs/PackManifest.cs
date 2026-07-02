namespace Companion.Core.Packs;

/// <summary>
/// pack.json — identity, version, requirements, licensing/attribution. Packs REFERENCE
/// OverTake skin packs by name/URL; they never ship textures.
/// </summary>
public sealed record PackManifest
{
    /// <summary>Folder-name id under Documents\AMS2CareerCompanion\Packs\ (e.g. "f1-1967").</summary>
    public required string PackId { get; init; }

    public required string Name { get; init; }

    /// <summary>Pack author version (e.g. "1.0.0").</summary>
    public required string Version { get; init; }

    /// <summary>Pack format version this file conforms to; v1 is the current contract.</summary>
    public required int FormatVersion { get; init; }

    /// <summary>AMS2 version the author verified against (e.g. "1.6.9.8").</summary>
    public string? GameVersionTested { get; init; }

    public string? License { get; init; }

    public IReadOnlyList<string> Attribution { get; init; } = [];

    public PackRequirements Requires { get; init; } = new();

    /// <summary>Author/generator free-text notes (v1.1, optional, additive): entrant-coverage
    /// caveats, authored data corrections, and mapping notes that belong to the pack as a whole
    /// rather than a single round. Absent in v1 packs.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
}

public sealed record PackRequirements
{
    /// <summary>Steam DLC names needed for the pack's cars/tracks.</summary>
    public IReadOnlyList<string> Dlc { get; init; } = [];

    public IReadOnlyList<PackSkinPackRequirement> SkinPacks { get; init; } = [];
}

/// <summary>A referenced community skin pack the liveries bind against.</summary>
public sealed record PackSkinPackRequirement
{
    public required string Name { get; init; }

    public string? Url { get; init; }

    /// <summary>Folder name under CustomLiveries\Overrides\&lt;vehicle&gt;\ once installed.</summary>
    public string? OverridesFolder { get; init; }
}
