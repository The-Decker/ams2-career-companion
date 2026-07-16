using Companion.Core.Career;
using Companion.Core.Smgp;
using Companion.ViewModels.Services;
using Companion.ViewModels.Shell;
using Companion.ViewModels.Start;
using Companion.ViewModels.Wizard;

namespace Companion.Tests.ViewModels;

public sealed class CareerModeMenuTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "ams2-career-mode-menu-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void StartPublishesExactlyThreeStableModeCardsWithHonestAvailability()
    {
        var start = new StartViewModel(new EmptyRecentStore());

        Assert.Collection(
            start.CareerModes,
            dynasty =>
            {
                Assert.Equal(CareerExperienceModes.GrandPrixDynasty, dynasty.Id);
                Assert.Equal("Grand Prix Dynasty", dynasty.DisplayName);
                Assert.True(dynasty.IsAvailable);
                Assert.Contains("FIRST PASS", dynasty.AvailabilityLabel, StringComparison.Ordinal);
            },
            smgp =>
            {
                Assert.Equal(CareerExperienceModes.Smgp, smgp.Id);
                Assert.Equal("Super Monaco GP", smgp.DisplayName);
                Assert.True(smgp.IsAvailable);
                Assert.Contains("PLAYABLE", smgp.AvailabilityLabel, StringComparison.Ordinal);
            },
            passport =>
            {
                Assert.Equal(CareerExperienceModes.RacingPassport, passport.Id);
                Assert.Equal("Racing Passport", passport.DisplayName);
                Assert.False(passport.IsAvailable);
                Assert.Contains("COMING", passport.AvailabilityLabel, StringComparison.Ordinal);
            });

        Assert.All(start.CareerModes, mode =>
        {
            Assert.False(string.IsNullOrWhiteSpace(mode.Tagline));
            Assert.False(string.IsNullOrWhiteSpace(mode.Description));
            Assert.False(string.IsNullOrWhiteSpace(mode.PersistenceSummary));
            Assert.False(string.IsNullOrWhiteSpace(mode.AvailabilityLabel));
        });
    }

    [Fact]
    public void ModeCommandRaisesStableIdsForPlayableCardsAndRefusesPassport()
    {
        var start = new StartViewModel(new EmptyRecentStore());
        var requested = new List<string>();
        start.CareerModeRequested += (_, request) => requested.Add(request.ExperienceMode);

        var dynasty = start.CareerModes.Single(mode =>
            mode.Id == CareerExperienceModes.GrandPrixDynasty);
        var smgp = start.CareerModes.Single(mode => mode.Id == CareerExperienceModes.Smgp);
        var passport = start.CareerModes.Single(mode =>
            mode.Id == CareerExperienceModes.RacingPassport);

        Assert.True(start.StartCareerModeCommand.CanExecute(dynasty));
        start.StartCareerModeCommand.Execute(dynasty);
        Assert.True(start.StartCareerModeCommand.CanExecute(CareerExperienceModes.Smgp));
        start.StartCareerModeCommand.Execute(CareerExperienceModes.Smgp);

        Assert.False(start.StartCareerModeCommand.CanExecute(passport));
        start.StartCareerModeCommand.Execute(passport); // direct invocation repeats the guard
        Assert.Equal(
            [CareerExperienceModes.GrandPrixDynasty, CareerExperienceModes.Smgp],
            requested);
    }

    [Fact]
    public void LegacyNewCareerCommandRemainsAvailableForCompatibility()
    {
        var start = new StartViewModel(new EmptyRecentStore());
        int requests = 0;
        start.NewCareerRequested += (_, _) => requests++;

        start.NewCareerCommand.Execute(null);

        Assert.Equal(1, requests);
    }

    [Theory]
    [InlineData(CareerExperienceModes.GrandPrixDynasty, WizardStep.SeasonPick)]
    [InlineData(CareerExperienceModes.Smgp, WizardStep.Verification)]
    public void ShellRoutesPlayableCardsIntoAnExplicitModeWizard(string mode, WizardStep expectedStep)
    {
        using var shell = CreateShell();
        var card = shell.Start.CareerModes.Single(entry => entry.Id == mode);

        shell.Start.StartCareerModeCommand.Execute(card);

        var wizard = Assert.IsType<NewCareerWizardViewModel>(shell.Current);
        Assert.Same(wizard, shell.Wizard);
        Assert.True(wizard.IsProgressionV2);
        Assert.True(wizard.HasResolvedExperienceMode);
        Assert.Equal(mode, wizard.ExperienceMode);
        Assert.Equal(expectedStep, wizard.Step);
    }

    [Fact]
    public void ExplicitSmgpEntrySelectsCanonicalPackRunsVerificationAndThenOpensCharacter()
    {
        string packs = Path.Combine(_root, "smgp-auto-packs");
        var alternate = SmgpPack("aaa-custom-smgp", "Custom SMGP");
        var canonical = SmgpPack(
            "smgp-1",
            "Canonical SMGP",
            secondLivery: "Ghost Livery #99");
        TestPackBuilder.Write(alternate, Path.Combine(packs, "aaa-custom"));
        TestPackBuilder.Write(canonical, Path.Combine(packs, "smgp-1"));

        var wizard = new NewCareerWizardViewModel(
            Environment(withCharacterRules: true),
            new NoCreateFactory(),
            packSearchRoots: [packs],
            experienceMode: CareerExperienceModes.Smgp);

        Assert.Equal(WizardStep.Verification, wizard.Step);
        Assert.Equal("smgp-1", wizard.SelectedPack!.Manifest!.PackId);
        Assert.Equal("smgp-1", wizard.Pack!.Manifest.PackId);
        Assert.True(wizard.HasWarnings);
        Assert.Contains(
            wizard.VerificationItems,
            item => !item.IsError && item.Message.Contains("Ghost Livery #99", StringComparison.Ordinal));
        Assert.False(wizard.CanGoBack);

        wizard.BackCommand.Execute(null);
        Assert.Equal(WizardStep.Verification, wizard.Step);

        wizard.ProceedAnyway = true;
        wizard.NextCommand.Execute(null);
        Assert.Equal(WizardStep.Character, wizard.Step);
        Assert.True(wizard.CanGoBack);

        wizard.BackCommand.Execute(null);
        Assert.Equal(WizardStep.Verification, wizard.Step);
        Assert.False(wizard.CanGoBack);
    }

    [Fact]
    public void ExplicitSmgpWithoutAReadablePackShowsBlockingVerificationInsteadOfPicker()
    {
        string packs = Path.Combine(_root, "missing-smgp-packs");
        Directory.CreateDirectory(packs);

        var wizard = new NewCareerWizardViewModel(
            Environment(),
            new NoCreateFactory(),
            packSearchRoots: [packs],
            experienceMode: CareerExperienceModes.Smgp);

        Assert.Equal(WizardStep.Verification, wizard.Step);
        Assert.Null(wizard.SelectedPack);
        Assert.Null(wizard.Pack);
        Assert.True(wizard.HasErrors);
        Assert.False(wizard.CanGoNext);
        Assert.False(wizard.CanGoBack);
        Assert.Contains(
            wizard.VerificationItems,
            item => item.IsError && item.Message.Contains("smgp-1", StringComparison.Ordinal));
    }

    [Fact]
    public void EscapeFromExplicitSmgpVerificationReturnsToMainMenuWithoutShowingSeasonPick()
    {
        using var shell = CreateShell();
        var smgp = shell.Start.CareerModes.Single(entry => entry.Id == CareerExperienceModes.Smgp);
        shell.Start.StartCareerModeCommand.Execute(smgp);

        var wizard = Assert.IsType<NewCareerWizardViewModel>(shell.Current);
        Assert.Equal(WizardStep.Verification, wizard.Step);
        Assert.False(wizard.CanGoBack);

        Assert.True(shell.TryEscapeBack());
        Assert.Same(shell.Start, shell.Current);
        Assert.Null(shell.Wizard);
    }

    [Fact]
    public void ShellKeepsPassportOnTheMenuUntilItsContainerExists()
    {
        using var shell = CreateShell();
        var passport = shell.Start.CareerModes.Single(entry =>
            entry.Id == CareerExperienceModes.RacingPassport);

        shell.Start.StartCareerModeCommand.Execute(passport);

        Assert.Same(shell.Start, shell.Current);
        Assert.Null(shell.Wizard);
    }

    [Fact]
    public void ExplicitWizardsExposeOnlyPacksFromTheirCareerEntity()
    {
        string packs = Path.Combine(_root, "packs");
        Directory.CreateDirectory(packs);

        var historical = TestPackBuilder.TwoRoundPack() with
        {
            Manifest = TestPackBuilder.TwoRoundPack().Manifest with
            {
                PackId = "historical-test",
                Name = "Historical Test",
                CareerStyle = null,
            },
        };
        var smgp = TestPackBuilder.TwoRoundPack() with
        {
            Manifest = TestPackBuilder.TwoRoundPack().Manifest with
            {
                PackId = "smgp-test",
                Name = "SMGP Test",
                CareerStyle = SmgpRules.CareerStyle,
            },
        };
        TestPackBuilder.Write(historical, Path.Combine(packs, "historical"));
        TestPackBuilder.Write(smgp, Path.Combine(packs, "smgp"));

        var dynastyWizard = new NewCareerWizardViewModel(
            Environment(),
            new NoCreateFactory(),
            packSearchRoots: [packs],
            experienceMode: CareerExperienceModes.GrandPrixDynasty);
        var smgpWizard = new NewCareerWizardViewModel(
            Environment(),
            new NoCreateFactory(),
            packSearchRoots: [packs],
            experienceMode: CareerExperienceModes.Smgp);

        Assert.Equal("historical-test", Assert.Single(dynastyWizard.Packs).Manifest!.PackId);
        Assert.Equal("smgp-test", Assert.Single(smgpWizard.Packs).Manifest!.PackId);
        Assert.Equal(WizardStep.SeasonPick, dynastyWizard.Step);
        Assert.Null(dynastyWizard.SelectedPack);
        Assert.Equal(WizardStep.Verification, smgpWizard.Step);
        Assert.Equal("smgp-test", smgpWizard.SelectedPack!.Manifest!.PackId);
    }

    private static Companion.Core.Packs.SeasonPack SmgpPack(
        string packId,
        string name,
        string? secondLivery = null)
    {
        var source = secondLivery is null
            ? TestPackBuilder.TwoRoundPack()
            : TestPackBuilder.TwoRoundPack(secondLivery: secondLivery);
        return source with
        {
            Manifest = source.Manifest with
            {
                PackId = packId,
                Name = name,
                CareerStyle = SmgpRules.CareerStyle,
            },
            Season = source.Season with
            {
                Rounds = Enumerable.Range(1, 16)
                    .Select(round => TestPackBuilder.Round(
                        round,
                        new DateOnly(1990, 1, 1).AddDays((round - 1) * 14).ToString("yyyy-MM-dd")))
                    .ToArray(),
            },
            Entries = source.Entries
                .Select(entry => entry with { Rounds = "1-16" })
                .ToArray(),
        };
    }

    private ShellViewModel CreateShell() => new(
        Environment(),
        new NoCreateFactory(),
        new EmptyRecentStore());

    private CareerEnvironment Environment(bool withCharacterRules = false) => new()
    {
        ContentLibrary = TestPackBuilder.Library(),
        LocateInstall = static () => null,
        DocumentsDirectory = _root,
        RulesDirectory = withCharacterRules ? ViewModelTestData.RulesDirectory : null,
    };

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class NoCreateFactory : ICareerFactory
    {
        public ICareerSession Create(CareerCreationRequest request) =>
            throw new NotSupportedException();

        public ICareerSession Open(string careerFilePath) =>
            throw new NotSupportedException();
    }

    private sealed class EmptyRecentStore : IRecentCareersStore
    {
        public IReadOnlyList<RecentCareer> Load() => [];
        public void Touch(string path, string careerName, int seasonYear = 0, string? careerStyle = null) { }
        public void Remove(string path) { }
    }
}
