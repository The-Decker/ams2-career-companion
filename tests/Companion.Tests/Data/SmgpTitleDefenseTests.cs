using Companion.Core.Career;
using Companion.Core.Packs;
using Companion.Core.Smgp;
using Companion.Data;
using Companion.Tests.ViewModels;
using Companion.ViewModels.Review;
using Companion.ViewModels.Services;

namespace Companion.Tests.Data;

/// <summary>
/// M3 slice 4 — the championship title fold + the Madonna title defense, end to end over the
/// REAL carryover machinery. Winning the title arms the defense: the champion is reseated onto
/// the top ladder car, the RESERVED challenger (authored but never entered — the Ceara
/// convention) is introduced into a real car, and rounds 1 + 2 of the new season resolve under
/// the defense rule — at least one win keeps the seat, losing both fires the player down the
/// ladder with the challenger taking the champion car. Every path re-simulates byte-identically
/// (this exercises the multi-season rollover verifier over SmgpState's structural equality).
/// </summary>
public sealed class SmgpTitleDefenseTests : IDisposable
{
    private const string SeatA = "Stock Livery #1"; // team.a  LEVEL A
    private const string SeatB = "Stock Livery #2"; // team.b  LEVEL B
    private const string SeatC = "Stock Livery #3"; // team.c  LEVEL C  (the player's start)
    private const string SeatD = "Stock Livery #4"; // team.d  LEVEL D  (the floor)
    private const string SeatE = "Stock Livery #5"; // team.e  LEVEL C  (the challenger's own car)
    // The forced title-defense challenger is a REAL authored entry (like smgp-1's G. Ceara at
    // Bullets) — DefenseChallenger picks the authored Ceara id even with an entry, and the clean
    // seat model needs him racing in his own car (it introduces nobody).
    private const string Challenger = SmgpSchedule.CearaDriverId;
    // The clean-swap player is their OWN distinct driver, so replay scores them under the synthetic id.
    private const string PlayerId = Companion.Core.Grid.RoundGridResolver.SyntheticPlayerDriverId;
    private const long Seed = 20260710;

