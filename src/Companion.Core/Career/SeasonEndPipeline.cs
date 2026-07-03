using System.Collections.ObjectModel;
using System.Globalization;
using Companion.Core.Determinism;
using Companion.Core.Packs;
using Companion.Core.Scoring;

namespace Companion.Core.Career;

/// <summary>Everything the season-end pipeline consumes. Same context + same master seed
/// ⇒ element-wise identical <see cref="JournalEvent"/> output (tested invariant).</summary>
public sealed record SeasonEndContext
{
    public required int Year { get; init; }

    public required StreamFactory Streams { get; init; }

    /// <summary>The pinned season pack (baseline ratings, teams, entries, points system).</summary>
    public required SeasonPack Pack { get; init; }

    /// <summary>The season's imported round results, in round order.</summary>
    public required IReadOnlyList<RoundResult> Rounds { get; init; }

    /// <summary>The driver id the player scored under in <see cref="Rounds"/>.</summary>
    public required string PlayerDriverId { get; init; }

    /// <summary>Player's age in the season being ended.</summary>
    public required int PlayerAge { get; init; }

    public required PlayerCareerState Player { get; init; }

    /// <summary>AI driver states in a stable caller-chosen order (journal order follows it).</summary>
    public required IReadOnlyList<DriverCareerState> Drivers { get; init; }

    public required IReadOnlyList<TeamCareerState> Teams { get; init; }

    public required AgingCurveSet AgingCurves { get; init; }

    public required TeamArchetypeCatalog Archetypes { get; init; }

    public HeadlineBank? Headlines { get; init; }

    /// <summary>Canon retirements from pack/lineage data when present: driver id → final
    /// season year. Drivers listed here never hazard-roll; they retire exactly on schedule.</summary>
    public IReadOnlyDictionary<string, int> CanonRetirements { get; init; } =
        ReadOnlyDictionary<string, int>.Empty;

    /// <summary>Explicit archetype per team id; teams not listed use the tier default.</summary>
    public IReadOnlyDictionary<string, string> TeamArchetypeOverrides { get; init; } =
        ReadOnlyDictionary<string, string>.Empty;

    /// <summary>Drivers available to fill vacated AI seats. Empty ⇒ vacancies stay unfilled.</summary>
    public IReadOnlyList<SeatCandidate> FreeAgents { get; init; } = [];

    /// <summary>The player's salary ask in BU; defaults to max(1, reputation/10).</summary>
    public double? PlayerSalaryAskBu { get; init; }

    /// <summary>Display name for {champion} tokens when the player wins; defaults to the id.</summary>
    public string? PlayerName { get; init; }
}

public sealed record SeasonEndResult
{
    /// <summary>Every state change of the pipeline, in the contract's strict step order.</summary>
    public required IReadOnlyList<JournalEvent> Events { get; init; }

    public required PlayerCareerState Player { get; init; }

    public required IReadOnlyList<DriverCareerState> Drivers { get; init; }

    public required IReadOnlyList<TeamCareerState> Teams { get; init; }

    public required IReadOnlyList<PlayerOffer> Offers { get; init; }

    public required StandingsSnapshot FinalStandings { get; init; }
}

