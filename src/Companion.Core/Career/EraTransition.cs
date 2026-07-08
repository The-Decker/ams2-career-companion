using Companion.Core.Character;
using Companion.Core.Determinism;
using Companion.Core.Packs;

namespace Companion.Core.Career;

/// <summary>
/// Everything needed to start the first season of the next era pack, derived by
/// <see cref="EraTransition.Build(SeasonPack, SeasonPack, SeasonEndResult, PlayerCareerState, PlayerOffer, StreamFactory, AgingCurveSet, IReadOnlyDictionary{string, int}?)"/>.
/// The Data layer persists it via CareerStore.StartNextSeason; replay re-derives it through
/// the same function and compares — the plan is pure derived state plus the one player choice
/// (the accepted offer) that fed it.
/// </summary>
public sealed record TransitionPlan
{
    public required string FromPackId { get; init; }

    public required string ToPackId { get; init; }

    public required int FromYear { get; init; }

    public required int ToYear { get; init; }

    /// <summary>The seasons between the packs that nobody plays (1967 → 1969 bridges 1968),
    /// ascending. v1 BRIDGES them: everyone ages through the gap and the aging + retirement
    /// streams run once per bridged year, keyed with that year — deterministic.</summary>
    public required IReadOnlyList<int> BridgedYears { get; init; }

    /// <summary>The player's start-of-new-season state: reputation/OPI/pace anchor carried
    /// verbatim (the Budget-Unit rescale is identity in v1), seated at the accepted team's
    /// resolved entry in the new pack. When <see cref="ValidationErrors"/> is non-empty the
    /// seat could not resolve and the team/livery are carried unchanged.</summary>
    public required PlayerCareerState Player { get; init; }

    /// <summary>Start-of-season AI driver states in the NEW pack's drivers.json order,
    /// excluding the player's seat: carried states (bridged age + aging-drift deltas
    /// re-clamped against the new baselines) where the driver id matches, fresh states for
    /// new-era entries.</summary>
    public required IReadOnlyList<DriverCareerState> Drivers { get; init; }

    /// <summary>Start-of-season team states in the NEW pack's teams.json order: the tier
    /// drift result carries by lineage id (clamped 1–5), new-era teams start at their
    /// authored budget tier.</summary>
    public required IReadOnlyList<TeamCareerState> Teams { get; init; }

    /// <summary>The transition's journal events, in order: one era.bridge per bridged year,
    /// era.departed per entity that does not reach the new era, then the era.economy
    /// Budget-Unit rescale note. The Data layer journals them under the new season.</summary>
    public required IReadOnlyList<JournalEvent> Events { get; init; }

    /// <summary>Human-readable problems that block the transition (the UI surfaces them):
    /// v1, the accepted offer's team missing from the new pack or having no entries.</summary>
    public required IReadOnlyList<string> ValidationErrors { get; init; }

    /// <summary>The accepted team's id in the new pack, when it resolved.</summary>
    public string? PlayerTeamId { get; init; }

    /// <summary>The resolved seat's exact livery name, when it resolved.</summary>
    public string? PlayerSeatLiveryName { get; init; }

    /// <summary>The new-pack driver whose entry the player takes — excluded from
    /// <see cref="Drivers"/>, exactly like the wizard excludes the season-1 seat pick.</summary>
    public string? DisplacedDriverId { get; init; }

    /// <summary>Budget-Unit rescale across the era boundary. v1: identity (1.0) — the seam
    /// for the Phase-2 economy (era inflation rescale) lives here and is journaled.</summary>
    public double BudgetRescaleFactor { get; init; } = 1.0;
}

/// <summary>
/// Era transition v1 (PLAN M6): carries a career from one season pack into the next. Pure
/// function — no I/O, no clocks; every random draw comes from named, keyed streams, so the
/// same inputs + master seed rebuild the identical plan (the replay contract).
///
/// Lineage mapping: teams match by <see cref="PackTeam.Id"/> and drivers by
/// <see cref="PackDriver.Id"/> — lineage ids are stable across era packs (both shipped packs
/// use the same "team.lotus" / "driver.jim_clark" convention). Unmatched new-pack entities
/// get fresh state; entities with no new-pack match are journaled as departed.
///
/// Gap years bridge (never block) as long as the target year is later than the source year:
/// everyone ages by the gap, and the aging + retirement hazard streams run once per bridged
/// year, keyed with the bridged year — the exact streams a played season of that year would
/// have consumed. A target year at or before the source year throws.
/// </summary>
public static class EraTransition
{
    /// <summary>The primary shape: builds the plan from the finished season's pipeline
    /// output. <paramref name="playerState"/> is the player end state to carry (normally
    /// <c>seasonEndResult.Player</c>; callers holding a later fold may pass that instead).</summary>
    public static TransitionPlan Build(
        SeasonPack fromPack,
        SeasonPack toPack,
        SeasonEndResult seasonEndResult,
        PlayerCareerState playerState,
        PlayerOffer acceptedOffer,
        StreamFactory streams,
        AgingCurveSet agingCurves,
        IReadOnlyDictionary<string, int>? canonRetirements = null,
        IReadOnlyList<CharacterSpend>? spends = null,
        CharacterRules? characterRules = null,
        int? fromYearOverride = null,
        int? toYearOverride = null) =>
        Build(
            fromPack, toPack, seasonEndResult.Drivers, seasonEndResult.Teams,
            playerState, acceptedOffer, streams, agingCurves, canonRetirements, spends, characterRules,
            fromYearOverride, toYearOverride);