    private readonly string _root = Directory.CreateTempSubdirectory("companion-smgp-title-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    [Fact]
    public void WinningTheTitle_ArmsTheDefense_AndOneWin_KeepsTheChampionSeat()
    {
        WinSeasonOneAndSign();

        using (var s2 = CareerSessionService.OpenCareer(CareerPath, Environment()))
        {
            // The championship banked a title and moved the champion up the ladder; the
            // reserved challenger was introduced into the floor car (no team.bullets here).
            using (var db = CareerDatabase.Open(CareerPath))
            {
                long season2 = CareerStore.ReadSeasons(db)[^1].Id;
                var start = StateStore.ReadPlayerState(db, season2, StateStore.StageStart)!.Smgp!;
                Assert.Equal(1, start.Titles);
                Assert.True(start.TitleDefense);
                Assert.Equal(SeatA, start.CurrentSeatLivery); // champion moved into Madonna's car
                Assert.Empty(start.AiSeatOverrides);          // CLEAN: no cascade, no introduction
                Assert.Empty(start.Tallies);                  // streaks reset
            }

            // The season-2 grid: the player holds Madonna (Senna benched); the challenger races his
            // OWN car; everyone else keeps their home seat.
            var seats = s2.CurrentGrid();
            Assert.Equal(SeatA, seats.Single(s => s.IsPlayer).Ams2LiveryName);
            Assert.Equal(SeatE, seats.Single(s => s.DriverId == Challenger).Ams2LiveryName);
            Assert.DoesNotContain(seats, s => s.DriverId == "driver.a"); // the champion car's AI benches
            Assert.Equal(5, seats.Count);

            // R1: the forced challenge, player ahead — the defense is decided but resolves at R2.
            ApplyRound(s2, playerWins: true, rival: ForcedCall());
            // R2: player behind — SmgpRules.TitleDefense(round1, round2) still says KEPT.
            ApplyRound(s2, playerWins: false, rival: ForcedCall());
        }

        using (var db = CareerDatabase.Open(CareerPath))
        {
            long season2 = CareerStore.ReadSeasons(db)[^1].Id;
            var after = StateStore.ReadRoundPlayerState(db, season2, 2)!.Player.Smgp!;
            Assert.False(after.TitleDefense);              // resolved
            Assert.Equal(SeatA, after.CurrentSeatLivery);  // Madonna kept
            Assert.Empty(after.Tallies);                   // defense battles never tally

            var journal = JournalStore.ReadSeason(db, season2);
            Assert.Equal("defense-round-won",
                Assert.Single(journal, r => r.Phase == JournalPhases.SmgpBattle && r.Round == 1).Cause);
            Assert.Equal("defense-held",
                Assert.Single(journal, r => r.Phase == JournalPhases.SmgpBattle && r.Round == 2).Cause);
        }

        AssertResimulatesByteIdentically();
    }

    [Fact]
    public void LosingBothDefenseRounds_FiresThePlayerDownTheLadder()
    {
        WinSeasonOneAndSign();

        using (var s2 = CareerSessionService.OpenCareer(CareerPath, Environment()))
        {
            ApplyRound(s2, playerWins: false, rival: ForcedCall());
            ApplyRound(s2, playerWins: false, rival: ForcedCall());
        }

        using (var db = CareerDatabase.Open(CareerPath))
        {
            long season2 = CareerStore.ReadSeasons(db)[^1].Id;
            var after = StateStore.ReadRoundPlayerState(db, season2, 2)!.Player.Smgp!;
            Assert.False(after.TitleDefense);
            // No team.dardan authored → the structural demotion: first seat one tier below A (B).
            // CLEAN: only the player moves — Madonna reverts to Senna, the challenger keeps his car.
            Assert.Equal(SeatB, after.CurrentSeatLivery);
            Assert.Empty(after.AiSeatOverrides);

            Assert.Equal("defense-lost",
                Assert.Single(JournalStore.ReadSeason(db, season2), r => r.Phase == JournalPhases.SmgpSeat).Cause);
        }

        AssertResimulatesByteIdentically();
    }

    [Fact]
    public void ScheduleRules_AreThePureDesignFacts()
    {
        var pack = LadderPack();
        var armed = new SmgpState { CurrentSeatLivery = SeatA, TitleDefense = true };
        var idle = new SmgpState { CurrentSeatLivery = SeatC };

        // Forced ONLY at rounds 1 + 2 of an armed season.
        Assert.Equal(Challenger, SmgpSchedule.ForcedChallenger(pack, armed, 1));
        Assert.Equal(Challenger, SmgpSchedule.ForcedChallenger(pack, armed, 2));
        Assert.Null(SmgpSchedule.ForcedChallenger(pack, armed, 3));
        Assert.Null(SmgpSchedule.ForcedChallenger(pack, idle, 1));
        Assert.Null(SmgpSchedule.ForcedChallenger(pack, armed with { CareerOver = true }, 1));

        // The reserved-challenger convention: the strongest driver without a season entry;
        // the authored Ceara id wins outright when present.
        Assert.Equal(Challenger, SmgpSchedule.DefenseChallenger(pack));
        var withCeara = pack with
        {
            Drivers = [.. pack.Drivers, TestPackBuilder.Driver(SmgpSchedule.CearaDriverId)],
        };
        Assert.Equal(SmgpSchedule.CearaDriverId, SmgpSchedule.DefenseChallenger(withCeara));

        // Champion seat = the top of the authored ladder; a champion already there stays put.
        Assert.Equal(SeatA, SmgpSchedule.ChampionSeat(pack));
        var already = SmgpSchedule.ChampionRollover(pack, armed);
        Assert.Equal(SeatA, already.CurrentSeatLivery);
        Assert.Empty(already.AiSeatOverrides); // CLEAN: the champion just holds Madonna, no introduction
    }

    // ---------- the ladder pack + drivers ----------

    private string PacksRoot => Path.Combine(_root, "packs");

    private string CareerPath => Path.Combine(_root, "career.ams2career");

    private CareerEnvironment Environment() => new()
    {
        ContentLibrary = FourSeatLibrary(),
        LocateInstall = static () => null,
        DocumentsDirectory = Path.Combine(_root, "docs"),
        RulesDirectory = ViewModelTestData.RulesDirectory,
        PackSearchRoots = () => [PacksRoot],
    };

    /// <summary>Four one-driver teams down the ladder plus the RESERVED challenger (authored,
    /// 0.99, no entry), two rounds, smgp style. The player takes Seat C.</summary>
    private static SeasonPack LadderPack()
    {
        var basePack = TestPackBuilder.TwoRoundPack();
        return basePack with
        {
            Manifest = basePack.Manifest with { CareerStyle = SmgpRules.CareerStyle },
            Teams =
            [
                Team("team.a", 5), Team("team.b", 4), Team("team.c", 3), Team("team.d", 2), Team("team.e", 3),
            ],
            Drivers =
            [
                TestPackBuilder.Driver("driver.a"),
                TestPackBuilder.Driver("driver.b"),
                TestPackBuilder.Driver("driver.c"),
                TestPackBuilder.Driver("driver.d"),
                TestPackBuilder.Driver(Challenger) with
                {
                    Ratings = TestPackBuilder.Driver(Challenger).Ratings with { RaceSkill = 0.99 },
                },
            ],
            Entries =
            [
                TestPackBuilder.Entry("team.a", "driver.a", "1", SeatA),
                TestPackBuilder.Entry("team.b", "driver.b", "2", SeatB),
                TestPackBuilder.Entry("team.c", "driver.c", "3", SeatC),
                TestPackBuilder.Entry("team.d", "driver.d", "4", SeatD),
                TestPackBuilder.Entry("team.e", Challenger, "17", SeatE), // the challenger's own car
            ],
        };
    }

    private static PackTeam Team(string id, int prestige) => new()
    {
        Id = id,
        Name = id,
        CarVehicleIds = [TestPackBuilder.VintageCar],
        Reliability = 0.93,
        Prestige = prestige,
        BudgetTier = prestige,
    };

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
                    StockLib1563 = [SeatA, SeatB, SeatC, SeatD, SeatE],
                },
            },
        };
    }

    private static SmgpRivalCall ForcedCall() => new() { RivalDriverId = Challenger, Forced = true };

    /// <summary>Season 1: the player wins every round (champion on raw points), accepts the top
    /// offer and signs into the same-pack carryover season.</summary>
    private void WinSeasonOneAndSign()
    {
        string packDirectory = Path.Combine(PacksRoot, "smgp-ladder");
        TestPackBuilder.Write(LadderPack(), packDirectory);

        using var session = CareerSessionService.CreateCareer(new CareerCreationRequest
        {
            PackDirectory = packDirectory,
            CareerFilePath = CareerPath,
            CareerName = "Title Career",
            MasterSeed = Seed,
            PlayerLiveryName = SeatC,
            SmgpMode = true,
        }, Environment());

        while (!session.Summary.SeasonComplete)
            ApplyRound(session, playerWins: true, rival: null);

        var review = session.SeasonReview();
        Assert.NotNull(review);
        session.AcceptOffer(review!.Offers[0].TeamId);

        var vm = new SeasonReviewViewModel(session);
        vm.SignAndContinueCommand.Execute(null);
        Assert.Null(vm.TransitionError);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }

    private static void ApplyRound(ICareerSession session, bool playerWins, SmgpRivalCall? rival)
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
            SmgpRival = rival,
        });
    }

    private void AssertResimulatesByteIdentically()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var db = CareerDatabase.Open(CareerPath);
        var rules = Environment().Rules;
        var report = ReplayService.Resimulate(db, unchecked((ulong)Seed), new ReplaySimInputs
        {
            AgingCurves = rules.AgingCurves,
            Archetypes = rules.Archetypes,
            Headlines = rules.Headlines,
            PlayerDriverId = PlayerId,
            PlayerAge = 30,
            CharacterRules = rules.Character,
        });
        Assert.True(report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} season={report.FirstDivergence?.SeasonId} " +
            $"stored={report.FirstDivergence?.StoredDeltaJson} regen={report.FirstDivergence?.RegeneratedDeltaJson}");
        Assert.True(report.ComparedRows > 0);
    }
}
