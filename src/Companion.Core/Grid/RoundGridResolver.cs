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
///
/// <para>Ratings Phase 3: when <c>applyWeekendForm</c> is set (a FormAware career), the round's
/// per-race <see cref="Companion.Core.Packs.SeasonDefinition.DriverForm"/> is overlaid onto the AI
/// seats' pace ratings AFTER the cap — the same additive, clamped nudge the staged AMS2 file gets —
/// so the field the sim scores reacts to who is hot that weekend. Default off, so a pre-Phase-3 (or
/// form-less) career resolves a byte-identical grid.</para>
/// </summary>
public static class RoundGridResolver
{
    public static GridPlan Resolve(
        SeasonPack pack, int round, PlayerSeat? playerSeat = null, GridSelection? selection = null,
        bool capToGridSize = true, bool applyWeekendForm = false,
        IReadOnlyDictionary<string, string>? seatOverrides = null, string? playerSeatOverride = null)
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
        // SMGP seat movements are cap-PROTECTED: every car a pending override touches (targets,
        // the movers' current cars, the player's earned car) survives the trim like the player
        // seat, else a small-track round would starve ApplySeatOverrides' closure check and the
        // whole swap set would silently refuse. Null without overrides ⇒ byte-identical.
        if (capToGridSize && packRound.Grid is { } capGrid)
            seats = CapToGridSize(seats, capGrid.Size, ProtectedLiveries(seats, seatOverrides, playerSeatOverride));

        // SMGP seat swaps (M3 slice 3): reseat drivers onto other cars AFTER the cap — driver
        // identity/ratings move, the cars (team, livery, scalars) stay put, membership never
        // changes. The player's block rides to playerSeatOverride (SmgpState.CurrentSeatLivery).
        // Default null ⇒ untouched ⇒ byte-identical; the oracle never passes either.
        if (seatOverrides is { Count: > 0 } || playerSeatOverride is not null)
            seats = ApplySeatOverrides(pack, packRound, seats, seatOverrides, playerSeatOverride);

        // Ratings Phase 3 (FormAware careers only): overlay the round's per-race form onto the AI
        // seats' pace ratings AFTER the cap, so form perturbs strength/pace but never grid
        // MEMBERSHIP. Default off ⇒ existing callers + form-less packs resolve a byte-identical grid.
        if (applyWeekendForm)
            seats = ApplyWeekendForm(seats, pack.Season.DriverForm?.GetValueOrDefault(round));

        // The character patch lands LAST, on wherever the player's block ended up — so an SMGP
        // seat swap carries the perk car scalars to the player's NEW car instead of leaving them
        // baked into the old one for a rival to inherit. Without overrides the player sits on the
        // same seat this always patched, so every existing career resolves byte-identically (the
        // cap keeps the player by flag, not rating; the form overlay skips the player seat).
        if (playerSeat?.Character is { } characterPatch)
        {
            int patchIndex = seats.FindIndex(s => s.IsPlayer);
            if (patchIndex >= 0)
                seats[patchIndex] = ApplyCharacter(seats[patchIndex], characterPatch);
        }

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
    /// player seat (and any override-protected cars) unconditionally and, among the rest, the
    /// highest raceSkill (stable by original order on ties) — the field's slowest tail is what
    /// the game would drop anyway.</summary>
    private static List<GridSeat> CapToGridSize(
        List<GridSeat> seats, int size, IReadOnlyCollection<string>? protectedLiveries = null)
    {
        if (size < 1 || seats.Count <= size)
            return seats;

        var kept = seats
            .Select((seat, index) => (seat, index))
            .OrderByDescending(x => x.seat.IsPlayer)
            .ThenByDescending(x => protectedLiveries?.Contains(x.seat.Ams2LiveryName) == true)
            .ThenByDescending(x => x.seat.Ratings.RaceSkill)
            .ThenBy(x => x.index)
            .Take(size)
            .OrderBy(x => x.index)               // restore stable entries.json / guest order
            .Select(x => x.seat)
            .ToList();

        return kept;
    }

