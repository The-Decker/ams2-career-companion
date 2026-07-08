using Companion.Core.Character;
using Companion.Core.Packs;

namespace Companion.Core.Grid;

/// <summary>
/// Pure round-grid resolution (no I/O): season pack + round number (+ optional player seat)
/// -> <see cref="GridPlan"/>. Seats are the entries whose rounds range covers the round, in
/// entries.json order, followed by the round's guest entries in their authored order. Ratings
/// merge in fixed precedence: pack driver baseline -> trackForm nudge for the round's track
/// (additive, clamped 0..1) -> the round's aiOverrides patch (absolute per-field values,
/// applied last so an authored override always beats the nudge).
///
/// When the round carries a <see cref="PackRoundGrid"/>, the regular (non-guest) seats are the
/// covering entries INTERSECTED with grid.starterDriverIds — the drivers who actually started
/// that round historically — so a pack that lists every season entrant no longer overfills the
/// grid with pre-qualifiers who mostly DNQ'd. Guests still append, the player's seat is always
/// present (they take a real seat even if the driver they replaced was a non-starter that round),
/// and a final safety cap trims to grid.size keeping the player and the highest-rated AI. A round
/// with no grid block keeps the pre-grid behaviour: every covering entry fills the grid.
/// </summary>
public static class RoundGridResolver
{
    public static GridPlan Resolve(
        SeasonPack pack, int round, PlayerSeat? playerSeat = null, GridSelection? selection = null,
        bool capToGridSize = true)
    {
        var packRound = pack.Season.Rounds.FirstOrDefault(r => r.Round == round)
            ?? throw new InvalidOperationException(
                $"Round {round} is not on the {pack.Manifest.PackId} calendar — " +
                $"the season has rounds {MinRound(pack)}-{MaxRound(pack)}.");

        var teamsById = IndexById(pack.Teams, t => t.Id, pack, "teams.json");
        var driversById = IndexById(pack.Drivers, d => d.Id, pack, "drivers.json");

        // When the round has a historical grid, only its listed starters seat from entries.json;
        // covering entries whose driver did not start that round stay out of the grid (they remain
        // in the pack — available for one-off drives / divergence — but do not fill every round).
        HashSet<string>? starters = packRound.Grid is { StarterDriverIds.Count: > 0 } grid
            ? new HashSet<string>(grid.StarterDriverIds, StringComparer.Ordinal)
            : null;

        // The player always takes a REAL seat: if they drive a covering entry that did not start
        // this round, add that driver to the starter set so the intersection keeps it. (An unknown
        // player livery is diagnosed later by ApplyPlayerSeat, which lists the round's grid.)
        if (starters is not null && playerSeat is not null)
        {
            foreach (var entry in pack.Entries)
            {
                if (string.Equals(entry.Ams2LiveryName, playerSeat.Ams2LiveryName, StringComparison.Ordinal)
                    && ParseRounds(entry).Contains(round))
                {
                    starters.Add(entry.DriverId);
                    break;
                }
            }
        }

        // "Choose the entire grid": when the career carries a field selection, only its chosen
        // liveries seat — EXCEPT the player's own livery, which is always kept so a chosen field can
        // never bench the player. Null selection = the whole pack field (byte-identical identity).
        bool Selected(string livery) =>
            selection is null || selection.Includes(livery) ||
            (playerSeat is not null && string.Equals(livery, playerSeat.Ams2LiveryName, StringComparison.Ordinal));

        var seats = new List<GridSeat>();

        foreach (var entry in pack.Entries)
        {
            if (!ParseRounds(entry).Contains(round))
                continue;
            if (starters is not null && !starters.Contains(entry.DriverId))
                continue;
            if (!Selected(entry.Ams2LiveryName))
                continue;

            seats.Add(BuildSeat(
                pack, packRound,
                LookupDriver(driversById, entry.DriverId, pack, packRound),
                LookupTeam(teamsById, entry.TeamId, pack, packRound),
                entry.Number, entry.Ams2LiveryName, isGuest: false));
        }

        foreach (var guest in packRound.GuestEntries)
        {
            if (!Selected(guest.Ams2LiveryName))
                continue;

            seats.Add(BuildSeat(
                pack, packRound,
                LookupDriver(driversById, guest.DriverId, pack, packRound),
                LookupTeam(teamsById, guest.TeamId, pack, packRound),
                guest.Number, guest.Ams2LiveryName, isGuest: true));
        }

        ThrowOnDuplicateLiveries(pack, packRound, seats);

        if (playerSeat is not null)
            seats = ApplyPlayerSeat(pack, packRound, seats, playerSeat);

        // The cap trims the field to the track's grid size for the SIM. Staging can opt out
        // (capToGridSize:false) to enumerate the whole qualified field — used only to name every
        // live-active livery so AMS2 never stock-fills a slot (cosmetic; the fold always caps).
        if (capToGridSize && packRound.Grid is { } capGrid)
            seats = CapToGridSize(seats, capGrid.Size);

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

    /// <summary>Safety cap: the intersection can still exceed grid.size when the historical grid
    /// itself was capped below the starter count by the track's Max AI participants (e.g. 1988
    /// Australia: 26 starters, Adelaide caps at 25). Trim to <paramref name="size"/> keeping the
    /// player seat unconditionally and, among the rest, the highest raceSkill (stable by original
    /// order on ties) — the field's slowest tail is what the game would drop anyway.</summary>
    private static List<GridSeat> CapToGridSize(List<GridSeat> seats, int size)
    {
        if (size < 1 || seats.Count <= size)
            return seats;

        var kept = seats
            .Select((seat, index) => (seat, index))
            .OrderByDescending(x => x.seat.IsPlayer)
            .ThenByDescending(x => x.seat.Ratings.RaceSkill)
            .ThenBy(x => x.index)
            .Take(size)
            .OrderBy(x => x.index)               // restore stable entries.json / guest order
            .Select(x => x.seat)
            .ToList();

        return kept;
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
            // The player takes over a real historical seat by livery, but the driver they replaced may
            // not have been entered EVERY round (they left or retired mid-season historically). The
            // player races the WHOLE season regardless: seat them from their livery's own entry — which
            // exists in the pack even when its rounds range excludes this round — so a mid-season
            // historical exit never benches the player. The final CapToGridSize keeps the player and
            // trims the slowest AI, so the field stays at grid.size. Only a livery that matches NO entry
            // at all is a genuine misconfiguration and still errors.
            var playerEntry = pack.Entries.FirstOrDefault(e =>
                string.Equals(e.Ams2LiveryName, playerSeat.Ams2LiveryName, StringComparison.Ordinal));
            if (playerEntry is null)
            {
                // Player-as-own-entrant: the chosen livery matches NO pack entry (a non-standard / custom
                // skin the player picked). Seat them as their OWN full-season synthetic entrant — a stable
                // synthetic driver id, a neutral independent team, and baseline ratings the character patch
                // then shapes — so a custom skin works AND the career never dead-ends on a livery no
                // historical driver holds. Existing careers pick a pack-entry livery, so they never reach
                // this branch (byte-identical). CapToGridSize keeps the player and trims the slowest AI.
                seats.Add(ApplyCharacter(
                    SyntheticPlayerSeat(playerSeat.Ams2LiveryName) with { IsPlayer = true },
                    playerSeat.Character));
                return seats;
            }

            var driversById = IndexById(pack.Drivers, d => d.Id, pack, "drivers.json");
            var teamsById = IndexById(pack.Teams, t => t.Id, pack, "teams.json");
            var addedSeat = BuildSeat(
                pack, round,
                LookupDriver(driversById, playerEntry.DriverId, pack, round),
                LookupTeam(teamsById, playerEntry.TeamId, pack, round),
                playerEntry.Number, playerEntry.Ams2LiveryName, isGuest: false);
            seats.Add(ApplyCharacter(addedSeat with { IsPlayer = true }, playerSeat.Character));
            return seats;
        }

        // Mark the player seat, then patch it from the character (last in the merge chain:
        // pack baseline → track form → aiOverrides → + character). No character = unchanged seat.
        seats[index] = ApplyCharacter(seats[index] with { IsPlayer = true }, playerSeat.Character);
        return seats;
    }

