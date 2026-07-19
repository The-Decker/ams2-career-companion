using Companion.Core.Career;
using Companion.Core.Character;
using Companion.Core.Determinism;
using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.Data;
using Companion.Tests.ViewModels;
using Companion.ViewModels.Services;

namespace Companion.Tests.Data;

/// <summary>
/// Character death &amp; injury, Slice 4, the AUTO-SIMULATED skipped-round fold (the highest replay-risk
/// slice). An injured player sits a round out; AMS2 cannot spectate, so the app simulates the AI field
/// deterministically (player DNS, OPI-neutral, zero points) and folds it. A minor suspension decrements
/// and heals; a season-ending injury skips the rest of the season and clears at the reset. A career that
/// is never injured draws zero from the auto-race stream and stays byte-identical. Outcomes are forced
/// deterministically by engineering the driver's durability against the (known) accident roll.
/// </summary>
public sealed class AutoSimFoldTests : IDisposable
{
    private const string PlayerId = "driver.hulme";
    private const long Seed = 20260712;
    private const int Year = 1967;

    private readonly string _root = Directory.CreateTempSubdirectory("companion-autosim-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    // ---- (a) the pure deterministic field generator ----

    private static readonly PackDriverRatings Ratings = new()
    {
        RaceSkill = 0.8, QualifyingSkill = 0.8, Aggression = 0.5, Defending = 0.5, Stamina = 0.5,
        Consistency = 0.5, StartReactions = 0.5, WetSkill = 0.5, TyreManagement = 0.5, AvoidanceOfMistakes = 0.5,
    };

    private static GridSeat Seat(string id, double power, bool isPlayer = false) => new()
    {
        DriverId = id, DriverName = id, TeamId = "t." + id, TeamName = "T " + id, Number = "1",
        Ams2LiveryName = id, Ratings = Ratings, Reliability = 1.0,
        WeightScalar = 1.0, PowerScalar = power, DragScalar = 1.0, IsPlayer = isPlayer,
    };

    [Fact]
    public void AutoRaceModel_IsDeterministic_ExcludesThePlayer_AndCoversTheField()
    {
        var seats = new List<GridSeat>
        {
            Seat("driver.player", 1.05, isPlayer: true),
            Seat("driver.a", 1.10),
            Seat("driver.b", 1.00),
            Seat("driver.c", 0.95),
            Seat("driver.d", 1.02),
        };

        var order1 = AutoRaceModel.ClassifiedOrder(seats, 777UL, Year, round: 3);
        var order2 = AutoRaceModel.ClassifiedOrder(seats, 777UL, Year, round: 3);

        Assert.Equal(order1, order2);                              // same seed+round → same field
        Assert.DoesNotContain("driver.player", order1);           // the player did not start
        Assert.Equal(4, order1.Count);                            // every non-player seat classified
        Assert.Equal(order1.Distinct().Count(), order1.Count);    // a clean permutation, no dupes

        // A different round rolls independent jitter (the strong field means it can reorder).
        var order4 = AutoRaceModel.ClassifiedOrder(seats, 777UL, Year, round: 4);
        Assert.Equal(order1.OrderBy(x => x), order4.OrderBy(x => x)); // same set...
    }

    // ---- injury/auto-sim careers ----

    private static CharacterProfile Character(double durability) => new()
    {
        Name = "Crash McTest",
        Stats = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["pace"] = 0.55, ["oneLap"] = 0.50, ["craft"] = 0.50, ["racecraft"] = 0.50,
            ["adaptability"] = 0.50, ["marketability"] = 0.55, ["durability"] = durability,
        },
        PerkIds = [],
        CpUnspent = 0,
    };

    /// <summary>The durability that makes round 1's (known, seeded) accident roll resolve to a target
    /// effective d500, so an injury outcome is forced deterministically. offset = (durability-0.5)*scale;
    /// effective = roll - offset ⇒ durability = 0.5 + (roll - target)/scale.</summary>
    private static double DurabilityForEffective(int targetEffective)
    {
        int roll = new StreamFactory(unchecked((ulong)Seed))
            .CreateStream(CareerStreams.Accident, Year, 1, "player").NextInt(1, 501);
        return 0.5 + (roll - targetEffective) / AccidentModel.DefaultRules.SafetyDurabilityScale;
    }

