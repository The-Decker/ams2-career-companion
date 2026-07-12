using Companion.Core.Career;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.Core.Smgp;
using Companion.Data;
using Companion.Tests.ViewModels;
using Companion.ViewModels.Review;
using Companion.ViewModels.Services;

namespace Companion.Tests.Data;

/// <summary>
/// The PER-SEASON DNQ RE-ROLL (17-season campaign): a career gated on <see cref="SmgpState.PerSeasonDnq"/>
/// re-rolls its backmarker DNQ field every season 2+ (each season a fresh seeded field). The starter set
/// is a FOLD INPUT (grid membership → seat-strength → the byte-compared player rows), so the SAME ordinal-
/// keyed transform must be applied on BOTH the live-fold pack (CareerSessionService ctor) and the replay
/// pack (ReplayService.ResimulateCore). These tests drive a real two-season carryover DNQ career over the
/// actual machinery and assert: the flag is seeded + carried; season 2's runtime field is exactly the
/// ordinal-2 re-roll; and the whole multi-season career RE-SIMULATES BYTE-IDENTICALLY (the locked invariant).
/// </summary>
public sealed class SmgpMultiSeasonDnqTests : IDisposable
{
    private const string SeatC = "Stock Livery #3"; // team.c LEVEL C — the player's start
    private const long Seed = 20260712;
    private const string PlayerId = Companion.Core.Grid.RoundGridResolver.SyntheticPlayerDriverId;

