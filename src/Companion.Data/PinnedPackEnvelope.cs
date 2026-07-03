using System.Security.Cryptography;
using System.Text.Json;
using Companion.Core.Json;
using Companion.Core.Packs;

namespace Companion.Data;

/// <summary>
/// THE pinned form of a season pack (one place for the whole app): all five JSON parts —
/// verbatim, as read from the pack folder — wrapped in one JSON envelope and stored as a
/// blob in the career's pinned_pack table with its SHA-256. Careers rehydrate from this,
/// never from the mutable pack folder.
///
/// <see cref="LoadSeasonPack"/> also accepts the legacy blob format (a canonical
/// <see cref="SeasonPack"/> serialization, as written by <see cref="CareerStore.PinPack"/>),
/// so replay verification works against either pinning path.
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

    public static PinnedPackEnvelope From(
        string packJson, string seasonJson, string teamsJson, string driversJson, string entriesJson) => new()
    {
        PackJson = packJson,
        SeasonJson = seasonJson,
        TeamsJson = teamsJson,
        DriversJson = driversJson,
        EntriesJson = entriesJson,
    };

    public byte[] ToBytes() => JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions);

    public static PinnedPackEnvelope FromBytes(byte[] bytes) =>
        JsonSerializer.Deserialize<PinnedPackEnvelope>(bytes, JsonOptions)
        ?? throw new JsonException("Pinned pack envelope deserialized to null.");

    public SeasonPack Parse() =>
        PackLoader.Parse(PackJson, SeasonJson, TeamsJson, DriversJson, EntriesJson);

    public static string Sha256Of(byte[] envelopeBytes) =>
        Convert.ToHexString(SHA256.HashData(envelopeBytes)).ToLowerInvariant();

    /// <summary>True when the blob is a five-file envelope (as opposed to the legacy
    /// canonical-SeasonPack blob <see cref="CareerStore.PinPack"/> writes).</summary>
    public static bool IsEnvelope(byte[] blob)
    {
        try
        {
            using var probe = JsonDocument.Parse(blob);
            return probe.RootElement.ValueKind == JsonValueKind.Object
                && probe.RootElement.TryGetProperty("packJson", out var packJson)
                && packJson.ValueKind == JsonValueKind.String
                && probe.RootElement.TryGetProperty("seasonJson", out var seasonJson)
                && seasonJson.ValueKind == JsonValueKind.String;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Loads the <see cref="SeasonPack"/> pinned in a pinned_pack blob, accepting
    /// BOTH storage formats: the app's five-file envelope and the legacy canonical
    /// serialization. The one entry point replay verification and tooling should use.</summary>
    public static SeasonPack LoadSeasonPack(byte[] blob)
    {
        if (IsEnvelope(blob))
            return FromBytes(blob).Parse();

        return JsonSerializer.Deserialize<SeasonPack>(blob, CoreJson.Options)
            ?? throw new InvalidDataException("Pinned pack blob deserialized to null.");
    }
}
