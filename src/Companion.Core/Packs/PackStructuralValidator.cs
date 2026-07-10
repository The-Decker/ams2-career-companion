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
        CheckWeekend(rounds, issues);
        CheckReferences(pack, teamIds, driverIds, issues);

        var entryRanges = ResolveEntryRanges(pack.Entries, rounds.Count, issues);
        CheckRoundCoverage(rounds, entryRanges, issues);
        CheckLiveryBindings(rounds, pack.Entries, entryRanges, issues);
        CheckGrids(rounds, pack.Entries, entryRanges, driverIds, issues);

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

            if (driver.Car is { } car)
                foreach (var (name, value) in car.Enumerate())
                    CheckCarValue($"Driver '{driver.Id}' car", name, value, issues);
        }
    }

    /// <summary>Car scalars hover around 1.0 (0.5..1.5 sane). vehicleReliability legitimately
    /// exceeds 1.0 in community sets (SMGP's 1.43) AND goes deeply NEGATIVE in the "Realistic"
    /// per-track blocks — a −19 at one venue is the community idiom for a scripted retirement
    /// there (the game clamps internally), so the floor admits those. Staged-file-only data, but
    /// a typo'd magnitude would still wreck the in-game field — validate loudly.</summary>
    private static void CheckCarValue(string owner, string name, double value, List<PackIssue> issues)
    {
        bool ok = name == "vehicleReliability" ? value is >= -25.0 and <= 2.0 : value is >= 0.5 and <= 1.5;
        if (!ok)
            issues.Add(Error(
                $"{owner} {name}={Num(value)} is outside the sane range " +
                $"({(name == "vehicleReliability" ? "-25..2" : "0.5..1.5")})."));
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

                foreach (var (name, value) in patch.EnumerateCar())
                    CheckCarValue($"Round {round.Round} aiOverrides for '{driverId}':", name, value, issues);
            }
        }
    }

    // ---------- weekend (per-session weather + durations) ----------

    /// <summary>Additive weekend-block sanity for the per-session weather + durations
    /// (SIM-INERT display data): each session/race declares at most 4 weather slots (AMS2's cap),
    /// and a declared duration is positive. Everything is optional, so a round without a weekend —
    /// or a weekend without these fields — is not flagged, and the other bundled packs validate
    /// unchanged.</summary>
    private static void CheckWeekend(IReadOnlyList<PackRound> rounds, List<PackIssue> issues)
    {
        foreach (var round in rounds)
        {
            if (round.Weekend is not { } weekend)
                continue;

            CheckWeekendSession(round, "practice", weekend.Practice, issues);
            CheckWeekendSession(round, "qualifying", weekend.Qualifying, issues);
            foreach (var race in weekend.Races)
                CheckWeatherSlots(round, $"race '{race.Id}'", race.WeatherSlots, issues);
        }
    }

    private static void CheckWeekendSession(
        PackRound round, string which, PackWeekendSession? session, List<PackIssue> issues)
    {
        if (session is null)
            return;

        if (session.DurationMinutes is { } minutes && minutes <= 0)
            issues.Add(Error(
                $"Round {round.Round} ({round.Name}) {which} durationMinutes is {minutes}; " +
                "a declared session length must be greater than 0."));

        CheckWeatherSlots(round, which, session.WeatherSlots, issues);
    }

    private static void CheckWeatherSlots(
        PackRound round, string which, IReadOnlyList<string>? slots, List<PackIssue> issues)
    {
        if (slots is null)
            return;

        if (slots.Count > 4)
            issues.Add(Error(
                $"Round {round.Round} ({round.Name}) {which} declares {slots.Count} weather slots; " +
                "AMS2 allows at most 4 per session."));

        if (slots.Any(string.IsNullOrWhiteSpace))
            issues.Add(Warning(
                $"Round {round.Round} ({round.Name}) {which} has a blank weather slot."));
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

    // ---------- optional historical grid ----------

    /// <summary>Grid block sanity (structural half — the venue AI-cap ceiling is a content check
    /// that lives in Companion.Ams2's preflight). grid.size must be at least 1 and must not exceed
    /// the number of entries whose rounds range covers the round (you cannot seat more historical
    /// starters than the pack has entries for that round); every starterDriverIds id must be a
    /// known pack driver. Grid is optional, so a round without one is not flagged.</summary>
    private static void CheckGrids(
        IReadOnlyList<PackRound> rounds,
        IReadOnlyList<PackEntry> entries,
        RoundsRange?[] entryRanges,
        HashSet<string> driverIds,
        List<PackIssue> issues)
    {
        foreach (var round in rounds)
        {
            if (round.Grid is not { } grid)
                continue;

            if (grid.Size < 1)
            {
                issues.Add(Error(
                    $"Round {round.Round} ({round.Name}) grid.size is {grid.Size}; a grid needs at least 1 car."));
            }

            foreach (var driverId in grid.StarterDriverIds)
            {
                if (!driverIds.Contains(driverId))
                    issues.Add(Error(
                        $"Round {round.Round} ({round.Name}) grid.starterDriverIds references unknown driver '{driverId}'."));
            }

            int covering = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entryRanges[i]?.Contains(round.Round) == true)
                    covering++;
            }
            covering += round.GuestEntries.Count;

            if (grid.Size > covering)
                issues.Add(Error(
                    $"Round {round.Round} ({round.Name}) grid.size {grid.Size} exceeds the {covering} " +
                    "entr(y/ies) covering the round — the grid cannot seat more cars than the pack provides."));

            // The setup guide is authored so the total grid is grid.size (player replaces one
            // historical starter): opponents should be exactly grid.size - 1.
            if (round.SetupGuide is { } guide && grid.Size >= 1 && guide.Session.Opponents != grid.Size - 1)
                issues.Add(Warning(
                    $"Round {round.Round} ({round.Name}) setupGuide opponents={guide.Session.Opponents} " +
                    $"but grid.size={grid.Size}; expected opponents = grid.size - 1 = {grid.Size - 1}."));
        }
    }

    // ---------- helpers ----------

    private static string Num(double value) => value.ToString(CultureInfo.InvariantCulture);

    private static PackIssue Error(string message) =>
        new() { Severity = PackIssueSeverity.Error, Message = message };

    private static PackIssue Warning(string message) =>
        new() { Severity = PackIssueSeverity.Warning, Message = message };
}