    private readonly string _root = Directory.CreateTempSubdirectory("companion-smgp-mdnq-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    [Fact]
    public void PerSeasonDnqFlag_IsSeededAtCreation_AndCarriedToSeasonTwo()
    {
        PlaySeasonOneAndSign();

        using var db = CareerDatabase.Open(CareerPath);
        var seasons = CareerStore.ReadSeasons(db);
        Assert.Equal(2, seasons.Count);
        var s1Start = StateStore.ReadPlayerState(db, seasons[0].Id, StateStore.StageStart)!.Smgp!;
        var s2Start = StateStore.ReadPlayerState(db, seasons[1].Id, StateStore.StageStart)!.Smgp!;
        Assert.True(s1Start.PerSeasonDnq); // seeded for a DNQ pack at creation
        Assert.True(s2Start.PerSeasonDnq); // carried across the rollover
        Assert.True(s1Start.StandingsReshuffle);
        Assert.True(s2Start.StandingsReshuffle);
    }

    [Fact]
    public void SeasonTwo_RuntimePack_IsTheOrdinalTwoReRoll_AndReplaysByteIdentical()
    {
        SeasonPack pinnedSeasonOne = PlaySeasonOneAndSign();

        using (var s2 = CareerSessionService.OpenCareer(CareerPath, Environment()))
        {
            // The runtime Pack the live fold resolves the grid from IS the ordinal-2 re-roll of the pinned
            // pack (variety only shuffles venues, keeping the grid with the round number, so the DNQ field
            // equals ForSeason regardless of variety). This is exactly what ResimulateCore re-applies.
            var expected = SmgpGridReshuffle.ForNextSeason(
                pinnedSeasonOne, SeasonOneFinal(pinnedSeasonOne), SeatC);
            expected = SmgpDnqField.ForSeason(expected, 2, unchecked((ulong)Seed));
            Assert.Equal(expected.Entries, s2.Pack.Entries);
            foreach (var round in s2.Pack.Season.Rounds)
            {
                var expectedStarters = expected.Season.Rounds.Single(r => r.Round == round.Round)
                    .Grid!.StarterDriverIds.ToHashSet(StringComparer.Ordinal);
                Assert.Equal(expectedStarters, round.Grid!.StarterDriverIds.ToHashSet(StringComparer.Ordinal));
            }

            // Play a round of season 2 so replay has a season-2 fold to re-derive against the re-rolled grid.
            ApplyRound(s2);
        }

        AssertResimulatesByteIdentically();
    }

    // ---------- scaffolding ----------

    private string PacksRoot => Path.Combine(_root, "packs");
    private string CareerPath => Path.Combine(_root, "career.ams2career");

    private CareerEnvironment Environment() => new()
    {
        ContentLibrary = FiveSeatLibrary(),
        LocateInstall = static () => null,
        DocumentsDirectory = Path.Combine(_root, "docs"),
        RulesDirectory = ViewModelTestData.RulesDirectory,
        PackSearchRoots = () => [PacksRoot],
    };

    /// <summary>Five one-driver teams down the ladder, TWO rounds, each capping the grid at 4 → one car
    /// DNQs per round (the seeded roll picks which). SMGP style. The player takes Seat C.</summary>
    private static SeasonPack DnqLadderPack()
    {
        var basePack = TestPackBuilder.TwoRoundPack();
        var grid = new PackRoundGrid
        {
            Size = 4,
            StarterDriverIds = ["driver.a", "driver.b", "driver.c", "driver.d"], // baked; the transform re-rolls
        };
        return basePack with
        {
            Manifest = basePack.Manifest with { CareerStyle = SmgpRules.CareerStyle },
            Teams =
            [
                Team("team.a", 5), Team("team.b", 4), Team("team.c", 3), Team("team.d", 2), Team("team.e", 3),
            ],
            Drivers =
            [
                TestPackBuilder.Driver("driver.a"), TestPackBuilder.Driver("driver.b"),
                TestPackBuilder.Driver("driver.c"), TestPackBuilder.Driver("driver.d"),
                TestPackBuilder.Driver("driver.e"),
            ],
            Entries =
            [
                TestPackBuilder.Entry("team.a", "driver.a", "1", "Stock Livery #1"),
                TestPackBuilder.Entry("team.b", "driver.b", "2", "Stock Livery #2"),
                TestPackBuilder.Entry("team.c", "driver.c", "3", SeatC),
                TestPackBuilder.Entry("team.d", "driver.d", "4", "Stock Livery #4"),
                TestPackBuilder.Entry("team.e", "driver.e", "5", "Stock Livery #5"),
            ],
            Season = basePack.Season with
            {
                Rounds = basePack.Season.Rounds.Select(r => r with { Grid = grid }).ToList(),
            },
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
                    StockLib1563 = ["Stock Livery #1", "Stock Livery #2", SeatC, "Stock Livery #4", "Stock Livery #5"],
                },
            },
        };
    }

    /// <summary>Creates the DNQ career, plays season 1 to completion, accepts the top offer and signs into
    /// the same-pack carryover season 2. Returns the pinned season-1 pack (the seeded creation roll).</summary>
    private SeasonPack PlaySeasonOneAndSign()
    {
        string packDirectory = Path.Combine(PacksRoot, "smgp-dnq-ladder");
        TestPackBuilder.Write(DnqLadderPack(), packDirectory);

        SeasonPack pinned;
        using (var session = CareerSessionService.CreateCareer(new CareerCreationRequest
        {
            PackDirectory = packDirectory,
            CareerFilePath = CareerPath,
            CareerName = "Multi DNQ Career",
            MasterSeed = Seed,
            PlayerLiveryName = SeatC,
            SmgpMode = true,
        }, Environment()))
        {
            pinned = session.Pack; // season 1: variety + ForSeason are both no-ops, so this is the pinned pack

            while (!session.Summary.SeasonComplete)
                ApplyRound(session);

            var review = session.SeasonReview();
            Assert.NotNull(review);
            session.AcceptOffer(review!.Offers[0].TeamId);

            var vm = new SeasonReviewViewModel(session);
            vm.SignAndContinueCommand.Execute(null);
            Assert.Null(vm.TransitionError);
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        return pinned;
    }

    /// <summary>Applies one round with the player finishing first (the DNQ field determines who else is on
    /// the grid; the player is always cap-protected).</summary>
    private static void ApplyRound(ICareerSession session)
    {
        var grid = session.CurrentGrid().Select(s => s.DriverId).ToList();
        session.Apply(new ResultDraft
        {
            Classified = grid,
            DidNotFinish = new Dictionary<string, string>(),
            Disqualified = [],
        });
    }

    private void AssertResimulatesByteIdentically()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var db = CareerDatabase.Open(CareerPath);
        var rules = CareerRulesData.Load(ViewModelTestData.RulesDirectory);
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

    private StandingsSnapshot SeasonOneFinal(SeasonPack pack)
    {
        using var db = CareerDatabase.Open(CareerPath);
        long seasonId = CareerStore.ReadSeasons(db)[0].Id;
        var results = ResultStore.ReadSeasonResults(db, seasonId)
            .Where(stored => ChampionshipCalendar.IsChampionshipRound(pack, stored.Round))
            .Select(stored => stored.ToRoundResult())
            .ToList();
        return Companion.Core.Scoring.StandingsEngine.ComputeSeason(
            ChampionshipCalendar.ResolveScoring(pack), results).Final;
    }
}
