using System.Text.Json;
using System.Text.Json.Nodes;

namespace Companion.Core.Packs;

/// <summary>
/// Applies a pack's OPT-IN alternate tracks to its <c>season.json</c>: for every round that declares
/// <c>track.alternate</c>, swaps the round's track <c>id</c> + <c>laps</c> to the alternate and sets
/// <c>isPlaceholder</c> (false when the alternate is the authentic real venue now available as a mod,
/// true for an era/character filler stand-in).
///
/// Pure string transform run at CAREER CREATION, gated upstream by the "use alternate tracks" tick AND
/// an install check (all required mods present) — the TRANSFORMED season.json is what gets pinned, so
/// the fold reads the alternates and replays stay byte-identical without any seed or fold change. A
/// round with no alternate is left exactly as authored, so a pack with zero alternates round-trips
/// unchanged in content.
/// </summary>
public static class AlternateTrackTransform
{
    /// <summary>True when the pack declares at least one round with a <c>track.alternate</c> — i.e.
    /// there is anything for the transform to do.</summary>
    public static bool HasAlternates(SeasonPack pack) =>
        pack.Season.Rounds.Any(r => r.Track.Alternate is not null);

    /// <summary>Rewrites <paramref name="seasonJson"/> so every round with an alternate now drives the
    /// alternate track (id + distance-preserving laps + placeholder flag). Returns the new JSON.</summary>
    public static string ApplyToSeasonJson(string seasonJson)
    {
        var doc = JsonNode.Parse(seasonJson)
                  ?? throw new JsonException("season.json parsed to null.");

        foreach (var node in doc["rounds"]!.AsArray())
        {
            if (node is not JsonObject round || round["track"] is not JsonObject track)
                continue;
            if (track["alternate"] is not JsonObject alt)
                continue;

            string altId = (string)alt["id"]!;
            int altLaps = (int)alt["laps"]!;
            bool isRealVenue = (bool?)alt["isRealVenue"] ?? false;

            track["id"] = altId;
            round["laps"] = altLaps;
            // A real-venue alternate IS the authentic circuit → no longer a placeholder; a filler
            // stand-in stays a labelled placeholder so the briefing keeps naming it honestly.
            track["isPlaceholder"] = !isRealVenue;

            // Rewrite the round's setup note so the briefing matches the swapped track + laps — the
            // authored note describes the ORIGINAL base stand-in, which would otherwise contradict the
            // new venue/distance/placeholder state (display-only, but the player reads it).
            if (round["setupGuide"] is JsonObject guide)
            {
                string venue = (string?)track["realVenue"] is { Length: > 0 } v ? v : altId;
                guide["notes"] = isRealVenue
                    ? $"Racing the authentic {venue}, available in AMS2 as the mod track \"{altId}\" — its historical {altLaps} laps."
                    : $"Alternate track for {venue} (not in AMS2 base): reproduced on the mod track \"{altId}\" at {altLaps} laps.";
            }
        }

        return doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
