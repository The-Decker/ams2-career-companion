using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Companion.Ams2.Packs;
using Companion.Core.Packs;
using Companion.ViewModels.Services;

namespace Companion.ViewModels.Wizard;

public sealed class CareerCreatedEventArgs(ICareerSession session, string careerFilePath) : EventArgs
{
    public ICareerSession Session { get; } = session;
    public string CareerFilePath { get; } = careerFilePath;
}

/// <summary>
/// The four-step new-career wizard (app-shell contract):
///  a. Season pick — packs from the exe-adjacent packs\ folder + Documents\AMS2CareerCompanion\Packs.
///  b. Content verification — PackStructuralValidator + PackContentValidator + installed-livery
///     scan. ERRORS BLOCK; warnings allow an explicit proceed-anyway.
///  c. Seat pick — the pack's entries with driver ratings and team tier/reliability.
///  d. Confirm — career name, master seed (random default, editable), rules-summary chip;
///     Create pins the pack and creates the career DB.
/// </summary>
public sealed partial class NewCareerWizardViewModel : ObservableObject
{
    private readonly CareerEnvironment _environment;
    private readonly ICareerFactory _factory;
    private readonly IReadOnlyList<string> _packSearchRoots;
    private readonly string _careersDirectory;
    private readonly Random _seedSource;

    private string? _packDirectory;

    public NewCareerWizardViewModel(
        CareerEnvironment environment,
        ICareerFactory factory,
        IReadOnlyList<string>? packSearchRoots = null,
        string? careersDirectory = null,
        Random? seedSource = null)
    {
        _environment = environment;
        _factory = factory;
        _packSearchRoots = packSearchRoots ?? PackDiscovery.DefaultSearchRoots(environment.DocumentsDirectory);
        _careersDirectory = careersDirectory
            ?? Path.Combine(environment.DocumentsDirectory, "AMS2CareerCompanion", "Careers");
        _seedSource = seedSource ?? Random.Shared;

        RefreshPacks();
    }

    public event EventHandler<CareerCreatedEventArgs>? CareerCreated;

    // ---------- step state ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand), nameof(BackCommand))]
    private WizardStep _step = WizardStep.SeasonPick;

    public bool CanGoNext => Step switch
    {
        WizardStep.SeasonPick => SelectedPack is { LoadError: null },
        WizardStep.Verification => !HasErrors && (!HasWarnings || ProceedAnyway),
        WizardStep.SeatPick => SelectedSeat is not null,
        WizardStep.Confirm => CanCreate,
        _ => false,
    };