    private CareerSessionService Create(string name, double durability, SeasonPack pack)
    {
        string packDirectory = Path.Combine(_root, name, "pack");
        TestPackBuilder.Write(pack, packDirectory);
        return CareerSessionService.CreateCareer(
            new CareerCreationRequest
            {
                PackDirectory = packDirectory,
                CareerFilePath = Path.Combine(_root, name, name + ".ams2career"),
                CareerName = name,
                MasterSeed = Seed,
                PlayerLiveryName = TestPackBuilder.StockLivery2,
                Character = Character(durability),
                Mortality = MortalityMode.Normal,
            },
            ViewModelTestData.Environment(
                documentsDirectory: Path.Combine(_root, name, "docs"),
                library: TestPackBuilder.Library()));
    }

    private static SeasonPack NRoundPack(int rounds)
    {
        var basePack = TestPackBuilder.TwoRoundPack();
        var list = new List<PackRound> { basePack.Season.Rounds[0], basePack.Season.Rounds[1] };
        for (int n = 3; n <= rounds; n++)
            list.Add(TestPackBuilder.Round(n, $"1967-{n:D2}-01"));
        // The Entry helper hardcodes Rounds = "1-2"; widen every entry to cover the added rounds so the
        // full field is entered all season (otherwise later rounds resolve to just the player, no AI).
        return basePack with
        {
            Season = basePack.Season with { Rounds = list },
            Entries = basePack.Entries.Select(e => e with { Rounds = $"1-{rounds}" }).ToList(),
        };
    }

    private static void ApplyPlayerAccident(ICareerSession session)
    {
        var seats = session.CurrentGrid();
        session.Apply(new ResultDraft
        {
            Classified = seats.Where(s => s.DriverId != PlayerId).Select(s => s.DriverId).ToList(),
            DidNotFinish = new Dictionary<string, string> { [PlayerId] = "a" },
            Disqualified = [],
            PlayerAccidentSeverity = AccidentSeverity.Heavy,
        });
    }

    private static void ApplyNormalRound(ICareerSession session)
    {
        var seats = session.CurrentGrid();
        session.Apply(new ResultDraft
        {
            Classified = seats.Select(s => s.DriverId).ToList(),
            DidNotFinish = new Dictionary<string, string>(),
            Disqualified = [],
        });
    }

