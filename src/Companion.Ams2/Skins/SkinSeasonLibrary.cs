namespace Companion.Ams2.Skins;

/// <summary>
/// One season's set of ACTIVE override pointer files: vehicle-model folder name →
/// the <c>&lt;model&gt;.xml</c> text that puts that model on this season's skins. Two season
/// packs for the same car model (1983↔1985, 1990↔SMGP, 1996↔1997, 1974↔1975, 2010↔2012)
/// collide ONLY on this one file, every pack keeps its textures in its own subfolder, so
/// swapping seasons is swapping these pointer files.
/// </summary>
public sealed record SkinSeasonSet
{
    public required string Key { get; init; }

    /// <summary>Vehicle model folder name (e.g. <c>formula_retro_g3</c>) → the season's
    /// <c>&lt;model&gt;.xml</c> content.</summary>
    public required IReadOnlyDictionary<string, string> ModelXml { get; init; }

    /// <summary>The app-owned payload manifest (<c>ownership.json</c> beside the pointer files):
    /// which install-side texture folders the app vaults and can repair for each model. Null =
    /// inspect-only set, the ownership feature does not cover it.</summary>
    public SkinSeasonOwnership? Ownership { get; init; }
}

/// <summary>
/// The app-shipped skin-season library (<c>data/ams2/skin-seasons/&lt;key&gt;/&lt;model&gt;.xml</c>):
/// per season key, the override pointer files extracted from that season's skin pack. Only the
/// pointer XMLs live here (small text files); the textures themselves are installed once by the
/// user and coexist on disk for every season.
/// </summary>
public sealed class SkinSeasonLibrary
{
    public static readonly SkinSeasonLibrary Empty = new() { Sets = new Dictionary<string, SkinSeasonSet>(StringComparer.OrdinalIgnoreCase) };

    public required IReadOnlyDictionary<string, SkinSeasonSet> Sets { get; init; }

    public SkinSeasonSet? Get(string? key) =>
        key is not null && Sets.TryGetValue(key, out var set) ? set : null;

    /// <summary>Every library set that carries a pointer file for <paramref name="model"/> —
    /// the "family" of seasons competing for that model. Used to recognize an installed
    /// <c>&lt;model&gt;.xml</c> as pack-managed content (some season's pointer) versus a
    /// hand-edited user file.</summary>
    public IEnumerable<SkinSeasonSet> SetsForModel(string model) =>
        Sets.Values.Where(s => s.ModelXml.ContainsKey(model));

    /// <summary>Loads every <c>&lt;directory&gt;/&lt;key&gt;/*.xml</c>. A missing directory is an
    /// empty library (the feature is simply absent). <c>*_dist.xml</c> files are skipped, they
    /// are the packs' inert "copy me without the _dist part" distribution templates, never read
    /// by the game.</summary>
    public static SkinSeasonLibrary Load(string directory)
    {
        if (!Directory.Exists(directory))
            return Empty;

        var sets = new Dictionary<string, SkinSeasonSet>(StringComparer.OrdinalIgnoreCase);
        foreach (string setDir in Directory.EnumerateDirectories(directory))
        {
            var models = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.EnumerateFiles(setDir, "*.xml"))
            {
                string model = Path.GetFileNameWithoutExtension(file);
                if (model.EndsWith("_dist", StringComparison.OrdinalIgnoreCase))
                    continue;
                models[model] = File.ReadAllText(file);
            }
            if (models.Count > 0)
                sets[Path.GetFileName(setDir)] = new SkinSeasonSet
                {
                    Key = Path.GetFileName(setDir),
                    ModelXml = models,
                    Ownership = SkinSeasonOwnership.Load(setDir),
                };
        }
        return new SkinSeasonLibrary { Sets = sets };
    }
}
