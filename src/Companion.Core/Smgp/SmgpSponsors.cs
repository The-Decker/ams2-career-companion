using System.Text.Json.Serialization;
using Companion.Core.Json;

namespace Companion.Core.Smgp;

/// <summary>
/// The SMGP-universe SPONSOR board: fictional SEGA-world brands (a title watch-house on the crown, a
/// struggling local shop clinging to the floor team, series-wide fuel/tyre giants) with their own story,
/// tagline, industry, tier, brand colour and the teams they back. Loaded from
/// <c>data/rules/smgp/sponsors.json</c>. DISPLAY-ONLY, never a fold input (like the team profiles / rival
/// quotes / almanac): shown on the Paddock's Sponsors tab, and the seed of the future Tycoon mode. An
/// absent file resolves to <see cref="Empty"/>, so a non-SMGP install is simply unaffected.
/// </summary>
public sealed class SmgpSponsors
{
    private readonly IReadOnlyList<SmgpSponsor> _all;
    private readonly IReadOnlyDictionary<string, SmgpSponsor> _byId;

    private SmgpSponsors(IReadOnlyList<SmgpSponsor> all)
    {
        _all = all;
        var byId = new Dictionary<string, SmgpSponsor>(StringComparer.Ordinal);
        foreach (var s in all)
            byId.TryAdd(s.Id, s);
        _byId = byId;
    }

    /// <summary>An empty board (no file shipped): <see cref="All"/> is empty, so the Paddock omits the tab.</summary>
    public static SmgpSponsors Empty { get; } = new([]);

    /// <summary>Every sponsor, in authored order (the board's display order).</summary>
    public IReadOnlyList<SmgpSponsor> All => _all;

    /// <summary>A sponsor by id, or null.</summary>
    public SmgpSponsor? ById(string id) => _byId.GetValueOrDefault(id);

    /// <summary>The sponsors that back a given team id, in board order (empty when none).</summary>
    public IReadOnlyList<SmgpSponsor> ForTeam(string teamId) =>
        _all.Where(s => s.Teams.Contains(teamId, StringComparer.Ordinal)).ToArray();

    /// <summary>Loads <c>data/rules/smgp/sponsors.json</c> from the rules directory, or <see cref="Empty"/>.</summary>
    public static SmgpSponsors Load(string rulesDirectory)
    {
        string path = Path.Combine(rulesDirectory, "smgp", "sponsors.json");
        return File.Exists(path) ? Parse(File.ReadAllText(path)) : Empty;
    }

    public static SmgpSponsors Parse(string json)
    {
        var dto = System.Text.Json.JsonSerializer.Deserialize<SponsorsDto>(json, CoreJson.Options)
            ?? new SponsorsDto();
        var all = dto.Sponsors
            .Where(s => s is not null && !string.IsNullOrEmpty(s.Id))
            .Select(s => s!)
            .ToArray();
        return new SmgpSponsors(all);
    }

    private sealed record SponsorsDto
    {
        [JsonPropertyName("sponsors")]
        public IReadOnlyList<SmgpSponsor?> Sponsors { get; init; } = [];
    }
}

/// <summary>One fictional SMGP-world sponsor. Display-only reference content (never a fold input); its
/// <c>logo</c> art is drop-in at <c>data/ams2/smgp/sponsors/&lt;id-without-prefix&gt;.png</c>.</summary>
public sealed record SmgpSponsor
{
    /// <summary>Stable id, "sponsor.&lt;kebab-name&gt;".</summary>
    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    /// <summary>Short industry label ("Haute Horlogerie", "Energy Drinks").</summary>
    public string Industry { get; init; } = "";

    /// <summary>Backing tier: title / major / minor / struggling.</summary>
    public string Tier { get; init; } = "";

    /// <summary>An ALL-CAPS arcade slogan.</summary>
    public string Tagline { get; init; } = "";

    /// <summary>2-3 paragraphs of SMGP-world lore.</summary>
    public IReadOnlyList<string> Story { get; init; } = [];

    /// <summary>Brand colour "#RRGGBB" (for accenting the sponsor's card).</summary>
    public string BrandColorHex { get; init; } = "";

    /// <summary>The exact team ids this sponsor backs.</summary>
    public IReadOnlyList<string> Teams { get; init; } = [];

    /// <summary>A one-line "since"/origin flavour.</summary>
    public string FoundedFlavor { get; init; } = "";
}
