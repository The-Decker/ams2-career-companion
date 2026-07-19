using Companion.Core.Career;
using Companion.Core.Newsroom;
using Companion.Core.Packs;
using Companion.Core.Smgp;
using Companion.Tests.ViewModels;
using Companion.ViewModels.Services;

namespace Companion.Tests.Newsroom;

/// <summary>Mode-narrative isolation (mode-separation finalization mission): the three modes
/// tell three stories, and none leaks into another. These tests prove at the event-spine level
/// that SMGP flavour never fires in a historical or pure-racing Passport career, that Dynasty
/// economy events never fire in an SMGP career, and that the SEGA fiction is provenance-badged
/// wherever it appears. Scaffolding mirrors <c>NewsroomEventsIntegrationTests</c>' real ladder:
/// real creation, real folds, real event spine.</summary>
public sealed class ModeNarrativeIsolationTests : IDisposable
{
    private const string PlayerSeat = "Stock Livery #3";
    private const long Seed = 20260716;

    private static readonly NewsEventKind[] SmgpOnlyKinds =
        [NewsEventKind.SmgpCanonDiverged, NewsEventKind.SmgpCanonHeld];

    private static readonly NewsEventKind[] EconomyOnlyKinds =
    [
        NewsEventKind.SponsorSigned, NewsEventKind.MajorRepairBill, NewsEventKind.NearBankruptcy,
        NewsEventKind.FinancialWindfall, NewsEventKind.BankruptcyDeclared,
        NewsEventKind.DevelopmentMilestone,
    ];

