using Companion.Core.Career;
using Companion.Core.Character;
using Companion.Core.Packs;
using Companion.Core.Smgp;
using Companion.Data;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>
/// <see cref="ICareerSession.CampaignTimeline"/>, the whole campaign arc as one timeline. An SMGP
/// career pins the full 17-season horizon up front (played seasons Completed/Current, the future
/// Locked); a legacy historical career with no pinned plan lists only the seasons it has actually
/// played. Scaffolding mirrors <c>SmgpMultiSeasonDnqTests</c>' real-machinery DNQ ladder (synthetic
/// five-seat pack + library, career carried over via AcceptOffer/StartNextSeason + reopen).
/// </summary>
public sealed class CampaignTimelineTests : IDisposable
{
    private const string SeatC = "Stock Livery #3"; // team.c, the player's start
    private const long Seed = 20260712;

    private readonly string _root = Directory.CreateTempSubdirectory("companion-campaign-tl-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    // ---------- SMGP: the pinned 17-season horizon ----------

    [Fact]
    public void FreshSmgpCareer_PinsTheFullSeventeenSeasonArc()
    {
        using var session = CreateSmgpCareer("fresh");

        var timeline = session.CampaignTimeline();

        Assert.Equal(SmgpRules.CampaignSeasons, timeline.Count);
        Assert.Equal(17, timeline.Count);
        Assert.Equal(Enumerable.Range(1, 17), timeline.Select(e => e.Ordinal));

        Assert.Equal(CampaignSeasonState.Current, timeline[0].State);
        Assert.False(timeline[0].PlayerChampion);
        Assert.Null(timeline[0].PlayerPosition);   // no completed outcome yet

        Assert.All(timeline.Skip(1), entry => Assert.Equal(CampaignSeasonState.Locked, entry.State));
    }

    [Fact]
    public void LockedSeasonsRevealNothing_NoFutureSpoilers()
    {
        using var session = CreateSmgpCareer("nospoil");

        var timeline = session.CampaignTimeline();

        // A future season is an anonymous placeholder, its title, era and year stay hidden until
        // the player reaches it. The campaign is never previewed ahead; you meet each season on
        // arrival. (The current season DOES carry its identity, you are playing it.)
        foreach (var locked in timeline.Where(e => e.State == CampaignSeasonState.Locked))
        {
            Assert.Equal("", locked.Title);
            Assert.Equal("", locked.Era);
            Assert.Null(locked.Year);
            Assert.Null(locked.Preview);      // SMGP futures never preview, no-spoiler
            Assert.False(locked.IsPrologue);  // and the prologue slot is Dynasty-only
        }
        Assert.Equal(CampaignSeasonState.Current, timeline[0].State);
        Assert.Contains(timeline, e => e.State == CampaignSeasonState.Locked); // 16 of them, fresh
    }

    // ---------- Dynasty: the gated chronological timeline (dynasty-passport-roadmap Piece 1) ----------

    [Fact]
    public void FreshDynastyCareer_PrologueThenCurrentThenPreviewedLockedSeasons()
    {
        using var session = CreateDynastyCareer("dynasty-fresh");

        var timeline = session.CampaignTimeline();

        Assert.Equal(4, timeline.Count); // the prologue slot + three pinned seasons

        // The Formula Junior prologue heads the timeline: a labelled pre-championship stretch,
        // coming-soon until content exists, never playable.
        var prologue = timeline[0];
        Assert.Equal(0, prologue.Ordinal);
        Assert.True(prologue.IsPrologue);
        Assert.Equal(CampaignSeasonState.Locked, prologue.State);
        Assert.Equal("Formula Junior → 1967", prologue.Title);
        Assert.Null(prologue.Year);
        Assert.Null(prologue.Preview);

        var current = timeline[1];
        Assert.Equal(1, current.Ordinal);
        Assert.Equal(CampaignSeasonState.Current, current.State);
        Assert.Equal(1967, current.Year);
        Assert.False(current.IsPrologue);
        Assert.Null(current.Preview); // the current season shows its facts, not a preview

        // Locked future seasons are preview-only: full pack-level identity (historical years are
        // real-world known), and still Locked, never a play entry.
        var locked = timeline.Skip(2).ToArray();
        Assert.Equal(2, locked.Length);
        Assert.All(locked, e => Assert.Equal(CampaignSeasonState.Locked, e.State));
        Assert.All(locked, e => Assert.False(e.IsPrologue));
        Assert.All(locked, e => Assert.Null(e.Year)); // the year rides the preview, like SMGP's null

        var preview1969 = Assert.IsType<CampaignSeasonPreview>(locked[0].Preview);
        Assert.Equal(1969, preview1969.Year);
        Assert.Equal("Synthetic Championship 1969", preview1969.SeriesName);
        Assert.Equal("1960s", preview1969.EraLabel);
        Assert.Equal(2, preview1969.RoundCount);
        Assert.Equal(["Round 1", "Round 2"], preview1969.Venues);
        Assert.NotEmpty(preview1969.Teams);

        var preview2020 = Assert.IsType<CampaignSeasonPreview>(locked[1].Preview);
        Assert.Equal(2020, preview2020.Year);
        Assert.Equal("2020s", preview2020.EraLabel);
        Assert.Equal(2, preview2020.RoundCount);

        // Chronological and gap-honest: strictly increasing years, and no 1968 slot is invented
        // for the missing year (the plan bridges it, the timeline never fabricates a season).
        Assert.Equal([1967, 1969, 2020], timeline.Skip(1).Select(e => e.Year ?? e.Preview!.Year));
        Assert.DoesNotContain(timeline, e => e.Year == 1968 || e.Preview?.Year == 1968);
    }

    [Fact]
    public void DynastyArc_AfterSigning_CompletedThenCurrentThenPreviewedLocked()
    {
        PlayDynastySeasonOneAndSign("dynasty-advance");
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        using var session = CareerSessionService.OpenCareer(
            CareerPath("dynasty-advance"), DynastyEnvironment("dynasty-advance"));
        var timeline = session.CampaignTimeline();

        Assert.Equal(4, timeline.Count);
        Assert.True(timeline[0].IsPrologue);

        Assert.Equal(CampaignSeasonState.Completed, timeline[1].State);
        Assert.Equal(1967, timeline[1].Year);
        Assert.NotNull(timeline[1].PlayerPosition);

        Assert.Equal(CampaignSeasonState.Current, timeline[2].State);
        Assert.Equal(1969, timeline[2].Year);
        Assert.Null(timeline[2].Preview);

        Assert.Equal(CampaignSeasonState.Locked, timeline[3].State);
        var preview = Assert.IsType<CampaignSeasonPreview>(timeline[3].Preview);
        Assert.Equal(2020, preview.Year);
    }

    [Fact]
    public void DynastyTimeline_IsDisplayOnly_NeverJournals()
    {
        using var session = CreateDynastyCareer("dynasty-readonly");
        using var db = CareerDatabase.Open(CareerPath("dynasty-readonly"));
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;

        int before = JournalStore.ReadSeason(db, seasonId).Count;
        _ = session.CampaignTimeline();
        _ = session.CampaignTimeline(); // the memoized second read
        int after = JournalStore.ReadSeason(db, seasonId).Count;

        Assert.Equal(before, after); // a preview projection never becomes a fold input
    }

    /// <summary>Creates a Dynasty career over the synthetic three-pack catalog (1967, 1969, 2020)
    /// pinned at creation, the player starting 1967 as the sequence head.</summary>
    private CareerSessionService CreateDynastyCareer(string name)
    {
        WriteDynastyPack(name, 1967);
        WriteDynastyPack(name, 1969);
        WriteDynastyPack(name, 2020);
        return CareerSessionService.CreateCareer(new CareerCreationRequest
        {
            PackDirectory = Path.Combine(PacksRoot(name), "1967"),
            CareerFilePath = CareerPath(name),
            CareerName = "Dynasty Timeline Career",
            MasterSeed = Seed,
            ExperienceMode = CareerExperienceModes.GrandPrixDynasty,
            PlayerLiveryName = TestPackBuilder.StockLivery2,
            Character = VersionTwoCharacter(),
        }, DynastyEnvironment(name));
    }

    /// <summary>Plays the 1967 season to completion and signs into the pinned 1969 occurrence
    /// (the head-only continuation: only the current season can ever be entered).</summary>
    private void PlayDynastySeasonOneAndSign(string name)
    {
        using var session = CreateDynastyCareer(name);

        while (!session.Summary.SeasonComplete)
            ApplyRound(session);

        var review = session.SeasonReview();
        Assert.NotNull(review);
        Assert.NotEmpty(review!.Offers);
        string teamId = review.Offers[0].TeamId;
        session.AcceptOffer(teamId);
        session.StartNextSeason(teamId);
    }

    /// <summary>Dynasty discovery must see ONLY the synthetic catalog (the shared Environment
    /// helper also roots the real packs dir, which would pin every faithful pack on disk).</summary>
    private CareerEnvironment DynastyEnvironment(string name)
    {
        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, name, "docs"),
            library: TestPackBuilder.Library());
        environment.PackSearchRoots = () => [PacksRoot(name)];
        return environment;
    }

    private void WriteDynastyPack(string name, int year)
    {
        var pack = TestPackBuilder.TwoRoundPack();
        TestPackBuilder.Write(pack with
        {
            Manifest = pack.Manifest with
            {
                PackId = $"dynasty-{year}",
                Name = $"Synthetic {year}",
            },
            Season = pack.Season with
            {
                Year = year,
                SeriesName = $"Synthetic Championship {year}",
                Rounds =
                [
                    TestPackBuilder.Round(1, $"{year}-01-02"),
                    TestPackBuilder.Round(2, $"{year}-05-07"),
                ],
            },
        }, Path.Combine(PacksRoot(name), year.ToString()));
    }

    private static CharacterProfile VersionTwoCharacter()
    {
        var talent = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["pace"] = 0.70,
            ["oneLap"] = 0.65,
            ["craft"] = 0.60,
            ["racecraft"] = 0.62,
            ["adaptability"] = 0.58,
        };
        var meta = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["marketability"] = 0.50,
            ["durability"] = 0.55,
        };
        return new CharacterProfile
        {
            Name = "Dynasty Timeline Driver",
            CountryCode = "BRA",
            Age = 22,
            Stats = talent.Concat(meta).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            PerkIds = ["engineers_favorite"],
            CreationPerkIds = ["engineers_favorite"],
            ProgressionVersion = CharacterLevelProgression.Level300Version,
            MasteryEffectsVersion = CharacterProfile.CurrentMasteryEffectsVersion,
            ExpectationModelVersion = CharacterProfile.CurrentExpectationModelVersion,
            RacingDnaId = "dna_circuit_specialist",
            RacingDnaVersion = 1,
            RacingDnaChoice = "technical",
            CreationBaseline = new CharacterCreationBaseline
            {
                Stats = talent,
                Meta = meta,
                TraitIds = ["engineers_favorite"],
            },
        };
    }


    [Fact]
    public void AfterSeasonOne_TheArcAdvances_CompletedThenCurrentThenLocked()
    {
        PlaySmgpSeasonOneAndSign("advance");
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        using var session = CareerSessionService.OpenCareer(CareerPath("advance"), Environment("advance"));
        var timeline = session.CampaignTimeline();

        Assert.Equal(17, timeline.Count);

        Assert.Equal(CampaignSeasonState.Completed, timeline[0].State);
        Assert.NotNull(timeline[0].PlayerPosition);                // the season's final outcome
        Assert.InRange(timeline[0].PlayerPosition!.Value, 1, 5);   // five-seat field

        Assert.Equal(CampaignSeasonState.Current, timeline[1].State);
        Assert.Null(timeline[1].PlayerPosition);

        Assert.All(timeline.Skip(2), entry => Assert.Equal(CampaignSeasonState.Locked, entry.State));
    }

    // ---------- legacy historical: only the played seasons ----------

    [Fact]
    public void HistoricalCareerWithNoPlan_ListsOnlyPlayedSeasons()
    {
        string packDirectory = Path.Combine(_root, "hist", "pack");
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), packDirectory);
        using var session = CareerSessionService.CreateCareer(new CareerCreationRequest
        {
            PackDirectory = packDirectory,
            CareerFilePath = Path.Combine(_root, "hist", "hist.ams2career"),
            CareerName = "Plain 1967",
            MasterSeed = Seed,
            PlayerLiveryName = TestPackBuilder.StockLivery2,
        }, ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "hist", "docs"),
            library: TestPackBuilder.Library()));

        // Fresh: one in-progress season, no locked future horizon invented.
        var fresh = session.CampaignTimeline();
        var current = Assert.Single(fresh);
        Assert.Equal(1, current.Ordinal);
        Assert.Equal(CampaignSeasonState.Current, current.State);
        Assert.Equal(1967, current.Year);
        Assert.Null(current.Preview);      // plan-less careers have nothing to preview
        Assert.False(current.IsPrologue);  // and no prologue slot

        // Play the whole (two-round) season: still exactly the played season, now Completed.
        ApplyRound(session);
        ApplyRound(session);
        Assert.True(session.Summary.SeasonComplete);

        var done = session.CampaignTimeline();
        var completed = Assert.Single(done);
        Assert.Equal(CampaignSeasonState.Completed, completed.State);
        Assert.NotNull(completed.PlayerPosition);
    }

    // ---------- scaffolding (mirrors SmgpMultiSeasonDnqTests) ----------

    private string PacksRoot(string name) => Path.Combine(_root, name, "packs");

    private string CareerPath(string name) => Path.Combine(_root, name, "career.ams2career");

    private CareerEnvironment Environment(string name) => new()
    {
        ContentLibrary = FiveSeatLibrary(),
        LocateInstall = static () => null,
        DocumentsDirectory = Path.Combine(_root, name, "docs"),
        RulesDirectory = ViewModelTestData.RulesDirectory,
        PackSearchRoots = () => [PacksRoot(name), Path.Combine(AppContext.BaseDirectory, "packs")],
    };

    private CareerSessionService CreateSmgpCareer(string name)
    {
        string packDirectory = Path.Combine(PacksRoot(name), "smgp-dnq-ladder");
        TestPackBuilder.Write(DnqLadderPack(), packDirectory);
        return CareerSessionService.CreateCareer(new CareerCreationRequest
        {
            PackDirectory = packDirectory,
            CareerFilePath = CareerPath(name),
            CareerName = "Campaign Timeline Career",
            MasterSeed = Seed,
            PlayerLiveryName = SeatC,
            SmgpMode = true,
        }, Environment(name));
    }

    /// <summary>Creates the SMGP career, plays season 1 to completion, accepts the top offer and
    /// signs into the same-pack carryover season 2 (then this session is spent, reopen to land there).</summary>
    private void PlaySmgpSeasonOneAndSign(string name)
    {
        using var session = CreateSmgpCareer(name);

        while (!session.Summary.SeasonComplete)
            ApplyRound(session);

        var review = session.SeasonReview();
        Assert.NotNull(review);
        Assert.NotEmpty(review!.Offers);
        string teamId = review.Offers[0].TeamId;
        session.AcceptOffer(teamId);
        session.StartNextSeason(teamId);
    }

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

    /// <summary>Applies one round in resolved-grid order (the DNQ field determines who is present;
    /// the player is always cap-protected).</summary>
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
}