    private static void AssertByteIdentical(string careerPath, SeasonPack pack)
    {
        using var db = CareerDatabase.Open(careerPath);
        var rules = CareerRulesData.Load(ViewModelTestData.RulesDirectory);
        var inputs = new ReplaySimInputs
        {
            AgingCurves = rules.AgingCurves, Archetypes = rules.Archetypes, Headlines = rules.Headlines,
            PlayerDriverId = PlayerId, PlayerAge = 30, CharacterRules = rules.Character,
        };
        var report = ReplayService.Resimulate(db, pack, unchecked((ulong)Seed), inputs);
        Assert.True(report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} stored={report.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={report.FirstDivergence?.RegeneratedDeltaJson}");
    }

    [Fact]
    public void MinorInjury_SkipsARound_HealsToFit_AndReplaysByteIdentically()
    {
        var pack = TestPackBuilder.TwoRoundPack();
        string careerPath;
        using (var session = Create("miss1", DurabilityForEffective(300), pack)) // heavy → miss 1 race
        {
            careerPath = session.CareerFilePath;
            ApplyPlayerAccident(session);                          // round 1: injured, miss 1
            Assert.Equal(1, session.PlayerMortality().RaceSuspensionRemaining);

            // Round 2: the player must sit out, the round is auto-simulated.
            var sitOut = session.CurrentSitOut();
            Assert.NotNull(sitOut);
            Assert.Contains("1 remaining", sitOut!.Headline);
            session.AutoSimulateRound();

            // Healed to fit; the season completed (2 rounds).
            Assert.Equal(0, session.PlayerMortality().RaceSuspensionRemaining);
            Assert.True(session.Summary.SeasonComplete);
        }

        using (var db = CareerDatabase.Open(careerPath))
        {
            long seasonId = CareerStore.ReadSeasons(db).Single().Id;
            // Round 2 folded a DERIVED DNS row; the player scored nothing that round.
            Assert.Contains(JournalStore.ReadSeason(db, seasonId),
                r => r.Phase == JournalPhases.PlayerDidNotStart && r.Round == 2);
        }
        AssertByteIdentical(careerPath, pack);
    }

    [Fact]
    public void MissesTwoRaces_ThenReturns_AndReplaysByteIdentically()
    {
        var pack = NRoundPack(4);
        string careerPath;
        using (var session = Create("miss2", DurabilityForEffective(400), pack)) // heavy → miss 2 races
        {
            careerPath = session.CareerFilePath;
            ApplyPlayerAccident(session);                          // round 1: injured, miss 2
            Assert.Equal(2, session.PlayerMortality().RaceSuspensionRemaining);

            session.AutoSimulateRound();                           // round 2 (auto): remaining 1
            Assert.Equal(1, session.PlayerMortality().RaceSuspensionRemaining);

            session.AutoSimulateRound();                           // round 3 (auto): remaining 0 → fit
            Assert.Equal(0, session.PlayerMortality().RaceSuspensionRemaining);
            Assert.Null(session.CurrentSitOut());                  // the driver has returned

            ApplyNormalRound(session);                             // round 4: raced normally again
            Assert.True(session.Summary.SeasonComplete);
        }

        using (var db = CareerDatabase.Open(careerPath))
        {
            long seasonId = CareerStore.ReadSeasons(db).Single().Id;
            var dns = JournalStore.ReadSeason(db, seasonId)
                .Where(r => r.Phase == JournalPhases.PlayerDidNotStart).Select(r => r.Round).ToList();
            Assert.Equal(new int?[] { 2, 3 }, dns);               // exactly rounds 2 and 3 were auto-sims
        }
        AssertByteIdentical(careerPath, pack);
    }

    [Fact]
    public void SeasonEndingInjury_SkipsTheRestOfTheSeason_AndClearsAtTheReset()
    {
        var pack = TestPackBuilder.TwoRoundPack();
        string careerPath;
        using (var session = Create("seasonend", DurabilityForEffective(470), pack)) // heavy → season-ending
        {
            careerPath = session.CareerFilePath;
            ApplyPlayerAccident(session);                          // round 1: season-ending injury
            Assert.True(session.PlayerMortality().SeasonEndingInjury);

            var sitOut = session.CurrentSitOut();
            Assert.NotNull(sitOut);
            Assert.True(sitOut!.SeasonEnding);
            Assert.Contains("SEASON OVER", sitOut.Headline);

            session.AutoSimulateRound();                           // round 2 (auto) → season complete
            Assert.True(session.Summary.SeasonComplete);
            Assert.NotNull(session.SeasonReview());                // runs season end (the reset)
        }

        using (var db = CareerDatabase.Open(careerPath))
        {
            long seasonId = CareerStore.ReadSeasons(db).Single().Id;
            // The season-end 'end' state cleared the transient injury, the driver returns next year.
            var endState = StateStore.ReadPlayerState(db, seasonId, StateStore.StageEnd);
            Assert.NotNull(endState);
            Assert.False(endState!.SeasonEndingInjury);
            Assert.Equal(0, endState.RaceSuspensionRemaining);
        }
        AssertByteIdentical(careerPath, pack);
    }

    [Fact]
    public void UninjuredCareer_NeverAutoSims_AndHasNoDnsRows()
    {
        var pack = TestPackBuilder.TwoRoundPack();
        string careerPath;
        using (var session = Create("healthy", durability: 0.5, pack))
        {
            careerPath = session.CareerFilePath;
            Assert.Null(session.CurrentSitOut());
            ApplyNormalRound(session);
            Assert.Null(session.CurrentSitOut());                  // fit all season
            ApplyNormalRound(session);
            Assert.NotNull(session.SeasonReview());
        }

        using var db = CareerDatabase.Open(careerPath);
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;
        Assert.DoesNotContain(JournalStore.ReadSeason(db, seasonId),
            r => r.Phase == JournalPhases.PlayerDidNotStart);
        AssertByteIdentical(careerPath, pack);
    }
}
