using Companion.Core.Scoring;

namespace Companion.Core.Smgp;

/// <summary>Per-driver result COUNTS accrued from actual races (a display-only projection over the
/// folded results, never a fold input). Points and titles are tracked separately (they need the
/// standings engine / season completion); this is the pure win/pole/podium/top-5/start tally.</summary>
public sealed record SmgpAccruedStats
{
    public int Starts { get; init; }
    public int Wins { get; init; }
    public int Podiums { get; init; }
    public int Poles { get; init; }
    public int Top5s { get; init; }

    public static SmgpAccruedStats Empty { get; } = new();

    internal SmgpAccruedStats Plus(int starts, int wins, int podiums, int poles, int top5s) => new()
    {
        Starts = Starts + starts,
        Wins = Wins + wins,
        Podiums = Podiums + podiums,
        Poles = Poles + poles,
        Top5s = Top5s + top5s,
    };
}

/// <summary>
/// Tallies wins / podiums / poles / top-5s / starts per driver across a set of races. Pure: the primary
/// RACE session's classification (position 1 = win, ≤3 = podium, ≤5 = top-5) and the qualifying pole
/// (the driver who qualified P1) are the only inputs. Used to grow the SMGP world's live record on top
/// of the predetermined baselines, the player from zero, the AI from their pre-history.
/// </summary>
public static class SmgpLiveStats
{
    /// <param name="rounds">One tuple per scored round: the primary race session's classification, and
    /// the pole driver id (qualifying P1) or null when the round ran no qualifying session.</param>
    public static IReadOnlyDictionary<string, SmgpAccruedStats> Accrue(
        IEnumerable<(IReadOnlyList<ClassifiedEntry> Race, string? PoleDriverId)> rounds)
    {
        var acc = new Dictionary<string, SmgpAccruedStats>(StringComparer.Ordinal);

        foreach (var (race, poleDriverId) in rounds)
        {
            foreach (var entry in race)
            {
                bool classified = entry.Status == FinishStatus.Classified && entry.Position is >= 1;
                int position = entry.Position ?? int.MaxValue;
                acc[entry.DriverId] = (acc.GetValueOrDefault(entry.DriverId) ?? SmgpAccruedStats.Empty).Plus(
                    starts: 1,
                    wins: classified && position == 1 ? 1 : 0,
                    podiums: classified && position <= 3 ? 1 : 0,
                    poles: 0,
                    top5s: classified && position <= 5 ? 1 : 0);
            }

            if (poleDriverId is { Length: > 0 })
                acc[poleDriverId] = (acc.GetValueOrDefault(poleDriverId) ?? SmgpAccruedStats.Empty)
                    .Plus(0, 0, 0, 1, 0);
        }

        return acc;
    }
}
