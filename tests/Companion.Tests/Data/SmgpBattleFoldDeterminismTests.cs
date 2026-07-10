using Companion.Core.Career;
using Companion.Core.Packs;
using Companion.Core.Smgp;
using Companion.Data;
using Companion.Tests.ViewModels;
using Companion.ViewModels.Services;

namespace Companion.Tests.Data;

/// <summary>
/// M3 slice 2 — the rival battle folds from stored envelope inputs (the load-bearing
/// determinism gate). A four-tier one-driver-per-team ladder (the SMGP shape): the player
/// starts at LEVEL C. Proves the two-wins swap moves exactly the three chained seats, the
/// forfeit demotes (and hard-fails at the floor team), the journal carries a Why?-inspectable
/// row per battle, and a battle-carrying career RE-SIMULATES BYTE-IDENTICALLY.
/// </summary>
public sealed class SmgpBattleFoldDeterminismTests : IDisposable
{
    private const string SeatA = "Stock Livery #1"; // team.a  LEVEL A  driver.a
    private const string SeatB = "Stock Livery #2"; // team.b  LEVEL B  driver.b
    private const string SeatC = "Stock Livery #3"; // team.c  LEVEL C  driver.c
    private const string SeatD = "Stock Livery #4"; // team.d  LEVEL D  driver.d (the floor team)
    private const long Seed = 20260710;