    public bool CanGoBack => Step > WizardStep.SeasonPick;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next()
    {
        if (!CanGoNext)
            return;

        switch (Step)
        {
            case WizardStep.SeasonPick:
                if (!LoadSelectedPack())
                    return;
                RunVerification();
                Step = WizardStep.Verification;
                break;

            case WizardStep.Verification:
                BuildSeats();
                Step = WizardStep.SeatPick;
                break;

            case WizardStep.SeatPick:
                PrepareConfirm();
                Step = WizardStep.Confirm;
                break;

            case WizardStep.Confirm:
                Create();
                break;
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back()
    {
        if (CanGoBack)
            Step = Step - 1;
    }

    partial void OnStepChanged(WizardStep value) => OnPropertyChanged(nameof(CanGoBack));

    // ---------- step a: season pick ----------

    public ObservableCollection<DiscoveredPack> Packs { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private DiscoveredPack? _selectedPack;

    [ObservableProperty]
    private string? _packLoadError;

    public SeasonPack? Pack { get; private set; }

    public void RefreshPacks()
    {
        Packs.Clear();
        foreach (var pack in PackDiscovery.Discover(_packSearchRoots))
            Packs.Add(pack);
    }

    private bool LoadSelectedPack()
    {
        PackLoadError = null;
        try
        {
            var files = SeasonPackFiles.Read(SelectedPack!.Directory);
            Pack = files.Parse();
            _packDirectory = SelectedPack.Directory;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Pack = null;
            _packDirectory = null;
            PackLoadError = ex.Message;
            return false;
        }
    }

    // ---------- step b: verification ----------

    public ObservableCollection<VerificationItem> VerificationItems { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private bool _proceedAnyway;

    public bool HasErrors => VerificationItems.Any(i => i.IsError);

    public bool HasWarnings => VerificationItems.Any(i => !i.IsError);

    private void RunVerification()
    {
        VerificationItems.Clear();
        ProceedAnyway = false;

        var pack = Pack!;

        foreach (var issue in PackStructuralValidator.Validate(pack).Issues)
            VerificationItems.Add(new VerificationItem(
                issue.Severity == PackIssueSeverity.Error, issue.Message));

        var installation = _environment.LocateInstall();
        var (installedLiveries, scanWarnings) = _environment.ScanInstalledLiveries(installation);
        foreach (string warning in scanWarnings)
            VerificationItems.Add(new VerificationItem(false, $"Livery scan: {warning}"));

        foreach (var issue in PackContentValidator.Validate(pack, _environment.ContentLibrary, installedLiveries).Issues)
            VerificationItems.Add(new VerificationItem(
                issue.Severity == Companion.Ams2.Preflight.PreflightSeverity.Error, issue.Message));

        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(HasWarnings));
        OnPropertyChanged(nameof(CanGoNext));
        NextCommand.NotifyCanExecuteChanged();
    }

    // ---------- step c: seat pick ----------

    public ObservableCollection<SeatOption> Seats { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private SeatOption? _selectedSeat;

    private void BuildSeats()
    {
        Seats.Clear();
        SelectedSeat = null;

        var pack = Pack!;
        var teamsById = pack.Teams.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var driversById = pack.Drivers.ToDictionary(d => d.Id, StringComparer.Ordinal);

        foreach (var entry in pack.Entries)
        {
            // A dangling reference is a validation finding, not a seat — skip defensively.
            if (!teamsById.TryGetValue(entry.TeamId, out var team) ||
                !driversById.TryGetValue(entry.DriverId, out var driver))
                continue;

            Seats.Add(new SeatOption
            {
                LiveryName = entry.Ams2LiveryName,
                DriverId = driver.Id,
                DriverName = driver.Name,
                TeamId = team.Id,
                TeamName = team.Name,
                Number = entry.Number,
                Rounds = entry.Rounds,
                RaceSkill = driver.Ratings.RaceSkill,
                QualifyingSkill = driver.Ratings.QualifyingSkill,
                TeamTier = team.BudgetTier,
                Prestige = team.Prestige,
                Reliability = team.Reliability,
            });
        }
    }

    // ---------- step d: confirm ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private string _careerName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private string _masterSeedText = "";

    [ObservableProperty]
    private string? _createError;

    public IReadOnlyList<string> RulesSummary { get; private set; } = [];

    public bool CanCreate =>
        Pack is not null &&
        SelectedSeat is not null &&
        !string.IsNullOrWhiteSpace(CareerName) &&
        long.TryParse(MasterSeedText, out _);

    [RelayCommand]
    private void RandomizeSeed() => MasterSeedText = _seedSource.NextInt64().ToString();

    private void PrepareConfirm()
    {
        var pack = Pack!;
        if (string.IsNullOrWhiteSpace(CareerName))
            CareerName = $"{pack.Season.SeriesName} {pack.Season.Year}";
        if (!long.TryParse(MasterSeedText, out _))
            MasterSeedText = _seedSource.NextInt64().ToString();
        RulesSummary = RulesSummaryComposer.Compose(pack.Season.PointsSystem, pack.Season.Rounds.Count);
        OnPropertyChanged(nameof(RulesSummary));
    }

    private void Create()
    {
        CreateError = null;
        var request = new CareerCreationRequest
        {
            PackDirectory = _packDirectory!,
            CareerFilePath = UniqueCareerFilePath(),
            CareerName = CareerName.Trim(),
            MasterSeed = long.Parse(MasterSeedText),
            PlayerLiveryName = SelectedSeat!.LiveryName,
        };

        ICareerSession session;
        try
        {
            session = _factory.Create(request);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or JsonException or UnauthorizedAccessException)
        {
            CreateError = ex.Message;
            return;
        }

        CareerCreated?.Invoke(this, new CareerCreatedEventArgs(session, request.CareerFilePath));
    }

    private string UniqueCareerFilePath()
    {
        string baseName = Sanitize(CareerName.Trim());
        string path = Path.Combine(_careersDirectory, baseName + ".ams2career");
        for (int i = 2; File.Exists(path); i++)
            path = Path.Combine(_careersDirectory, $"{baseName} ({i}).ams2career");
        return path;
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string cleaned = new(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return cleaned.Length == 0 ? "career" : cleaned;
    }
}