    /// <summary>The cars a pending SMGP seat-movement set touches — every override TARGET, every
    /// overridden driver's CURRENT car (his block must exist on the grid to move), and the
    /// player's earned car. Null when no overrides ride this resolve, so the cap's ordering (and
    /// every non-smgp career's grid) is byte-identical.</summary>
    private static HashSet<string>? ProtectedLiveries(
        List<GridSeat> seats, IReadOnlyDictionary<string, string>? seatOverrides, string? playerSeatOverride)
    {
        if (seatOverrides is not { Count: > 0 } && playerSeatOverride is null)
            return null;

        var protectedSet = new HashSet<string>(StringComparer.Ordinal);
        if (playerSeatOverride is not null)
            protectedSet.Add(playerSeatOverride);
        if (seatOverrides is not null)
        {
            foreach (var (driverId, livery) in seatOverrides)
            {
                protectedSet.Add(livery);
                if (seats.FirstOrDefault(s => string.Equals(s.DriverId, driverId, StringComparison.Ordinal))
                    is { } mover)
                    protectedSet.Add(mover.Ams2LiveryName);
            }
        }
        return protectedSet;
    }

    // ---------- SMGP seat overrides (M3) ----------

    /// <summary>Reseats drivers onto other cars — the SMGP swap ladder's view of the grid. Each
    /// move (driver id → target livery, plus the player's block riding to
    /// <paramref name="playerSeatOverride"/>) transplants the DRIVER side of the seat (identity,
    /// ratings, driver car tuning, the IsPlayer mark) onto the target CAR (team, livery, number,
    /// reliability, scalars — which stay with the livery, exactly like the game's own swaps). A
    /// moved driver with NO seat on the grid but authored in the pack (the reserved title-defense
    /// challenger, who holds no season entry) is INTRODUCED onto the target car — its authored
    /// occupant loses the ride. STRICTLY ALL-OR-NOTHING: a move whose target car or mover is
    /// unresolvable, two moves booking one car, an unfilled vacated car, or a receiving car whose
    /// sitting driver did not move (unless he is being deliberately replaced by an introduction —
    /// never the player) each refuse the ENTIRE set and resolve the grid verbatim. A partial
    /// application could otherwise overwrite the player's seat or field a driver twice; verbatim
    /// keeps every round foldable. No moves ⇒ byte-identical.</summary>
    private static List<GridSeat> ApplySeatOverrides(
        SeasonPack pack, PackRound packRound,
        List<GridSeat> seats, IReadOnlyDictionary<string, string>? seatOverrides, string? playerSeatOverride)
    {
        var moves = new Dictionary<string, string>(StringComparer.Ordinal);
        if (seatOverrides is not null)
        {
            foreach (var pair in seatOverrides)
                moves[pair.Key] = pair.Value;
        }
        if (playerSeatOverride is not null &&
            seats.FirstOrDefault(s => s.IsPlayer) is { } playerSeat &&
            !string.Equals(playerSeat.Ams2LiveryName, playerSeatOverride, StringComparison.Ordinal))
        {
            moves[playerSeat.DriverId] = playerSeatOverride;
        }
        if (moves.Count == 0)
            return seats;

        var byDriverId = new Dictionary<string, GridSeat>(StringComparer.Ordinal);
        var byLivery = new Dictionary<string, GridSeat>(StringComparer.Ordinal);
        foreach (var seat in seats)
        {
            byDriverId[seat.DriverId] = seat;
            byLivery[seat.Ams2LiveryName] = seat;
        }

        // incoming: target livery -> the driver block arriving there.
        var incoming = new Dictionary<string, GridSeat>(StringComparer.Ordinal);
        var vacated = new HashSet<string>(StringComparer.Ordinal);
        var introducedTargets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (driverId, livery) in moves)
        {
            if (!byLivery.ContainsKey(livery))
                return seats; // target car absent this round → refuse all

            GridSeat block;
            if (byDriverId.TryGetValue(driverId, out var source))
            {
                block = source;
                vacated.Add(source.Ams2LiveryName);
            }
            else if (pack.Drivers.FirstOrDefault(d =>
                         string.Equals(d.Id, driverId, StringComparison.Ordinal)) is { } introduced)
            {
                // The driver side only — the car block comes from the target seat below.
                block = new GridSeat
                {
                    DriverId = introduced.Id,
                    DriverName = introduced.Name,
                    Country = introduced.Country,
                    TeamId = "", // never read: only the driver-side fields transplant
                    TeamName = "",
                    Ams2LiveryName = livery,
                    Ratings = MergeRatings(introduced, packRound),
                    CarTuning = MergeCarTuning(introduced, packRound),
                    Reliability = 0.0,
                    WeightScalar = 1.0,
                    PowerScalar = 1.0,
                    DragScalar = 1.0,
                    IsGuest = false,
                };
                introducedTargets.Add(livery);
            }
            else
            {
                return seats; // the mover is neither on the grid nor authored → refuse all
            }

            if (!incoming.TryAdd(livery, block))
                return seats; // two drivers booked onto one car → refuse all
        }

