using System.Text.Json;
using Companion.Core.Json;

namespace Companion.Core.Packs;

/// <summary>
/// Parses the five season-pack files from JSON text into a <see cref="SeasonPack"/>.
/// Core has no file I/O, callers read the files and hand in strings. Every
/// <see cref="JsonException"/> is rethrown with the pack file-part name prefixed so import
/// errors point at the right file.
/// </summary>
public static class PackLoader
{
    public static SeasonPack Parse(
        string manifestJson,
        string seasonJson,
        string teamsJson,
        string driversJson,
        string entriesJson) => new()
    {
        Manifest = ParseManifest(manifestJson),
        Season = ParseSeason(seasonJson),
        Teams = ParseTeams(teamsJson),
        Drivers = ParseDrivers(driversJson),
        Entries = ParseEntries(entriesJson),
    };

    public static PackManifest ParseManifest(string json) =>
        Deserialize<PackManifest>(json, "pack.json");

    public static SeasonDefinition ParseSeason(string json) =>
        Deserialize<SeasonDefinition>(json, "season.json");

    public static IReadOnlyList<PackTeam> ParseTeams(string json) =>
        Deserialize<PackTeamsFile>(json, "teams.json").Teams;

    public static IReadOnlyList<PackDriver> ParseDrivers(string json) =>
        Deserialize<PackDriversFile>(json, "drivers.json").Drivers;

    public static IReadOnlyList<PackEntry> ParseEntries(string json) =>
        Deserialize<PackEntriesFile>(json, "entries.json").Entries;

    private static T Deserialize<T>(string json, string filePart)
    {
        T? value;
        try
        {
            value = JsonSerializer.Deserialize<T>(json, CoreJson.Options);
        }
        catch (JsonException ex)
        {
            throw new JsonException($"{filePart}: {ex.Message}", ex);
        }

        return value ?? throw new JsonException(
            $"{filePart}: file deserialized to null (empty document or literal 'null').");
    }
}
