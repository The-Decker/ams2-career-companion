using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Companion.Ams2.CustomAi;
using Companion.Ams2.Packs;
using Companion.Core.Grid;
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
        WizardStep.SeatPick => SelectedSeat is not null || IsOwnEntrant,
        WizardStep.Grid => GridChoices.Count(c => c.IsIncluded) >= 2,
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
                BuildGridChoices();
                Step = WizardStep.Grid;
                break;

            case WizardStep.Grid:
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
        // Confirm steps back over the (possibly skipped) character step to the grid step.
        Step = Step switch
        {
            WizardStep.Confirm => HasCharacterStep ? WizardStep.Character : WizardStep.Grid,
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

        RefreshAlternateTrackStatus(installation?.InstallDirectory);

        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(HasWarnings));
        OnPropertyChanged(nameof(LiveryScanDetails));
        OnPropertyChanged(nameof(HasLiveryScanDetails));
        OnPropertyChanged(nameof(CanGoNext));
        NextCommand.NotifyCanExecuteChanged();
    }

    // ---------- step b (cont): optional alternate MOD tracks (the "RockyTM track switch") ----------

    /// <summary>OPT-IN alternate mod tracks. When ticked AND every required mod is installed, the new
    /// career swaps the flagged rounds to their alternate venues; otherwise the season stays on its
    /// base/DLC defaults (no mod dependency). Toggling re-checks the install.</summary>
    [ObservableProperty]
    private bool _useAlternateTracks;

    /// <summary>The mod tracks this pack's alternates need, each flagged installed or not (empty when
    /// the pack offers no alternates). Refreshed from the install on verification + on toggling.</summary>
    public ObservableCollection<Companion.Ams2.Preflight.RequiredModTrack> AlternateModTracks { get; } = [];

    public bool PackHasAlternateTracks => AlternateModTracks.Count > 0;

    public bool AllAlternateModsInstalled =>
        AlternateModTracks.Count > 0 && AlternateModTracks.All(t => t.Installed);

    /// <summary>One honest line on what the tick will actually do given the install state.</summary>
    public string AlternateTrackStatus
    {
        get
        {
            int total = AlternateModTracks.Count;
            if (total == 0)
                return "This season has no alternate tracks.";
            int missing = AlternateModTracks.Count(t => !t.Installed);
            if (!UseAlternateTracks)
                return $"{total} alternate mod track(s) available — tick to use them (checks they're installed).";
            return missing == 0
                ? $"✔ All {total} alternate mod track(s) installed — the season will use them."
                : $"⚠ {missing} of {total} required mod track(s) not installed — alternates will NOT be " +
                  "applied; the season stays on its default AMS2 tracks. Install the missing mods and re-tick, " +
                  "or race the defaults.";
        }
    }

    private void RefreshAlternateTrackStatus(string? installDirectory)
    {
        AlternateModTracks.Clear();
        if (Pack is { } pack)
            foreach (var t in Companion.Ams2.Preflight.AlternateTrackPreflight.RequiredModTracks(
                         pack, _environment.ContentLibrary, installDirectory))
                AlternateModTracks.Add(t);
        OnPropertyChanged(nameof(PackHasAlternateTracks));
        OnPropertyChanged(nameof(AllAlternateModsInstalled));
        OnPropertyChanged(nameof(AlternateTrackStatus));
    }

    /// <summary>The "check installed" action — re-probe the install for the required mod tracks
    /// (e.g. after installing a missing one). Mirrors the tick's own re-check.</summary>
    [RelayCommand]
    private void CheckAlternateMods() =>
        RefreshAlternateTrackStatus(_environment.LocateInstall()?.InstallDirectory);

    partial void OnUseAlternateTracksChanged(bool value) =>
        // Ticking re-checks the install (Mike: "the tick is pressed to check the optional maps are installed").
        RefreshAlternateTrackStatus(_environment.LocateInstall()?.InstallDirectory);

    // ---------- step c: seat pick ----------

    public ObservableCollection<SeatOption> Seats { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private SeatOption? _selectedSeat;

    /// <summary>Optional escape hatch from the pack seats: type the exact name of a custom AMS2 livery
    /// you have installed and race as your OWN independent entrant (the player-as-own-entrant path — a
    /// stable synthetic driver, a neutral car, character-shaped ratings), added to the grid rather than
    /// replacing a historical driver. When set it takes precedence over the seat selection; empty = the
    /// ordinary "pick a pack seat" flow (byte-identical to a career created before this field).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private string _customLiveryName = "";

    /// <summary>True when the player typed a custom livery — they race as their own entrant instead of
    /// taking a pack seat.</summary>
    public bool IsOwnEntrant => !string.IsNullOrWhiteSpace(CustomLiveryName);

    /// <summary>The livery the player will drive: the typed custom livery (own entrant) when present,
    /// otherwise the selected pack seat's livery. Null only before either is chosen (the Next gate
    /// blocks that).</summary>
    private string? PlayerLivery => IsOwnEntrant ? CustomLiveryName.Trim() : SelectedSeat?.LiveryName;

    private void BuildSeats()
    {
        Seats.Clear();
        SelectedSeat = null;
        CustomLiveryName = "";

        var pack = Pack!;
        var teamsById = pack.Teams.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var driversById = pack.Drivers.ToDictionary(d => d.Id, StringComparer.Ordinal);

        // The player picks a SEAT (a livery) and drives it the WHOLE season. When a historical seat
        // changed drivers mid-year (e.g. Watson subbing for Lauda), the pack has two entries with the
        // SAME livery — so group by livery and show ONE seat, represented by the driver who held it
        // for the most rounds. Packs whose livery names embed the driver (e.g. "1988 Williams #5 -
        // N. Mansell") never collide, so they are unaffected; team-only livery names (1985) no longer
        // list the same seat twice. (Dangling team/driver refs are validation findings, skipped.)
        foreach (var group in pack.Entries
                     .Where(e => teamsById.ContainsKey(e.TeamId) && driversById.ContainsKey(e.DriverId))
                     .GroupBy(e => e.Ams2LiveryName, StringComparer.Ordinal))
        {
            var entry = group.OrderByDescending(e => RoundsCovered(pack, e.Rounds)).First();
            var team = teamsById[entry.TeamId];
            var driver = driversById[entry.DriverId];

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

    /// <summary>How many of the season's rounds an entry's rounds-range covers — used to pick the
    /// primary driver of a seat that changed hands mid-season.</summary>
    private static int RoundsCovered(Companion.Core.Packs.SeasonPack pack, string rounds) =>
        Companion.Core.Packs.RoundsRange.TryParse(rounds, out var range, out _)
            ? pack.Season.Rounds.Count(r => range.Contains(r.Round))
            : 0;

    // ---------- step: choose the grid (v0.6.0) ----------

    /// <summary>The season's seats (one per livery) the player can include/exclude for the field.
    /// Every seat starts included (whole pack = the default, byte-identical); the player's own seat is
    /// locked in.</summary>
    public ObservableCollection<GridSeatChoice> GridChoices { get; } = [];

    /// <summary>Total cars on the grid — every included seat, the player's own car included.</summary>
    /// <summary>The largest per-round grid size in the pack (26 for 1988, where only 26 of ~30 qualify
    /// each round). Cached when the choices are built. Zero for a pack with no grid blocks.</summary>
    private int _maxRoundGridSize;

    /// <summary>The season roster the player picked — every included seat (their own car included).
    /// This is the pool of cars that CAN race; the per-race grid draws from it (see MaxRaceCars).</summary>
    public int IncludedCount => GridChoices.Count(c => c.IsIncluded);

    /// <summary>The most cars actually on track in a single round given the selection: the per-race
    /// grid size, capped by how many seats are included. For a pre-qualifying season this is smaller
    /// than the roster (1988: 30-car roster, 26 on the grid) — so it, not IncludedCount, is what the
    /// player sees racing.</summary>
    public int MaxRaceCars => _maxRoundGridSize <= 0
        ? IncludedCount
        : Math.Min(IncludedCount, _maxRoundGridSize);

    /// <summary>The exact number to type into AMS2's "AI Opponents" — the on-track grid minus the
    /// player's own car. Derived from MaxRaceCars, NOT the roster: a pre-qualifying season fields
    /// fewer cars per round than the full field, so roster-minus-one would be too high.</summary>
    public int AiOpponentCount => Math.Max(0, MaxRaceCars - 1);

    private void BuildGridChoices()
    {
        foreach (var old in GridChoices)
            old.PropertyChanged -= OnGridChoiceChanged;
        GridChoices.Clear();

        var pack = Pack!;
        var teamsById = pack.Teams.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var driversById = pack.Drivers.ToDictionary(d => d.Id, StringComparer.Ordinal);
        // An own entrant is no pack seat, so no pack row is locked as "You" — a synthetic locked row
        // is added after the pack seats below.
        string? playerLivery = IsOwnEntrant ? null : SelectedSeat?.LiveryName;

        // The largest field the game actually puts on track in any round. With per-race grids
        // (1988 pre-qualifying: ~30 cars for 26 slots) this is the round grid size, not the roster —
        // it drives the AI-opponent guidance so the number matches what the player sees racing.
        _maxRoundGridSize = pack.Season.Rounds.Select(r => r.Grid?.Size ?? 0).DefaultIfEmpty(0).Max();

        // One choice per SEAT (car number), NOT per livery. A seat that changed drivers mid-season
        // (Williams #5 = Mansell/Brundle/Schlesser) is ONE car across the year, so it is one row —
        // otherwise a pack whose livery names embed the driver lists the same seat several times and
        // over-counts the field (the "34 cars" bug). Represented by its longest-tenure driver; the
        // choice carries every livery so excluding it drops them all.
        foreach (var group in pack.Entries
                     .Where(e => teamsById.ContainsKey(e.TeamId) && driversById.ContainsKey(e.DriverId))
                     .GroupBy(e => e.Number, StringComparer.Ordinal))
        {
            var entry = group.OrderByDescending(e => RoundsCovered(pack, e.Rounds)).First();
            var liveries = group.Select(e => e.Ams2LiveryName).Distinct(StringComparer.Ordinal).ToList();
            var choice = new GridSeatChoice
            {
                LiveryName = entry.Ams2LiveryName,
                Liveries = liveries,
                DriverName = driversById[entry.DriverId].Name,
                TeamName = teamsById[entry.TeamId].Name,
                IsLocked = playerLivery is not null && liveries.Contains(playerLivery, StringComparer.Ordinal),
                IsIncluded = true,
            };
            choice.PropertyChanged += OnGridChoiceChanged;
            GridChoices.Add(choice);
        }

        // Own entrant: the player is not a pack seat, so add their independent car as a locked "You"
        // row. Its Liveries are empty so it never joins the pack-field selection (the grid resolver
        // adds the synthetic player seat itself); IsLocked keeps it on the grid and in the field count.
        if (IsOwnEntrant)
        {
            var you = new GridSeatChoice
            {
                LiveryName = PlayerLivery!,
                Liveries = [],
                DriverName = "You — own entrant",
                TeamName = "Independent",
                IsLocked = true,
            };
            you.PropertyChanged += OnGridChoiceChanged;
            GridChoices.Add(you);
        }

        OnPropertyChanged(nameof(IncludedCount));
        OnPropertyChanged(nameof(MaxRaceCars));
        OnPropertyChanged(nameof(AiOpponentCount));
    }

    private void OnGridChoiceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GridSeatChoice.IsIncluded))
            return;
        OnPropertyChanged(nameof(IncludedCount));
        OnPropertyChanged(nameof(MaxRaceCars));
        OnPropertyChanged(nameof(AiOpponentCount));
        OnPropertyChanged(nameof(CanGoNext));
        NextCommand.NotifyCanExecuteChanged();
    }

    /// <summary>The chosen field, or null when every seat is included (the whole pack — the identity
    /// that keeps the career byte-identical to one made before this feature). The player's own seat is
    /// always included.</summary>
    private GridSelection? BuildGridSelection()
    {
        if (GridChoices.Count == 0)
            return null;
        // Whole field (no seat excluded) → null selection, so the career is byte-identical to one
        // created before this feature existed.
        if (GridChoices.All(c => c.IsIncluded || c.IsLocked))
            return null;
        var included = GridChoices
            .Where(c => c.IsIncluded || c.IsLocked)
            .SelectMany(c => c.Liveries)   // a seat contributes ALL its liveries (mid-season swaps)
            .ToList();
        return new GridSelection { IncludedLiveries = included };
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
        // Pre-fill the driver name with the seat's historical driver as a starting point; an own
        // entrant has no seat driver, so they name themselves (empty seed).
        Character = new CharacterViewModel(
            _environment.Rules.Character, IsOwnEntrant ? null : SelectedSeat?.DriverName);
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
        (SelectedSeat is not null || IsOwnEntrant) &&
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
            PlayerLiveryName = PlayerLivery!,
            CommunityBaselineXml = importBaseline ? _installedAiFileXml : null,
            CommunityBaselineSourcePath = importBaseline ? InstalledAiFilePath : null,
            Character = Character?.BuildProfile(),
            GridSelection = BuildGridSelection(),
            // Ratings Phase 3: every new career is form-reactive — the sim's field reacts to who is
            // hot each weekend (the pinned pack's per-race form). Existing careers stay form-inert.
            FormAware = true,
            // Opt-in alternate mod tracks — the service applies them only if every required mod is
            // installed (else it silently keeps the default AMS2 tracks). Default OFF.
            UseAlternateTracks = UseAlternateTracks,
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