    private readonly string _root = Directory.CreateTempSubdirectory("companion-smgp-battle-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    [Fact]
    public void TwoWins_AcceptedSwap_MovesTheThreeChainedSeats_AndReplaysByteIdentically()
    {
        // Player (Seat C, tier C) beats the LEVEL A rival twice and accepts: player -> Seat A,
        // the rival drops one tier below the player's OLD tier (D) -> Seat D, and Seat D's
        // occupant takes the player's old Seat C. Nobody else moves.
        var (careerPath, seasonId) = FoldTwoBattleRounds(
            "swap.ams2career",
            playerWins: true,
            round2Call: new SmgpRivalCall { RivalDriverId = "driver.a", SeatSwapAccepted = true });

        using var db = CareerDatabase.Open(careerPath);
        var smgp = StateStore.ReadRoundPlayerState(db, seasonId, 2)!.Player.Smgp!;
        Assert.Equal(SeatA, smgp.CurrentSeatLivery);
        Assert.Equal(SeatD, smgp.AiSeatOverrides["driver.a"]);
        Assert.Equal(SeatC, smgp.AiSeatOverrides["driver.d"]);
        Assert.Equal(2, smgp.AiSeatOverrides.Count);
        Assert.False(smgp.CareerOver);
        // The consumed trigger reset the streak (the ladder restarts after each offer).
        Assert.Equal(0, smgp.TallyFor("driver.a").PlayerStreak);

        // One battle row per battle round + the seat row on the trigger round.
        var journal = JournalStore.ReadSeason(db, seasonId);
        Assert.Equal(2, journal.Count(r => r.Phase == JournalPhases.SmgpBattle));
        var seatRow = Assert.Single(journal, r => r.Phase == JournalPhases.SmgpSeat);
        Assert.Equal("seat-swap", seatRow.Cause);

        AssertResimulatesByteIdentically(db);
    }

    [Fact]
    public void TwoLosses_AtMidLadder_DemotesThePlayerDownTheMirroredChain()
    {
        // The LEVEL A rival beats the player (Seat C) twice: he takes Seat C, the player is
        // demoted one tier below (D) -> Seat D, and Seat D's occupant takes the rival's old Seat A.
        var (careerPath, seasonId) = FoldTwoBattleRounds(
            "forfeit.ams2career",
            playerWins: false,
            round2Call: new SmgpRivalCall { RivalDriverId = "driver.a" });

        using var db = CareerDatabase.Open(careerPath);
        var smgp = StateStore.ReadRoundPlayerState(db, seasonId, 2)!.Player.Smgp!;
        Assert.Equal(SeatD, smgp.CurrentSeatLivery);
        Assert.Equal(SeatC, smgp.AiSeatOverrides["driver.a"]);
        Assert.Equal(SeatA, smgp.AiSeatOverrides["driver.d"]);
        Assert.False(smgp.CareerOver);
        Assert.Equal("seat-forfeit",
            Assert.Single(JournalStore.ReadSeason(db, seasonId), r => r.Phase == JournalPhases.SmgpSeat).Cause);

        AssertResimulatesByteIdentically(db);
    }

    [Fact]
    public void TwoLosses_AtTheFloorTeam_EndTheCareer_WithoutMovingSeats()
    {
        // The player IS the floor team (Seat D, the last ladder seat): the second loss is the
        // replica's one hard-fail state — CareerOver, nothing moves, later battles fold inert.
        var (careerPath, seasonId) = FoldTwoBattleRounds(
            "career-over.ams2career",
            playerWins: false,
            round2Call: new SmgpRivalCall { RivalDriverId = "driver.a" },
            playerSeat: SeatD);

        using var db = CareerDatabase.Open(careerPath);
        var smgp = StateStore.ReadRoundPlayerState(db, seasonId, 2)!.Player.Smgp!;
        Assert.True(smgp.CareerOver);
        Assert.Equal(SeatD, smgp.CurrentSeatLivery);
        Assert.Empty(smgp.AiSeatOverrides);
        Assert.DoesNotContain(JournalStore.ReadSeason(db, seasonId), r => r.Phase == JournalPhases.SmgpSeat);

        AssertResimulatesByteIdentically(db);
    }

    [Fact]
    public void AfterAnAcceptedSwap_TheNextRound_SeatsTheSwappedCars_EverywhereTheSameWay()
    {
        // M3 slice 3: the round AFTER the swap — the session grid (display + staging), the
        // fold, and replay all seat the player on the rival's old car, the rival on the tier-D
        // car, the displaced driver on the player's old car, and NOBODY else moves. Identities
        // (driver ids) stay with the drivers; cars (team, livery) stay with the seats.
        string packDirectory = Path.Combine(_root, "packs", "post-swap");
        TestPackBuilder.Write(LadderPack(), packDirectory);
        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs", "post-swap"),
            library: FourSeatLibrary());
        string careerPath = Path.Combine(_root, "careers", "post-swap.ams2career");

        using (var session = CareerSessionService.CreateCareer(
                   new CareerCreationRequest
                   {
                       PackDirectory = packDirectory,
                       CareerFilePath = careerPath,
                       CareerName = "post-swap",
                       MasterSeed = Seed,
                       PlayerLiveryName = SeatC,
                       SmgpMode = true,
                   },
                   environment))
        {
            for (int round = 1; round <= 2; round++)
            {
                ApplyPlayerFirst(session, round == 2
                    ? new SmgpRivalCall { RivalDriverId = "driver.a", SeatSwapAccepted = true }
                    : new SmgpRivalCall { RivalDriverId = "driver.a" });
            }

            // Round 3's grid: the swap is live.
            var seats = session.CurrentGrid();
            var player = seats.Single(s => s.IsPlayer);
            Assert.Equal(SeatA, player.Ams2LiveryName);
            Assert.Equal("driver.c", player.DriverId);   // identity rides with the player
            Assert.Equal("team.a", player.TeamId);       // the car's team rides with the car
            Assert.Equal(SeatD, seats.Single(s => s.DriverId == "driver.a").Ams2LiveryName);
            Assert.Equal(SeatC, seats.Single(s => s.DriverId == "driver.d").Ams2LiveryName);
            Assert.Equal(SeatB, seats.Single(s => s.DriverId == "driver.b").Ams2LiveryName);

            // ...and the post-swap round FOLDS on that grid (the expected finish now measures
            // the player against the field from the LEVEL A car).
            ApplyPlayerFirst(session, rival: null);
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var db = CareerDatabase.Open(careerPath);
        AssertResimulatesByteIdentically(db);
    }

    private static void ApplyPlayerFirst(ICareerSession session, SmgpRivalCall? rival)
    {
        var others = session.CurrentGrid()
            .Select(s => s.DriverId)
            .Where(id => !string.Equals(id, session.Summary.PlayerDriverId, StringComparison.Ordinal))
            .ToList();
        session.Apply(new ResultDraft
        {
            Classified = new List<string> { session.Summary.PlayerDriverId }.Concat(others).ToList(),
            DidNotFinish = new Dictionary<string, string>(),
            Disqualified = [],
            SmgpRival = rival,
        });
    }

    [Fact]
    public void TheFloorTeamsSecondCar_IsNotInvincible_TeamLevelCareerOver()
    {
        // Two-car floor team (the 26-car field shape): the player starts in team.d's SECOND car.
        // Losing the rival battle twice must still end the career — the floor check is
        // TEAM-level, not a compare against the last ladder car's livery.
        const string SeatE = "Stock Livery #5";
        var basePack = LadderPack();
        var pack = basePack with
        {
            Drivers = [.. basePack.Drivers, TestPackBuilder.Driver("driver.e")],
            Entries = [.. basePack.Entries, TestPackBuilder.Entry("team.d", "driver.e", "5", SeatE) with { Rounds = "1-3" }],
        };

        string packDirectory = Path.Combine(_root, "packs", "floor-second-car");
        TestPackBuilder.Write(pack, packDirectory);
        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs", "floor-second-car"),
            library: FiveSeatLibrary());
        string careerPath = Path.Combine(_root, "careers", "floor-second-car.ams2career");

        using (var session = CareerSessionService.CreateCareer(
                   new CareerCreationRequest
                   {
                       PackDirectory = packDirectory,
                       CareerFilePath = careerPath,
                       CareerName = "floor-second-car",
                       MasterSeed = Seed,
                       PlayerLiveryName = SeatE,
                       SmgpMode = true,
                   },
                   environment))
        {
            ApplyPlayerLast(session, new SmgpRivalCall { RivalDriverId = "driver.a" });
            ApplyPlayerLast(session, new SmgpRivalCall { RivalDriverId = "driver.a" });
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var db = CareerDatabase.Open(careerPath);
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;
        var smgp = StateStore.ReadRoundPlayerState(db, seasonId, 2)!.Player.Smgp!;
        Assert.True(smgp.CareerOver);
        Assert.Equal(SeatE, smgp.CurrentSeatLivery); // nothing moved — the game-over screen
    }

    private static void ApplyPlayerLast(ICareerSession session, SmgpRivalCall rival)
    {
        var others = session.CurrentGrid()
            .Select(s => s.DriverId)
            .Where(id => !string.Equals(id, session.Summary.PlayerDriverId, StringComparison.Ordinal))
            .ToList();
        session.Apply(new ResultDraft
        {
            Classified = others.Append(session.Summary.PlayerDriverId).ToList(),
            DidNotFinish = new Dictionary<string, string>(),
            Disqualified = [],
            SmgpRival = rival,
        });
    }

    private static Companion.Ams2.ContentLibrary.Ams2ContentLibrary FiveSeatLibrary()
    {
        var library = TestPackBuilder.Library();
        return new()
        {
            ExtractedFrom = library.ExtractedFrom,
            Classes = library.Classes,
            Vehicles = library.Vehicles,
            Tracks = library.Tracks,
            Liveries = new Dictionary<string, Companion.Ams2.ContentLibrary.Ams2LiveryClassEntry>(StringComparer.Ordinal)
            {
                [TestPackBuilder.VintageClass] = new()
                {
                    Name = TestPackBuilder.VintageClass,
                    StockLib1563 = [SeatA, SeatB, SeatC, SeatD, "Stock Livery #5"],
                },
            },
        };
    }

    [Fact]
    public void RoundsWithoutARivalCall_FoldNoBattleRows()
    {
        // The off path: an smgp career whose envelopes never stored a rival call folds exactly
        // like slice 1 left it — no battle rows, no seat rows, untouched state.
        var (careerPath, seasonId) = FoldTwoBattleRounds(
            "no-calls.ams2career", playerWins: true, round2Call: null, round1Call: false);

        using var db = CareerDatabase.Open(careerPath);
        var journal = JournalStore.ReadSeason(db, seasonId);
        Assert.DoesNotContain(journal, r => r.Phase == JournalPhases.SmgpBattle);
        Assert.DoesNotContain(journal, r => r.Phase == JournalPhases.SmgpSeat);
        var smgp = StateStore.ReadRoundPlayerState(db, seasonId, 2)!.Player.Smgp!;
        Assert.Empty(smgp.Tallies);
        Assert.Equal(SeatC, smgp.CurrentSeatLivery);
    }

    // ---------- the four-tier ladder pack + the fold driver ----------

    /// <summary>Four one-driver teams down the authored ladder (A/B/C/D), THREE rounds (so a
    /// round can still fold after a round-2 swap), smgp style.</summary>
    private static SeasonPack LadderPack()
    {
        var basePack = TestPackBuilder.TwoRoundPack();
        return basePack with
        {
            Manifest = basePack.Manifest with { CareerStyle = SmgpRules.CareerStyle },
            Season = basePack.Season with
            {
                Rounds =
                [
                    TestPackBuilder.Round(1, "1967-01-02"),
                    TestPackBuilder.Round(2, "1967-05-07"),
                    TestPackBuilder.Round(3, "1967-09-03"),
                ],
            },
            Teams =
            [
                Team("team.a", "Alpha", prestige: 5),
                Team("team.b", "Bravo", prestige: 4),
                Team("team.c", "Charlie", prestige: 3),
                Team("team.d", "Delta", prestige: 2),
            ],
            Drivers =
            [
                TestPackBuilder.Driver("driver.a"),
                TestPackBuilder.Driver("driver.b"),
                TestPackBuilder.Driver("driver.c"),
                TestPackBuilder.Driver("driver.d"),
            ],
            Entries =
            [
                Entry("team.a", "driver.a", "1", SeatA),
                Entry("team.b", "driver.b", "2", SeatB),
                Entry("team.c", "driver.c", "3", SeatC),
                Entry("team.d", "driver.d", "4", SeatD),
            ],
        };
    }

    private static PackEntry Entry(string teamId, string driverId, string number, string livery) =>
        TestPackBuilder.Entry(teamId, driverId, number, livery) with { Rounds = "1-3" };

    private static PackTeam Team(string id, string name, int prestige) => new()
    {
        Id = id,
        Name = name,
        CarVehicleIds = [TestPackBuilder.VintageCar],
        Reliability = 0.93,
        Prestige = prestige,
        BudgetTier = prestige,
    };

    /// <summary>The stock test library, rebuilt with a livery list covering all four ladder seats.</summary>
    private static Companion.Ams2.ContentLibrary.Ams2ContentLibrary FourSeatLibrary()
    {
        var library = TestPackBuilder.Library();
        return new()
        {
            ExtractedFrom = library.ExtractedFrom,
            Classes = library.Classes,
            Vehicles = library.Vehicles,
            Tracks = library.Tracks,
            Liveries = new Dictionary<string, Companion.Ams2.ContentLibrary.Ams2LiveryClassEntry>(StringComparer.Ordinal)
            {
                [TestPackBuilder.VintageClass] = new()
                {
                    Name = TestPackBuilder.VintageClass,
                    StockLib1563 = [SeatA, SeatB, SeatC, SeatD],
                },
            },
        };
    }

    /// <summary>Creates an smgp-mode career on the ladder pack and folds two rounds carrying
    /// rival calls against driver.a — the player finishing first (or last) of the four.</summary>
    private (string CareerPath, long SeasonId) FoldTwoBattleRounds(
        string fileName, bool playerWins, SmgpRivalCall? round2Call,
        bool round1Call = true, string playerSeat = SeatC)
    {
        string name = Path.GetFileNameWithoutExtension(fileName);
        string packDirectory = Path.Combine(_root, "packs", name);
        TestPackBuilder.Write(LadderPack(), packDirectory);
        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs", name),
            library: FourSeatLibrary());

        string careerPath = Path.Combine(_root, "careers", fileName);
        using (var session = CareerSessionService.CreateCareer(
                   new CareerCreationRequest
                   {
                       PackDirectory = packDirectory,
                       CareerFilePath = careerPath,
                       CareerName = name,
                       MasterSeed = Seed,
                       PlayerLiveryName = playerSeat,
                       SmgpMode = true,
                   },
                   environment))
        {
            for (int round = 1; round <= 2; round++)
            {
                var others = session.CurrentGrid()
                    .Select(s => s.DriverId)
                    .Where(id => !string.Equals(id, session.Summary.PlayerDriverId, StringComparison.Ordinal))
                    .ToList();
                var classified = playerWins
                    ? new List<string> { session.Summary.PlayerDriverId }.Concat(others).ToList()
                    : others.Append(session.Summary.PlayerDriverId).ToList();
                session.Apply(new ResultDraft
                {
                    Classified = classified,
                    DidNotFinish = new Dictionary<string, string>(),
                    Disqualified = [],
                    SmgpRival = round == 1
                        ? (round1Call ? new SmgpRivalCall { RivalDriverId = "driver.a" } : null)
                        : round2Call,
                });
            }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var db = CareerDatabase.Open(careerPath);
        return (careerPath, CareerStore.ReadSeasons(db).Single().Id);
    }

    private static void AssertResimulatesByteIdentically(CareerDatabase db, string playerDriverId = "driver.c")
    {
        var rules = CareerRulesData.Load(ViewModelTestData.RulesDirectory);
        var report = ReplayService.Resimulate(db, LadderPack(), unchecked((ulong)Seed), new ReplaySimInputs
        {
            AgingCurves = rules.AgingCurves,
            Archetypes = rules.Archetypes,
            Headlines = rules.Headlines,
            PlayerDriverId = playerDriverId,
            PlayerAge = 30,
            CharacterRules = rules.Character,
        });
        Assert.True(report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} stored={report.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={report.FirstDivergence?.RegeneratedDeltaJson}");
        Assert.True(report.ComparedRows > 0);
    }
}
