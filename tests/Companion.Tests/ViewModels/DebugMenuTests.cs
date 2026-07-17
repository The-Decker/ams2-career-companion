using CommunityToolkit.Mvvm.ComponentModel;
using Companion.Core.Career;
using Companion.ViewModels.Debug;
using Companion.ViewModels.Hub;
using Companion.ViewModels.Services;
using Companion.ViewModels.Settings;
using Companion.ViewModels.Shell;

namespace Companion.Tests.ViewModels;

/// <summary>
/// The app-wide developer debug menu (dynasty-passport-roadmap.md Piece 2): the runtime gate, the
/// two-tier fold-safety split, and the shell overlay wiring. Every assertion here defends a line of
/// the build brief §7 acceptance criteria.
/// </summary>
public sealed class DebugMenuTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-debug-menu-").FullName;

    private string PacksRoot => Path.Combine(_root, "packs");
    private string DebugCareersDir => Path.Combine(_root, "debug-careers");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private CareerEnvironment Environment()
    {
        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "documents"),
            library: TestPackBuilder.Library());
        environment.PackSearchRoots = () => [PacksRoot];
        return environment;
    }

    private void WriteHistoricalPack(int year) =>
        TestPackBuilder.Write(HistoricalPack(year), Path.Combine(PacksRoot, year.ToString()));

    private static Companion.Core.Packs.SeasonPack HistoricalPack(int year)
    {
        var pack = TestPackBuilder.TwoRoundPack();
        return pack with
        {
            Manifest = pack.Manifest with { PackId = $"debug-{year}", Name = $"Season {year}" },
            Season = pack.Season with
            {
                Year = year,
                Rounds = [TestPackBuilder.Round(1, $"{year}-01-02"), TestPackBuilder.Round(2, $"{year}-05-07")],
            },
        };
    }

    private DebugMenuViewModel NewMenu(ICareerFactory factory, Func<ICareerSession?>? current = null) =>
        new(Environment(), factory, DebugCareersDir, current);

    private bool AnyCareerFileExists() =>
        Directory.Exists(_root) &&
        Directory.EnumerateFiles(_root, "*.ams2career", SearchOption.AllDirectories).Any();

    // ---------- Tier 2: previews are display-only and write NOTHING ----------

    [Fact]
    public void RacingPassport_IsReachableOnlyAsAPreviewThatWritesNoFile()
    {
        var menu = NewMenu(new FakeCareerFactory());
        ICareerSession? previewed = null;
        menu.PreviewRequested += (_, s) => previewed = s;

        menu.PreviewRacingPassportCommand.Execute(null);

        var preview = Assert.IsType<PreviewCareerSession>(previewed);
        Assert.Contains("Racing Passport", preview.Summary.CareerName, StringComparison.Ordinal);
        Assert.False(AnyCareerFileExists());
    }

    [Fact]
    public void RacingPassport_RealCreationThrowsAndNeverWritesAFile()
    {
        WriteHistoricalPack(1967);
        string careerPath = Path.Combine(_root, "passport.ams2career");
        var request = DebugCareerFactory.BuildRequest(
            Path.Combine(PacksRoot, "1967"), careerPath,
            CareerExperienceModes.RacingPassport, masterSeed: 1);

        Assert.Throws<InvalidOperationException>(() =>
            CareerSessionService.CreateCareer(request, Environment()));
        Assert.False(File.Exists(careerPath));
    }

    [Fact]
    public void PreviewLevel_RendersTheDriverDossierAtTheRequestedLevel()
    {
        var menu = NewMenu(new FakeCareerFactory());
        menu.PreviewLevel = 250;
        ICareerSession? previewed = null;
        menu.PreviewRequested += (_, s) => previewed = s;

        menu.PreviewLevelScreenCommand.Execute(null);

        Assert.True(previewed is not null, $"preview not produced; inspector said: {menu.InspectorText}");
        var preview = Assert.IsType<PreviewCareerSession>(previewed);
        Assert.Equal(250, preview.CharacterDossier()!.Level);
        Assert.False(AnyCareerFileExists());
    }

    [Theory]
    [InlineData("PreviewDeathHardcore")]
    [InlineData("PreviewSmgpCareerOver")]
    [InlineData("PreviewInjury")]
    [InlineData("PreviewFinale")]
    public void EveryTerminalPreview_ProducesAPreviewSessionWithoutTouchingDisk(string command)
    {
        var menu = NewMenu(new FakeCareerFactory());
        ICareerSession? previewed = null;
        menu.PreviewRequested += (_, s) => previewed = s;

        Execute(menu, command);

        Assert.IsType<PreviewCareerSession>(previewed);
        Assert.False(AnyCareerFileExists());
    }

    [Fact]
    public void DeathPreview_RoutesTheHomeScreenToTheDeathSurface()
    {
        var menu = NewMenu(new FakeCareerFactory());
        ICareerSession? previewed = null;
        menu.PreviewRequested += (_, s) => previewed = s;
        menu.PreviewDeathHardcoreCommand.Execute(null);

        // Hosting the preview in the real Home drives the real terminal routing: a deceased mortality
        // status makes the Home go terminal exactly as a live fatal round would.
        using var home = new HomeViewModel(previewed!);
        Assert.True(home.IsCareerTerminal);
        Assert.NotNull(home.DeathScreen);
    }

    [Fact]
    public void Promotion_IsShownAsALeafScreen()
    {
        var menu = NewMenu(new FakeCareerFactory());
        ObservableObject? screen = null;
        menu.ScreenRequested += (_, s) => screen = s;

        menu.PreviewPromotionCommand.Execute(null);

        Assert.IsType<PromotionViewModel>(screen);
        Assert.False(AnyCareerFileExists());
    }

    // ---------- Tier 1: real careers route through the factory ----------

    [Fact]
    public void OpenDynasty_RoutesThroughTheRealFactoryWithTheDynastyMode()
    {
        WriteHistoricalPack(1967);
        var factory = new FakeCareerFactory();
        var menu = NewMenu(factory);
        DebugCareerOpenedEventArgs? opened = null;
        menu.RealCareerRequested += (_, e) => opened = e;

        Assert.True(menu.HasPacks);
        menu.OpenDynastyCommand.Execute(null);

        Assert.NotNull(opened);
        Assert.Equal(CareerExperienceModes.GrandPrixDynasty, factory.LastRequest!.ExperienceMode);
        Assert.StartsWith(DebugCareersDir, opened!.CareerFilePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OpenSmgp_SetsTheSmgpGateOnTheRequest()
    {
        TestPackBuilder.Write(SmgpPack(), Path.Combine(PacksRoot, "smgp"));
        var factory = new FakeCareerFactory();
        var menu = NewMenu(factory);

        menu.OpenSmgpCommand.Execute(null);

        Assert.Equal(CareerExperienceModes.Smgp, factory.LastRequest!.ExperienceMode);
        Assert.True(factory.LastRequest.SmgpMode);
    }

    [Fact]
    public void Tier1_TakesARealSeatFromThePack_NotThePreviewLivery()
    {
        // A pack whose grid liveries do NOT include the in-memory preview livery — the debug player
        // must take a REAL seat from the pack, else its seat is a malformed synthetic own-entrant.
        var basePack = TestPackBuilder.TwoRoundPack();
        var pack = basePack with
        {
            Manifest = basePack.Manifest with { PackId = "real-seats", Name = "Real Seats" },
            Entries =
            [
                TestPackBuilder.Entry("team.brabham", "driver.brabham", "1", "Alpha Livery"),
                TestPackBuilder.Entry("team.brabham", "driver.hulme", "2", "Omega Livery"),
            ],
        };
        TestPackBuilder.Write(pack, Path.Combine(PacksRoot, "real"));
        var factory = new FakeCareerFactory();
        var menu = NewMenu(factory);

        menu.OpenPackCommand.Execute(menu.Packs.First());

        Assert.Equal("Omega Livery", factory.LastRequest!.PlayerLiveryName);
        Assert.NotEqual(DebugPreviewPack.PlayerLivery, factory.LastRequest.PlayerLiveryName);
    }

    [Fact]
    public void SitOutPreview_ContinueAdvancesWithoutThrowing()
    {
        var menu = NewMenu(new FakeCareerFactory());
        ICareerSession? previewed = null;
        menu.PreviewRequested += (_, s) => previewed = s;
        menu.PreviewInjuryCommand.Execute(null);

        using var home = new HomeViewModel(previewed!);
        Assert.True(home.IsSitOutStep);
        var sitOut = Assert.IsType<SitOutViewModel>(home.CurrentContent);

        sitOut.ContinueCommand.Execute(null); // previously threw NotSupportedException -> error dialog

        Assert.False(home.IsSitOutStep); // advanced off the sit-out cleanly
    }

    // ---------- inspect / dump ----------

    [Fact]
    public void RevealSmgpLore_FillsTheInspector()
    {
        var menu = NewMenu(new FakeCareerFactory());
        menu.RevealSmgpLoreCommand.Execute(null);
        Assert.NotEmpty(menu.InspectorText);
    }

    [Fact]
    public void DumpJournal_WithNoLiveCareerSaysSo()
    {
        var menu = NewMenu(new FakeCareerFactory(), current: static () => null);
        menu.DumpJournalCommand.Execute(null);
        Assert.Contains("No live career", menu.InspectorText, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenSmgpAtSeason_RoutesThroughTheRealFactoryWithTheSmgpGate()
    {
        TestPackBuilder.Write(SmgpPack(), Path.Combine(PacksRoot, "smgp"));
        var factory = new FakeCareerFactory();
        var menu = NewMenu(factory);
        DebugCareerOpenedEventArgs? opened = null;
        menu.RealCareerRequested += (_, e) => opened = e;

        menu.TargetSmgpSeason = 4;
        menu.OpenSmgpAtSeasonCommand.Execute(null);

        Assert.Equal(CareerExperienceModes.Smgp, factory.LastRequest!.ExperienceMode);
        Assert.True(factory.LastRequest.SmgpMode);
        Assert.Contains("debug-smgp-s4", opened!.CareerFilePath, StringComparison.Ordinal);
        // The fake session has no grid and no review, so the jump stops with an honest note —
        // the command must surface it instead of hanging or throwing.
        Assert.Contains("Stopped", menu.InspectorText, StringComparison.Ordinal);
    }

    [Fact]
    public void DumpEconomy_WithNoLiveCareerSaysSo()
    {
        var menu = NewMenu(new FakeCareerFactory(), current: static () => null);
        menu.DumpEconomyCommand.Execute(null);
        Assert.Contains("No live career", menu.InspectorText, StringComparison.Ordinal);
    }

    [Fact]
    public void DumpEconomy_WithANonEconomyCareerSaysSo()
    {
        var factory = new FakeCareerFactory();
        var menu = NewMenu(factory, current: () => factory.Session);
        menu.DumpEconomyCommand.Execute(null);
        Assert.Contains("not a Dynasty owner-economy career", menu.InspectorText, StringComparison.Ordinal);
    }

    [Fact]
    public void DumpEconomy_FormatsTheLiveDashboard()
    {
        var factory = new FakeCareerFactory();
        factory.Session.Economy = SampleDashboard();
        var menu = NewMenu(factory, current: () => factory.Session);

        menu.DumpEconomyCommand.Execute(null);

        Assert.Contains("Balance: 88,000", menu.InspectorText, StringComparison.Ordinal);
        Assert.Contains("Apex Lubricants", menu.InspectorText, StringComparison.Ordinal);
        Assert.Contains("Round 3", menu.InspectorText, StringComparison.Ordinal);
        Assert.Contains("Buy development", menu.InspectorText, StringComparison.Ordinal);
    }

    [Fact]
    public void PreviewLevel_AppliesTheSelectedDnaAndMasteryTrackPosition()
    {
        var menu = NewMenu(new FakeCareerFactory());
        var dna = menu.DnaOptions.FirstOrDefault(d => d.Id != "dna_circuit_specialist");
        Assert.NotNull(dna); // the shipped racing-dna-v2 catalog is loaded
        menu.PreviewLevel = 120;
        menu.SelectedDnaId = dna.Id;

        PreviewCareerSession? previewed = null;
        menu.PreviewRequested += (_, s) => previewed = (PreviewCareerSession)s;
        menu.PreviewCompletedSeasons = 0;
        menu.PreviewLevelScreenCommand.Execute(null);
        var dossier = previewed!.CharacterDossier()!;
        Assert.Equal(dna.Id, dossier.RacingDna?.Id);
        Assert.Equal(dna.Name, dossier.RacingDna?.Name);
        int unspentAtZeroSeasons = dossier.CpUnspent;

        menu.PreviewCompletedSeasons = 16;
        menu.PreviewLevelScreenCommand.Execute(null);
        int unspentAtSixteenSeasons = previewed!.CharacterDossier()!.CpUnspent;

        // The 499-SP pool gate credits mastery-track seasons: more seasons, more spendable points.
        Assert.True(unspentAtSixteenSeasons > unspentAtZeroSeasons,
            $"expected the mastery track to gate SP ({unspentAtZeroSeasons} at 0 seasons vs " +
            $"{unspentAtSixteenSeasons} at 16)");
        Assert.False(AnyCareerFileExists());
    }

    private static Companion.ViewModels.Services.DynastyEconomyDashboard SampleDashboard() => new()
    {
        Balance = "88,000",
        InDeficit = false,
        DeficitRounds = 0,
        GraceRounds = 2,
        HardFloor = "-50,000",
        Bankrupt = false,
        DevelopmentLevel = 2,
        DevelopmentMaxLevel = 5,
        NextDevelopmentCost = "40,000",
        DevelopmentAtCap = false,
        StaffTier = 1,
        StaffOptions =
        [
            new Companion.ViewModels.Services.DynastyStaffOptionModel
                { Tier = 0, UpkeepPerSeason = "0", IsCurrent = false },
            new Companion.ViewModels.Services.DynastyStaffOptionModel
                { Tier = 1, UpkeepPerSeason = "12,000", IsCurrent = true },
        ],
        SecondSeat = Companion.Core.Dynasty.SecondSeatDeal.Retained,
        SecondSeatSalaryPerSeason = "30,000",
        PayDriverBackingPerSeason = "55,000",
        ActiveSponsors =
        [
            new Companion.ViewModels.Services.DynastySponsorContractModel
            {
                Id = "apex", Name = "Apex Lubricants", TierSlot = "major", SeasonsRemaining = 2,
                PerRace = "3,000", PerSeason = "20,000",
            },
        ],
        SponsorBoard = [],
        Statement =
        [
            new Companion.ViewModels.Services.DynastyLedgerLineModel
                { Label = "Round 3", Round = 3, Net = "+9,500", BalanceAfter = "88,000", IsDeficit = false },
        ],
        PendingDecisions =
        [
            new Companion.ViewModels.Services.DynastyPendingDecisionModel
                { Description = "Buy development (stage 3)", Amount = "-40,000", Seq = 41 },
        ],
        NextRound = 4,
    };

    [Fact]
    public void Close_RaisesCloseRequested()
    {
        var menu = NewMenu(new FakeCareerFactory());
        bool closed = false;
        menu.CloseRequested += (_, _) => closed = true;
        menu.CloseCommand.Execute(null);
        Assert.True(closed);
    }

    // ---------- shell gating + overlay ----------

    [Fact]
    public void WithTheFlagOff_TheDebugKeybindIsANoOp()
    {
        var (shell, _, settings) = CreateShell();
        Assert.False(settings.Current.DeveloperMode);

        shell.ToggleDebugCommand.Execute(null);

        Assert.Same(shell.Start, shell.Current);      // nothing rendered
        Assert.IsNotType<DebugMenuViewModel>(shell.Current);
    }

    [Fact]
    public void UnlockingDeveloperMode_OpensTheOverlayAndPersistsTheFlag()
    {
        var (shell, _, settings) = CreateShell();

        shell.ToggleDeveloperModeCommand.Execute(null);

        Assert.True(shell.DeveloperMode);
        Assert.True(settings.Current.DeveloperMode);   // persisted through the settings seam
        Assert.IsType<DebugMenuViewModel>(shell.Current);
    }

    [Fact]
    public void OnceUnlocked_TheKeybindTogglesAndEscapeClosesBackToWhereYouWere()
    {
        var (shell, _, _) = CreateShell();
        shell.ToggleDeveloperModeCommand.Execute(null);       // unlock + open
        Assert.IsType<DebugMenuViewModel>(shell.Current);

        shell.ToggleDebugCommand.Execute(null);               // close
        Assert.Same(shell.Start, shell.Current);

        shell.ToggleDebugCommand.Execute(null);               // open
        Assert.IsType<DebugMenuViewModel>(shell.Current);

        Assert.True(shell.TryEscapeBack());                   // Esc closes
        Assert.Same(shell.Start, shell.Current);
    }

    [Fact]
    public void Tier1OpenPack_NavigatesToARealHub()
    {
        WriteHistoricalPack(1967);
        var (shell, _, _) = CreateShell();
        shell.ToggleDeveloperModeCommand.Execute(null);
        var menu = Assert.IsType<DebugMenuViewModel>(shell.Current);

        menu.OpenPackCommand.Execute(menu.Packs.First(p => !p.IsSmgp));

        Assert.IsType<HubViewModel>(shell.Current);
    }

    [Fact]
    public void ReopeningDebugFromALeaf_RestoresTheOriginalScreenNotTheLeaf()
    {
        var (shell, _, _) = CreateShell();
        shell.ToggleDeveloperModeCommand.Execute(null);   // unlock + open (over Start)
        var menu = Assert.IsType<DebugMenuViewModel>(shell.Current);

        menu.PreviewPromotionCommand.Execute(null);        // navigate to a leaf (promotion screen)
        Assert.IsType<PromotionViewModel>(shell.Current);

        shell.ToggleDebugCommand.Execute(null);            // re-open debug from the leaf
        Assert.IsType<DebugMenuViewModel>(shell.Current);

        shell.ToggleDebugCommand.Execute(null);            // close
        Assert.Same(shell.Start, shell.Current);           // back to Start, NOT the transient leaf
    }

    [Fact]
    public void Tier2Preview_NavigatesToAHubAndWritesNoCareerFile()
    {
        var (shell, _, _) = CreateShell();
        shell.ToggleDeveloperModeCommand.Execute(null);
        var menu = Assert.IsType<DebugMenuViewModel>(shell.Current);

        menu.PreviewDeathHardcoreCommand.Execute(null);

        var hub = Assert.IsType<HubViewModel>(shell.Current);
        Assert.IsType<PreviewCareerSession>(hub.Home.Session);
        Assert.False(AnyCareerFileExists());
    }

    // ---------- helpers ----------

    private (ShellViewModel Shell, FakeCareerFactory Factory, ISettingsService Settings) CreateShell()
    {
        var factory = new FakeCareerFactory();
        var settings = new SettingsService(new InMemorySettingsStore());
        var environment = ViewModelTestData.Environment(
            documentsDirectory: _root,
            library: TestPackBuilder.Library());
        environment.PackSearchRoots = () => [PacksRoot];
        var shell = new ShellViewModel(environment, factory, new FakeRecentStore(), settings: settings);
        return (shell, factory, settings);
    }

    private static void Execute(DebugMenuViewModel menu, string command) =>
        (command switch
        {
            "PreviewDeathHardcore" => menu.PreviewDeathHardcoreCommand,
            "PreviewSmgpCareerOver" => menu.PreviewSmgpCareerOverCommand,
            "PreviewInjury" => menu.PreviewInjuryCommand,
            "PreviewFinale" => menu.PreviewFinaleCommand,
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        }).Execute(null);

    private static Companion.Core.Packs.SeasonPack SmgpPack()
    {
        var pack = TestPackBuilder.TwoRoundPack();
        return pack with
        {
            Manifest = pack.Manifest with
            {
                PackId = "smgp-1",
                Name = "SMGP",
                CareerStyle = Companion.Core.Smgp.SmgpRules.CareerStyle,
            },
            Season = pack.Season with { Year = 1990 },
        };
    }

    private sealed class FakeRecentStore : IRecentCareersStore
    {
        private readonly List<RecentCareer> _entries = [];
        public IReadOnlyList<RecentCareer> Load() => _entries.ToList();
        public void Touch(string path, string careerName, int seasonYear = 0, string? careerStyle = null) =>
            _entries.Insert(0, new RecentCareer
            {
                Path = path,
                CareerName = careerName,
                LastOpenedUtc = DateTimeOffset.UnixEpoch,
            });
        public void Remove(string path) => _entries.RemoveAll(e => e.Path == path);
    }
}