/// <summary>
/// The season-end pipeline (docs/dev/career-sim.md): seven steps in strict order —
/// (1) final standings via the standings engine, (2) player reputation/OPI finals,
/// (3) aging along era-shifted curves (stream `aging`, keyed per entity),
/// (4) retirements: canon on schedule + age/performance hazard (stream `retirement`, keyed
/// per entity) with seeded foreshadowing for next season,
/// (5) AI seat market (stream `offers`, keyed per team),
/// (6) player offer letters (archetype-weighted contract formula, tier-gated, top N),
/// (7) tier drift ±1 (stream `tier-drift`, keyed per team) — then the season digest headline.
/// Pure function: no I/O, no clocks, no ambient randomness; every stream is named and keyed,
/// so consuming numbers for one entity never shifts another entity's rolls.
/// </summary>
public static class SeasonEndPipeline
{
    public static SeasonEndResult Run(SeasonEndContext context)
    {
        var events = new List<JournalEvent>();
        var pack = context.Pack;
        int year = context.Year;

        var packDriversById = pack.Drivers.ToDictionary(d => d.Id, StringComparer.Ordinal);
        var curve = context.AgingCurves.ForYear(year);

        // ---- step 1: final standings -------------------------------------------------
        var scoring = pack.Season.PointsSystem.ResolveScoringDefinition(pack.Season.Rounds.Count);
        var standings = StandingsEngine.ComputeSeason(scoring, context.Rounds);
        var final = standings.Final;

        foreach (var driver in final.Drivers)
        {
            events.Add(new JournalEvent
            {
                Phase = JournalPhases.Championship,
                Entity = driver.DriverId,
                DeltaJson = CareerJson.Serialize(new
                {
                    position = driver.Position,
                    points = driver.CountedPoints.ToString(),
                }),
                Cause = "standings-final",
            });
        }
        if (final.Constructors is { } constructors)
        {
            foreach (var team in constructors)
            {
                events.Add(new JournalEvent
                {
                    Phase = JournalPhases.Championship,
                    Entity = team.ConstructorId,
                    DeltaJson = CareerJson.Serialize(new
                    {
                        position = team.Position,
                        points = team.CountedPoints.ToString(),
                    }),
                    Cause = "standings-final",
                });
            }
        }

        // ---- step 2: player reputation/OPI finals -------------------------------------
        var player = context.Player;
        int? playerPosition = final.Drivers
            .FirstOrDefault(d => string.Equals(d.DriverId, context.PlayerDriverId, StringComparison.Ordinal))
            ?.Position;
        int playerTier = context.Teams
            .FirstOrDefault(t => string.Equals(t.TeamId, player.CurrentTeamId, StringComparison.Ordinal))
            ?.Tier ?? 3;

        double seasonRepDelta = ReputationMath.SeasonDelta(playerPosition, playerTier);
        double finalRep = ReputationMath.Apply(player.Reputation, seasonRepDelta);

        events.Add(new JournalEvent
        {
            Phase = JournalPhases.PlayerReputation,
            Entity = "player",
            DeltaJson = CareerJson.Serialize(new
            {
                from = Round4(player.Reputation),
                to = Round4(finalRep),
                delta = Round4(seasonRepDelta),
                championshipPosition = playerPosition,
            }),
            Cause = "season-final",
        });
        events.Add(new JournalEvent
        {
            Phase = JournalPhases.PlayerOpi,
            Entity = "player",
            DeltaJson = CareerJson.Serialize(new { value = Round4(player.Opi) }),
            Cause = "season-final",
        });

        player = player with
        {
            Reputation = finalRep,
            SeasonsCompleted = player.SeasonsCompleted + 1,
        };

        // ---- step 3: aging -------------------------------------------------------------
        var agedDrivers = new List<DriverCareerState>(context.Drivers.Count);
        foreach (var driver in context.Drivers)
        {
            if (driver.Retired)
            {
                agedDrivers.Add(driver);
                continue;
            }

            var aged = AgeOneSeason(driver, packDriversById, curve, context.Streams, year);
            agedDrivers.Add(aged);
            events.Add(new JournalEvent
            {
                Phase = JournalPhases.DriverAging,
                Entity = driver.DriverId,
                DeltaJson = CareerJson.Serialize(new
                {
                    age = aged.Age,
                    raceSkillDelta = Round4(aged.RaceSkillDelta),
                    qualifyingSkillDelta = Round4(aged.QualifyingSkillDelta),
                }),
                Cause = "aging",
            });
        }

        // ---- step 4: retirements (canon + hazard) with seeded foreshadowing -----------
        var retiredNow = new HashSet<string>(StringComparer.Ordinal);
        var finalDrivers = new List<DriverCareerState>(agedDrivers.Count);
        foreach (var driver in agedDrivers)
        {
            if (driver.Retired)
            {
                finalDrivers.Add(driver);
                continue;
            }

            var decision = DecideRetirement(driver, packDriversById, curve, context, year);
            if (decision.Retires)
            {
                retiredNow.Add(driver.DriverId);
                finalDrivers.Add(driver with { Retired = true });
                events.Add(new JournalEvent
                {
                    Phase = JournalPhases.Retirement,
                    Entity = driver.DriverId,
                    DeltaJson = decision.DeltaJson,
                    Cause = decision.Cause,
                });
                continue;
            }

            finalDrivers.Add(driver);

            // Seeded foreshadowing: peek next season's deterministic decision. Aging drift
            // only moves at season end and the peek consumes next year's named streams,
            // which replay identically when next year's pipeline runs for real.
            var nextCurve = context.AgingCurves.TryForYear(year + 1);
            if (nextCurve is null)
                continue;
            var peekAged = AgeOneSeason(driver, packDriversById, nextCurve, context.Streams, year + 1);
            var peek = DecideRetirement(peekAged, packDriversById, nextCurve, context, year + 1);
            if (!peek.Retires)
                continue;

            events.Add(new JournalEvent
            {
                Phase = JournalPhases.RetirementForeshadow,
                Entity = driver.DriverId,
                DeltaJson = CareerJson.Serialize(new { age = driver.Age }),
                Cause = "considering-future",
            });
            if (context.Headlines is { } foreshadowBank)
            {
                string? text = HeadlineSelector.Select(
                    foreshadowBank, JournalPhases.RetirementForeshadow, "considering-future", year,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["driver"] = DriverName(packDriversById, driver.DriverId),
                        ["year"] = year.ToString(CultureInfo.InvariantCulture),
                    },
                    context.Streams.CreateStream(CareerStreams.Headlines, year, 0, driver.DriverId));
                if (text is not null)
                {
                    events.Add(new JournalEvent
                    {
                        Phase = JournalPhases.Headline,
                        Entity = driver.DriverId,
                        DeltaJson = CareerJson.Serialize(new { text }),
                        Cause = "considering-future",
                    });
                }
            }
        }

