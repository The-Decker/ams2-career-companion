using Companion.Core.Packs;

namespace Companion.Core.Grid;

/// <summary>
/// Pure round-grid resolution (no I/O): season pack + round number (+ optional player seat)
/// -> <see cref="GridPlan"/>. Seats are the entries whose rounds range covers the round, in
/// entries.json order, followed by the round's guest entries in their authored order. Ratings
/// merge in fixed precedence: pack driver baseline -> trackForm nudge for the round's track
/// (additive, clamped 0..1) -> the round's aiOverrides patch (absolute per-field values,
/// applied last so an authored override always beats the nudge).
/// </summary>
public static class RoundGridResolver
{
    public static GridPlan Resolve(SeasonPack pack, int round, PlayerSeat? playerSeat = null)
    {
        var packRound = pack.Season.Rounds.FirstOrDefault(r => r.Round == round)
            ?? throw new InvalidOperationException(
                $"Round {round} is not on the {pack.Manifest.PackId} calendar — " +
                $"the season has rounds {MinRound(pack)}-{MaxRound(pack)}.");

        var teamsById = IndexById(pack.Teams, t => t.Id, pack, "teams.json");
        var driversById = IndexById(pack.Drivers, d => d.Id, pack, "drivers.json");

        var seats = new List<GridSeat>();

        foreach (var entry in pack.Entries)
        {
            if (!ParseRounds(entry).Contains(round))
                continue;

            seats.Add(BuildSeat(
                pack, packRound,
                LookupDriver(driversById, entry.DriverId, pack, packRound),
                LookupTeam(teamsById, entry.TeamId, pack, packRound),
                entry.Number, entry.Ams2LiveryName, isGuest: false));
        }

        foreach (var guest in packRound.GuestEntries)
        {
            seats.Add(BuildSeat(
                pack, packRound,
                LookupDriver(driversById, guest.DriverId, pack, packRound),
                LookupTeam(teamsById, guest.TeamId, pack, packRound),
                guest.Number, guest.Ams2LiveryName, isGuest: true));
        }

        ThrowOnDuplicateLiveries(pack, packRound, seats);

        if (playerSeat is not null)
            seats = ApplyPlayerSeat(pack, packRound, seats, playerSeat);

        return new GridPlan
        {
            PackId = pack.Manifest.PackId,
            Year = pack.Season.Year,
            SeriesName = pack.Season.SeriesName,
            Ams2Class = pack.Season.Ams2Class,
            Round = packRound.Round,
            RoundName = packRound.Name,
            TrackId = packRound.Track.Id,
            Seats = seats,
        };
    }

    // ---------- seat construction ----------

    private static GridSeat BuildSeat(
        SeasonPack pack,
        PackRound round,
        PackDriver driver,
        PackTeam team,
        string? number,
        string ams2LiveryName,
        bool isGuest) => new()
    {
        DriverId = driver.Id,
        DriverName = driver.Name,
        Country = driver.Country,
        TeamId = team.Id,
        TeamName = team.Name,
        Number = number,
        Ams2LiveryName = ams2LiveryName,
        Ratings = MergeRatings(driver, round),
        Reliability = team.Reliability,
        WeightScalar = team.Performance.WeightScalar,
        PowerScalar = team.Performance.PowerScalar,
        DragScalar = team.Performance.DragScalar,
        IsGuest = isGuest,
    };

    /// <summary>Baseline -> trackForm -> aiOverrides. The trackForm nudge expresses per-venue
    /// FORM, so it moves the pace ratings (raceSkill, qualifyingSkill) and nothing else — a
    /// driver's aggression or blue-flag manners do not change with the venue. Nudged values
    /// clamp to 0..1; the aiOverrides patch then applies absolute per-field values verbatim.</summary>
    private static PackDriverRatings MergeRatings(PackDriver driver, PackRound round)
    {
        var ratings = driver.Ratings;

        if (driver.TrackForm.TryGetValue(round.Track.Id, out double nudge) && nudge != 0.0)
        {
            ratings = ratings with
            {
                RaceSkill = Math.Clamp(ratings.RaceSkill + nudge, 0.0, 1.0),
                QualifyingSkill = Math.Clamp(ratings.QualifyingSkill + nudge, 0.0, 1.0),
            };
        }

        if (round.AiOverrides.TryGetValue(driver.Id, out var patch))
        {
            ratings = ratings with
            {
                RaceSkill = patch.RaceSkill ?? ratings.RaceSkill,
                QualifyingSkill = patch.QualifyingSkill ?? ratings.QualifyingSkill,
                Aggression = patch.Aggression ?? ratings.Aggression,
                Defending = patch.Defending ?? ratings.Defending,
                Stamina = patch.Stamina ?? ratings.Stamina,
                Consistency = patch.Consistency ?? ratings.Consistency,
                StartReactions = patch.StartReactions ?? ratings.StartReactions,
                WetSkill = patch.WetSkill ?? ratings.WetSkill,
                TyreManagement = patch.TyreManagement ?? ratings.TyreManagement,
                AvoidanceOfMistakes = patch.AvoidanceOfMistakes ?? ratings.AvoidanceOfMistakes,
                BlueFlagConceding = patch.BlueFlagConceding ?? ratings.BlueFlagConceding,
                WeatherTyreChanges = patch.WeatherTyreChanges ?? ratings.WeatherTyreChanges,
                AvoidanceOfForcedMistakes = patch.AvoidanceOfForcedMistakes ?? ratings.AvoidanceOfForcedMistakes,
                FuelManagement = patch.FuelManagement ?? ratings.FuelManagement,
            };
        }

        return ratings;
    }

