using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Companion.Ams2.CustomAi;
using Companion.Ams2.Packs;
using Companion.Core.Packs;
using Companion.ViewModels.Services;
using Companion.ViewModels.Settings;

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
    private readonly ISettingsService? _settings;

    private string? _packDirectory;

    public NewCareerWizardViewModel(
        CareerEnvironment environment,
        ICareerFactory factory,
        IReadOnlyList<string>? packSearchRoots = null,
        string? careersDirectory = null,
        Random? seedSource = null,
        ISettingsService? settings = null)
    {
        _environment = environment;
        _factory = factory;
        _settings = settings;
        // Explicit roots win (tests); otherwise the defaults plus the settings screen's
        // custom pack folders (missing folders are skipped by discovery).
        _packSearchRoots = packSearchRoots
            ?? [.. PackDiscovery.DefaultSearchRoots(environment.DocumentsDirectory),
                .. settings?.Current.PackFolders ?? []];
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
        WizardStep.Character => Character?.IsValid ?? true,
        WizardStep.Confirm => CanCreate,
        _ => false,
    };

    public bool CanGoBack => Step > WizardStep.SeasonPick;

    /// <summary>The character step exists only when character rules are available (the app always
    /// ships perks.json; a rules-less environment — some tests — skips straight to confirm).</summary>
    public bool HasCharacterStep => _environment.RulesDirectory is not null;

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
                if (HasCharacterStep)
                {
                    PrepareCharacter();
                    Step = WizardStep.Character;
                }
                else
                {
                    PrepareConfirm();
                    Step = WizardStep.Confirm;
                }
                break;

            case WizardStep.Character:
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
        if (!CanGoBack)
            return;
        // Confirm steps back over the (possibly skipped) character step.
        Step = Step switch
        {
            WizardStep.Confirm => HasCharacterStep ? WizardStep.Character : WizardStep.SeatPick,
            _ => Step - 1,
        };
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

    /// <summary>Info items (the livery-scan summary when everything read fine) never gate.</summary>
    public bool HasWarnings => VerificationItems.Any(i => i is { IsError: false, IsInfo: false });

    /// <summary>Per-file livery-scan detail lines (the unreadable files) behind the ONE
    /// aggregate summary item — rendered in a collapsed-by-default details section instead
    /// of a wall of per-file rows.</summary>
    public IReadOnlyList<string> LiveryScanDetails { get; private set; } = [];

    public bool HasLiveryScanDetails => LiveryScanDetails.Count > 0;

    [ObservableProperty]
    private bool _liveryScanDetailsExpanded;

    [RelayCommand]
    private void ToggleLiveryScanDetails() => LiveryScanDetailsExpanded = !LiveryScanDetailsExpanded;

    private void RunVerification()
    {
        VerificationItems.Clear();
        ProceedAnyway = false;
        LiveryScanDetails = [];
        LiveryScanDetailsExpanded = false;

        var pack = Pack!;

        foreach (var issue in PackStructuralValidator.Validate(pack).Issues)
            VerificationItems.Add(new VerificationItem(
                issue.Severity == PackIssueSeverity.Error, issue.Message));

        // The livery scan reports as ONE aggregate line: an info item when every override
        // file yielded its liveries (lenient recovery included), a single warning when some
        // files stayed unreadable — with the per-file list behind the collapsed details.
        var installation = _environment.LocateInstall();
        var scan = _environment.ScanInstalledLiveries(installation);
        if (scan.FilesScanned > 0)
        {
            LiveryScanDetails = scan.UnreadableFiles;
            VerificationItems.Add(new VerificationItem(
                IsError: false,
                scan.UnreadableFiles.Count > 0
                    ? $"{scan.Summary} — the unreadable files are listed under details."
                    : scan.Summary,
                IsInfo: scan.UnreadableFiles.Count == 0));
        }

        // PRIMARY name authority: the user's installed CustomAIDrivers class file. A name it
        // defines is valid whatever the skin state — the briefing/verification screens must not
        // show a false "won't bind" warning for a name the installed AI file already defines.
        var installedAiNames = _environment.ScanInstalledAiNames(installation, pack.Season.Ams2Class);

        foreach (var issue in PackContentValidator
                     .Validate(pack, _environment.ContentLibrary, scan.Liveries, installedAiNames).Issues)
            VerificationItems.Add(new VerificationItem(
                issue.Severity == Companion.Ams2.Preflight.PreflightSeverity.Error, issue.Message,
                IsInfo: issue.Severity == Companion.Ams2.Preflight.PreflightSeverity.Info));

        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(HasWarnings));
        OnPropertyChanged(nameof(LiveryScanDetails));
        OnPropertyChanged(nameof(HasLiveryScanDetails));
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

    // ---------- step c2: character (Increment 4a) ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private CharacterViewModel? _character;

    /// <summary>Builds the character step over the loaded rules and keeps the Next gate in sync as
    /// the player edits it (a perk toggle can flip validity).</summary>
    private void PrepareCharacter()
    {
        if (Character is not null)
            Character.PropertyChanged -= OnCharacterChanged;
        // Pre-fill the driver name with the seat's historical driver as a starting point.
        Character = new CharacterViewModel(_environment.Rules.Character, SelectedSeat?.DriverName);
        Character.PropertyChanged += OnCharacterChanged;
    }

    private void OnCharacterChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CharacterViewModel.IsValid))
        {
            OnPropertyChanged(nameof(CanGoNext));
            NextCommand.NotifyCanExecuteChanged();
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
        DetectInstalledBaseline();
    }

    // ---------- step d: NAMeS-first baseline import (locked decision #7a) ----------

    /// <summary>"Use your installed AI file as the season baseline" — defaults ON whenever
    /// the install has a parseable class XML for the pack's ams2Class.</summary>
    [ObservableProperty]
    private bool _useInstalledAiBaseline;

    /// <summary>True when the install has a class XML the lenient reader can parse.</summary>
    public bool BaselineImportAvailable { get; private set; }

    /// <summary>Set when a class XML exists but could not be parsed even leniently.</summary>
    public string? BaselineImportError { get; private set; }

    public string? InstalledAiFilePath { get; private set; }

    /// <summary>Summary diff: drivers whose values the installed file would provide.</summary>
    public int BaselineImportedCount { get; private set; }

    /// <summary>Summary diff: drivers keeping pack-authored values (no livery match).</summary>
    public int BaselinePackOnlyCount { get; private set; }

    public string? BaselineImportSummary { get; private set; }

    /// <summary>The exact bytes previewed at the confirm step — the same text is handed to
    /// career creation so what was previewed is what gets pinned.</summary>
    private string? _installedAiFileXml;

    private void DetectInstalledBaseline()
    {
        BaselineImportAvailable = false;
        BaselineImportError = null;
        InstalledAiFilePath = null;
        BaselineImportedCount = 0;
        BaselinePackOnlyCount = 0;
        BaselineImportSummary = null;
        _installedAiFileXml = null;

        var pack = Pack!;
        var installation = _environment.LocateInstall();
        if (installation is not null)
        {
            string path = Path.Combine(
                installation.CustomAiDriversDirectory, pack.Season.Ams2Class + ".xml");
            if (File.Exists(path))
            {
                InstalledAiFilePath = path;
                try
                {
                    string xml = File.ReadAllText(path);
                    var installed = CommunityAiReader.Parse(xml);
                    var preview = CommunityBaselineImport.Apply(pack, installed);

                    _installedAiFileXml = xml;
                    BaselineImportAvailable = true;
                    BaselineImportedCount = preview.ImportedDriverCount;
                    BaselinePackOnlyCount = preview.PackOnlyDriverCount;
                    BaselineImportSummary = preview.Summary;
                }
                catch (Exception ex) when (
                    ex is InvalidOperationException or IOException or UnauthorizedAccessException)
                {
                    BaselineImportError = ex.Message;
                }
            }
        }

        // Default ON when parseable — unless the settings screen turned the NAMeS-first
        // default off (the checkbox stays available either way).
        UseInstalledAiBaseline = BaselineImportAvailable
            && (_settings?.Current.PreferInstalledBaseline ?? true);

        OnPropertyChanged(nameof(BaselineImportAvailable));
        OnPropertyChanged(nameof(BaselineImportError));
        OnPropertyChanged(nameof(InstalledAiFilePath));
        OnPropertyChanged(nameof(BaselineImportedCount));
        OnPropertyChanged(nameof(BaselinePackOnlyCount));
        OnPropertyChanged(nameof(BaselineImportSummary));
    }

    private void Create()
    {
        CreateError = null;
        bool importBaseline = UseInstalledAiBaseline && _installedAiFileXml is not null;
        var request = new CareerCreationRequest
        {
            PackDirectory = _packDirectory!,
            CareerFilePath = UniqueCareerFilePath(),
            CareerName = CareerName.Trim(),
            MasterSeed = long.Parse(MasterSeedText),
            PlayerLiveryName = SelectedSeat!.LiveryName,
            CommunityBaselineXml = importBaseline ? _installedAiFileXml : null,
            CommunityBaselineSourcePath = importBaseline ? InstalledAiFilePath : null,
            Character = Character?.BuildProfile(),
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
