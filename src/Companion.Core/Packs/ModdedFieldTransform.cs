using System.Text.Json;
using System.Text.Json.Nodes;

namespace Companion.Core.Packs;

/// <summary>
/// Applies a pack's OPT-IN modded field (<see cref="PackManifest.ModdedField"/>) to its
/// entries.json + season.json: appends the mod's extra entries, adds their drivers to every
/// round's starter list, and bumps each round's grid size (opponents track it) up to the
/// track's AI cap. Community CAR mods only add to the grid, the SMGP pack's two McLaren MP4/5B
/// teams by Kobra Fleetworks round the field from 24 to 26.
///
/// Pure string transform run at CAREER CREATION, gated upstream by the "use the mod" tick AND an
/// install check (the mod vehicle is present), the TRANSFORMED files are what get pinned, so the
/// fold reads the fuller field and replays stay byte-identical without any seed or fold change. A
/// pack with no modded field (every other pack) never calls this, so it round-trips unchanged.
/// </summary>
public static class ModdedFieldTransform
{
    /// <summary>True when the pack declares a modded field with at least one entry to add.</summary>
    public static bool HasModdedField(SeasonPack pack) =>
        pack.Manifest.ModdedField is { Entries.Count: > 0 };

    /// <summary>Appends the modded field's entries to <paramref name="entriesJson"/> (verbatim
    /// entries.json rows), returning the new JSON.</summary>
    public static string ApplyToEntriesJson(string entriesJson, PackModdedField field)
    {
        var doc = JsonNode.Parse(entriesJson) ?? throw new JsonException("entries.json parsed to null.");
        var entries = doc["entries"]!.AsArray();
        foreach (var entry in field.Entries)
            entries.Add(new JsonObject
            {
                ["teamId"] = entry.TeamId,
                ["driverId"] = entry.DriverId,
                ["number"] = entry.Number,
                ["rounds"] = entry.Rounds,
                ["ams2LiveryName"] = entry.Ams2LiveryName,
            });
        return doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Bumps every round's <c>grid.size</c> (and <c>setupGuide.session.opponents</c> =
    /// size − 1) by the number of added entries, clamped to the track's AI cap, and adds the mod's
    /// driver ids to each round's <c>grid.starterDriverIds</c>. <paramref name="trackCapById"/>
    /// maps track id → Max AI participants (from the extracted library); a track missing from it
    /// leaves that round's size unbumped (never over-fills). Returns the new season.json.</summary>
    public static string ApplyToSeasonJson(
        string seasonJson, PackModdedField field, IReadOnlyDictionary<string, int> trackCapById)
    {
        var doc = JsonNode.Parse(seasonJson) ?? throw new JsonException("season.json parsed to null.");
        int added = field.Entries.Count;

        foreach (var node in doc["rounds"]!.AsArray())
        {
            if (node is not JsonObject round)
                continue;

            if (round["grid"] is JsonObject grid && grid["size"] is { } sizeNode)
            {
                int size = (int)sizeNode;
                int cap = round["track"] is JsonObject track && track["id"] is { } idNode &&
                          trackCapById.TryGetValue((string)idNode!, out int c)
                    ? c
                    : size + added; // no cap known → allow the full bump (never trims silently)
                int newSize = Math.Min(size + added, cap);
                grid["size"] = newSize;

                if (grid["starterDriverIds"] is JsonArray starters)
                    foreach (var entry in field.Entries)
                        starters.Add(entry.DriverId);

                if (round["setupGuide"] is JsonObject guide && guide["session"] is JsonObject session)
                    session["opponents"] = newSize - 1;
            }
        }

        return doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
