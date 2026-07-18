using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Companion.Core.Determinism;
using Companion.Core.Packs;

namespace Companion.Core.Smgp;

/// <summary>
/// The SMGP dynamic per-race DNQ field, as a SEEDED PER-CAREER roll (Mike: "a random generator should
/// determine the bottom 8 cars ... who stays and who goes for the next race"). The pack fields all its
/// painted cars (34), but the class shows at most ~26 liveries, so each round the slowest sit out, and
/// WHICH ones is a fresh roll of the backmarker bubble, seeded by the career's master seed, so the
/// rotation differs per playthrough and is revealed race by race.
///
/// The strong are never at risk: the top <c>size − churn</c> cars by qualifying pace ALWAYS qualify;
/// only the bubble (the rest) rolls for the remaining slots. The roll is a deterministic PCG32 draw per
/// (round, driver) via <see cref="StreamFactory"/>, so a career re-derives its exact field, and because
/// this runs at CAREER CREATION and the generated starters are pinned into <c>season.json</c> (exactly
/// like <see cref="AlternateTrackTransform"/> / the modded field), the fold reads them and replays stay
/// byte-identical with no fold change and no seed threading. Existing careers keep their pinned starters.
/// </summary>
public static class SmgpDnqField
{
    /// <summary>How many of a round's qualifying spots are CONTESTED (rolled), the rest of the grid is
    /// the guaranteed fast runners. Larger = a bigger bubble rotates. Structural, tunable.</summary>
    private const int Churn = 6;

    /// <summary>The per-race qualifying-pace jitter magnitude (±): big enough to reshuffle the bubble,
    /// far smaller than the gap from the bubble up to the guaranteed runners, so a strong car can never
    /// be rolled out.</summary>
    private const double JitterMagnitude = 0.12;

    private const string Subsystem = "smgp-dnq";

    /// <summary>True when at least one round caps its grid below the full field, i.e. there is a DNQ
    /// tail to roll. False for a full-field pack (nothing to do).</summary>
    public static bool HasDnqField(SeasonPack pack)
    {
        int field = pack.Entries.Select(e => e.DriverId).Distinct(StringComparer.Ordinal).Count();
        return pack.Season.Rounds.Any(r => r.Grid is { } g && g.Size < field);
    }

    /// <summary>Rolls each capped round's qualifiers from the master seed: the top <c>size − churn</c> by
    /// base qualifying skill are guaranteed; the bubble competes for the remaining slots on a per-race
    /// jittered pace. Returns round number → the (grid.size) starter driver ids. Rounds without a DNQ cap
    /// are omitted (their authored grid stands). This is the SEASON-1 roll pinned at creation.</summary>
    public static IReadOnlyDictionary<int, IReadOnlyList<string>> Generate(SeasonPack pack, ulong masterSeed) =>
        Generate(pack, masterSeed, seasonOrdinal: 1);

    /// <summary>The ordinal-aware roll. Season 1 (<paramref name="seasonOrdinal"/> ≤ 1) keys the jitter by
    /// driver id ALONE, the exact original roll, so the pinned season-1 field is byte-identical. Seasons
    /// 2+ fold the ordinal into the stream key so each season re-rolls an INDEPENDENT backmarker field
    /// (Mike: "the second year is all random"). <c>pack.Season.Year</c> is constant across a single-pack
    /// SMGP carryover career, so without the ordinal every season would share one field.</summary>
    public static IReadOnlyDictionary<int, IReadOnlyList<string>> Generate(
        SeasonPack pack, ulong masterSeed, int seasonOrdinal)
    {
        var factory = new StreamFactory(masterSeed);

        var qualiById = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var d in pack.Drivers)
            qualiById.TryAdd(d.Id, d.Ratings.QualifyingSkill);