    private readonly string _root = Directory.CreateTempSubdirectory("companion-mode-iso-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    [Fact]
    public void HistoricalCareer_NeverEmitsSmgpFlavourOrDynastyEconomy()
    {
        using var session = NewCareer(CareerPath("historical"), smgp: false, passport: false);
        ApplyRound(session, playerWins: true);
        ApplyRound(session, playerWins: false);

        var events = session.NewsroomEvents();

        Assert.DoesNotContain(events, e => SmgpOnlyKinds.Contains(e.Kind));
        Assert.DoesNotContain(events, e => EconomyOnlyKinds.Contains(e.Kind));
        Assert.DoesNotContain(events, e =>
            NewsroomCategories.ProvenanceFor(e.Kind) == ContentProvenance.SmgpFiction);
    }

    [Fact]
    public void SmgpCareer_NeverEmitsDynastyEconomy_AndItsFictionStaysBadged()
    {
        using var session = NewCareer(CareerPath("smgp"), smgp: true, passport: false);
        ApplyRound(session, playerWins: true);

        var events = session.NewsroomEvents();

        Assert.DoesNotContain(events, e => EconomyOnlyKinds.Contains(e.Kind));
        // Wherever the SEGA canon surfaces, it is provenance-badged as fiction by construction,
        // never as verified history.
        Assert.DoesNotContain(events, e =>
            NewsroomCategories.ProvenanceFor(e.Kind) == ContentProvenance.VerifiedHistorical &&
            SmgpOnlyKinds.Contains(e.Kind));
    }

    [Fact]
    public void PassportCareer_NeverEmitsSmgpFlavourOrDynastyEconomy()
    {
        using var session = NewCareer(CareerPath("passport"), smgp: false, passport: true);
        ApplyRound(session, playerWins: true);
        ApplyRound(session, playerWins: false);

        var events = session.NewsroomEvents();

        Assert.DoesNotContain(events, e => SmgpOnlyKinds.Contains(e.Kind));
        Assert.DoesNotContain(events, e => EconomyOnlyKinds.Contains(e.Kind));
        Assert.DoesNotContain(events, e =>
            NewsroomCategories.ProvenanceFor(e.Kind) == ContentProvenance.SmgpFiction);
    }

    [Fact]
    public void TheEraKey_IsTheModeKey_ForPoolVoiceAndDesk()
    {
        var corpus = NewsroomCorpus.LoadDirectory(
            Path.Combine(ViewModelTestData.RulesDirectory, "newsroom"));
        var desks = NewsDesks.Load(Path.Combine(ViewModelTestData.RulesDirectory, "newsroom"));

        // A plain season year resolves by decade, never into the fictional universe.
        string historical = corpus.ResolveEra(1967);
        Assert.NotEqual("smgp", historical);

        // The SMGP identity override selects the fictional universe's own desk for the same year.
        var podiumEvent = new NewsEvent
        {
            Kind = NewsEventKind.PodiumFinish,
            SeasonOrdinal = 1,
            SeasonYear = 1990,
            Round = 3,
            SubjectId = "driver.p",
            SubjectName = "Pat Player",
            SubjectTeamId = "team.test",
            SubjectTeamName = "Test Team",
            VenueName = "Test Ring",
            Facts = new NewsEventFacts { PlayerFinish = 2, WinnerName = "Rival Example", WinnerTeamName = "Rival Team" },
        };
        var smgpArticle = NewsroomComposer.Compose(
            podiumEvent, corpus, desks,
            new NewsroomIdentity { PlayerName = "Pat Player", PlayerTeamName = "Test Team", PreferredEra = "smgp" },
            unchecked((ulong)Seed));
        var historicalArticle = NewsroomComposer.Compose(
            podiumEvent, corpus, desks,
            new NewsroomIdentity { PlayerName = "Pat Player", PlayerTeamName = "Test Team" },
            unchecked((ulong)Seed));

        Assert.NotNull(smgpArticle);
        Assert.NotNull(historicalArticle);
        Assert.NotEqual(historicalArticle!.Body, smgpArticle!.Body); // different universe voices
    }

    // ---------- scaffolding (mirrors NewsroomEventsIntegrationTests' real-machinery ladder) ----------

    private string PacksRoot => Path.Combine(_root, "packs");
    private string CareerPath(string name) => Path.Combine(_root, name + ".ams2career");

    private CareerEnvironment Environment() => new()
    {
        ContentLibrary = FiveSeatLibrary(),
        LocateInstall = static () => null,
        DocumentsDirectory = Path.Combine(_root, "docs"),
        RulesDirectory = ViewModelTestData.RulesDirectory,
        PackSearchRoots = () => [PacksRoot],
    };

    private CareerSessionService NewCareer(string careerPath, bool smgp, bool passport)
    {
        string packDirectory = Path.Combine(PacksRoot, smgp ? "smgp" : "historical");
        TestPackBuilder.Write(ThePack(smgp), packDirectory);
        return CareerSessionService.CreateCareer(new CareerCreationRequest
        {
            PackDirectory = packDirectory,
            CareerFilePath = careerPath,
            CareerName = "Isolation Career",
            MasterSeed = Seed,
            PlayerLiveryName = PlayerSeat,
            SmgpMode = smgp,
            ExperienceMode = passport ? CareerExperienceModes.RacingPassport : null,
        }, Environment());
    }

    private static SeasonPack ThePack(bool smgp)
    {
        var basePack = TestPackBuilder.TwoRoundPack();
        return basePack with
        {
            Manifest = basePack.Manifest with
            {
                CareerStyle = smgp ? SmgpRules.CareerStyle : null,
            },
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
                TestPackBuilder.Driver("driver.e"),
            ],
            Entries =
            [
                TestPackBuilder.Entry("team.a", "driver.a", "1", "Stock Livery #1"),
                TestPackBuilder.Entry("team.b", "driver.b", "2", "Stock Livery #2"),
                TestPackBuilder.Entry("team.c", "driver.c", "3", PlayerSeat),
                TestPackBuilder.Entry("team.d", "driver.d", "4", "Stock Livery #4"),
                TestPackBuilder.Entry("team.e", "driver.e", "5", "Stock Livery #5"),
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
                    StockLib1563 = ["Stock Livery #1", "Stock Livery #2", PlayerSeat, "Stock Livery #4", "Stock Livery #5"],
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
