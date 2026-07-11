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

    /// <summary>Skin-season key (v1.2, optional, additive): which
    /// <c>data/ams2/skin-seasons/&lt;key&gt;/</c> pointer set this pack's liveries belong to.
    /// Two season skin packs for the same car model collide on the model's active override XML
    /// (1983↔1985, 1990↔SMGP, 1996↔1997…); when set, career load/staging asks the Skin Season
    /// Manager to make this season's pointers active (backup-first). Null = no managed season
    /// (the pack's skins own their models outright). Staging-side only — the sim never reads it.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? SkinSeason { get; init; }

    /// <summary>Career style (v1.3, optional, additive): a replica-mode key gating special career
    /// mechanics — <c>"smgp"</c> = the Super Monaco GP mode (rival battles, two-wins seat swaps,
    /// the Ceara title defense). Null/unknown = a normal historical season; a pack with a style
    /// the app does not implement still plays as a normal season (the mode machinery is additive
    /// and determinism-gated per career).</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? CareerStyle { get; init; }

    /// <summary>OPT-IN modded field (v1.4, optional, additive): extra grid entries a COMMUNITY CAR
    /// MOD adds to round the season out — the SMGP pack's two McLaren MP4/5B teams (Iris, Azalea)
    /// by Kobra Fleetworks. Gated exactly like <c>track.alternate</c>: the entries + a per-round
    /// grid-size bump apply at CAREER CREATION only when the player ticks it on AND the required
    /// vehicle mod is installed; off/absent = the base field only (no mod dependency). Null = a
    /// pack with no modded field (every other pack, byte-identical).</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public PackModdedField? ModdedField { get; init; }
}

/// <summary>An opt-in, install-gated set of extra grid entries a community CAR MOD provides
/// (<see cref="PackManifest.ModdedField"/>). The teams + drivers these entries reference live in
/// the pack's teams.json/drivers.json already (inert without an entry); the creation-time
/// transform appends the entries and bumps each round's grid size when the mod is present.</summary>
public sealed record PackModdedField
{
    /// <summary>The AMS2 vehicle id the mod adds (e.g. <c>mclaren_mp45b</c>) — the install check
    /// verifies it is present in the extracted content library.</summary>
    public required string VehicleId { get; init; }

    /// <summary>Human name of the mod + author, for the wizard tick ("SMGP Iris &amp; Azalea (Kobra
    /// Fleetworks)").</summary>
    public required string ModName { get; init; }

    public string? Url { get; init; }

    /// <summary>The extra entries (each referencing a team + driver already in the pack).</summary>
    public required IReadOnlyList<PackModdedEntry> Entries { get; init; }
}

/// <summary>One modded grid entry (<see cref="PackModdedField.Entries"/>) — the same shape as a
/// normal <c>entries.json</c> row; the transform appends it verbatim when the mod applies.</summary>
public sealed record PackModdedEntry
{
    public required string TeamId { get; init; }
    public required string DriverId { get; init; }
    public required string Number { get; init; }
    public required string Rounds { get; init; }
    public required string Ams2LiveryName { get; init; }
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
