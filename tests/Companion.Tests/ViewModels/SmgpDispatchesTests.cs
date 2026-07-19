using Companion.Core.Career;
using Companion.Core.Character;
using Companion.Core.Packs;
using Companion.Core.Smgp;
using Companion.Data;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>
/// The SMGP "living world" dispatch feed (Task 4) over the REAL career machinery: the reactive per-round news
/// the career writes as it unfolds, the player's own milestones plus the AI-world stories around them, voiced
/// through the dispatch corpus. A pure display projection over the folded results (deterministic body
/// selection off the master seed), so it never touches the fold, these assert the feed reads the stored
/// results correctly, is chronologically ordered newest-first, and is stable/deterministic. Scaffolding
/// mirrors <see cref="SmgpPaddockDepthTests"/>' real-ladder ladder.
/// </summary>
public sealed class SmgpDispatchesTests : IDisposable
{
    private const string SeatC = "Stock Livery #3"; // team.c LEVEL C, the player's start
    private const long Seed = 20260712;

    private readonly string _root = Directory.CreateTempSubdirectory("companion-smgp-dispatch-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    [Fact]
    public void A_player_win_writes_a_first_win_milestone_dispatch()
    {
        using var session = NewCareer();
        ApplyRound(session, playerWins: true);

        var feed = session.SmgpDispatches();
        Assert.NotEmpty(feed);
        var win = Assert.Single(feed, d => d.Headline == "FIRST WIN");
        Assert.Equal(SmgpDispatchKind.Milestone, win.Kind);
        Assert.False(string.IsNullOrWhiteSpace(win.Body));
        // The very start of the career is also on the board.
        Assert.Contains(feed, d => d.Headline == "ARRIVED");
    }

    [Fact]
    public void The_feed_is_ordered_newest_first_and_is_deterministic()
    {
        using var session = NewCareer();
        ApplyRound(session, playerWins: true);
        ApplyRound(session, playerWins: false);

        var feed = session.SmgpDispatches();
        // Chronological sort keys descend down the list (newest first).
        for (int i = 1; i < feed.Count; i++)
        {
            var prev = feed[i - 1];
            var cur = feed[i];
            bool ordered =
                prev.SortSeason > cur.SortSeason
                || (prev.SortSeason == cur.SortSeason && prev.SortRound > cur.SortRound)
                || (prev.SortSeason == cur.SortSeason && prev.SortRound == cur.SortRound && prev.SortSeq >= cur.SortSeq);
            Assert.True(ordered, $"feed not newest-first at index {i}");
        }

        // A pure projection off the master seed: a second call renders byte-identically.
        var again = session.SmgpDispatches();
        Assert.Equal(
            feed.Select(d => d.Headline + "|" + d.Body),
            again.Select(d => d.Headline + "|" + d.Body));
    }

    [Fact]
    public void An_ai_dominating_the_races_surfaces_a_world_dispatch()
    {
        using var session = NewCareer();
        // The player loses both rounds -> a consistent AI leads the field and the standings, so a world story
        // (a rival's win streak, or a leader/standings/title-race move) reaches the feed.
        ApplyRound(session, playerWins: false);
        ApplyRound(session, playerWins: false);

        var feed = session.SmgpDispatches();
        Assert.Contains(feed, d => d.Kind is SmgpDispatchKind.RivalWatch or SmgpDispatchKind.TitleRace);
    }

    [Fact]
    public void The_paddock_carries_a_rotating_rumor_line()
    {
        using var session = NewCareer();
        ApplyRound(session, playerWins: true);

        var paddock = session.SmgpPaddock();
        Assert.NotNull(paddock);
        Assert.False(string.IsNullOrWhiteSpace(paddock!.PaddockRumor));
    }

    [Fact]
    public void Outside_the_smgp_mode_the_feed_is_empty()
    {
        using var session = NewCareer(smgp: false);
        ApplyRound(session, playerWins: true);
        Assert.Empty(session.SmgpDispatches());
    }

    [Fact]
    public void A_fatal_accident_writes_a_setback_died_dispatch_and_the_career_replays_byte_identically()
    {
        // Character death & injury §6: a mortality SMGP career whose driver is KILLED in an accident writes a
        // living-world "TRAGEDY" setback. The dispatch is a pure display read over the folded journal, never a
        // fold input, so the dead career still re-simulates byte-for-byte.
        SeasonPack pack;
        string playerId;
        using (var session = NewMortalCareer(durability: -50.0)) // a lethal shunt (forced-death offset)
        {
            pack = session.Pack;
            playerId = session.Summary.PlayerDriverId;
            ApplyPlayerAccident(session, AccidentSeverity.Heavy);
            Assert.True(session.PlayerMortality().Deceased);

            var feed = session.SmgpDispatches();
            var tragedy = Assert.Single(feed, d => d.Headline == "TRAGEDY");
            Assert.Equal(SmgpDispatchKind.Setback, tragedy.Kind);
            Assert.False(string.IsNullOrWhiteSpace(tragedy.Body));
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var db = CareerDatabase.Open(CareerPath);
        var rules = CareerRulesData.Load(ViewModelTestData.RulesDirectory);
        var report = ReplayService.Resimulate(db, pack, unchecked((ulong)Seed), new ReplaySimInputs
        {
            AgingCurves = rules.AgingCurves,
            Archetypes = rules.Archetypes,
            Headlines = rules.Headlines,
            PlayerDriverId = playerId,
            PlayerAge = 30,
            CharacterRules = rules.Character,
        });
        Assert.True(report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} stored={report.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={report.FirstDivergence?.RegeneratedDeltaJson}");
    }

    // ---------- scaffolding (mirrors SmgpPaddockDepthTests' real-machinery ladder) ----------

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

    private CareerSessionService NewCareer(bool smgp = true)
    {
        string packDirectory = Path.Combine(PacksRoot, "smgp-ladder");
        TestPackBuilder.Write(LadderPack(smgp), packDirectory);
        return CareerSessionService.CreateCareer(new CareerCreationRequest
        {
            PackDirectory = packDirectory,
            CareerFilePath = CareerPath,
            CareerName = "Dispatch Career",
            MasterSeed = Seed,
            PlayerLiveryName = SeatC,
            SmgpMode = smgp,
        }, Environment());
    }

    /// <summary>An SMGP career with mortality ON and a character whose durability forces the accident roll,
    /// so a death can be pinned deterministically (an out-of-range offset floors the effective d500).</summary>
    private CareerSessionService NewMortalCareer(double durability)
    {
        string packDirectory = Path.Combine(PacksRoot, "smgp-ladder");
        TestPackBuilder.Write(LadderPack(true), packDirectory);
        return CareerSessionService.CreateCareer(new CareerCreationRequest
        {
            PackDirectory = packDirectory,
            CareerFilePath = CareerPath,
            CareerName = "Mortal Dispatch Career",
            MasterSeed = Seed,
            PlayerLiveryName = SeatC,
            SmgpMode = true,
            Character = Character(durability),
            Mortality = MortalityMode.Normal, // Normal keeps the file after death, so the feed can still read it
        }, Environment());
    }

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

    private static void ApplyPlayerAccident(ICareerSession session, AccidentSeverity severity)
    {
        string player = session.Summary.PlayerDriverId;
        var others = session.CurrentGrid()
            .Select(s => s.DriverId)
            .Where(id => !string.Equals(id, player, StringComparison.Ordinal))
            .ToList();
        session.Apply(new ResultDraft
        {
            Classified = others,
            DidNotFinish = new Dictionary<string, string>(StringComparer.Ordinal) { [player] = "a" },
            Disqualified = [],
            PlayerAccidentSeverity = severity,
        });
    }

    private static SeasonPack LadderPack(bool smgp = true)
    {
        var basePack = TestPackBuilder.TwoRoundPack();
        return basePack with
        {
            Manifest = basePack.Manifest with { CareerStyle = smgp ? SmgpRules.CareerStyle : "" },
            Teams =
            [
                Team("team.a", 5), Team("team.bullets", 4), Team("team.c", 3), Team("team.d", 2), Team("team.e", 3),
            ],
            Drivers =
            [
                TestPackBuilder.Driver("driver.a"),
                TestPackBuilder.Driver("driver.b"),
                TestPackBuilder.Driver("driver.c"),
                TestPackBuilder.Driver("driver.d"),
                TestPackBuilder.Driver(SmgpSchedule.CearaDriverId) with
                {
                    Ratings = TestPackBuilder.Driver(SmgpSchedule.CearaDriverId).Ratings with { RaceSkill = 0.99 },
                },
            ],
            Entries =
            [
                TestPackBuilder.Entry("team.a", "driver.a", "1", "Stock Livery #1"),
                TestPackBuilder.Entry("team.bullets", "driver.b", "2", "Stock Livery #2"),
                TestPackBuilder.Entry("team.c", "driver.c", "3", SeatC),
                TestPackBuilder.Entry("team.d", "driver.d", "4", "Stock Livery #4"),
                TestPackBuilder.Entry("team.e", SmgpSchedule.CearaDriverId, "17", "Stock Livery #5"),
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

    private static void ApplyRound(ICareerSession session, bool playerWins)
    {
        string player = session.Summary.PlayerDriverId;
        var others = session.CurrentGrid()
            .Select(s => s.DriverId)
            .Where(id => !string.Equals(id, player, StringComparison.Ordinal))
            .ToList();
        var classified = playerWins
            ? new List<string> { player }.Concat(others).ToList()
            : others.Append(player).ToList();
        session.Apply(new ResultDraft
        {
            Classified = classified,
            DidNotFinish = new Dictionary<string, string>(StringComparer.Ordinal),
            Disqualified = [],
        });
    }
}
