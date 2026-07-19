using Companion.Core.Packs;
using Companion.Core.Smgp;
using Companion.Data;
using Companion.Tests.ViewModels;
using Companion.ViewModels.Services;

namespace Companion.Tests.Data;

/// <summary>
/// The best-of-7 rivalry series (owner-approved 2026-07-19, docs/dev/smgp-series-ladder.md),
/// proven over the real fold machinery: the offer only fires on the FOURTH win (never two), a
/// live series carries across the season rollover, a lost series relegates above D, demotes to
/// the floor team at D, and ends the career at the floor team, and a series career replays
/// byte-identically. The legacy two-wins path keeps its own contract in
/// <see cref="SmgpBattleFoldDeterminismTests"/>.
/// </summary>
public sealed class SmgpSeriesFoldDeterminismTests : IDisposable
{
    private const string SeatA = "Stock Livery #1"; // team.a  LEVEL A
    private const string SeatB = "Stock Livery #2"; // team.b  LEVEL B
    private const string SeatC = "Stock Livery #3"; // team.c  LEVEL C, the player's start
    private const string SeatD = "Stock Livery #4"; // team.d  LEVEL D
    private const string SeatE = "Stock Livery #5"; // team.e  LEVEL D
    private const string SeatZ = "Stock Livery #6"; // team.zeroforce  the floor (ladder's last)
    private const long Seed = 20260719;