    /// <summary>State-list overload for callers that persisted the season's end states and
    /// no longer hold the <see cref="SeasonEndResult"/> (the app's sign-and-continue flow).</summary>
    public static TransitionPlan Build(
        SeasonPack fromPack,
        SeasonPack toPack,
        IReadOnlyList<DriverCareerState> driversEnd,
        IReadOnlyList<TeamCareerState> teamsEnd,
        PlayerCareerState playerState,
        PlayerOffer acceptedOffer,
        StreamFactory streams,
        AgingCurveSet agingCurves,
        IReadOnlyDictionary<string, int>? canonRetirements = null,
        IReadOnlyList<CharacterSpend>? spends = null,
        CharacterRules? characterRules = null,
        int? fromYearOverride = null,
        int? toYearOverride = null)
    {
        ArgumentNullException.ThrowIfNull(fromPack);
        ArgumentNullException.ThrowIfNull(toPack);
        ArgumentNullException.ThrowIfNull(playerState);
        ArgumentNullException.ThrowIfNull(acceptedOffer);
        ArgumentNullException.ThrowIfNull(streams);
        ArgumentNullException.ThrowIfNull(agingCurves);

        // The SEASON years drive the transition, not the packs' nominal years: a carryover season
        // (same car reused for a later year) makes the FROM season's year run ahead of the from-pack's
        // year, so the caller passes the real season years. Default to the pack years — for every
        // non-carryover career season year == pack year, so existing careers are byte-identical.
        int fromYear = fromYearOverride ?? fromPack.Season.Year;
        int toYear = toYearOverride ?? toPack.Season.Year;
        if (toYear <= fromYear)
            throw new InvalidOperationException(
                $"Era transition must move the career forward in time: the finished season is " +
                $"{fromYear} and pack '{toPack.Manifest.PackId}' is a {toYear} season. " +
                "Pick a pack with a later season year.");

        canonRetirements ??= new Dictionary<string, int>(StringComparer.Ordinal);
        var events = new List<JournalEvent>();

        // ---- gap years: bridge (age + retire, one keyed stream pass per year) ----------
        var bridgedYears = Enumerable.Range(fromYear + 1, toYear - fromYear - 1).ToList();
        var fromDriversById = fromPack.Drivers.ToDictionary(d => d.Id, StringComparer.Ordinal);
        var bridged = driversEnd.ToList();

        foreach (int year in bridgedYears)
        {
            var curve = agingCurves.TryForYear(year)
                ?? throw new InvalidOperationException(
                    $"No aging era covers bridged year {year} — career-aging-curves.json " +
                    "must span every year between the two packs.");

            int aged = 0;
            var retired = new List<BridgeRetirement>();
            for (int i = 0; i < bridged.Count; i++)
            {
                var driver = bridged[i];
                if (driver.Retired)
                    continue;

                // Aging exactly as the season-end pipeline's step 3 would have run it for
                // this year: +1 age, curve drift, two noise draws (race, quali) from the
                // per-entity `aging` stream keyed with the bridged year, deltas clamped so
                // FROM-pack baseline + delta stays 0..1 (pool outsiders anchor at 0.5).
                int newAge = driver.Age + 1;
                double drift = curve.AnnualDelta(newAge);
                var agingStream = streams.CreateStream(CareerStreams.Aging, year, 0, driver.DriverId);
                double noiseRace = (2.0 * agingStream.NextDouble() - 1.0) * curve.NoiseAmplitude;
                double noiseQuali = (2.0 * agingStream.NextDouble() - 1.0) * curve.NoiseAmplitude;

                fromDriversById.TryGetValue(driver.DriverId, out var packDriver);
                double baseRace = packDriver?.Ratings.RaceSkill ?? 0.5;
                double baseQuali = packDriver?.Ratings.QualifyingSkill ?? 0.5;

                driver = driver with
                {
                    Age = newAge,
                    RaceSkillDelta = Math.Clamp(
                        driver.RaceSkillDelta + drift + noiseRace, -baseRace, 1.0 - baseRace),
                    QualifyingSkillDelta = Math.Clamp(
                        driver.QualifyingSkillDelta + drift + noiseQuali, -baseQuali, 1.0 - baseQuali),
                };
                aged++;

                // Retirement exactly as step 4 would have: canon drivers retire on schedule
                // and never roll; everyone else rolls the per-entity `retirement` stream
                // (keyed with the bridged year) against the age+performance hazard.
                string? cause = null;
                if (canonRetirements.TryGetValue(driver.DriverId, out int finalYear))
                {
                    if (finalYear <= year)
                        cause = "canon";
                }
                else
                {
                    double skill = baseRace + driver.RaceSkillDelta;
                    double hazard = curve.Retirement.Probability(driver.Age, skill);
                    double roll = streams
                        .CreateStream(CareerStreams.Retirement, year, 0, driver.DriverId)
                        .NextDouble();
                    if (roll < hazard)
                        cause = "age-performance";
                }
                if (cause is not null)
                {
                    driver = driver with { Retired = true };
                    retired.Add(new BridgeRetirement(driver.DriverId, cause));
                }

                bridged[i] = driver;
            }

            events.Add(new JournalEvent
            {
                Phase = JournalPhases.EraBridge,
                Entity = "season",
                DeltaJson = CareerJson.Serialize(new { year, aged, retired }),
                Cause = "gap-year",
            });
        }

        // ---- player seat in the new pack (validation errors surface in the plan) --------
        var errors = new List<string>();
        var offerTeam = toPack.Teams.FirstOrDefault(t =>
            string.Equals(t.Id, acceptedOffer.TeamId, StringComparison.Ordinal));
        PackEntry? seat = null;
        if (offerTeam is null)
        {
            errors.Add(
                $"The accepted offer names team '{acceptedOffer.TeamId}', which does not exist in " +
                $"pack '{toPack.Manifest.PackId}' ({toYear}) — the offer cannot be honored in the new era.");
        }
        else
        {
            seat = ResolveSeat(toPack, offerTeam.Id);
            if (seat is null)
                errors.Add(
                    $"Team '{offerTeam.Id}' has no entries in pack '{toPack.Manifest.PackId}' ({toYear}) — " +
                    "there is no seat for the player to take.");
        }

        // ---- lineage mapping: teams by PackTeam.Id ---------------------------------------
        var toTeams = new List<TeamCareerState>(toPack.Teams.Count);
        foreach (var team in toPack.Teams)
        {
            var carried = teamsEnd.FirstOrDefault(s =>
                string.Equals(s.LineageId, team.Id, StringComparison.Ordinal));
            toTeams.Add(new TeamCareerState
            {
                TeamId = team.Id,
                LineageId = team.Id,
                Tier = Math.Clamp(carried?.Tier ?? team.BudgetTier, 1, 5),
            });
        }

        // ---- lineage mapping: drivers by PackDriver.Id -----------------------------------
        var bridgedById = bridged.ToDictionary(d => d.DriverId, StringComparer.Ordinal);
        var carriedIds = new HashSet<string>(StringComparer.Ordinal);
        var toDrivers = new List<DriverCareerState>(toPack.Drivers.Count);
        foreach (var driver in toPack.Drivers)
        {
            if (seat is not null &&
                string.Equals(driver.Id, seat.DriverId, StringComparison.Ordinal))
                continue; // the player's seat — excluded like the wizard's season-1 seat pick

            if (bridgedById.TryGetValue(driver.Id, out var carried))
            {
                carriedIds.Add(driver.Id);
                // Deltas re-clamp against the NEW baselines so baseline + delta stays 0..1.
                double baseRace = driver.Ratings.RaceSkill;
                double baseQuali = driver.Ratings.QualifyingSkill;
                toDrivers.Add(carried with
                {
                    RaceSkillDelta = Math.Clamp(carried.RaceSkillDelta, -baseRace, 1.0 - baseRace),
                    QualifyingSkillDelta = Math.Clamp(
                        carried.QualifyingSkillDelta, -baseQuali, 1.0 - baseQuali),
                });
            }
            else
            {
                // New-era entry: fresh state, aged from the pack's Born year (the wizard's rule).
                toDrivers.Add(new DriverCareerState
                {
                    DriverId = driver.Id,
                    Age = toYear - (driver.Born ?? toYear - 30),
                });
            }
        }

        // ---- departed entities, journaled -------------------------------------------------
        foreach (var team in teamsEnd)
        {
            if (toPack.Teams.Any(t => string.Equals(t.Id, team.LineageId, StringComparison.Ordinal)))
                continue;
            events.Add(new JournalEvent
            {
                Phase = JournalPhases.EraDeparted,
                Entity = team.TeamId,
                DeltaJson = CareerJson.Serialize(new { kind = "team", tier = team.Tier }),
                Cause = "not-in-next-pack",
            });
        }
        foreach (var driver in bridged)
        {
            if (carriedIds.Contains(driver.DriverId))
                continue;
            bool displaced = seat is not null &&
                string.Equals(driver.DriverId, seat.DriverId, StringComparison.Ordinal);
            events.Add(new JournalEvent
            {
                Phase = JournalPhases.EraDeparted,
                Entity = driver.DriverId,
                DeltaJson = CareerJson.Serialize(new
                {
                    kind = "driver",
                    age = driver.Age,
                    retired = driver.Retired,
                }),
                Cause = displaced ? "seat-taken-by-player" : "not-in-next-pack",
            });
        }

        // ---- Budget-Unit rescale seam (v1: identity; the BU economy lands in Phase 2) ----
        const double buRescaleFactor = 1.0;
        events.Add(new JournalEvent
        {
            Phase = JournalPhases.EraEconomy,
            Entity = "season",
            DeltaJson = CareerJson.Serialize(new
            {
                factor = buRescaleFactor,
                note = "identity rescale — the Budget-Unit economy lands in Phase 2",
            }),
            Cause = "bu-rescale",
        });

        // ---- player carryover: rep/OPI/pace anchor verbatim, seated in the new pack ------
        var player = playerState;
        if (offerTeam is not null && seat is not null)
        {
            player = playerState with
            {
                CurrentTeamId = offerTeam.Id,
                LiveryName = seat.Ams2LiveryName,
            };
        }

        // Between-season development (character depth 4): apply the player's spends to the carried
        // character. These are journaled player.statSpend INPUTs, re-applied identically on replay so
        // the evolving driver reproduces byte-for-byte. No spends (or no character) → unchanged.
        if (spends is { Count: > 0 } && characterRules is not null && player.Character is { } devCharacter)
        {
            player = player with { Character = CharacterProgress.ApplyAll(devCharacter, spends, characterRules) };
        }

        return new TransitionPlan
        {
            FromPackId = fromPack.Manifest.PackId,
            ToPackId = toPack.Manifest.PackId,
            FromYear = fromYear,
            ToYear = toYear,
            BridgedYears = bridgedYears,
            Player = player,
            Drivers = toDrivers,
            Teams = toTeams,
            Events = events,
            ValidationErrors = errors,
            PlayerTeamId = offerTeam?.Id,
            PlayerSeatLiveryName = seat?.Ams2LiveryName,
            DisplacedDriverId = seat?.DriverId,
            BudgetRescaleFactor = buRescaleFactor,
        };
    }