        // ---- step 5: AI seat market ----------------------------------------------------
        var teams = context.Teams.ToList();
        var seatMap = BuildSeatMap(pack, player.LiveryName);
        var vacancies = seatMap
            .Where(s => retiredNow.Contains(s.DriverId))
            .OrderByDescending(s => TierOf(teams, s.TeamId))
            .ThenBy(s => s.TeamId, StringComparer.Ordinal)
            .ToList();

        var pool = context.FreeAgents.ToList();
        foreach (var vacancy in vacancies)
        {
            int tier = TierOf(teams, vacancy.TeamId);
            if (pool.Count == 0)
            {
                events.Add(new JournalEvent
                {
                    Phase = JournalPhases.SeatMarket,
                    Entity = vacancy.TeamId,
                    DeltaJson = CareerJson.Serialize(new { vacatedBy = vacancy.DriverId }),
                    Cause = "vacancy-unfilled",
                });
                continue;
            }

            var archetype = context.Archetypes.ForTeam(
                tier, ArchetypeOverride(context, vacancy.TeamId));
            var offerStream = context.Streams.CreateStream(CareerStreams.Offers, year, 0, vacancy.TeamId);

            SeatCandidate? best = null;
            double bestScore = double.NegativeInfinity;
            foreach (var candidate in pool)
            {
                double score = candidate.RaceSkill
                               - 0.02 * Math.Max(0, candidate.Age - curve.PeakAgeEnd)
                               + archetype.PayDriverWeight * candidate.PayBudgetBu / 100.0
                               + 0.01 * (2.0 * offerStream.NextDouble() - 1.0);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            pool.Remove(best!);
            events.Add(new JournalEvent
            {
                Phase = JournalPhases.SeatMarket,
                Entity = vacancy.TeamId,
                DeltaJson = CareerJson.Serialize(new
                {
                    vacatedBy = vacancy.DriverId,
                    hired = best!.DriverId,
                    score = Round4(bestScore),
                }),
                Cause = "vacancy-filled",
            });
        }

        // ---- step 6: player offers ------------------------------------------------------
        double salaryAsk = context.PlayerSalaryAskBu ?? Math.Max(1.0, finalRep / 10.0);
        double ageRisk = Math.Max(0, context.PlayerAge + 1 - curve.PeakAgeEnd);

        var scored = new List<PlayerOffer>();
        foreach (var team in teams.OrderBy(t => t.TeamId, StringComparer.Ordinal))
        {
            if (finalRep < context.Archetypes.RepFloor(team.Tier))
                continue;

            var archetype = context.Archetypes.ForTeam(team.Tier, ArchetypeOverride(context, team.TeamId));
            double score = TeamArchetypeCatalog.OfferScore(
                archetype, finalRep, player.Opi, player.SeasonsCompleted, salaryAsk, ageRisk);
            scored.Add(new PlayerOffer
            {
                TeamId = team.TeamId,
                Tier = team.Tier,
                SalaryBu = context.Archetypes.SalaryOffer(team.Tier, finalRep),
                Score = Round4(score),
            });
        }

        var offers = scored
            .OrderByDescending(o => o.Score)
            .ThenBy(o => o.TeamId, StringComparer.Ordinal)
            .Take(context.Archetypes.MaxOffers)
            .ToList();

        foreach (var offer in offers)
        {
            events.Add(new JournalEvent
            {
                Phase = JournalPhases.OfferExtended,
                Entity = offer.TeamId,
                DeltaJson = CareerJson.Serialize(new
                {
                    tier = offer.Tier,
                    salaryBu = offer.SalaryBu,
                    score = offer.Score,
                }),
                Cause = "player-offer",
            });
        }

        // ---- step 7: tier drift ----------------------------------------------------------
        var driftedTiers = new Dictionary<string, int>(StringComparer.Ordinal);
        if (final.Constructors is { } table)
        {
            var expectedRanks = teams
                .OrderByDescending(t => t.Tier)
                .ThenBy(t => t.TeamId, StringComparer.Ordinal)
                .Select((t, i) => (t.TeamId, Rank: i + 1))
                .ToDictionary(x => x.TeamId, x => x.Rank, StringComparer.Ordinal);

            // Journal in teamId order (stable regardless of caller list order); the drift is
            // ±1 at most and never stacks — a bounded, tested invariant.
            foreach (var team in teams.OrderBy(t => t.TeamId, StringComparer.Ordinal))
            {
                int? actual = table
                    .FirstOrDefault(c => string.Equals(c.ConstructorId, team.TeamId, StringComparison.Ordinal))
                    ?.Position;
                if (actual is null)
                    continue;

                int rankDelta = expectedRanks[team.TeamId] - actual.Value; // >0 ⇒ overachieved
                int direction = Math.Sign(rankDelta);
                int target = team.Tier + direction;
                if (direction == 0 || target < 1 || target > 5)
                    continue;

                double probability = Math.Min(0.75, 0.25 * Math.Abs(rankDelta));
                double roll = context.Streams
                    .CreateStream(CareerStreams.TierDrift, year, 0, team.TeamId)
                    .NextDouble();
                if (roll >= probability)
                    continue;

                driftedTiers[team.TeamId] = target;
                events.Add(new JournalEvent
                {
                    Phase = JournalPhases.TeamTier,
                    Entity = team.TeamId,
                    DeltaJson = CareerJson.Serialize(new
                    {
                        from = team.Tier,
                        to = target,
                        expectedRank = expectedRanks[team.TeamId],
                        actualRank = actual.Value,
                        probability = Round4(probability),
                        roll = Round4(roll),
                    }),
                    Cause = direction > 0 ? "overperformed" : "underperformed",
                });
            }
        }

        var finalTeams = teams
            .Select(t => driftedTiers.TryGetValue(t.TeamId, out int tier) ? t with { Tier = tier } : t)
            .ToList();

        // ---- season digest headline -------------------------------------------------------
        if (context.Headlines is { } bank)
        {
            var champion = final.Drivers.FirstOrDefault(d => d.Position == 1);
            if (champion is not null)
            {
                string championName =
                    string.Equals(champion.DriverId, context.PlayerDriverId, StringComparison.Ordinal)
                        ? context.PlayerName ?? champion.DriverId
                        : DriverName(packDriversById, champion.DriverId);
                string? digest = HeadlineSelector.Select(
                    bank, "season.digest", "season-complete", year,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["champion"] = championName,
                        ["year"] = year.ToString(CultureInfo.InvariantCulture),
                    },
                    context.Streams.CreateStream(CareerStreams.Headlines, year, 0, "season"));
                if (digest is not null)
                {
                    events.Add(new JournalEvent
                    {
                        Phase = JournalPhases.Headline,
                        Entity = "season",
                        DeltaJson = CareerJson.Serialize(new { text = digest }),
                        Cause = "season-digest",
                    });
                }
            }
        }

