using Companion.Ams2.CustomAi;
using Companion.Ams2.Scenarios;

namespace Companion.Ams2.Skins;

/// <summary>Per-model state of a skin season against the install.</summary>
public enum SkinSeasonModelState
{
    /// <summary>The installed <c>&lt;model&gt;.xml</c> IS this season's pointer.</summary>
    Active,

    /// <summary>The installed file matches ANOTHER library season's pointer for this model —
    /// pack-managed content, safe to swap.</summary>
    OtherSeason,

    /// <summary>The installed file matches one of the model folder's own <c>&lt;model&gt;_*.xml</c>
    /// per-race variants, pack-managed content (a round swap left it active), safe to swap.</summary>
    Variant,

    /// <summary>There is no active <c>&lt;model&gt;.xml</c> yet (folder exists), activation
    /// simply creates it.</summary>
    NoActiveFile,

    /// <summary>The installed file matches nothing we know, possibly hand-edited. Swapping it
    /// requires the force gate (backup-first, like the AI-file contract).</summary>
    Unrecognized,

    /// <summary>The model's Overrides folder does not exist, the skins are not installed.</summary>
    FolderMissing,

    /// <summary>The pointer references texture subfolders that are not on disk, the season's
    /// textures are not (fully) installed; activating would show broken cars.</summary>
    TexturesMissing,
}

public sealed record SkinSeasonModelStatus
{
    public required string Model { get; init; }
    public required SkinSeasonModelState State { get; init; }
    public string? Detail { get; init; }
}

/// <summary>Status of one season set against the install's Overrides root.</summary>
public sealed record SkinSeasonStatus
{
    public required string Key { get; init; }
    public required IReadOnlyList<SkinSeasonModelStatus> Models { get; init; }

    public bool IsFullyActive => Models.Count > 0 && Models.All(m => m.State == SkinSeasonModelState.Active);

    /// <summary>True when every model can be switched without the force gate (already active,
    /// another season's pointer, a variant, or no file yet).</summary>
    public bool CanActivate => Models.Count > 0 && Models.All(m => m.State is
        SkinSeasonModelState.Active or SkinSeasonModelState.OtherSeason or
        SkinSeasonModelState.Variant or SkinSeasonModelState.NoActiveFile);

    /// <summary>True when the only thing standing in the way is an unrecognized user file —
    /// the force gate (backup-first overwrite) unblocks it.</summary>
    public bool RequiresForce =>
        Models.Any(m => m.State == SkinSeasonModelState.Unrecognized) &&
        Models.All(m => m.State is not (SkinSeasonModelState.FolderMissing or SkinSeasonModelState.TexturesMissing));

    public string Summary => IsFullyActive
        ? $"The {Key} skins are active."
        : $"{Models.Count(m => m.State == SkinSeasonModelState.Active)} of {Models.Count} car models on the {Key} skins.";
}

