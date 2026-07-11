using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.ViewModels.Confirm;
using Companion.ViewModels.Hub;
using Companion.ViewModels.ResultEntry;
using Companion.ViewModels.Services;
using Companion.ViewModels.Settings;
using Companion.ViewModels.Shell;
using Companion.ViewModels.Standings;
using Companion.ViewModels.Wizard;

namespace Companion.Tests.ViewModels;

/// <summary>
/// Settings consumers (ux-round contract section 3, "wire consumers"): the wizard reads the
/// prefer-installed-baseline default and the custom pack folders, the result screen prefills
/// the default difficulty, confirm respects minimal-narrative, home honors auto-open-briefing,
/// and Esc walks back non-destructively at shell level.
/// </summary>
public sealed class SettingsConsumersTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-consumers-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static SettingsService Service(AppSettings? initial = null) =>
        new(new InMemorySettingsStore(initial));

    // ---------- wizard: prefer-installed-baseline default (NAMeS-first) ----------

    private const string InstalledXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <custom_ai_drivers>
        	<driver livery_name="Stock Livery #1">
        		<name>Jack B. Community</name>
        		<country>AUS</country>
                <race_skill>0.93</race_skill>
        	</driver>
        </custom_ai_drivers>
        """;

    private NewCareerWizardViewModel WizardAtConfirm(ISettingsService settings)
    {
        string packsRoot = Path.Combine(_root, "packs");
        string installDirectory = Path.Combine(_root, "install");
        string installedAiPath = Path.Combine(
            installDirectory, "UserData", "CustomAIDrivers", TestPackBuilder.VintageClass + ".xml");

        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), Path.Combine(packsRoot, "pack"));
        Directory.CreateDirectory(Path.GetDirectoryName(installedAiPath)!);
        File.WriteAllText(installedAiPath, InstalledXml);

        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs"),
            installDirectory: installDirectory,
            library: TestPackBuilder.Library());
        var wizard = new NewCareerWizardViewModel(
            environment,
            new FakeCareerFactory(),
            packSearchRoots: [packsRoot],
            careersDirectory: Path.Combine(_root, "careers"),
            seedSource: new Random(7),
            settings: settings);

        wizard.SelectedPack = Assert.Single(wizard.Packs);
        wizard.NextCommand.Execute(null);
        if (wizard.HasWarnings)
            wizard.ProceedAnyway = true;
        wizard.NextCommand.Execute(null);
        wizard.SelectedSeat = wizard.Seats.First(s => s.LiveryName == TestPackBuilder.StockLivery2);
        wizard.NextCommand.Execute(null);          // -> Character (rules loaded) or Grid (no rules)
        if (wizard.Step == WizardStep.Character)
            wizard.NextCommand.Execute(null);      // -> Grid (archetype preset is valid)
        wizard.NextCommand.Execute(null);          // -> Confirm
        Assert.Equal(WizardStep.Confirm, wizard.Step);
        return wizard;
    }

    [Fact]
    public void Wizard_PreferInstalledBaselineOn_DefaultsTheCheckboxOn()
    {
        var wizard = WizardAtConfirm(Service()); // default: prefer installed

        Assert.True(wizard.BaselineImportAvailable);
        Assert.True(wizard.UseInstalledAiBaseline);
    }

    [Fact]
    public void Wizard_PreferInstalledBaselineOff_DefaultsTheCheckboxOff_ButStillOffersIt()
    {
        var wizard = WizardAtConfirm(Service(new AppSettings { PreferInstalledBaseline = false }));

        Assert.True(wizard.BaselineImportAvailable); // the option is still there...
        Assert.False(wizard.UseInstalledAiBaseline); // ...just not the default
    }

    [Fact]
    public void Wizard_SearchesTheSettingsPackFolders()
    {
        string customRoot = Path.Combine(_root, "custom-packs");
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), Path.Combine(customRoot, "my-pack"));

        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs2"),
            library: TestPackBuilder.Library());
        var wizard = new NewCareerWizardViewModel(
            environment,
            new FakeCareerFactory(),
            settings: Service(new AppSettings { PackFolders = [customRoot] }));

        Assert.Contains(wizard.Packs, p =>
            p.Directory.StartsWith(customRoot, StringComparison.OrdinalIgnoreCase));
    }

    // ---------- home: default difficulty + auto-open briefing ----------

    [Fact]
    public void ResultScreen_PrefillsTheSettingsDefaultDifficulty_WhenNoRecommendationExists()
    {
        var settings = Service(new AppSettings { DefaultDifficulty = 108.0 });
        using var home = new HomeViewModel(new GridSession(), settings: settings);

        home.EnterResultCommand.Execute(null);
        var entry = Assert.IsType<ResultEntryViewModel>(home.CurrentContent);

        Assert.Equal(108.0, entry.SliderUsed);
    }

    [Fact]
    public void ResultScreen_RecommendationStillBeatsTheDefault()
    {
        var settings = Service(new AppSettings { DefaultDifficulty = 108.0 });
        var session = new GridSession { SliderRecommendation = 97 };
        using var home = new HomeViewModel(session, settings: settings);

        home.EnterResultCommand.Execute(null);
        var entry = Assert.IsType<ResultEntryViewModel>(home.CurrentContent);

        Assert.Equal(97.0, entry.SliderUsed);
    }

    [Fact]
    public void Home_AutoOpenBriefingOff_LandsOnStandings()
    {
        var settings = Service(new AppSettings { AutoOpenBriefing = false });
        using var home = new HomeViewModel(new GridSession(), settings: settings);

        Assert.True(home.IsStandingsState);
        Assert.False(home.IsBriefingState);
    }

    [Fact]
    public void Home_AutoOpenBriefingOn_LandsOnTheBriefing()
    {
        using var home = new HomeViewModel(new GridSession(), settings: Service());
        Assert.True(home.IsBriefingState);
    }

    // ---------- confirm: minimal narrative ----------

    private static ConfirmModel Model(string headline) => new()
    {
        RoundPoints = [],
        Movements = [("d1", 5, 2)],
        Headline = headline,
    };

    [Fact]
    public void MinimalNarrative_HidesFlavorHeadlines()
    {
        var vm = new ConfirmViewModel(Model("Local hero shines at Monza"),
            () => { }, () => { }, minimalNarrative: true);
        Assert.False(vm.ShowHeadline);
    }

    [Fact]
    public void MinimalNarrative_KeepsChampionshipCriticalHeadlines()
    {
        var vm = new ConfirmViewModel(Model("CHAMPION! The title is decided"),
            () => { }, () => { }, minimalNarrative: true);
        Assert.True(vm.ShowHeadline);
    }

    [Fact]
    public void FullNarrative_AlwaysShowsTheHeadline()
    {
        var vm = new ConfirmViewModel(Model("Local hero shines at Monza"),
            () => { }, () => { });
        Assert.True(vm.ShowHeadline);
    }

    [Fact]
    public void MovementRows_CarryPlainWordsTooltips()
    {
        var vm = new ConfirmViewModel(Model("h"), () => { }, () => { });
        var row = Assert.Single(vm.Movements);
        Assert.Equal("d1 — P5 → P2: gained 3 places", row.Tooltip);

        Assert.Equal("X — P2 → P5: lost 3 places", ConfirmViewModel.MovementTooltip("X", 2, 5));
        Assert.Equal("X — first classification: P4", ConfirmViewModel.MovementTooltip("X", null, 4));
        Assert.Equal("X — holds P3", ConfirmViewModel.MovementTooltip("X", 3, 3));
    }

    // ---------- Esc = back (shell level, non-destructive only) ----------

    private ShellViewModel Shell(out SettingsService settings)
    {
        settings = Service();
        var environment = ViewModelTestData.Environment(Path.Combine(_root, "docs3"));
        return new ShellViewModel(
            environment, new FakeCareerFactory(), new FakeRecentStore(), settings: settings);
    }

    [Fact]
    public void ToggleSettings_OpensAndFocusesTheSettingsScreen_EscGoesBack()
    {
        var shell = Shell(out _);
        Assert.Same(shell.Start, shell.Current);

        shell.ToggleSettingsCommand.Execute(null);
        Assert.IsType<SettingsViewModel>(shell.Current);

        Assert.True(shell.TryEscapeBack());
        Assert.Same(shell.Start, shell.Current);
    }

    [Fact]
    public void GearButton_TogglesSettingsClosedAgain()
    {
        var shell = Shell(out _);
        shell.ToggleSettingsCommand.Execute(null);
        shell.ToggleSettingsCommand.Execute(null);
        Assert.Same(shell.Start, shell.Current);
    }

    [Fact]
    public void SettingsDoneButton_ReturnsToThePreviousScreen()
    {
        var shell = Shell(out _);
        shell.Start.NewCareerCommand.Execute(null); // wizard open
        var wizard = shell.Current;

        shell.ToggleSettingsCommand.Execute(null);
        var settingsVm = Assert.IsType<SettingsViewModel>(shell.Current);
        settingsVm.CloseCommand.Execute(null);

        Assert.Same(wizard, shell.Current); // back to the wizard, not Start
    }

    [Fact]
    public void Esc_InTheWizard_StepsBack_ThenLeavesToStart()
    {
        var shell = Shell(out _);
        shell.Start.NewCareerCommand.Execute(null);
        Assert.IsType<NewCareerWizardViewModel>(shell.Current);

        // First step: nothing entered yet — Esc leaves to Start (non-destructive).
        Assert.True(shell.TryEscapeBack());
        Assert.Same(shell.Start, shell.Current);
    }

    [Fact]
    public void Esc_OnTheStartScreen_MeansNothing()
    {
        var shell = Shell(out _);
        Assert.False(shell.TryEscapeBack());
    }

    [Fact]
    public void Esc_OnStandings_GoesBackToTheRound()
    {
        using var home = new HomeViewModel(new GridSession(), settings: Service());
        home.ShowStandingsCommand.Execute(null);
        Assert.True(home.IsStandingsState);

        Assert.True(home.TryEscapeBack());
        Assert.True(home.IsBriefingState);
    }

    [Fact]
    public void Esc_OnConfirm_GoesBackToTheResultEntry_KeepingTheDraft()
    {
        using var home = new HomeViewModel(new GridSession(), settings: Service());
        home.EnterResultCommand.Execute(null);
        var entry = (ResultEntryViewModel)home.CurrentContent!;
        foreach (string number in new[] { "1", "2" })
        {
            entry.Input = number;
            entry.SubmitCommand.Execute(null);
        }
        home.ConfirmResultCommand.Execute(null);
        Assert.True(home.IsConfirmState);

        Assert.True(home.TryEscapeBack());
        Assert.Same(entry, home.CurrentContent);
    }

    [Fact]
    public void Esc_DuringResultEntry_IsLeftToTheGrammar()
    {
        using var home = new HomeViewModel(new GridSession(), settings: Service());
        home.EnterResultCommand.Execute(null);
        Assert.False(home.TryEscapeBack()); // the shell must not steal Esc mid-entry
    }

    // ---------- immersion: era theming flows to the hub, news detail gates the body ----------

    [Fact]
    public void EraThemingEnabled_FlowsFromSettings_ToTheHub()
    {
        var on = Service(new AppSettings { EraThemingEnabled = true });
        using var hubOn = new HubViewModel(new NewsSession(), settings: on);
        Assert.True(hubOn.EraThemingEnabled);

        var off = Service(new AppSettings { EraThemingEnabled = false });
        using var hubOff = new HubViewModel(new NewsSession(), settings: off);
        Assert.False(hubOff.EraThemingEnabled);
    }

    [Fact]
    public void EraThemingEnabled_UpdatesLive_WhenTheSettingChanges()
    {
        var settings = Service(new AppSettings { EraThemingEnabled = true });
        using var hub = new HubViewModel(new NewsSession(), settings: settings);

        bool raised = false;
        hub.PropertyChanged += (_, e) =>
            raised |= e.PropertyName == nameof(HubViewModel.EraThemingEnabled);

        settings.Update(s => s with { EraThemingEnabled = false });

        Assert.False(hub.EraThemingEnabled); // reads live off the service...
        Assert.True(raised);                 // ...and notified so the era badge re-binds
    }

    [Fact]
    public void NewsDetail_Articles_ShowsTheExpandedBody()
    {
        var settings = Service(new AppSettings { NewsDetail = NewsDetailLevel.Articles });
        using var hub = new HubViewModel(new NewsSession(), settings: settings);

        var item = Assert.Single(hub.News.Items);
        Assert.True(item.HasBody);
        Assert.Equal("The full period article body.", item.Body);

        item.ToggleExpandedCommand.Execute(null);
        Assert.True(item.IsExpanded);
    }

    [Theory]
    [InlineData(NewsDetailLevel.HeadlinesOnly)]
    [InlineData(NewsDetailLevel.Minimal)]
    public void NewsDetail_HeadlineModes_HideTheBody(NewsDetailLevel level)
    {
        var settings = Service(new AppSettings { NewsDetail = level });
        using var hub = new HubViewModel(new NewsSession(), settings: settings);

        Assert.True(hub.News.HeadlinesOnly);
        var item = Assert.Single(hub.News.Items);

        Assert.False(item.HasBody);
        Assert.Equal("", item.Body);            // no expanded article body...
        Assert.Equal("Big race result", item.Headline); // ...just the headline

        item.ToggleExpandedCommand.Execute(null);
        Assert.False(item.IsExpanded);          // and the item cannot be expanded
    }

    // ---------- fakes ----------

    /// <summary>A session that emits one news dispatch with a body — for the NewsDetail gating.</summary>
    private sealed class NewsSession : ICareerSession, IDisposable
    {
        public SeasonPack Pack { get; } = TestPackBuilder.TwoRoundPack();

        public CareerSummary Summary => new()
        {
            CareerName = "News Career",
            SeasonYear = 1967,
            SeriesName = "Test Championship",
            CurrentRound = 1,
            RoundCount = Pack.Season.Rounds.Count,
            PlayerDriverId = "driver.hulme",
            PlayerLiveryName = TestPackBuilder.StockLivery2,
            SeasonComplete = false,
        };

        public BriefingModel? CurrentBriefing() => new()
        {
            Round = Pack.Season.Rounds[0],
            VenueDisplayName = "Kyalami",
            IsPlaceholder = false,
            Settings = [new CopyableSetting("Track", "Kyalami Historic")],
        };

        public StageOutcome StageCurrentGrid() =>
            new() { Success = true, WrittenPath = "X.xml", Messages = [] };

        public IReadOnlyList<GridSeat> CurrentGrid() => [];

        public ConfirmModel Preview(ResultDraft draft) =>
            new() { RoundPoints = [], Movements = [], Headline = "h" };

        public void Apply(ResultDraft draft)
        {
        }

        public StandingsSnapshot? CurrentStandings() => null;

        public IReadOnlyList<StandingsSnapshot> AllSnapshots() => [];

        public IReadOnlyList<NewsDispatch> ReadFeed() =>
        [
            new NewsDispatch
            {
                Headline = "Big race result",
                SeasonYear = 1967,
                Round = 1,
                Kind = "race",
                Body = "The full period article body.",
            },
        ];

        public int? CurrentSliderRecommendation() => null;

        public SeasonReviewModel? SeasonReview() => null;

        public void AcceptOffer(string teamId)
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class GridSession : ICareerSession
    {
        public int AppliedRounds;

        public SeasonPack Pack { get; } = TestPackBuilder.TwoRoundPack();

        public int? SliderRecommendation { get; set; }

        public CareerSummary Summary => new()
        {
            CareerName = "Fake Career",
            SeasonYear = 1967,
            SeriesName = "Test Championship",
            CurrentRound = Math.Min(AppliedRounds + 1, Pack.Season.Rounds.Count),
            RoundCount = Pack.Season.Rounds.Count,
            PlayerDriverId = "driver.hulme",
            PlayerLiveryName = TestPackBuilder.StockLivery2,
            SeasonComplete = AppliedRounds >= Pack.Season.Rounds.Count,
        };

        public BriefingModel? CurrentBriefing() => new()
        {
            Round = Pack.Season.Rounds[0],
            VenueDisplayName = "Kyalami",
            IsPlaceholder = false,
            Settings = [new CopyableSetting("Track", "Kyalami Historic")],
        };

        public StageOutcome StageCurrentGrid() =>
            new() { Success = true, WrittenPath = "X.xml", Messages = [] };

        public IReadOnlyList<GridSeat> CurrentGrid() =>
        [
            Seat("driver.brabham", "Jack Brabham", "1"),
            Seat("driver.hulme", "Denny Hulme", "2"),
        ];

        public ConfirmModel Preview(ResultDraft draft) => new()
        {
            RoundPoints = [],
            Movements = [],
            Headline = "fake headline",
        };

        public void Apply(ResultDraft draft) => AppliedRounds++;

        public StandingsSnapshot? CurrentStandings() => null;

        public IReadOnlyList<StandingsSnapshot> AllSnapshots() => [];

        public int? CurrentSliderRecommendation() => SliderRecommendation;

        public SeasonReviewModel? SeasonReview() => null;

        public void AcceptOffer(string teamId)
        {
        }

        private static GridSeat Seat(string id, string name, string number) => new()
        {
            DriverId = id,
            DriverName = name,
            Number = number,
            TeamId = "team.brabham",
            TeamName = "Brabham-Repco",
            Ams2LiveryName = $"Livery #{number}",
            Ratings = TestPackBuilder.Driver(id).Ratings,
            Reliability = 0.9,
            WeightScalar = 1.0,
            PowerScalar = 1.0,
            DragScalar = 1.0,
            IsPlayer = id == "driver.hulme",
        };
    }

    private sealed class FakeRecentStore : IRecentCareersStore
    {
        public IReadOnlyList<RecentCareer> Load() => [];

        public void Touch(string path, string careerName, int seasonYear = 0, string? careerStyle = null)
        {
        }

        public void Remove(string path)
        {
        }
    }
}
