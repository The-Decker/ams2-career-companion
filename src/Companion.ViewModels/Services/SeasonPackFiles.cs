using Companion.Core.Packs;
using Companion.Data;

namespace Companion.ViewModels.Services;

/// <summary>
/// The five season-pack files read from disk as raw text. Core's <see cref="PackLoader"/> is
/// string-in by design, so all file I/O for packs happens here. The same five strings are
/// what gets pinned verbatim into the career database at season start.
/// </summary>
public sealed record SeasonPackFiles
{
    public required string Directory { get; init; }
    public required string ManifestJson { get; init; }
    public required string SeasonJson { get; init; }
    public required string TeamsJson { get; init; }
    public required string DriversJson { get; init; }
    public required string EntriesJson { get; init; }

    public static SeasonPackFiles Read(string packDirectory) => new()
    {
        Directory = packDirectory,
        ManifestJson = ReadPart(packDirectory, "pack.json"),
        SeasonJson = ReadPart(packDirectory, "season.json"),
        TeamsJson = ReadPart(packDirectory, "teams.json"),
        DriversJson = ReadPart(packDirectory, "drivers.json"),
        EntriesJson = ReadPart(packDirectory, "entries.json"),
    };

    public SeasonPack Parse() =>
        PackLoader.Parse(ManifestJson, SeasonJson, TeamsJson, DriversJson, EntriesJson);

    /// <summary>The five verbatim parts as the app's pinned form (the Data layer's
    /// <see cref="PinnedPackEnvelope"/>, one pinning format for the whole app).</summary>
    public PinnedPackEnvelope ToPinnedEnvelope() =>
        PinnedPackEnvelope.From(ManifestJson, SeasonJson, TeamsJson, DriversJson, EntriesJson);

    private static string ReadPart(string packDirectory, string fileName)
    {
        string path = Path.Combine(packDirectory, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Season pack part '{fileName}' not found in '{packDirectory}', a pack folder needs " +
                "pack.json, season.json, teams.json, drivers.json and entries.json.", path);
        return File.ReadAllText(path);
    }
}

// The pinned five-file envelope moved to the Data layer (Companion.Data.PinnedPackEnvelope)
// so replay verification and the app share ONE pinning format definition.
