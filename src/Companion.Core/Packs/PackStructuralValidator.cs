using System.Globalization;

namespace Companion.Core.Packs;

/// <summary>
/// Structural pack validation that needs NO content library: id integrity, calendar sanity,
/// entry coverage via rounds ranges, points-system resolution, rating ranges, and livery
/// double-binding. Content-dependent checks (class/track/livery existence, venue AI caps)
/// live in Companion.Ams2's preflight — Core never references them.
/// </summary>
public static class PackStructuralValidator
{
    public static PackValidationReport Validate(SeasonPack pack)
    {
        var issues = new List<PackIssue>();

        var teamIds = CollectUniqueIds(pack.Teams.Select(t => t.Id), "team", issues);
        var driverIds = CollectUniqueIds(pack.Drivers.Select(d => d.Id), "driver", issues);

        var rounds = pack.Season.Rounds;
        CheckRoundNumbering(rounds, issues);
        CheckDates(rounds, issues);
        CheckSetupGuides(rounds, issues);
        CheckPointsSystem(pack.Season, issues);
        CheckDriverRatings(pack.Drivers, issues);
        CheckAiOverrides(rounds, driverIds, issues);
        CheckReferences(pack, teamIds, driverIds, issues);

        var entryRanges = ResolveEntryRanges(pack.Entries, rounds.Count, issues);
        CheckRoundCoverage(rounds, entryRanges, issues);
        CheckLiveryBindings(rounds, pack.Entries, entryRanges, issues);

        return new PackValidationReport { Issues = issues };
    }

    // ---------- id integrity ----------