    /// <summary>Patches the player seat's ratings (talent stats + perk deltas) and car scalars
    /// (perk deltas) from the character. Null character returns the seat verbatim, so a pre-character
    /// career resolves a byte-identical grid.</summary>
    private static GridSeat ApplyCharacter(GridSeat seat, PlayerCharacterPatch? character)
    {
        if (character is null)
            return seat;

        var mods = character.Modifiers;
        return seat with
        {
            Ratings = CharacterRatingWriter.Apply(seat.Ratings, character.Profile, character.Rules, mods),
            WeightScalar = seat.WeightScalar + mods.WeightScalarDelta,
            PowerScalar = seat.PowerScalar + mods.PowerScalarDelta,
            DragScalar = seat.DragScalar + mods.DragScalarDelta,
        };
    }

    // ---------- player-as-own-entrant ----------

    /// <summary>Stable driver id for a player racing their OWN entrant (a non-pack / custom livery). One
    /// player ⇒ one id, so it never collides with a pack driver ("driver.&lt;name&gt;") and stays fixed
    /// across the whole career — the fold's player-identity key.</summary>
    public const string SyntheticPlayerDriverId = "driver.player-entrant";

    /// <summary>A neutral independent seat for the player-as-own-entrant path: mid-field baseline ratings
    /// (the character patch then shapes them), a neutral team (no physics scalars), on the player's chosen
    /// livery. The display name is a placeholder — the app shows the character's name via PlayerIdentity.</summary>
    private static GridSeat SyntheticPlayerSeat(string livery) => new()
    {
        DriverId = SyntheticPlayerDriverId,
        DriverName = "Privateer",
        Country = "",
        TeamId = "team.independent",
        TeamName = "Independent",
        Number = null,
        Ams2LiveryName = livery,
        Ratings = NeutralRatings,
        Reliability = 0.85,
        WeightScalar = 1.0,
        PowerScalar = 1.0,
        DragScalar = 1.0,
        IsGuest = false,
    };

    private static readonly PackDriverRatings NeutralRatings = new()
    {
        RaceSkill = 0.80,
        QualifyingSkill = 0.78,
        Aggression = 0.5,
        Defending = 0.5,
        Stamina = 0.8,
        Consistency = 0.8,
        StartReactions = 0.5,
        WetSkill = 0.5,
        TyreManagement = 0.7,
        FuelManagement = 0.7,
        BlueFlagConceding = 0.5,
        WeatherTyreChanges = 0.5,
        AvoidanceOfMistakes = 0.5,
        AvoidanceOfForcedMistakes = 0.5,
    };

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
