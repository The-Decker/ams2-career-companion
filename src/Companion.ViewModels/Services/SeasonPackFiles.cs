using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Companion.Core.Packs;

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

    private static string ReadPart(string packDirectory, string fileName)
    {
        string path = Path.Combine(packDirectory, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Season pack part '{fileName}' not found in '{packDirectory}' — a pack folder needs " +
                "pack.json, season.json, teams.json, drivers.json and entries.json.", path);
        return File.ReadAllText(path);
    }
}

/// <summary>
/// The immutable pinned form of a season pack: all five JSON parts wrapped in one JSON
/// envelope, stored as a blob in the career's pinned_pack table with its SHA-256. Careers
/// rehydrate from this — never from the mutable pack folder.
/// </summary>
public sealed record PinnedPackEnvelope
{
    public required string PackJson { get; init; }
    public required string SeasonJson { get; init; }
    public required string TeamsJson { get; init; }
    public required string DriversJson { get; init; }
    public required string EntriesJson { get; init; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static PinnedPackEnvelope From(SeasonPackFiles files) => new()
    {
        PackJson = files.ManifestJson,
        SeasonJson = files.SeasonJson,
        TeamsJson = files.TeamsJson,
        DriversJson = files.DriversJson,
        EntriesJson = files.EntriesJson,
    };

    public byte[] ToBytes() => JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions);

    public static PinnedPackEnvelope FromBytes(byte[] bytes) =>
        JsonSerializer.Deserialize<PinnedPackEnvelope>(bytes, JsonOptions)
        ?? throw new JsonException("Pinned pack envelope deserialized to null.");

    public SeasonPack Parse() =>
        PackLoader.Parse(PackJson, SeasonJson, TeamsJson, DriversJson, EntriesJson);

    public static string Sha256Of(byte[] envelopeBytes) =>
        Convert.ToHexString(SHA256.HashData(envelopeBytes)).ToLowerInvariant();
}