    // ---------- player seat ----------

    private static List<GridSeat> ApplyPlayerSeat(
        SeasonPack pack,
        PackRound round,
        List<GridSeat> seats,
        PlayerSeat playerSeat)
    {
        int index = seats.FindIndex(s =>
            string.Equals(s.Ams2LiveryName, playerSeat.Ams2LiveryName, StringComparison.Ordinal));

        if (index < 0)
        {
            throw new InvalidOperationException(
                $"Player livery '{playerSeat.Ams2LiveryName}' is not in the round-{round.Round} grid of " +
                $"{pack.Manifest.PackId} ({round.Name}). The binding is exact (case-sensitive); " +
                $"this round's liveries are: {string.Join(", ", seats.Select(s => $"'{s.Ams2LiveryName}'"))}.");
        }

        seats[index] = seats[index] with { IsPlayer = true };
        return seats;
    }

    // ---------- validation ----------

    private static void ThrowOnDuplicateLiveries(SeasonPack pack, PackRound round, List<GridSeat> seats)
    {
        var duplicates = seats
            .GroupBy(s => s.Ams2LiveryName, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .ToList();
        if (duplicates.Count == 0)
            return;

        var details = duplicates.Select(g =>
            $"'{g.Key}' is bound to {g.Count()} seats ({string.Join(", ", g.Select(s => s.DriverId))})");
        throw new InvalidOperationException(
            $"Round {round.Round} of {pack.Manifest.PackId} resolves duplicate liveries — one livery, " +
            $"one seat, or the game binds only one of them: {string.Join("; ", details)}.");
    }

    // ---------- lookups ----------

    private static RoundsRange ParseRounds(PackEntry entry)
    {
        if (!RoundsRange.TryParse(entry.Rounds, out var range, out var error))
        {
            throw new InvalidOperationException(
                $"Entry '{entry.Ams2LiveryName}' ({entry.DriverId}) has an invalid rounds " +
                $"expression '{entry.Rounds}': {error}");
        }
        return range;
    }

    private static Dictionary<string, T> IndexById<T>(
        IReadOnlyList<T> items, Func<T, string> id, SeasonPack pack, string filePart)
    {
        var byId = new Dictionary<string, T>(items.Count, StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (!byId.TryAdd(id(item), item))
            {
                throw new InvalidOperationException(
                    $"{pack.Manifest.PackId} {filePart} declares id '{id(item)}' more than once.");
            }
        }
        return byId;
    }

    private static PackDriver LookupDriver(
        Dictionary<string, PackDriver> drivers, string driverId, SeasonPack pack, PackRound round) =>
        drivers.TryGetValue(driverId, out var driver)
            ? driver
            : throw new InvalidOperationException(
                $"Round {round.Round} of {pack.Manifest.PackId} references driver '{driverId}', " +
                "which is not in drivers.json.");

    private static PackTeam LookupTeam(
        Dictionary<string, PackTeam> teams, string teamId, SeasonPack pack, PackRound round) =>
        teams.TryGetValue(teamId, out var team)
            ? team
            : throw new InvalidOperationException(
                $"Round {round.Round} of {pack.Manifest.PackId} references team '{teamId}', " +
                "which is not in teams.json.");

    private static int MinRound(SeasonPack pack) =>
        pack.Season.Rounds.Count == 0 ? 0 : pack.Season.Rounds.Min(r => r.Round);

    private static int MaxRound(SeasonPack pack) =>
        pack.Season.Rounds.Count == 0 ? 0 : pack.Season.Rounds.Max(r => r.Round);
}
