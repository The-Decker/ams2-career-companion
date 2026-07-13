using System.Text.Json.Serialization;
using Companion.Core.Json;

namespace Companion.Core.Smgp;

/// <summary>
/// SMGP-universe driver biographies (a short arcade epithet + a ~3-paragraph biography + in-world
/// quotes), keyed by DRIVER id (e.g. "driver.ayrton_senna"). Loaded from
/// <c>data/rules/smgp/driver-profiles.json</c>. DISPLAY-ONLY — never a fold input (exactly like
/// <see cref="SmgpTeamProfiles"/> / <see cref="SmgpRivalQuotes"/>): shown on the Paddock driver-preview
/// tab. Fully fictional (the SEGA universe, never real F1). An absent file (or an un-authored driver)
/// resolves to null, so a non-SMGP install or an un-updated data folder is simply unaffected.
/// </summary>
public sealed class SmgpDriverProfiles
{
    private readonly IReadOnlyDictionary<string, SmgpDriverProfile> _byDriver;

    private SmgpDriverProfiles(IReadOnlyDictionary<string, SmgpDriverProfile> byDriver) => _byDriver = byDriver;

    /// <summary>An empty catalog (no file shipped): every lookup returns null and <see cref="Drivers"/>
    /// is empty, so the Paddock tab simply shows the roster without bios.</summary>
    public static SmgpDriverProfiles Empty { get; } =
        new(new Dictionary<string, SmgpDriverProfile>(StringComparer.Ordinal));

    /// <summary>This driver's SMGP-world profile, or null when none is authored for them.</summary>
    public SmgpDriverProfile? ForDriver(string driverId) => _byDriver.GetValueOrDefault(driverId);

    /// <summary>The driver ids the catalog has an authored profile for (drift-guard source).</summary>
    public IReadOnlyCollection<string> Drivers => _byDriver.Keys.ToArray();

    /// <summary>Loads <c>data/rules/smgp/driver-profiles.json</c> from the rules directory, or
    /// <see cref="Empty"/> when the file is absent.</summary>
    public static SmgpDriverProfiles Load(string rulesDirectory)
    {
        string path = Path.Combine(rulesDirectory, "smgp", "driver-profiles.json");
        return File.Exists(path) ? Parse(File.ReadAllText(path)) : Empty;
    }

    public static SmgpDriverProfiles Parse(string json)
    {
        var dto = System.Text.Json.JsonSerializer.Deserialize<ProfilesDto>(json, CoreJson.Options)
            ?? new ProfilesDto();
        var byDriver = new Dictionary<string, SmgpDriverProfile>(StringComparer.Ordinal);
        foreach (var profile in dto.Drivers)
            if (profile is { DriverId.Length: > 0 })
                byDriver[profile.DriverId] = profile;
        return new SmgpDriverProfiles(byDriver);
    }

    private sealed record ProfilesDto
    {
        [JsonPropertyName("drivers")]
        public IReadOnlyList<SmgpDriverProfile?> Drivers { get; init; } = [];
    }
}

/// <summary>One SMGP-world driver's profile — display-only reference content for the Paddock tab.
/// Fully fictional (the SEGA universe); names may echo real F1 but the people are invented.</summary>
public sealed record SmgpDriverProfile
{
    /// <summary>The driver id this profile is for ("driver.ayrton_senna").</summary>
    public string DriverId { get; init; } = "";

    /// <summary>The driver's display name ("Ayrton Senna").</summary>
    public string Name { get; init; } = "";

    /// <summary>A short ALL-CAPS arcade epithet ("THE UNTOUCHABLE KING").</summary>
    public string Epithet { get; init; } = "";

    /// <summary>The driver's SMGP-world biography — a few paragraphs (Mike: ~3).</summary>
    public IReadOnlyList<string> Bio { get; init; } = [];

    /// <summary>One or two in-character quotes (their own voice, or a paddock line about them).</summary>
    public IReadOnlyList<string> Quotes { get; init; } = [];

    /// <summary>The driver's gender, for gendered copy ("female" → she/her; anything else defaults to he/him,
    /// the majority of the fictional grid). Only the non-default drivers need mark it in the JSON.</summary>
    public string Gender { get; init; } = "";

    /// <summary>The pronoun set for this driver's <see cref="Gender"/> (defaults to he/him).</summary>
    public SmgpPronouns Pronouns => SmgpPronouns.For(Gender);
}

/// <summary>A gendered pronoun set for rival/driver copy — subject (she/he/they), object (her/him/them) and
/// possessive determiner (her/his/their). DISPLAY-ONLY. <see cref="For"/> maps a gender string to a set;
/// anything but "female" defaults to he/him (the majority of the fictional grid, and the safe legacy default
/// so existing copy is unchanged for every driver not explicitly marked).</summary>
public readonly record struct SmgpPronouns(string Subject, string Object, string Possessive)
{
    /// <summary>he / him / his — the default for an unmarked or male driver.</summary>
    public static readonly SmgpPronouns He = new("he", "him", "his");

    /// <summary>she / her / her.</summary>
    public static readonly SmgpPronouns She = new("she", "her", "her");

    /// <summary>they / them / their — reserved for a driver explicitly marked non-binary.</summary>
    public static readonly SmgpPronouns They = new("they", "them", "their");

    /// <summary>The default set (he/him) — used where no rival/gender is known.</summary>
    public static SmgpPronouns Default => He;

    /// <summary>Maps a gender string ("female"/"male"/"nonbinary", case-insensitive) to its pronoun set;
    /// anything else → he/him.</summary>
    public static SmgpPronouns For(string? gender) => (gender ?? "").Trim().ToLowerInvariant() switch
    {
        "female" or "f" or "she" => She,
        "nonbinary" or "non-binary" or "nb" or "they" => They,
        _ => He,
    };

    /// <summary>The subject pronoun with its first letter capitalised ("She"/"He"/"They").</summary>
    public string SubjectCap => Capitalize(Subject);

    /// <summary>The object pronoun with its first letter capitalised ("Her"/"Him"/"Them").</summary>
    public string ObjectCap => Capitalize(Object);

    private static string Capitalize(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