        // Closure, both directions: every vacated car is refilled, and every receiving car was
        // either vacated by its own driver's move or holds a non-player driver being REPLACED by
        // an INTRODUCTION (the only move allowed to push someone off the field). Anything else
        // would strand or duplicate someone → refuse all.
        if (!vacated.IsSubsetOf(incoming.Keys))
            return seats;
        foreach (var livery in incoming.Keys)
        {
            if (vacated.Contains(livery))
                continue;
            if (!introducedTargets.Contains(livery) || byLivery[livery].IsPlayer)
                return seats;
        }

        return seats
            .Select(car => incoming.TryGetValue(car.Ams2LiveryName, out var occupant)
                ? car with
                {
                    DriverId = occupant.DriverId,
                    DriverName = occupant.DriverName,
                    Country = occupant.Country,
                    Ratings = occupant.Ratings,
                    CarTuning = occupant.CarTuning,
                    IsPlayer = occupant.IsPlayer,
                }
                : car)
            .ToList();
    }

    // ---------- Ratings Phase 3: per-race form overlay ----------

    /// <summary>Overlays the round's per-race form deltas onto the AI seats' pace ratings — the SAME
    /// additive, 0..1-clamped nudge the staged AMS2 file gets (<c>GridStager.Nudge</c>), so the grid
    /// the sim scores equals the grid AMS2 shows. The PLAYER seat is deliberately excluded: the player
    /// is not the historical driver they replaced (their ability is tracked by OPI / pace anchor), so a
    /// hot weekend must move only the FIELD around them, never their own strength term. Null/empty
    /// form, a zero delta, or a driver id absent from the round map ⇒ the seat is returned verbatim, so
    /// a form-less pack (or a non-FormAware career, which never calls this) resolves byte-identically.</summary>
    private static List<GridSeat> ApplyWeekendForm(
        List<GridSeat> seats, IReadOnlyDictionary<string, PackDriverForm>? roundForm)
    {
        if (roundForm is null || roundForm.Count == 0)
            return seats;

        return seats
            .Select(seat =>
            {
                if (seat.IsPlayer || !roundForm.TryGetValue(seat.DriverId, out var form))
                    return seat;
                return seat with
                {
                    Ratings = seat.Ratings with
                    {
                        RaceSkill = NudgeForm(seat.Ratings.RaceSkill, form.RaceSkill),
                        QualifyingSkill = NudgeForm(seat.Ratings.QualifyingSkill, form.QualifyingSkill),
                    },
                };
            })
            .ToList();
    }

    /// <summary>Additive form delta, clamped to 0..1 — mirrors <c>GridStager.Nudge</c> exactly so a
    /// scored AI rating equals the staged AMS2 file's. A zero delta returns the base verbatim.</summary>
    private static double NudgeForm(double baseValue, double delta) =>
        delta != 0.0 ? Math.Clamp(baseValue + delta, 0.0, 1.0) : baseValue;

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
        CarTuning = MergeCarTuning(driver, round),
        IsGuest = isGuest,
    };

    /// <summary>Driver-level car block with the round's per-driver aiOverrides car fields on top
    /// (patch wins per field). STAGING-ONLY — the staged file prefers it over the team values;
    /// the sim's seat-strength model never reads it. Null when neither authors anything.</summary>
    private static PackDriverCar? MergeCarTuning(PackDriver driver, PackRound round)
    {
        var car = driver.Car;
        if (round.AiOverrides.TryGetValue(driver.Id, out var patch))
        {
            var merged = new PackDriverCar
            {
                WeightScalar = patch.WeightScalar ?? car?.WeightScalar,
                PowerScalar = patch.PowerScalar ?? car?.PowerScalar,
                DragScalar = patch.DragScalar ?? car?.DragScalar,
                VehicleReliability = patch.VehicleReliability ?? car?.VehicleReliability,
            };
            return merged.IsEmpty ? null : merged;
        }
        return car is { IsEmpty: false } ? car : null;
    }

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
                SetupDownforce = patch.SetupDownforce ?? ratings.SetupDownforce,
                SetupDownforceRandomness = patch.SetupDownforceRandomness ?? ratings.SetupDownforceRandomness,
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

        // DISTINCT-DRIVER player (the SMGP clean-swap model): the player is their OWN driver, not the
        // authored occupant of the car they sit in. Stamp the player's id onto the car and DROP its
        // authored AI (benched — he re-appears the moment the player moves to another car, because a
        // FRESH resolve only ever benches the CURRENT seat's driver). Everyone else keeps their home
        // seat, so a seat swap never cascades. Null DriverId keeps the historical "wear the seat's own
        // driver id" behavior below (byte-identical for every non-SMGP / pre-change career).
        if (playerSeat.DriverId is { } distinctId)
        {
            if (index >= 0)
            {
                seats[index] = seats[index] with { DriverId = distinctId, IsPlayer = true };
                return seats;
            }
            // The player's car did not make this round's cut (pre-qualifying) — add it from its own
            // entry with the player's id, so the player always races (CapToGridSize trims the slowest AI).
            var ownEntry = pack.Entries.FirstOrDefault(e =>
                string.Equals(e.Ams2LiveryName, playerSeat.Ams2LiveryName, StringComparison.Ordinal));
            if (ownEntry is not null)
            {
                var driversById2 = IndexById(pack.Drivers, d => d.Id, pack, "drivers.json");
                var teamsById2 = IndexById(pack.Teams, t => t.Id, pack, "teams.json");
                var addedOwn = BuildSeat(
                    pack, round,
                    LookupDriver(driversById2, ownEntry.DriverId, pack, round),
                    LookupTeam(teamsById2, ownEntry.TeamId, pack, round),
                    ownEntry.Number, ownEntry.Ams2LiveryName, isGuest: false);
                seats.Add(addedOwn with { DriverId = distinctId, IsPlayer = true });
                return seats;
            }
            // A custom livery matching no entry: the player's own synthetic entrant.
            seats.Add(SyntheticPlayerSeat(playerSeat.Ams2LiveryName) with { DriverId = distinctId, IsPlayer = true });
            return seats;
        }

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
                seats.Add(SyntheticPlayerSeat(playerSeat.Ams2LiveryName) with { IsPlayer = true });
                return seats;
            }

            var driversById = IndexById(pack.Drivers, d => d.Id, pack, "drivers.json");
            var teamsById = IndexById(pack.Teams, t => t.Id, pack, "teams.json");
            var addedSeat = BuildSeat(
                pack, round,
                LookupDriver(driversById, playerEntry.DriverId, pack, round),
                LookupTeam(teamsById, playerEntry.TeamId, pack, round),
                playerEntry.Number, playerEntry.Ams2LiveryName, isGuest: false);
            seats.Add(addedSeat with { IsPlayer = true });
            return seats;
        }

        // Mark the player seat only — the character patch is applied by Resolve as the LAST
        // step (after cap / SMGP seat overrides / form), so it always lands on the car the
        // player actually drives. No character = unchanged seat either way.
        seats[index] = seats[index] with { IsPlayer = true };
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
        // Per-driver car tuning (staged-file-only) gets the same perk deltas, so a character's
        // car tweaks survive on a pack that authors juppo-style tuning for the player's seat.
        var tuning = seat.CarTuning;
        if (tuning is not null)
            tuning = tuning with
            {
                WeightScalar = tuning.WeightScalar is { } w ? w + mods.WeightScalarDelta : null,
                PowerScalar = tuning.PowerScalar is { } p ? p + mods.PowerScalarDelta : null,
                DragScalar = tuning.DragScalar is { } d ? d + mods.DragScalarDelta : null,
            };
        return seat with
        {
            Ratings = CharacterRatingWriter.Apply(seat.Ratings, character.Profile, character.Rules, mods),
            WeightScalar = seat.WeightScalar + mods.WeightScalarDelta,
            PowerScalar = seat.PowerScalar + mods.PowerScalarDelta,
            DragScalar = seat.DragScalar + mods.DragScalarDelta,
            CarTuning = tuning,
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
