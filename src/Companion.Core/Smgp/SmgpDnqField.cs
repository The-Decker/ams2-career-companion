using System.Text.Json;
using System.Text.Json.Nodes;
using Companion.Core.Determinism;
using Companion.Core.Packs;

namespace Companion.Core.Smgp;

/// <summary>
/// The SMGP dynamic per-race DNQ field, as a SEEDED PER-CAREER roll (Mike: "a random generator should
/// determine the bottom 8 cars ... who stays and who goes for the next race"). The pack fields all its
/// painted cars (34), but the class shows at most ~26 liveries, so each round the slowest sit out — and
/// WHICH ones is a fresh roll of the backmarker bubble, seeded by the career's master seed, so the
/// rotation differs per playthrough and is revealed race by race.
///
/// The strong are never at risk: the top <c>size − churn</c> cars by qualifying pace ALWAYS qualify;
/// only the bubble (the rest) rolls for the remaining slots. The roll is a deterministic PCG32 draw per
/// (round, driver) via <see cref="StreamFactory"/>, so a career re-derives its exact field — and because
/// this runs at CAREER CREATION and the generated starters are pinned into <c>season.json</c> (exactly
/// like <see cref="AlternateTrackTransform"/> / the modded field), the fold reads them and replays stay
/// byte-identical with no fold change and no seed threading. Existing careers keep their pinned starters.
/// </summary>
public static class SmgpDnqField
{
    /// <summary>How many of a round's qualifying spots are CONTESTED (rolled) — the rest of the grid is
    /// the guaranteed fast runners. Larger = a bigger bubble rotates. Structural, tunable.</summary>
    private const int Churn = 6;

    /// <summary>The per-race qualifying-pace jitter magnitude (±): big enough to reshuffle the bubble,
    /// far smaller than the gap from the bubble up to the guaranteed runners, so a strong car can never
    /// be rolled out.</summary>
    private const double JitterMagnitude = 0.12;

    private const string Subsystem = "smgp-dnq";

    /// <summary>True when at least one round caps its grid below the full field — i.e. there is a DNQ
    /// tail to roll. False for a full-field pack (nothing to do).</summary>
    public static bool HasDnqField(SeasonPack pack)
    {
        int field = pack.Entries.Select(e => e.DriverId).Distinct(StringComparer.Ordinal).Count();
        return pack.Season.Rounds.Any(r => r.Grid is { } g && g.Size < field);
    }

    /// <summary>Rolls each capped round's qualifiers from the master seed: the top <c>size − churn</c> by
    /// base qualifying skill are guaranteed; the bubble competes for the remaining slots on a per-race
    /// jittered pace. Returns round number → the (grid.size) starter driver ids. Rounds without a DNQ cap
    /// are omitted (their authored grid stands).</summary>
    public static IReadOnlyDictionary<int, IReadOnlyList<string>> Generate(SeasonPack pack, ulong masterSeed)
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
                .Select(id => (id, eff: qualiById.GetValueOrDefault(id) + Jitter(factory, pack.Season.Year, round.Round, id)))
                .OrderByDescending(t => t.eff)
                .ThenBy(t => t.id, StringComparer.Ordinal)
                .Select(t => t.id)
                .Take(size - guaranteedCount);

            result[round.Round] = guaranteed.Concat(rolled).ToList();
        }
        return result;
    }

    private static double Jitter(StreamFactory factory, int year, int round, string driverId) =>
        (factory.CreateStream(Subsystem, year, round, driverId).NextDouble() * 2.0 - 1.0) * JitterMagnitude;

    /// <summary>Rewrites <paramref name="seasonJson"/> so each round in <paramref name="startersByRound"/>
    /// carries the generated <c>grid.starterDriverIds</c> (replacing the authored/baked default). Rounds
    /// not in the map are untouched. Pure string transform — the result is what gets pinned.</summary>
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
