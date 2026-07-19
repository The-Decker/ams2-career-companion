using System.Text.Json;

namespace Companion.Ams2.Skins;

/// <summary>One model's owned payload: the texture subfolders under <c>Overrides\&lt;model&gt;\</c>
/// the app keeps safe, plus an optional archive to re-seed them from when the vault itself lacks
/// them.</summary>
public sealed record SkinModelOwnership
{
    /// <summary>Texture subfolder names that must exist (and stay) under
    /// <c>Overrides\&lt;model&gt;\</c> — e.g. <c>["SMGP"]</c> for the formula_classic_g3m* models,
    /// <c>["skins"]</c> for mclaren_mp45b. The app vaults and restores these folders verbatim.</summary>
    public required IReadOnlyList<string> Folders { get; init; }

    /// <summary>Optional fallback source archive (.zip handled natively; .rar/.7z via a 7-Zip
    /// CLI when one is installed). Entries containing <c>/Overrides/&lt;model&gt;/&lt;folder&gt;/</c>
    /// are mapped to the vault verbatim, so archives that mirror the install tree need no prefix
    /// config. Null = vault-only (repair reports SourceUnavailable when the vault lacks payload).</summary>
    public string? ArchivePath { get; init; }
}

/// <summary>
/// The ownership manifest for one skin-season set (<c>data/ams2/skin-seasons/&lt;key&gt;/ownership.json</c>):
/// which install-side payload (texture folders) the app OWNS for each model in the set. Mike's
/// direction after RCM stripped the SMGP + McLaren mods (2026-07-11): the app keeps its own copy
/// of the mod files where no mod manager can touch it, detects the stripped state, and offers a
/// Repair that re-lays them into the install. The pointer XMLs are already app-owned (the set's
/// <c>&lt;model&gt;.xml</c> files); this manifest extends ownership to the heavy payload.
/// Absent file = the set is inspect-only (no ownership), never an error.
/// </summary>
public sealed record SkinSeasonOwnership
{
    public const string FileName = "ownership.json";

    /// <summary>Vehicle model folder name (e.g. <c>formula_classic_g3m1</c>) → its owned payload.</summary>
    public required IReadOnlyDictionary<string, SkinModelOwnership> Payload { get; init; }

    /// <summary>Loads the manifest beside a set's pointer files, or null when the set carries
    /// none (the ownership feature simply does not cover that set). Tolerant: a broken or
    /// schema-mismatched file reads as no ownership rather than failing the library load.</summary>
    public static SkinSeasonOwnership? Load(string setDirectory)
    {
        string path = Path.Combine(setDirectory, FileName);
        if (!File.Exists(path))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("payload", out var payload) ||
                payload.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var models = new Dictionary<string, SkinModelOwnership>(StringComparer.OrdinalIgnoreCase);
            foreach (var modelEntry in payload.EnumerateObject())
            {
                if (modelEntry.Value.ValueKind != JsonValueKind.Object)
                    continue;

                var folders = new List<string>();
                if (modelEntry.Value.TryGetProperty("folders", out var foldersElement) &&
                    foldersElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var folder in foldersElement.EnumerateArray())
                        if (folder.ValueKind == JsonValueKind.String &&
                            folder.GetString() is { Length: > 0 } name)
                        {
                            folders.Add(name);
                        }
                }

                if (folders.Count == 0)
                    continue;

                string? archivePath = null;
                if (modelEntry.Value.TryGetProperty("archive", out var archiveElement) &&
                    archiveElement.ValueKind == JsonValueKind.Object &&
                    archiveElement.TryGetProperty("path", out var pathElement) &&
                    pathElement.ValueKind == JsonValueKind.String)
                {
                    archivePath = pathElement.GetString();
                }

                models[modelEntry.Name] = new SkinModelOwnership
                {
                    Folders = folders,
                    ArchivePath = string.IsNullOrWhiteSpace(archivePath) ? null : archivePath,
                };
            }

            return models.Count == 0 ? null : new SkinSeasonOwnership { Payload = models };
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null; // a corrupt manifest disables ownership for the set; never a load failure
        }
    }
}