        return new SeasonEndResult
        {
            Events = events,
            Player = player,
            Drivers = finalDrivers,
            Teams = finalTeams,
            Offers = offers,
            FinalStandings = final,
        };
    }

    // ---------- helpers ----------

    /// <summary>One season of aging: age +1, curve drift + per-entity noise (stream `aging`,
    /// two draws: raceSkill then qualifyingSkill), drift clamped so baseline+delta stays 0..1.
    /// Used identically by step 3 and the step-4 foreshadow peek so the peek is exact.</summary>
    private static DriverCareerState AgeOneSeason(
        DriverCareerState driver,
        IReadOnlyDictionary<string, PackDriver> packDriversById,
        AgingCurve curve,
        StreamFactory streams,
        int year)
    {
        int newAge = driver.Age + 1;
        double drift = curve.AnnualDelta(newAge);

        var stream = streams.CreateStream(CareerStreams.Aging, year, 0, driver.DriverId);
        double noiseRace = (2.0 * stream.NextDouble() - 1.0) * curve.NoiseAmplitude;
        double noiseQuali = (2.0 * stream.NextDouble() - 1.0) * curve.NoiseAmplitude;

        packDriversById.TryGetValue(driver.DriverId, out var packDriver);
        double baseRace = packDriver?.Ratings.RaceSkill ?? 0.5;
        double baseQuali = packDriver?.Ratings.QualifyingSkill ?? 0.5;

        return driver with
        {
            Age = newAge,
            RaceSkillDelta = Math.Clamp(
                driver.RaceSkillDelta + drift + noiseRace, -baseRace, 1.0 - baseRace),
            QualifyingSkillDelta = Math.Clamp(
                driver.QualifyingSkillDelta + drift + noiseQuali, -baseQuali, 1.0 - baseQuali),
        };
    }

    private readonly record struct RetirementDecision(bool Retires, string Cause, string DeltaJson);

    /// <summary>Retirement decision for a driver at the end of the given year. Canon drivers
    /// retire exactly on schedule and never roll; everyone else rolls the per-entity
    /// `retirement` stream against the age+performance hazard.</summary>
    private static RetirementDecision DecideRetirement(
        DriverCareerState driver,
        IReadOnlyDictionary<string, PackDriver> packDriversById,
        AgingCurve curve,
        SeasonEndContext context,
        int year)
    {
        if (context.CanonRetirements.TryGetValue(driver.DriverId, out int finalYear))
        {
            bool canonNow = finalYear <= year;
            return new RetirementDecision(
                canonNow, "canon",
                CareerJson.Serialize(new { age = driver.Age, finalYear }));
        }

        packDriversById.TryGetValue(driver.DriverId, out var packDriver);
        double skill = (packDriver?.Ratings.RaceSkill ?? 0.5) + driver.RaceSkillDelta;
        double hazard = curve.Retirement.Probability(driver.Age, skill);
        double roll = context.Streams
            .CreateStream(CareerStreams.Retirement, year, 0, driver.DriverId)
            .NextDouble();

        return new RetirementDecision(
            roll < hazard, "age-performance",
            CareerJson.Serialize(new
            {
                age = driver.Age,
                hazard = Round4(hazard),
                roll = Round4(roll),
            }));
    }

    /// <summary>The AI seats of the season's final round, in entries.json order, excluding
    /// the player's own entry (matched by exact livery name).</summary>
    private static List<(string DriverId, string TeamId)> BuildSeatMap(SeasonPack pack, string? playerLivery)
    {
        int lastRound = pack.Season.Rounds.Count == 0 ? 1 : pack.Season.Rounds.Max(r => r.Round);
        var seats = new List<(string DriverId, string TeamId)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in pack.Entries)
        {
            if (playerLivery is not null &&
                string.Equals(entry.Ams2LiveryName, playerLivery, StringComparison.Ordinal))
                continue;
            if (!RoundsRange.TryParse(entry.Rounds, out var range, out _) || !range.Contains(lastRound))
                continue;
            if (!seen.Add(entry.DriverId))
                continue;
            seats.Add((entry.DriverId, entry.TeamId));
        }
        return seats;
    }

    private static int TierOf(List<TeamCareerState> teams, string teamId) =>
        teams.FirstOrDefault(t => string.Equals(t.TeamId, teamId, StringComparison.Ordinal))?.Tier ?? 3;

    private static string? ArchetypeOverride(SeasonEndContext context, string teamId) =>
        context.TeamArchetypeOverrides.TryGetValue(teamId, out string? name) ? name : null;

    private static string DriverName(IReadOnlyDictionary<string, PackDriver> drivers, string driverId) =>
        drivers.TryGetValue(driverId, out var driver) ? driver.Name : driverId;

    private static double Round4(double value) => Math.Round(value, 4);
}
