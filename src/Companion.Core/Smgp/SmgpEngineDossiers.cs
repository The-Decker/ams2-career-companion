using System.Text.Json.Serialization;
using Companion.Core.Json;

namespace Companion.Core.Smgp;

/// <summary>
/// The SMGP-universe engine dossiers: each permanent engine specification's tagline, naming
/// note, character paragraph, three-paragraph history and quotes, keyed by CANON engine id
/// (e.g. "palm-190-v10", the <c>engineId</c> field of <c>data/rules/smgp/canon.json</c>).
/// Loaded from <c>data/rules/smgp/engine-dossiers.json</c>. One dossier per official
/// specification, including the shared ones (LIZZIE 24 V8, VAPOR DNPQ V8, LORRY 32 V8,
/// RAM V12): a shared engine's dossier is the manufacturer's own story, team-specific
/// integration belongs to the car dossiers. The engine names never change across the 17
/// seasons (mission SMGP-024 canon lock); annual development packages are recorded separately,
/// never a rename, never an architecture change. VAPOR DN's architecture is deliberately
/// unpublished in-world and is never stated here. DISPLAY-ONLY, never a fold input (exactly
/// like <see cref="SmgpTeamProfiles"/> and the news corpora): the dossier surfaces show the
/// engine's story. An absent file (or an un-authored engine) resolves to null, so a non-SMGP
/// install or an un-updated data folder is simply unaffected.
/// </summary>
public sealed class SmgpEngineDossiers
{
    private readonly IReadOnlyDictionary<string, SmgpEngineDossier> _byEngine;

    private SmgpEngineDossiers(IReadOnlyDictionary<string, SmgpEngineDossier> byEngine) =>
        _byEngine = byEngine;

    /// <summary>An empty catalog (no file shipped): every lookup returns null and
    /// <see cref="Engines"/> is empty, so the dossier surfaces simply omit the engine story.</summary>
    public static SmgpEngineDossiers Empty { get; } =
        new(new Dictionary<string, SmgpEngineDossier>(StringComparer.Ordinal));

    /// <summary>This engine's SMGP-world dossier, or null when none is authored for it.</summary>
    public SmgpEngineDossier? ForEngine(string engineId) => _byEngine.GetValueOrDefault(engineId);

    /// <summary>The canon engine ids the catalog has an authored dossier for (drift-guard source).</summary>
    public IReadOnlyCollection<string> Engines => _byEngine.Keys.ToArray();

    /// <summary>Loads <c>data/rules/smgp/engine-dossiers.json</c> from the rules directory, or
    /// <see cref="Empty"/> when the file is absent.</summary>
    public static SmgpEngineDossiers Load(string rulesDirectory)
    {
        string path = Path.Combine(rulesDirectory, "smgp", "engine-dossiers.json");
        return File.Exists(path) ? Parse(File.ReadAllText(path)) : Empty;
    }

    public static SmgpEngineDossiers Parse(string json)
    {
        var dto = System.Text.Json.JsonSerializer.Deserialize<DossiersDto>(json, CoreJson.Options)
            ?? new DossiersDto();
        var byEngine = new Dictionary<string, SmgpEngineDossier>(StringComparer.Ordinal);
        foreach (var (engineId, dossier) in dto.Engines)
            if (dossier is not null)
                byEngine[engineId] = dossier;
        return new SmgpEngineDossiers(byEngine);
    }

    private sealed record DossiersDto
    {
        [JsonPropertyName("engines")]
        public IReadOnlyDictionary<string, SmgpEngineDossier?> Engines { get; init; } =
            new Dictionary<string, SmgpEngineDossier?>(StringComparer.Ordinal);
    }
}

/// <summary>One permanent SMGP engine specification's dossier, display-only reference content
/// for the dossier surfaces. The "name" field echoes the canon registry (canon.json): the
/// engine's permanent display name ("PALM 190 V10"). Fully fictional (the SEGA universe,
/// never real F1).</summary>
public sealed record SmgpEngineDossier
{
    /// <summary>The engine's permanent registered specification name ("PALM 190 V10"), matching
    /// canon.json.</summary>
    public string Name { get; init; } = "";

    /// <summary>A one-line arcade tagline for the engine ("THE ENGINE THE ORDER OF THINGS RUNS ON.").</summary>
    public string Tagline { get; init; } = "";

    /// <summary>The manufacturer/works behind the engine and the in-world name logic (a race shop,
    /// an industrial giant, a stubborn family firm), a sentence or two.</summary>
    public string Naming { get; init; } = "";

    /// <summary>The engine's character, one dense paragraph (sound, delivery, torque, weight,
    /// cooling thirst, fuel appetite, reliability, qualifying vs race trim, wet drivability,
    /// development ceiling), distilled rather than a checklist.</summary>
    public string Character { get; init; } = "";

    /// <summary>The engine's SMGP-world history in three paragraphs: origin, famous failures and
    /// victories, and the 17-season evolution under the permanent registered specification name.</summary>
    public IReadOnlyList<string> History { get; init; } = [];

    /// <summary>Two in-character quotes about the engine (a works man, a mechanic, an engineer, a rival).</summary>
    public IReadOnlyList<string> Quotes { get; init; } = [];
}