    /// <summary>The exact livery the player takes at <paramref name="teamId"/> in
    /// <paramref name="pack"/> — the same seat <see cref="Build"/> resolves for an era changeover,
    /// exposed for the SAME-PACK carryover path (which seats through
    /// <see cref="SeasonRollover"/> rather than a transition plan). Null when the team has no
    /// entries.</summary>
    public static string? ResolveSeatLivery(SeasonPack pack, string teamId)
    {
        ArgumentNullException.ThrowIfNull(pack);
        return ResolveSeat(pack, teamId)?.Ams2LiveryName;
    }

    /// <summary>The seat the player takes at the accepted team: the first entries.json entry
    /// covering EVERY calendar round ("1-N" preference), else the first covering round 1,
    /// else the team's first entry. Null when the team has no entries at all.</summary>
    private static PackEntry? ResolveSeat(SeasonPack toPack, string teamId)
    {
        var candidates = toPack.Entries
            .Where(e => string.Equals(e.TeamId, teamId, StringComparison.Ordinal))
            .ToList();
        if (candidates.Count == 0)
            return null;

        var allRounds = toPack.Season.Rounds.Select(r => r.Round).ToList();
        PackEntry? fullSeason = null;
        PackEntry? opener = null;
        foreach (var entry in candidates)
        {
            if (!RoundsRange.TryParse(entry.Rounds, out var range, out _))
                continue;
            if (fullSeason is null && allRounds.All(range.Contains))
            {
                fullSeason = entry;
                break; // entries.json order: the first full-season entry wins outright
            }
            if (opener is null && range.Contains(1))
                opener = entry;
        }
        return fullSeason ?? opener ?? candidates[0];
    }

    /// <summary>One retirement inside an era.bridge journal delta.</summary>
    private readonly record struct BridgeRetirement(string Driver, string Cause);
}
