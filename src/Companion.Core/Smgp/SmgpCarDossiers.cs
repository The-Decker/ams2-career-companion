using System.Text.Json.Serialization;
using Companion.Core.Json;

namespace Companion.Core.Smgp;

/// <summary>
/// The SMGP-universe car dossiers: each permanent car's tagline, naming note, character
/// paragraph, three-paragraph history and quotes, keyed by CANON car id (e.g. "madonna-456",
/// the <c>carId</c> field of <c>data/rules/smgp/canon.json</c>). Loaded from
/// <c>data/rules/smgp/car-dossiers.json</c>. The dossier narrates the team's REGISTERED
/// PROGRAM NAME (mission SMGP-024 canon lock): the name never changes across the 17 seasons,
/// the chassis generations and rules dispensations evolve beneath it, and annual development
/// packages are recorded separately, so the lore describes aero packages, reliability
/// campaigns and setup craft, never a renamed car. DISPLAY-ONLY, never a fold input (exactly
/// like <see cref="SmgpTeamProfiles"/> and the news corpora): the dossier surfaces show the
/// machine's story. An absent file (or an un-authored car) resolves to null, so a non-SMGP
/// install or an un-updated data folder is simply unaffected.
/// </summary>
public sealed class SmgpCarDossiers
{
    private readonly IReadOnlyDictionary<string, SmgpCarDossier> _byCar;

    private SmgpCarDossiers(IReadOnlyDictionary<string, SmgpCarDossier> byCar) => _byCar = byCar;

    /// <summary>An empty catalog (no file shipped): every lookup returns null and <see cref="Cars"/>
    /// is empty, so the dossier surfaces simply omit the car story.</summary>
    public static SmgpCarDossiers Empty { get; } =
        new(new Dictionary<string, SmgpCarDossier>(StringComparer.Ordinal));

    /// <summary>This car's SMGP-world dossier, or null when none is authored for it.</summary>
    public SmgpCarDossier? ForCar(string carId) => _byCar.GetValueOrDefault(carId);

    /// <summary>The canon car ids the catalog has an authored dossier for (drift-guard source).</summary>
    public IReadOnlyCollection<string> Cars => _byCar.Keys.ToArray();

    /// <summary>Loads <c>data/rules/smgp/car-dossiers.json</c> from the rules directory, or
    /// <see cref="Empty"/> when the file is absent.</summary>
    public static SmgpCarDossiers Load(string rulesDirectory)
    {
        string path = Path.Combine(rulesDirectory, "smgp", "car-dossiers.json");
        return File.Exists(path) ? Parse(File.ReadAllText(path)) : Empty;
    }

    public static SmgpCarDossiers Parse(string json)
    {
        var dto = System.Text.Json.JsonSerializer.Deserialize<DossiersDto>(json, CoreJson.Options)
            ?? new DossiersDto();
        var byCar = new Dictionary<string, SmgpCarDossier>(StringComparer.Ordinal);
        foreach (var (carId, dossier) in dto.Cars)
            if (dossier is not null)
                byCar[carId] = dossier;
        return new SmgpCarDossiers(byCar);
    }

    private sealed record DossiersDto
    {
        [JsonPropertyName("cars")]
        public IReadOnlyDictionary<string, SmgpCarDossier?> Cars { get; init; } =
            new Dictionary<string, SmgpCarDossier?>(StringComparer.Ordinal);
    }
}

/// <summary>One permanent SMGP car's dossier, display-only reference content for the dossier
/// surfaces. The "name" and "team" fields echo the canon registry (canon.json): the car's
/// permanent display name ("MADONNA 456") and its pack team id ("team.madonna"). Fully
/// fictional (the SEGA universe, never real F1).</summary>
public sealed record SmgpCarDossier
{
    /// <summary>The car's permanent registered program name ("MADONNA 456"), matching canon.json.</summary>
    public string Name { get; init; } = "";

    /// <summary>The pack team id this car belongs to ("team.madonna"), matching canon.json.</summary>
    public string Team { get; init; } = "";

    /// <summary>A one-line arcade tagline for the machine ("THE CROWN IN METAL, FIRST AND FLAWLESS.").</summary>
    public string Tagline { get; init; } = "";

    /// <summary>Why the number/name exists, the in-world logic (a founding year, a wind-tunnel run,
    /// a registry entry), a sentence or two.</summary>
    public string Naming { get; init; } = "";

    /// <summary>The car's driving character, one dense paragraph (balance, grip, brakes, tyres,
    /// reliability, wet and high-speed behaviour), distilled rather than a checklist.</summary>
    public string Character { get; init; } = "";

    /// <summary>The car's SMGP-world history in three paragraphs: origins/design philosophy,
    /// famous triumphs and famous failures across the 17-season program, and the permanent-name
    /// tradition spanning the evolving chassis generations.</summary>
    public IReadOnlyList<string> History { get; init; } = [];

    /// <summary>Two in-character quotes about the machine (a mechanic, a principal, a driver, a rival).</summary>
    public IReadOnlyList<string> Quotes { get; init; } = [];
}
