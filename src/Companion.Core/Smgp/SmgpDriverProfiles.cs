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

    /// <summary>One or two in-character quotes (his own voice, or a paddock line about him).</summary>
    public IReadOnlyList<string> Quotes { get; init; } = [];
}