    private static HashSet<string> CollectUniqueIds(
        IEnumerable<string> ids, string kind, List<PackIssue> issues)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (!seen.Add(id))
                issues.Add(Error($"Duplicate {kind} id '{id}'."));
        }
        return seen;
    }

    private static void CheckReferences(
        SeasonPack pack, HashSet<string> teamIds, HashSet<string> driverIds, List<PackIssue> issues)
    {
        foreach (var entry in pack.Entries)
        {
            if (!teamIds.Contains(entry.TeamId))
                issues.Add(Error($"Entry #{entry.Number} ({entry.DriverId}) references unknown team '{entry.TeamId}'."));
            if (!driverIds.Contains(entry.DriverId))
                issues.Add(Error($"Entry #{entry.Number} references unknown driver '{entry.DriverId}'."));
        }

        foreach (var round in pack.Season.Rounds)
        {
            foreach (var guest in round.GuestEntries)
            {
                if (!teamIds.Contains(guest.TeamId))
                    issues.Add(Error($"Round {round.Round} guest entry ({guest.DriverId}) references unknown team '{guest.TeamId}'."));
                if (!driverIds.Contains(guest.DriverId))
                    issues.Add(Error($"Round {round.Round} guest entry references unknown driver '{guest.DriverId}'."));
            }
        }
    }

    // ---------- calendar ----------

    private static void CheckRoundNumbering(IReadOnlyList<PackRound> rounds, List<PackIssue> issues)
    {
        if (rounds.Count == 0)
        {
            issues.Add(Error("season.json has no rounds."));
            return;
        }

        for (int i = 0; i < rounds.Count; i++)
        {
            if (rounds[i].Round != i + 1)
            {
                issues.Add(Error(
                    $"Round numbers must be contiguous from 1: calendar position {i + 1} " +
                    $"has round number {rounds[i].Round}."));
                return;
            }
        }
    }

    private static void CheckDates(IReadOnlyList<PackRound> rounds, List<PackIssue> issues)
    {
        DateOnly? previous = null;
        foreach (var round in rounds)
        {
            if (!DateOnly.TryParseExact(
                    round.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                issues.Add(Error(
                    $"Round {round.Round} ({round.Name}) date '{round.Date}' is not a valid yyyy-MM-dd date."));
                continue;
            }

            if (previous is { } prev)
            {
                if (date < prev)
                    issues.Add(Error(
                        $"Round {round.Round} ({round.Name}) date {round.Date} is earlier than the previous " +
                        $"round's {prev:yyyy-MM-dd} — calendar dates must ascend."));
                else if (date == prev)
                    issues.Add(Warning(
                        $"Round {round.Round} ({round.Name}) shares its date {round.Date} with the previous round."));
            }

            previous = date;
        }
    }

    private static void CheckSetupGuides(IReadOnlyList<PackRound> rounds, List<PackIssue> issues)
    {
        foreach (var round in rounds)
        {
            if (round.Laps < 1)
                issues.Add(Error(
                    $"Round {round.Round} ({round.Name}) has laps={round.Laps}; every round needs at least 1 lap."));

            // v1.1 placeholder venues (locked decision #6): the historical identity must
            // survive the substitution.
            if (round.Track.IsPlaceholder && string.IsNullOrWhiteSpace(round.Track.RealVenue))
                issues.Add(Error(
                    $"Round {round.Round} ({round.Name}) uses a placeholder track but names no realVenue — " +
                    "the historical venue must stay on record."));

            if (round.SetupGuide is null)
            {
                issues.Add(round.Championship
                    ? Error($"Championship round {round.Round} ({round.Name}) has no setupGuide.")
                    : Warning($"Non-championship round {round.Round} ({round.Name}) has no setupGuide."));
                continue;
            }

            if (round.SetupGuide.Session.Opponents < 1)
                issues.Add(Error(
                    $"Round {round.Round} ({round.Name}) setupGuide has opponents=" +
                    $"{round.SetupGuide.Session.Opponents}; at least 1 opponent is required."));
        }
    }

    // ---------- points system ----------

    private static void CheckPointsSystem(SeasonDefinition season, List<PackIssue> issues)
    {
        int championshipRounds = season.Rounds.Count(r => r.Championship);
        if (championshipRounds == 0)
        {
            issues.Add(Error("Season has no championship rounds."));
            return;
        }

        try
        {
            season.PointsSystem.ResolveScoringDefinition(championshipRounds);
        }
        catch (Exception ex)
        {
            issues.Add(Error(
                $"pointsSystem does not resolve for {championshipRounds} championship rounds: {ex.Message}"));
        }
    }

    // ---------- ratings ----------

    private static void CheckDriverRatings(IReadOnlyList<PackDriver> drivers, List<PackIssue> issues)
    {
        foreach (var driver in drivers)
        {
            foreach (var (name, value) in driver.Ratings.Enumerate())
            {
                if (value is < 0.0 or > 1.0)
                    issues.Add(Error(
                        $"Driver '{driver.Id}' rating {name}={Num(value)} is outside 0..1."));
            }

            foreach (var (trackId, nudge) in driver.TrackForm)
            {
                if (Math.Abs(nudge) > 0.05 + 1e-9)
                    issues.Add(Warning(
                        $"Driver '{driver.Id}' trackForm nudge for '{trackId}' is {Num(nudge)}; " +
                        "expected within -0.05..+0.05."));
            }
        }
    }

    private static void CheckAiOverrides(
        IReadOnlyList<PackRound> rounds, HashSet<string> driverIds, List<PackIssue> issues)
    {
        foreach (var round in rounds)
        {
            foreach (var (driverId, patch) in round.AiOverrides)
            {
                if (!driverIds.Contains(driverId))
                    issues.Add(Warning(
                        $"Round {round.Round} aiOverrides references unknown driver '{driverId}'."));

                foreach (var (name, value) in patch.Enumerate())
                {
                    if (value is < 0.0 or > 1.0)
                        issues.Add(Error(
                            $"Round {round.Round} aiOverrides for '{driverId}': {name}={Num(value)} is outside 0..1."));
                }
            }
        }
    }

    // ---------- entries: rounds ranges, coverage, livery binding ----------

    private static RoundsRange?[] ResolveEntryRanges(
        IReadOnlyList<PackEntry> entries, int roundCount, List<PackIssue> issues)
    {
        var ranges = new RoundsRange?[entries.Count];
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (!RoundsRange.TryParse(entry.Rounds, out var range, out var error))
            {
                issues.Add(Error($"Entry #{entry.Number} ({entry.DriverId}): {error}"));
                continue;
            }

            if (range.Max > roundCount)
                issues.Add(Error(
                    $"Entry #{entry.Number} ({entry.DriverId}) rounds '{entry.Rounds}' includes round " +
                    $"{range.Max}, but the season has only {roundCount} rounds."));

            ranges[i] = range;
        }
        return ranges;
    }

    private static void CheckRoundCoverage(
        IReadOnlyList<PackRound> rounds, RoundsRange?[] entryRanges, List<PackIssue> issues)
    {
        foreach (var round in rounds)
        {
            if (!round.Championship)
                continue;

            int entrants = entryRanges.Count(r => r?.Contains(round.Round) == true)
                           + round.GuestEntries.Count;
            if (entrants == 0)
                issues.Add(Error($"Championship round {round.Round} ({round.Name}) has no entries."));
        }
    }

    private static void CheckLiveryBindings(
        IReadOnlyList<PackRound> rounds,
        IReadOnlyList<PackEntry> entries,
        RoundsRange?[] entryRanges,
        List<PackIssue> issues)
    {
        var duplicatedRoundsByLivery = new Dictionary<string, List<int>>(StringComparer.Ordinal);

        foreach (var round in rounds)
        {
            var liveries = new List<string>();
            for (int i = 0; i < entries.Count; i++)
            {
                if (entryRanges[i]?.Contains(round.Round) == true)
                    liveries.Add(entries[i].Ams2LiveryName);
            }
            foreach (var guest in round.GuestEntries)
                liveries.Add(guest.Ams2LiveryName);

            foreach (var group in liveries.GroupBy(l => l, StringComparer.Ordinal).Where(g => g.Count() > 1))
            {
                if (!duplicatedRoundsByLivery.TryGetValue(group.Key, out var list))
                    duplicatedRoundsByLivery[group.Key] = list = [];
                list.Add(round.Round);
            }
        }

        foreach (var (livery, dupRounds) in duplicatedRoundsByLivery)
        {
            issues.Add(Error(
                $"Livery '{livery}' is bound by more than one entry in round(s) " +
                $"{string.Join(", ", dupRounds)} — only one driver can bind to a livery per race."));
        }
    }

    // ---------- helpers ----------

    private static string Num(double value) => value.ToString(CultureInfo.InvariantCulture);

    private static PackIssue Error(string message) =>
        new() { Severity = PackIssueSeverity.Error, Message = message };

    private static PackIssue Warning(string message) =>
        new() { Severity = PackIssueSeverity.Warning, Message = message };
}