    private readonly string _root = Directory.CreateTempSubdirectory("companion-smgp-series-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    [Fact]
    public void TheOffer_FiresOnTheFourthWin_NeverBefore_AndReplaysByteIdentically()
    {
        string file = "four-wins.ams2career";
        using var session = CreateSeriesCareer(file, SeatC);
        // Rounds 1-3: banked wins, but NO offer, the two-wins rule is gone for series careers.
        for (int round = 1; round <= 3; round++)
        {
            ApplyPlayerFirst(session, Call("driver.a"));
            Assert.Null(session.CurrentSmgpPendingOffer());
            Assert.Equal(round, CurrentSmgp(file).TallyFor("driver.a").PlayerStreak);
        }

        // Round 4 takes the series: the deferred offer appears, the tally resets for the next one.
        ApplyPlayerFirst(session, Call("driver.a"));
        var pending = session.CurrentSmgpPendingOffer();
        Assert.NotNull(pending);
        Assert.Equal(SeatA, pending!.OfferedSeat);
        Assert.Equal(0, CurrentSmgp(file).TallyFor("driver.a").PlayerStreak);

        session.ResolveSmgpOffer(accept: true);
        Assert.Equal(SeatA, CurrentSmgp(file).CurrentSeatLivery);
        AssertResimulatesByteIdentically(file);
    }

    [Fact]
    public void ALiveSeries_CarriesAcrossTheSeasonRollover_AndCompletesInSeasonTwo()
    {
        string file = "carry.ams2career";
        using (var session = CreateSeriesCareer(file, SeatC))
        {
            // Two banked wins over driver.b in season 1, then finish the season without the rival.
            ApplyPlayerFirst(session, Call("driver.b"));
            ApplyPlayerFirst(session, Call("driver.b"));
            while (!session.Summary.SeasonComplete)
                ApplyPlayerFirst(session, rival: null);

            var review = session.SeasonReview();
            Assert.NotNull(review);
            session.AcceptOffer(review!.Offers[0].TeamId);
            var vm = new Companion.ViewModels.Review.SeasonReviewViewModel(session);
            vm.SignAndContinueCommand.Execute(null);
            Assert.Null(vm.TransitionError);
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        using var s2 = CareerSessionService.OpenCareer(CareerPath(file), SeriesEnvironment("carry"));
        // The series is still 2-0 at the next opener: carried, not reset. (The champion rolls
        // into the top ladder seat for season 2, driver.a's car, so this fight is over driver.b's
        // seat, one the player does not hold.)
        Assert.Equal(2, CurrentSmgp(file).TallyFor("driver.b").PlayerStreak);

        // Two more wins complete the series in season 2 (the carry actually matters).
        ApplyPlayerFirst(s2, Call("driver.b"));
        Assert.Null(s2.CurrentSmgpPendingOffer());
        ApplyPlayerFirst(s2, Call("driver.b"));
        var pending = s2.CurrentSmgpPendingOffer();
        Assert.NotNull(pending);
        Assert.Equal(SeatB, pending!.OfferedSeat);
        AssertResimulatesByteIdentically(file);
    }

    [Fact]
    public void SeriesLost_AboveD_RelegatesToTheTierBelow_AndReplaysByteIdentically()
    {
        string file = "relegate.ams2career";
        using var session = CreateSeriesCareer(file, SeatC);
        for (int round = 1; round <= 3; round++)
        {
            ApplyPlayerLast(session, Call("driver.a"));
            Assert.Equal(round, CurrentSmgp(file).TallyFor("driver.a").RivalStreak);
        }
        ApplyPlayerLast(session, Call("driver.a"));

        var state = CurrentSmgp(file);
        Assert.NotEqual(SeatC, state.CurrentSeatLivery);
        // C's forfeit drops one tier to a D seat (deterministic-random among the D teams).
        Assert.Contains(state.CurrentSeatLivery, new[] { SeatD, SeatE, SeatZ });
        Assert.False(state.CareerOver);
        AssertResimulatesByteIdentically(file);
    }

    [Fact]
    public void SeriesLost_AtD_DemotesToTheFloorTeam()
    {
        string file = "floor-drop.ams2career";
        using var session = CreateSeriesCareer(file, SeatD);
        for (int round = 1; round <= 4; round++)
            ApplyPlayerLast(session, Call("driver.a"));

        var state = CurrentSmgp(file);
        Assert.Equal(SeatZ, state.CurrentSeatLivery); // the original game's drop to the floor
        Assert.False(state.CareerOver);               // the drop is a demotion, not the end
        AssertResimulatesByteIdentically(file);
    }

    [Fact]
    public void SeriesLost_AtTheFloorTeam_EndsTheCareer()
    {
        string file = "floor-over.ams2career";
        using var session = CreateSeriesCareer(file, SeatZ);
        for (int round = 1; round <= 3; round++)
            ApplyPlayerLast(session, Call("driver.a"));
        Assert.False(CurrentSmgp(file).CareerOver);

        ApplyPlayerLast(session, Call("driver.a"));
        Assert.True(CurrentSmgp(file).CareerOver); // the floor's floor: game over
        AssertResimulatesByteIdentically(file);
    }

    // ---------- scaffolding ----------

    private string CareerPath(string fileName) => Path.Combine(_root, "careers", fileName);

    private CareerEnvironment SeriesEnvironment(string name) => new()
    {
        ContentLibrary = SixSeatLibrary(),
        LocateInstall = static () => null,
        DocumentsDirectory = Path.Combine(_root, "docs", name),
        RulesDirectory = ViewModelTestData.RulesDirectory,
        PackSearchRoots = () => [Path.Combine(_root, "packs")],
    };

    private CareerSessionService CreateSeriesCareer(string fileName, string playerSeat)
    {
        string name = Path.GetFileNameWithoutExtension(fileName);
        string packDirectory = Path.Combine(_root, "packs", name);
        TestPackBuilder.Write(SeriesLadderPack(), packDirectory);
        return CareerSessionService.CreateCareer(new CareerCreationRequest
        {
            PackDirectory = packDirectory,
            CareerFilePath = CareerPath(fileName),
            CareerName = name,
            MasterSeed = Seed,
            PlayerLiveryName = playerSeat,
            SmgpMode = true,
        }, SeriesEnvironment(name));
    }

    private static Companion.Data.SmgpRivalCall Call(string rivalId) => new() { RivalDriverId = rivalId };

    /// <summary>The latest folded SMGP state in a career file (the newest season's last round,
    /// or its start state before any round).</summary>
    private SmgpState CurrentSmgp(string fileName)
    {
        using var db = CareerDatabase.Open(CareerPath(fileName));
        long seasonId = CareerStore.ReadSeasons(db)[^1].Id;
        var rounds = StateStore.ReadRoundPlayerStates(db, seasonId);
        return rounds.Count > 0
            ? rounds[^1].State.Player.Smgp!
            : StateStore.ReadPlayerState(db, seasonId, StateStore.StageStart)!.Smgp!;
    }

    private static void ApplyPlayerFirst(ICareerSession session, Companion.Data.SmgpRivalCall? rival)
    {
        string playerId = session.Summary.PlayerDriverId;
        var others = session.CurrentGrid()
            .Select(s => s.DriverId)
            .Where(id => !string.Equals(id, playerId, StringComparison.Ordinal))
            .ToList();
        session.Apply(new ResultDraft
        {
            Classified = new List<string> { playerId }.Concat(others).ToList(),
            DidNotFinish = new Dictionary<string, string>(),
            Disqualified = [],
            SmgpRival = rival,
        });
    }

    private static void ApplyPlayerLast(ICareerSession session, Companion.Data.SmgpRivalCall? rival)
    {
        string playerId = session.Summary.PlayerDriverId;
        var others = session.CurrentGrid()
            .Select(s => s.DriverId)
            .Where(id => !string.Equals(id, playerId, StringComparison.Ordinal))
            .ToList();
        session.Apply(new ResultDraft
        {
            Classified = others.Append(playerId).ToList(),
            DidNotFinish = new Dictionary<string, string>(),
            Disqualified = [],
            SmgpRival = rival,
        });
    }

    private void AssertResimulatesByteIdentically(string fileName)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var db = CareerDatabase.Open(CareerPath(fileName));
        var rules = CareerRulesData.Load(ViewModelTestData.RulesDirectory);
        var report = ReplayService.Resimulate(db, unchecked((ulong)Seed), new ReplaySimInputs
        {
            AgingCurves = rules.AgingCurves,
            Archetypes = rules.Archetypes,
            Headlines = rules.Headlines,
            PlayerDriverId = Companion.Core.Grid.RoundGridResolver.SyntheticPlayerDriverId,
            PlayerAge = 30,
            CharacterRules = rules.Character,
        });
        Assert.True(report.Identical, $"diverged: {report.FirstDivergence?.Reason}");
        Assert.True(report.ComparedRows > 0);
    }

    /// <summary>Six one-driver teams: A/B/C above, D team.d + team.e, and the floor team last.
    /// SIX rounds, enough for a full series plus season-end.</summary>
    private static SeasonPack SeriesLadderPack()
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
                    TestPackBuilder.Round(2, "1967-02-06"),
                    TestPackBuilder.Round(3, "1967-03-06"),
                    TestPackBuilder.Round(4, "1967-04-03"),
                    TestPackBuilder.Round(5, "1967-05-01"),
                    TestPackBuilder.Round(6, "1967-06-05"),
                ],
            },
            Teams =
            [
                Team("team.a", 5), Team("team.b", 4), Team("team.c", 3),
                Team("team.d", 2), Team("team.e", 2), Team("team.zeroforce", 2),
            ],
            Drivers =
            [
                TestPackBuilder.Driver("driver.a"), TestPackBuilder.Driver("driver.b"),
                TestPackBuilder.Driver("driver.c"), TestPackBuilder.Driver("driver.d"),
                TestPackBuilder.Driver("driver.e"), TestPackBuilder.Driver("driver.z"),
            ],
            Entries =
            [
                Entry("team.a", "driver.a", "1", SeatA),
                Entry("team.b", "driver.b", "2", SeatB),
                Entry("team.c", "driver.c", "3", SeatC),
                Entry("team.d", "driver.d", "4", SeatD),
                Entry("team.e", "driver.e", "5", SeatE),
                Entry("team.zeroforce", "driver.z", "6", SeatZ),
            ],
        };
    }

    private static PackEntry Entry(string teamId, string driverId, string number, string livery) =>
        TestPackBuilder.Entry(teamId, driverId, number, livery) with { Rounds = "1-6" };

    private static PackTeam Team(string id, int prestige) => new()
    {
        Id = id, Name = id,
        CarVehicleIds = [TestPackBuilder.VintageCar],
        Reliability = 0.93, Prestige = prestige, BudgetTier = prestige,
    };

    private static Companion.Ams2.ContentLibrary.Ams2ContentLibrary SixSeatLibrary()
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
                    StockLib1563 = [SeatA, SeatB, SeatC, SeatD, SeatE, SeatZ],
                },
            },
        };
    }
}