        var field = pack.Entries
            .Select(e => e.DriverId)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var result = new Dictionary<int, IReadOnlyList<string>>();
        foreach (var round in pack.Season.Rounds)
        {
            if (round.Grid is not { } grid || grid.Size >= field.Count)
                continue;

            int size = grid.Size;
            int churn = Math.Clamp(Churn, 1, size);
            int guaranteedCount = Math.Max(0, size - churn);

            // Fastest-first by BASE pace (id tie-break = deterministic), then split guaranteed vs bubble.
            var byBase = field
                .OrderByDescending(id => qualiById.GetValueOrDefault(id))
                .ThenBy(id => id, StringComparer.Ordinal)
                .ToList();
            var guaranteed = byBase.Take(guaranteedCount);

            // The bubble competes on a per-race jittered pace; the top (size − guaranteed) get the slots.
            var rolled = byBase
                .Skip(guaranteedCount)
                .Select(id => (id, eff: qualiById.GetValueOrDefault(id) + Jitter(factory, pack.Season.Year, round.Round, seasonOrdinal, id)))
                .OrderByDescending(t => t.eff)
                .ThenBy(t => t.id, StringComparer.Ordinal)
                .Select(t => t.id)
                .Take(size - guaranteedCount);

            result[round.Round] = guaranteed.Concat(rolled).ToList();
        }
        return result;
    }

    private static double Jitter(StreamFactory factory, int year, int round, int seasonOrdinal, string driverId)
    {
        // Season 1 keeps the ORIGINAL driver-only key so the pinned creation roll is byte-identical.
        // Season 2+ folds the ordinal into the entity (StreamFactory escapes the '|', keeping the key
        // injective), so every season draws an independent field off the same master seed.
        string entity = seasonOrdinal <= 1
            ? driverId
            : seasonOrdinal.ToString(CultureInfo.InvariantCulture) + "|" + driverId;
        return (factory.CreateStream(Subsystem, year, round, entity).NextDouble() * 2.0 - 1.0) * JitterMagnitude;
    }

    /// <summary>The per-season DNQ RE-ROLL for the 17-season campaign: seasons 2+ get their OWN seeded
    /// backmarker field, so the rotation differs each season instead of every season sharing season 1's
    /// pinned roll. Rewrites each capped round's <c>grid.StarterDriverIds</c> on an IN-MEMORY pack, a
    /// same-pack carryover season has no separate pinned bytes to re-pin. The starter set IS a fold input
    /// (grid membership → seat-strength → the byte-compared player rows), so this MUST be applied
    /// IDENTICALLY on the live-fold pack (CareerSessionService.Pack) AND the replay pack (ResimulateCore),
    /// both fed the same ordinal, the transform is a pure function of (pack, ordinal, seed), so live and
    /// replay agree by construction. Season 1 (<paramref name="seasonOrdinal"/> ≤ 1), a non-smgp pack, or
    /// a full-field pack returns the pack VERBATIM: season 1 keeps its pinned creation roll, and every
    /// legacy / non-DNQ / non-smgp career is byte-identical. Per-career gated by
    /// <see cref="SmgpState.PerSeasonDnq"/> at the call sites so pre-change careers never re-roll.</summary>
    public static SeasonPack ForSeason(SeasonPack pack, int seasonOrdinal, ulong masterSeed)
    {
        if (seasonOrdinal <= 1 ||
            !string.Equals(pack.Manifest.CareerStyle, SmgpRules.CareerStyle, StringComparison.Ordinal) ||
            !HasDnqField(pack))
            return pack;

        var starters = Generate(pack, masterSeed, seasonOrdinal);
        if (starters.Count == 0)
            return pack;

        var newRounds = pack.Season.Rounds
            .Select(r => starters.TryGetValue(r.Round, out var s) && r.Grid is { } grid
                ? r with { Grid = grid with { StarterDriverIds = s } }
                : r)
            .ToList();
        return pack with { Season = pack.Season with { Rounds = newRounds } };
    }

    /// <summary>Rewrites <paramref name="seasonJson"/> so each round in <paramref name="startersByRound"/>
    /// carries the generated <c>grid.starterDriverIds</c> (replacing the authored/baked default). Rounds
    /// not in the map are untouched. Pure string transform, the result is what gets pinned.</summary>
    public static string ApplyToSeasonJson(
        string seasonJson, IReadOnlyDictionary<int, IReadOnlyList<string>> startersByRound)
    {
        var doc = JsonNode.Parse(seasonJson)
                  ?? throw new JsonException("season.json parsed to null.");

        foreach (var node in doc["rounds"]!.AsArray())
        {
            if (node is not JsonObject round || round["round"] is null)
                continue;
            int roundNumber = (int)round["round"]!;
            if (!startersByRound.TryGetValue(roundNumber, out var starters))
                continue;
            if (round["grid"] is not JsonObject grid)
                continue;

            var array = new JsonArray();
            foreach (string id in starters)
                array.Add(id);
            grid["starterDriverIds"] = array;
        }

        return doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