public sealed record SkinSeasonApplyResult
{
    public required bool Success { get; init; }
    public required int Applied { get; init; }
    public bool RequiresForce { get; init; }
    public IReadOnlyList<string> Backups { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
    public required string Message { get; init; }
}

/// <summary>
/// The Skin Season Manager: swaps which season's skins a car model shows by writing that
/// season's <c>&lt;model&gt;.xml</c> pointer over the active one, the ONLY file two season
/// packs collide on (textures live in per-pack subfolders and coexist). Backup-first always;
/// an installed file we cannot recognize as pack-managed content is never overwritten without
/// the force gate (the AI-file staging contract). All-or-nothing per season set: a season is
/// only activated when EVERY model in the set can go (otherwise a half-swapped grid mixes two
/// seasons' cars). Purely a skin-file operation, never the career DB / sim / oracle.
/// </summary>
public static class SkinSeasonManager
{
    /// <summary>Inspects <paramref name="set"/> against the install without touching anything.</summary>
    public static SkinSeasonStatus Inspect(SkinSeasonSet set, SkinSeasonLibrary library, string overridesRoot)
    {
        var models = new List<SkinSeasonModelStatus>();
        foreach (var (model, xml) in set.ModelXml.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            models.Add(InspectModel(set, library, overridesRoot, model, xml));
        return new SkinSeasonStatus { Key = set.Key, Models = models };
    }

    private static SkinSeasonModelStatus InspectModel(
        SkinSeasonSet set, SkinSeasonLibrary library, string overridesRoot, string model, string xml)
    {
        string folder = Path.Combine(overridesRoot, model);
        if (!Directory.Exists(folder))
            return new SkinSeasonModelStatus
            {
                Model = model,
                State = SkinSeasonModelState.FolderMissing,
                Detail = $"No Overrides\\{model} folder, install the season's skin pack first.",
            };

        var missingTextures = MissingTextureFolders(folder, xml);
        if (missingTextures.Count > 0)
            return new SkinSeasonModelStatus
            {
                Model = model,
                State = SkinSeasonModelState.TexturesMissing,
                Detail = $"Texture folder(s) not installed: {string.Join(", ", missingTextures)}.",
            };

        string activePath = Path.Combine(folder, model + ".xml");
        if (!File.Exists(activePath))
            return new SkinSeasonModelStatus { Model = model, State = SkinSeasonModelState.NoActiveFile };

        string installed;
        try
        {
            installed = File.ReadAllText(activePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SkinSeasonModelStatus
            {
                Model = model,
                State = SkinSeasonModelState.Unrecognized,
                Detail = ex.Message,
            };
        }

        if (SameContent(installed, xml))
            return new SkinSeasonModelStatus { Model = model, State = SkinSeasonModelState.Active };

        foreach (var other in library.SetsForModel(model))
            if (!string.Equals(other.Key, set.Key, StringComparison.OrdinalIgnoreCase) &&
                SameContent(installed, other.ModelXml[model]))
                return new SkinSeasonModelStatus
                {
                    Model = model,
                    State = SkinSeasonModelState.OtherSeason,
                    Detail = $"Currently on the {other.Key} skins.",
                };

        if (MatchesAnySiblingVariant(folder, model, installed))
            return new SkinSeasonModelStatus
            {
                Model = model,
                State = SkinSeasonModelState.Variant,
                Detail = "A per-race variant is active.",
            };

        return new SkinSeasonModelStatus
        {
            Model = model,
            State = SkinSeasonModelState.Unrecognized,
            Detail = $"{model}.xml matches no known season or variant, it may be hand-edited. " +
                     "Overwriting takes a timestamped backup first.",
        };
    }

    /// <summary>
    /// Activates <paramref name="set"/>: writes each model's pointer file, backup-first.
    /// All-or-nothing, when any model's skins are missing the whole activation refuses, and an
    /// unrecognized (possibly hand-edited) file refuses without <paramref name="force"/>.
    /// </summary>
    public static SkinSeasonApplyResult Activate(
        SkinSeasonSet set, SkinSeasonLibrary library, string overridesRoot, bool force, DateTimeOffset now)
    {
        var status = Inspect(set, library, overridesRoot);
        if (status.IsFullyActive)
            return new SkinSeasonApplyResult
            {
                Success = true,
                Applied = 0,
                Message = $"The {set.Key} skins are already active.",
            };

        var blockers = status.Models
            .Where(m => m.State is SkinSeasonModelState.FolderMissing or SkinSeasonModelState.TexturesMissing)
            .ToList();
        if (blockers.Count > 0)
            return new SkinSeasonApplyResult
            {
                Success = false,
                Applied = 0,
                Errors = blockers.Select(b => $"{b.Model}: {b.Detail}").ToList(),
                Message = $"Cannot switch to the {set.Key} skins, {blockers.Count} car model(s) are " +
                          "missing installed skins (a partial swap would mix two seasons' cars).",
            };

        if (!force && status.Models.Any(m => m.State == SkinSeasonModelState.Unrecognized))
        {
            var held = status.Models.Where(m => m.State == SkinSeasonModelState.Unrecognized).ToList();
            return new SkinSeasonApplyResult
            {
                Success = false,
                Applied = 0,
                RequiresForce = true,
                Errors = held.Select(h => $"{h.Model}: {h.Detail}").ToList(),
                Message = $"{held.Count} installed override file(s) match no known season, they may be " +
                          "hand-edited. 'Overwrite anyway' takes a timestamped backup first.",
            };
        }

        int applied = 0;
        var backups = new List<string>();
        var errors = new List<string>();
        foreach (var (model, xml) in set.ModelXml.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            string target = Path.Combine(overridesRoot, model, model + ".xml");
            try
            {
                if (File.Exists(target))
                {
                    if (SameContent(File.ReadAllText(target), xml))
                        continue; // already this season's pointer
                    backups.Add(ScenarioApplier.BackUp(target, now));
                }
                File.WriteAllText(target, xml, new System.Text.UTF8Encoding(false));
                applied++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{model}: {ex.Message}");
            }
        }

        return new SkinSeasonApplyResult
        {
            Success = errors.Count == 0,
            Applied = applied,
            Backups = backups,
            Errors = errors,
            Message = errors.Count == 0
                ? $"Switched {applied} car model(s) to the {set.Key} skins (previous pointers backed up)."
                : $"Switched {applied} car model(s) to the {set.Key} skins, but {errors.Count} failed.",
        };
    }

    /// <summary>Referenced texture subfolders (the first path segment of every PATH attribute,
    /// e.g. <c>F1_1985\body3.dds</c> → <c>F1_1985</c>) that do NOT exist under the model folder.
    /// Root-relative paths (no subfolder) never block.</summary>
    private static IReadOnlyList<string> MissingTextureFolders(string modelFolder, string xml)
    {
        var referenced = LenientXml
            .ExtractAttributeValues(LenientXml.StripComments(xml), "TEXTURE", "PATH")
            .Concat(LenientXml.ExtractAttributeValues(LenientXml.StripComments(xml), "PREVIEWIMAGE", "PATH"))
            .Select(p => p.Replace('/', '\\'))
            .Where(p => p.Contains('\\', StringComparison.Ordinal))
            .Select(p => p[..p.IndexOf('\\', StringComparison.Ordinal)])
            .Where(d => d.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return referenced
            .Where(d => !Directory.Exists(Path.Combine(modelFolder, d)))
            .ToList();
    }

    /// <summary>Whether the installed content matches any sibling per-race variant
    /// (<c>&lt;model&gt;_*.xml</c>, excluding the inert <c>_dist</c> template).</summary>
    private static bool MatchesAnySiblingVariant(string folder, string model, string installed)
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(folder, model + "_*.xml"))
            {
                if (Path.GetFileNameWithoutExtension(file)
                        .EndsWith("_dist", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (SameContent(installed, File.ReadAllText(file)))
                    return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable siblings just mean we cannot recognize the content that way.
        }
        return false;
    }

    /// <summary>Content equality modulo line endings and trailing whitespace, archives and
    /// installs disagree on CRLF/LF, which must not make a season look inactive.</summary>
    internal static bool SameContent(string a, string b)
    {
        static string Normalize(string s) =>
            string.Join('\n', s.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd())).Trim();
        return string.Equals(Normalize(a), Normalize(b), StringComparison.Ordinal);
    }
}
