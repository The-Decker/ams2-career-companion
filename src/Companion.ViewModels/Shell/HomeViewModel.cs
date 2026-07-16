using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.ViewModels.Briefing;
using Companion.ViewModels.Confirm;
using Companion.ViewModels.ResultEntry;
using Companion.ViewModels.Review;
using Companion.ViewModels.Services;
using Companion.ViewModels.Settings;
using Companion.ViewModels.Standings;
using Companion.ViewModels.Wizard;

namespace Companion.ViewModels.Shell;

/// <summary>
/// The Home screen conductor (app-shell contract screen 3): a persistent career header
/// (season, round, player standing) over a two-state content area — Race Day briefing ⇄
/// Enter result for the current round — plus the Confirm interstitial and the Standings
/// screen. When the season is complete the content pins to the season review (final
/// standings). Owns the career session's lifetime: disposing the home disposes the session.
/// </summary>
public sealed partial class HomeViewModel : ObservableObject, IDisposable
{
    private readonly ICareerSession _session;
    private readonly IFileWatcher? _watcher;
    private readonly TimeProvider _clock;
    private readonly ISettingsService? _settings;

    private ResultEntryViewModel? _resultEntry;

    /// <summary>The weekend qualifying-order entry (Increment 2b.3): shown before the race result
    /// when the round's weekend declares a qualifying session. Reuses the result-entry grammar to
    /// capture the grid order pole-first. Null on single-race rounds — the loop stays byte-identical.</summary>
    private ResultEntryViewModel? _qualifyingEntry;

    /// <summary>The captured qualifying order (pole first) for the current round, held in memory
    /// until the race result is applied — then written verbatim into the round's raw envelope via
    /// <see cref="ResultDraft.QualifyingOrder"/>. Null when the round ran no qualifying session.</summary>
    private IReadOnlyList<string>? _capturedQualifyingOrder;

    /// <summary>The starting-grid look shown AFTER qualifying and BEFORE the race — display-only, so
    /// the player sees the grid pole-first before racing. Null except while on that step.</summary>
    private StartingGridViewModel? _startingGrid;

    /// <summary>The SMGP rival screen — its own step AFTER race setup and BEFORE qualifying (a wrapper
    /// over the shared Briefing so the naming persists). Null except while on that step.</summary>
    private RivalScreenViewModel? _rivalScreen;

    /// <summary>The transient cinematic gate immediately before a qualifying/race editor. It is
    /// retained only so a standings/briefing round-trip returns to the same gate until Continue;
    /// once its editor exists, re-entry goes straight to that editor and never replays the gate.</summary>
    private SessionIntroViewModel? _sessionIntro;

    /// <summary>The SMGP promotion / demotion screen (3c-3) — its own full-immersion step AFTER the
    /// confirm, when the round produced a seat change (a two-wins offer to accept/decline, or a forced
    /// relegation to acknowledge). Null except while on that step; the round does not advance until it
    /// is answered (season end is held too — see CareerSessionService.EnsureSeasonEnd).</summary>
    private PromotionViewModel? _promotion;

    /// <summary>The 17-season SMGP campaign FINALE (Mike's "final final screen") — its own full-immersion
    /// step shown ONCE at the fold that completes the campaign, before the final season review. Null
    /// except while on that step; its Continue command advances into the review. Display-only.</summary>
    private SmgpFinaleViewModel? _finale;

    /// <summary>True once the rival step has been shown-and-passed this round, so re-entering the flow
    /// (or a career with no rival) goes straight to qualifying. Reset on Apply.</summary>
    private bool _rivalStepDone;

    /// <summary>Races already confirmed this round (Increment 2e.3): as the player advances "Next
    /// race" each race is captured and LOCKED (exactly like the qualifying step); the final race
    /// stays live so confirm → back can re-edit it. Empty on a single race. Cleared on Apply.</summary>
    private readonly List<ResultDraft> _capturedRaces = [];

    public HomeViewModel(
        ICareerSession session,
        IFileWatcher? stagedFileWatcher = null,
        TimeProvider? clock = null,
        ISettingsService? settings = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _watcher = stagedFileWatcher;
        _clock = clock ?? TimeProvider.System;
        _settings = settings;

        Briefing = new BriefingViewModel(session, stagedFileWatcher);
        CoachMarks = new CoachMarksViewModel(settings);
        _summary = session.Summary;

        // A Normal-mode death leaves the career file available for restore, so reopening that file must
        // land on the same terminal screen as the live fatal-round handoff below. Hardcore cannot reopen
        // (its file is deleted), but the deletion flag remains part of the DB-free bind contract for fakes
        // and any already-spent session. Read the rich model only after the terminal status is known.
        var mortality = session.PlayerMortality();
        if (mortality.Deceased || mortality.CareerFileDeleted)
        {
            _careerOver = mortality;
            _deathScreen = session.DeathScreen();
        }

        // Both terminal routes keep the ordinary hub content inert underneath the App-owned takeover:
        // mortality through CareerOver/DeathScreen, and the SMGP floor through Briefing.SmgpCareerOver.
        if (IsCareerTerminal)
            _currentContent = Briefing;
        else if (_summary.SeasonComplete)
        {
            // A beaten campaign summit leads with the FINALE on reopen too — closing the app right
            // after the final fold must not be the only chance to ever see the celebration. Its
            // Continue advances into the review exactly like the live handoff in AdvanceAfterRound.
            if (_session.SmgpFinale() is { } finale)
                ShowFinale(finale);
            else
                ShowSeasonReview();
        }
        else if (_session.CurrentSitOut() is { } sitOut)
            // Opened onto an injured round (e.g. reopened mid-suspension): the player sits out, so the
            // auto-sim screen leads — never manual result entry. (Character death & injury §5.)
            _currentContent = MakeSitOut(sitOut);
        else if (_settings?.Current.AutoOpenBriefing ?? true)
            _currentContent = Briefing;
        else
            _currentContent = NewStandings(); // auto-open briefing turned off in settings
    }

    public ICareerSession Session => _session;

    /// <summary>The single briefing instance for the career; refreshed after every Apply.</summary>
    public BriefingViewModel Briefing { get; }

    /// <summary>First-run coach marks for the three career screens (ux-round section 4);
    /// the briefing/result-entry/standings views bind through Home so one dismissal state
    /// serves them all.</summary>
    public CoachMarksViewModel CoachMarks { get; }

    // ---------- header ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(HeaderTitle), nameof(SeasonYearText), nameof(RoundText), nameof(StandingText),
        nameof(IsSeasonReview), nameof(FormText), nameof(HasForm),
        nameof(PlayerCarWeatherChoiceRequired), nameof(PlayerCarIsWet),
        nameof(DriverLevelText), nameof(DriverAvailabilityLabel))]
    private CareerSummary _summary;

    /// <summary>What the LAST applied round did to the player's progression (XP applied, level
    /// movement, banked Skill Points) — announced where it happens instead of waiting to be found
    /// on the Driver tab. Null before any apply this session or for a character-free career.</summary>
    [ObservableProperty]
    private RoundProgressionSummary? _lastProgression;

    /// <summary>Header chip: the driver's current level ("LV 137"), or null for a character-free
    /// career (the chip collapses).</summary>
    public string? DriverLevelText =>
        _session.CharacterDossier() is { } dossier ? $"LV {dossier.Level}" : null;

    /// <summary>Header chip: the driver's availability ("Fit", "Injured — out 2 races", …), or null
    /// for a character-free career. An injury is visible at a glance, not two tabs deep.</summary>
    public string? DriverAvailabilityLabel => _session.CharacterDossier()?.AvailabilityLabel;

    /// <summary>Bind contract for the pre-race wet/dry chooser. True means the authored weather is
    /// mixed/dynamic/unknown and a conditional v2 player-car build cannot stage until the player
    /// selects one band.</summary>
    public bool PlayerCarWeatherChoiceRequired =>
        _session.CurrentRoundNeedsWeatherDeclaration();

    /// <summary>The persisted or safe authored prefill; null while the chooser above is required.</summary>
    public bool? PlayerCarIsWet => _session.CurrentRoundIsWet();

    [RelayCommand]
    private void DeclarePlayerCarWeather(bool isWet)
    {
        try
        {
            _session.DeclareCurrentRoundWeather(isWet);
            ContentError = null;
            OnPropertyChanged(nameof(PlayerCarWeatherChoiceRequired));
            OnPropertyChanged(nameof(PlayerCarIsWet));
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException)
        {
            ContentError = ex.Message;
        }
    }

    public string HeaderTitle
    {
        get
        {
            // The wizard defaults the career name to "<series> <year>" — don't echo it twice.
            string season = $"{Summary.SeriesName} {Summary.SeasonYear}";
            return string.Equals(Summary.CareerName, season, StringComparison.OrdinalIgnoreCase)
                ? season
                : $"{Summary.CareerName} · {season}";
        }
    }

    /// <summary>The season year, rendered big in the career header — multi-season careers
    /// (M6) make "which year am I in?" the header's first question.</summary>
    public string SeasonYearText => Summary.SeasonYear.ToString();

    public string RoundText => Summary.SeasonComplete
        ? "Season complete"
        : $"Round {Summary.CurrentRound} of {Summary.RoundCount}";

    public string StandingText => Summary.PlayerPosition is { } position
        ? $"P{position} in the championship"
        : "No standings yet";

    /// <summary>True once at least one round has folded — the header shows the form line.</summary>
    public bool HasForm => Summary.Reputation is not null;

    /// <summary>Reputation + OPI with trend glyphs, from the FOLDED player state
    /// (m5-fix-integration "App wiring": the home header reads the fold, never recomputes).</summary>
    public string FormText => Summary is { Reputation: { } reputation, Opi: { } opi }
        ? $"Rep {reputation:0.#}{TrendGlyph(Summary.ReputationDelta)}   ·   " +
          $"OPI {opi:+0.00;-0.00;0.00}{TrendGlyph(Summary.OpiDelta)}"
        : "";

    /// <summary>▲ improving / ▼ falling / flat within ±0.05 (or no trend yet).</summary>
    public static string TrendGlyph(double? delta) => delta switch
    {
        > 0.05 => " ▲",
        < -0.05 => " ▼",
        _ => "",
    };

    /// <summary>True once every round has an applied result — the content area pins to the
    /// season review (final standings).</summary>
    public bool IsSeasonReview => Summary.SeasonComplete;

    // ---------- two-state content ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(IsBriefingState), nameof(IsResultEntryState),
        nameof(IsConfirmState), nameof(IsStandingsState), nameof(IsSeasonReviewState),
        nameof(IsQualifyingStep), nameof(IsStartingGridState), nameof(IsRivalStep),
        nameof(IsSessionIntroState), nameof(IsPromotionStep), nameof(IsFinaleStep),
        nameof(IsSitOutStep), nameof(ConfirmButtonText))]
    private ObservableObject? _currentContent;

    [ObservableProperty]
    private string? _contentError;

    /// <summary>Non-null once a fatal accident ENDS the career (character death &amp; injury §3.3): the driver
    /// died (Normal — the death screen offers a restore) or a Hardcore death just deleted the save. The
    /// shell routes to the death / permadeath screen from this (Slice 5 renders it). DB-FREE — for a
    /// Hardcore death the session's DB is already disposed, so nothing may query it once this is set.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCareerTerminal))]
    [NotifyCanExecuteChangedFor(nameof(ShowBriefingCommand), nameof(EnterResultCommand))]
    private PlayerMortalityStatus? _careerOver;

    /// <summary>The RICH death-screen projection (Slice 5) — an in-world obituary, the career record, the
    /// fatal accident's cause/venue, and (Normal) the restorable save slots. Set alongside
    /// <see cref="CareerOver"/>; the death screen binds this for the obituary + record and falls back to
    /// <see cref="CareerOver"/> for the bare status. Also DB-free on the Hardcore path (captured before
    /// deletion).</summary>
    [ObservableProperty]
    private DeathScreenModel? _deathScreen;

    /// <summary>One additive terminal predicate over the two existing, frozen GUI bind contracts:
    /// fatal mortality uses <see cref="CareerOver"/>, while the SMGP Level-D floor uses
    /// <see cref="BriefingViewModel.SmgpCareerOver"/>. It changes navigation only; each ending keeps its
    /// purpose-built projection and view.</summary>
    public bool IsCareerTerminal => CareerOver is not null || Briefing.SmgpCareerOver;

    public bool IsBriefingState => CurrentContent is BriefingViewModel;
    public bool IsResultEntryState => CurrentContent is ResultEntryViewModel;
    public bool IsConfirmState => CurrentContent is ConfirmViewModel;
    public bool IsStandingsState => CurrentContent is StandingsViewModel;
    public bool IsSeasonReviewState => CurrentContent is SeasonReviewViewModel;

    /// <summary>True while the CURRENT content is the starting-grid look (after qualifying, before the
    /// race). Drives the primary action's "Start the race" label.</summary>
    public bool IsStartingGridState => CurrentContent is StartingGridViewModel;

    /// <summary>True while the CURRENT content is the SMGP rival screen (after race setup, before
    /// qualifying). Drives the primary action's "Continue" label.</summary>
    public bool IsRivalStep => CurrentContent is RivalScreenViewModel;

    /// <summary>True on the click-through QUALIFYING/RACE cinematic gate. The gate owns its Continue
    /// command, so the shared result-confirm action remains disabled/suppressed on this state.</summary>
    public bool IsSessionIntroState => CurrentContent is SessionIntroViewModel;

    /// <summary>True while the CURRENT content is the SMGP promotion / demotion screen (after confirm,
    /// when the round moved seats). Its own accept/decline buttons drive the flow, so the header's
    /// primary confirm action is suppressed.</summary>
    public bool IsPromotionStep => CurrentContent is PromotionViewModel;

    /// <summary>True while the CURRENT content is the 17-season SMGP campaign finale (Mike's "final final
    /// screen"). Its own Continue button drives the flow, so the header's primary confirm is suppressed
    /// exactly like the promotion step.</summary>
    public bool IsFinaleStep => CurrentContent is SmgpFinaleViewModel;

    /// <summary>True on the INJURED sit-out step (character death &amp; injury §5): the player is
    /// unavailable, so the round is auto-simulated instead of entered manually.</summary>
    public bool IsSitOutStep => CurrentContent is SitOutViewModel;

    /// <summary>True when this round has an SMGP rival step to show (an active rival briefing). A
    /// non-SMGP / character-free career has none, so the flow is byte-identical to the shipped loop.</summary>
    private bool HasSmgpRivalStep => Briefing.SmgpActive;

    /// <summary>True while the CURRENT content is the weekend qualifying-order step (not the race
    /// result) — both reuse the result-entry grammar, so this drives the primary action's label
    /// and the "which step am I on" cues. Always false on single-race rounds.</summary>
    public bool IsQualifyingStep => _qualifyingEntry is not null
        && ReferenceEquals(CurrentContent, _qualifyingEntry);

    /// <summary>The primary confirm button's label: the qualifying step locks the grid; a race that
    /// is not the round's last advances to the next race; the last (or only) race scores the round.</summary>
    public string ConfirmButtonText =>
        IsRivalStep ? "Continue  ⏎"
        : IsQualifyingStep ? "Set the grid  ⏎"
        : IsStartingGridState ? "Start the race  ⏎"
        : IsResultEntryState && !IsLastRace ? "Next race  ⏎"
        : "Confirm result  ⏎";

    /// <summary>The scoring races the current round declares (Increment 2e.3); null on single-race.</summary>
    private IReadOnlyList<PackWeekendRace>? WeekendRaces => _session.CurrentWeekend()?.Races;

    /// <summary>How many races this round scores — 2 for an authored two-race weekend, else 1.</summary>
    private int WeekendRaceCount => WeekendRaces?.Count ?? 1;

    /// <summary>The 0-based index of the race being entered (confirmed races are captured + locked).</summary>
    private int CurrentRaceIndex => _capturedRaces.Count;

    /// <summary>True when the current race is the round's last — its confirm scores the whole round.</summary>
    private bool IsLastRace => CurrentRaceIndex >= WeekendRaceCount - 1;

    partial void OnCurrentContentChanged(ObservableObject? value) =>
        ConfirmResultCommand.NotifyCanExecuteChanged();

    private bool RoundInProgress => !Summary.SeasonComplete && !IsCareerTerminal;

    [RelayCommand(CanExecute = nameof(RoundInProgress))]
    private void ShowBriefing()
    {
        ContentError = null;
        Briefing.Refresh();
        CurrentContent = Briefing;
    }

    [RelayCommand(CanExecute = nameof(RoundInProgress))]
    private void EnterResult()
    {
        ContentError = null;

        // Injured: the player sits this round out (AMS2 cannot spectate a single-player race), so it is
        // auto-simulated — never a manual result. Show the sit-out screen instead. (Character death &
        // injury §5; this is the guard the normal advance path also routes through.)
        if (_session.CurrentSitOut() is { } sitOut)
        {
            ShowSitOut(sitOut);
            return;
        }

        // SMGP rival step: name your rival on its own screen, once per round, BEFORE qualifying
        // (Mike's Upcoming Race loop). A non-SMGP / character-free career has no rival step, so the
        // shipped loop stays byte-identical.
        if (!_rivalStepDone && HasSmgpRivalStep)
        {
            ShowRival();
            return;
        }

        // Weekend qualifying step (Increment 2b.3): on a round whose weekend declares a qualifying
        // session, capture the grid order (pole first) BEFORE the race — once per round. A round
        // with no weekend / no qualifying skips straight to the race, so the shipped single-race
        // loop is byte-identical.
        if (QualifyingSession is not null && _capturedQualifyingOrder is null)
        {
            ShowQualifyingIntroOrEntry();
            return;
        }

        ShowRaceIntroOrEntry();
    }

    /// <summary>Show the SMGP rival screen — a wrapper over the shared Briefing so the pick / dossier
    /// / "name him" state (consumed at Apply via BuildSmgpRival) is preserved. Its "Continue" advances
    /// to qualifying.</summary>
    private void ShowRival()
    {
        _rivalScreen ??= new RivalScreenViewModel(Briefing);
        CurrentContent = _rivalScreen;
        ConfirmResultCommand.NotifyCanExecuteChanged();
    }

    /// <summary>The current round's qualifying session when its weekend declares one present; null
    /// on a single-race round (the byte-identical default — every bundled pack).</summary>
    private PackWeekendSession? QualifyingSession =>
        _session.CurrentWeekend()?.Qualifying is { Present: true } qualifying ? qualifying : null;

    /// <summary>Gate a newly-created qualifying editor with its cinematic intro. Once the editor
    /// exists, navigation returns to it directly so a half-entered order never replays the intro.</summary>
    private void ShowQualifyingIntroOrEntry()
    {
        if (_qualifyingEntry is not null)
        {
            CurrentContent = _qualifyingEntry;
            return;
        }

        ShowSessionIntro(
            SessionIntroKind.Qualifying,
            QualifyingSession?.Label,
            ShowQualifyingEntry);
    }

    /// <summary>Show the qualifying-order entry — the same result-entry grammar, reused to capture
    /// the grid pole-first. Built lazily so a half-entered order survives a toggle to the briefing.</summary>
    private void ShowQualifyingEntry()
    {
        if (_qualifyingEntry is null)
        {
            var grid = _session.CurrentGrid();
            if (grid.Count == 0)
            {
                ContentError = "This round has no grid to qualify.";
                CurrentContent = Briefing;
                return;
            }
            _qualifyingEntry = new ResultEntryViewModel(grid, Summary.PlayerDriverId, _clock)
            {
                SessionLabel = QualifyingSession?.Label ?? "Qualifying",
                // Surface where the named SMGP rival qualifies as the grid is entered (null = no rival).
                RivalDriverId = Briefing.NamedSmgpRival?.DriverId,
                RivalName = Briefing.NamedSmgpRival?.DriverName,
                RivalPronouns = Briefing.NamedSmgpRival?.Pronouns ?? Companion.Core.Smgp.SmgpPronouns.Default,
            };
            _qualifyingEntry.PropertyChanged += OnResultEntryPropertyChanged;
            ConfirmResultCommand.NotifyCanExecuteChanged();
        }
        CurrentContent = _qualifyingEntry;
    }

    /// <summary>Show the starting grid — the qualifying result laid out pole-first as driver + car
    /// cards, a display-only look before the race (Increment 2 GUI: "see the starting grid"). Built
    /// fresh each time from the captured order; never a fold input.</summary>
    private void ShowStartingGrid()
    {
        var grid = OrderByQualifying(_session.CurrentGrid(), _capturedQualifyingOrder);
        if (grid.Count == 0)
        {
            ContentError = "This round has no grid to show.";
            ShowRaceIntroOrEntry();
            return;
        }
        // Car art follows the physical livery, not the active driver: season-to-season SMGP reshuffles
        // move drivers while the cars stay fixed. The session captured this display-only mapping from
        // the authored pinned pack before applying that reshuffle. It also resolves the synthetic
        // player's current car after promotions/demotions. Unknown/custom liveries retain the legacy
        // player-donor / active-driver fallbacks in StartingGridViewModel.
        var carArtKeyByLivery = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var seat in grid)
        {
            if (_session.GridCarArtKeyForLivery(seat.Ams2LiveryName) is { } carArtKey)
                carArtKeyByLivery[seat.Ams2LiveryName] = carArtKey;
        }
        var playerSeat = grid.FirstOrDefault(s => s.IsPlayer);
        string? playerCarArtDriverId = playerSeat is null
            ? null
            : carArtKeyByLivery.GetValueOrDefault(playerSeat.Ams2LiveryName)
              ?? _session.Pack.Entries
                .FirstOrDefault(e => string.Equals(
                    e.Ams2LiveryName, playerSeat.Ams2LiveryName, StringComparison.Ordinal))
                ?.DriverId;

        // The dynamic per-race DNQ field: the cars whose livery didn't make this round's grid. The pack
        // fields all its painted cars (SMGP = 34) but the grid caps at ~26, so the slowest 8-9 sit out —
        // and which ones rotates race to race (the field is pre-qualified). Empty for a full-field pack,
        // which hides the strip. Ordered fastest-first, so the cars that narrowly missed lead.
        var seatedLiveries = grid.Select(s => s.Ams2LiveryName).ToHashSet(StringComparer.Ordinal);
        var driverName = new Dictionary<string, string>(StringComparer.Ordinal);
        var driverQuali = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var d in _session.Pack.Drivers)
        {
            driverName.TryAdd(d.Id, d.Name);
            driverQuali.TryAdd(d.Id, d.Ratings.QualifyingSkill);
        }
        var teamName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var t in _session.Pack.Teams)
            teamName.TryAdd(t.Id, t.Name);
        var dnq = _session.Pack.Entries
            .Where(e => !seatedLiveries.Contains(e.Ams2LiveryName))
            .OrderByDescending(e => driverQuali.GetValueOrDefault(e.DriverId))
            .Select(e => new StartingGridDnq(
                driverName.GetValueOrDefault(e.DriverId, e.DriverId),
                teamName.GetValueOrDefault(e.TeamId, e.TeamId),
                e.Number))
            .ToList();

        _startingGrid = new StartingGridViewModel(
            grid, Summary.PlayerDriverId,
            WeekendRaceCount > 1 ? WeekendRaces?[CurrentRaceIndex].Label : null,
            BuildGridConditions(), playerCarArtDriverId, dnq,
            CharacterCountryCatalog.Find(_session.CurrentPlayerCountryCode())?.FlagKey,
            carArtKeyByLivery);
        CurrentContent = _startingGrid;
        ConfirmResultCommand.NotifyCanExecuteChanged();
    }

    /// <summary>The starting-grid bars' race conditions — lap distance + weather read from the current
    /// briefing; the atmospheric readouts (track/air temp, wind, humidity) are synthesised
    /// deterministically from the weather for flavour (display-only, never folded).</summary>
    private GridConditions BuildGridConditions()
    {
        var briefing = _session.CurrentBriefing();
        string weather = briefing?.Settings.FirstOrDefault(s =>
            string.Equals(s.Section, "Race", StringComparison.Ordinal) &&
            s.Label.StartsWith("Weather", StringComparison.OrdinalIgnoreCase))?.Value ?? "Clear";
        bool wet = weather.Contains("rain", StringComparison.OrdinalIgnoreCase)
            || weather.Contains("wet", StringComparison.OrdinalIgnoreCase)
            || weather.Contains("storm", StringComparison.OrdinalIgnoreCase)
            || weather.Contains("shower", StringComparison.OrdinalIgnoreCase);
        int seed = Math.Abs(Summary.CurrentRound * 7 + weather.Length * 3);
        return new GridConditions
        {
            LapDistanceKm = ParseKm(Briefing.CircuitCaption),
            Weather = weather,
            IsWet = wet,
            TrackTempC = wet ? 16 + seed % 5 : 26 + seed % 7,
            AirTempC = wet ? 13 + seed % 4 : 20 + seed % 5,
            WindMs = Math.Round((wet ? 3.5 : 1.5) + seed % 4 * 0.4, 1),
            HumidityPct = wet ? 80 + seed % 15 : 30 + seed % 20,
            FuelPct = 100,
        };
    }

    /// <summary>Pulls the "X.XXX km" lap distance out of the briefing's circuit caption, or null.</summary>
    private static double? ParseKm(string? caption)
    {
        var match = System.Text.RegularExpressions.Regex.Match(caption ?? "", @"([\d.]+)\s*km");
        return match.Success && double.TryParse(match.Groups[1].Value,
            System.Globalization.CultureInfo.InvariantCulture, out double km) ? km : null;
    }

    /// <summary>Gate every newly-created race editor, including no-qualifying and subsequent-race
    /// weekend paths. An already-created editor is reopened directly (confirm-back/briefing toggles).</summary>
    private void ShowRaceIntroOrEntry()
    {
        if (_resultEntry is not null)
        {
            CurrentContent = _resultEntry;
            return;
        }

        ShowSessionIntro(
            SessionIntroKind.Race,
            WeekendRaceCount > 1 ? WeekendRaces?[CurrentRaceIndex].Label : null,
            ShowRaceEntry);
    }

    private void ShowSessionIntro(SessionIntroKind kind, string? sessionLabel, Action onContinue)
    {
        if (_sessionIntro is { } existing && existing.Kind == kind)
        {
            CurrentContent = existing;
            return;
        }

        SessionIntroViewModel? intro = null;
        intro = new SessionIntroViewModel(kind, BuildSessionIntroSubtitle(sessionLabel), () =>
        {
            if (ReferenceEquals(_sessionIntro, intro))
                _sessionIntro = null;
            onContinue();
        });
        _sessionIntro = intro;
        CurrentContent = intro;
    }

    private string BuildSessionIntroSubtitle(string? sessionLabel)
    {
        string venue = string.IsNullOrWhiteSpace(Briefing.VenueDisplayName)
            ? Summary.SeriesName
            : Briefing.VenueDisplayName;
        string round = $"Round {Summary.CurrentRound} of {Summary.RoundCount}";
        return string.IsNullOrWhiteSpace(sessionLabel)
            ? $"{venue}  ·  {round}"
            : $"{venue}  ·  {sessionLabel}  ·  {round}";
    }

    /// <summary>Show the race result entry — the shipped flow, now seeded pole-first from any
    /// captured qualifying order (a single-race round leaves the grid untouched).</summary>
    private void ShowRaceEntry()
    {
        if (_resultEntry is null)
        {
            var grid = _session.CurrentGrid();
            if (grid.Count == 0)
            {
                ContentError = "This round has no grid to score.";
                CurrentContent = Briefing;
                return;
            }
            _resultEntry = new ResultEntryViewModel(
                OrderByQualifying(grid, _capturedQualifyingOrder), Summary.PlayerDriverId, _clock)
            {
                // Name the race only on a two-race weekend (Feature/Sprint); a single race keeps
                // the null label, so its screen is byte-identical to the shipped loop.
                SessionLabel = WeekendRaceCount > 1 ? WeekendRaces?[CurrentRaceIndex].Label : null,
                // Surface where the named SMGP rival finishes as the order is entered (null = no rival).
                RivalDriverId = Briefing.NamedSmgpRival?.DriverId,
                RivalName = Briefing.NamedSmgpRival?.DriverName,
                RivalPronouns = Briefing.NamedSmgpRival?.Pronouns ?? Companion.Core.Smgp.SmgpPronouns.Default,
                // Prefill the slider prompt with the pace-anchor recommendation (the same
                // value the briefing showed); before the anchor calibrates, the settings
                // screen's default difficulty (neutral 100 out of the box).
                SliderUsed = _session.CurrentSliderRecommendation()
                    ?? _settings?.Current.DefaultDifficulty
                    ?? ResultEntryViewModel.NeutralSlider,
                // Progression-v2 conditional player-car physics is decided before AMS2 staging.
                // Mirror that persisted/pre-filled fact here; BuildEnvelope rejects any later
                // contradiction so the result screen can never retroactively change car physics.
                IsWet = _session.CurrentRoundIsWet() ?? false,
            };
            _resultEntry.PropertyChanged += OnResultEntryPropertyChanged;
            ConfirmResultCommand.NotifyCanExecuteChanged();
        }
        CurrentContent = _resultEntry;
    }

    /// <summary>Orders the race grid pole-first by a captured qualifying order (Increment 2b.3):
    /// seats named in the qualifying order lead in that order; any seat the order omits keeps grid
    /// order behind them. A null/empty order (single-race round) returns the grid unchanged, so the
    /// result screen is byte-identical to the shipped loop.</summary>
    private static IReadOnlyList<GridSeat> OrderByQualifying(
        IReadOnlyList<GridSeat> grid, IReadOnlyList<string>? qualifyingOrder)
    {
        if (qualifyingOrder is null || qualifyingOrder.Count == 0)
            return grid;

        var rank = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < qualifyingOrder.Count; i++)
            rank.TryAdd(qualifyingOrder[i], i);

        return grid
            .Select((seat, index) => (seat, index))
            .OrderBy(t => rank.TryGetValue(t.seat.DriverId, out int r) ? r : int.MaxValue)
            .ThenBy(t => t.index)
            .Select(t => t.seat)
            .ToArray();
    }

    private bool CanConfirmResult =>
        CurrentContent is ResultEntryViewModel { IsComplete: true } or StartingGridViewModel or RivalScreenViewModel;

    /// <summary>The result-entry primary action. On the qualifying step it locks the entered grid
    /// (no scoring) and advances to the race; on the race step it scores the draft into the Confirm
    /// interstitial without committing. The captured qualifying order rides on the race draft.</summary>
    [RelayCommand(CanExecute = nameof(CanConfirmResult))]
    private void ConfirmResult()
    {
        // Rival step: the player has named (or declined) their rival — continue to qualifying/race.
        if (IsRivalStep)
        {
            _rivalStepDone = true;
            EnterResult();
            return;
        }

        // Starting-grid step: the player has looked at the grid — go racing.
        if (IsStartingGridState)
        {
            ShowRaceIntroOrEntry();
            return;
        }

        // Qualifying step: lock the grid, hold the order, show the starting grid before the race.
        if (IsQualifyingStep && _qualifyingEntry is { IsComplete: true } qualifying)
        {
            _capturedQualifyingOrder = qualifying.BuildDraft().Classified;
            _qualifyingEntry.PropertyChanged -= OnResultEntryPropertyChanged;
            _qualifyingEntry = null;
            ContentError = null;
            ShowStartingGrid();
            return;
        }

        if (_resultEntry is not { IsComplete: true } entry)
            return;

        // Two-race weekend (Increment 2e.3): a race that isn't the round's last locks its result
        // and advances to the next race (no scoring yet). The last (or only) race scores the round.
        if (!IsLastRace)
        {
            _capturedRaces.Add(entry.BuildDraft());
            _resultEntry.PropertyChanged -= OnResultEntryPropertyChanged;
            _resultEntry = null;
            ContentError = null;
            ShowRaceIntroOrEntry();
            return;
        }

        var draft = BuildWeekendDraft(entry);
        ConfirmModel model;
        try
        {
            model = _session.Preview(draft);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            ContentError = ex.Message;
            return;
        }

        ContentError = null;
        CurrentContent = new ConfirmViewModel(
            model,
            onApply: () => ApplyDraft(draft),
            onBack: () => CurrentContent = _resultEntry,
            displayName: PlayerAwareNames(),
            minimalNarrative: _settings?.Current.MinimalNarrative ?? false);
    }

    /// <summary>A driver-id → display-name resolver that knows the PLAYER: an SMGP clean-swap player
    /// is a synthetic driver ("driver.player-entrant") absent from the pack, so the pack resolver
    /// would echo the raw id in the round's points / movements. This maps the player's id to their
    /// name (character name, else "You") and defers to the pack resolver for everyone else.</summary>
    private Func<string, string> PlayerAwareNames()
    {
        var packNames = PackDisplayNames.ResolverFor(_session.Pack);
        var player = _session.PlayerIdentity();
        return id => player is { } p && string.Equals(id, p.DriverId, StringComparison.Ordinal)
            ? p.DisplayName
            : packNames(id);
    }

    /// <summary>Assembles the round's draft from the captured races (locked) plus the final race
    /// entry: race 0 is the primary classification, races 1… become <see cref="ResultDraft.AdditionalRaces"/>,
    /// and the captured qualifying order rides along. A single race yields exactly today's draft
    /// (no additional races), so the shipped loop is byte-identical.</summary>
    private ResultDraft BuildWeekendDraft(ResultEntryViewModel lastRace)
    {
        var races = _capturedRaces.Append(lastRace.BuildDraft()).ToList();
        return races[0] with
        {
            QualifyingOrder = _capturedQualifyingOrder,
            // The Setup Gamble called at the briefing (pre-race) rides the round's raw envelope.
            CalledShot = Briefing.CalledShot,
            // The SMGP rival named at the briefing (null outside the mode / no rival) rides the
            // same way — the fold derives the battle from the stored result.
            SmgpRival = Briefing.BuildSmgpRival(),
            AdditionalRaces = races.Count > 1
                ? races.Skip(1).Select(r => new ExtraRaceResult
                {
                    Classified = r.Classified,
                    DidNotFinish = r.DidNotFinish,
                    Disqualified = r.Disqualified,
                }).ToList()
                : null,
        };
    }

    private void ApplyDraft(ResultDraft draft)
    {
        // Captured BEFORE the fold so a forced demotion this round (a seat move with no pending
        // offer) can be detected by the team changing. Null for every non-SMGP career.
        string? smgpTeamBefore = _session.CurrentSmgpTeamId();
        int appliedRound = Summary.CurrentRound;

        try
        {
            _session.Apply(draft);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException)
        {
            ContentError = ex.Message;
            return;
        }

        ClearRoundEntryState();

        // Character death & injury (Slice 3): a fatal accident ENDS the career. For a Hardcore death the
        // session's DB is already disposed and the file deleted, so we must NOT touch Summary/Briefing/
        // commands (they query the DB) — hand off to the death screen from the DB-FREE mortality status
        // instead. Normal death keeps the file (the death screen offers a restore). Reads the guarded
        // PlayerMortality(), never the DB, so it is safe even after the file is gone.
        var mortality = _session.PlayerMortality();
        if (mortality.Deceased || mortality.CareerFileDeleted)
        {
            CareerOver = mortality;
            // The richer obituary/record model rides alongside — also DB-free after a Hardcore death
            // (captured pre-deletion), so this is safe even once the file is gone.
            DeathScreen = _session.DeathScreen();
            return;
        }

        // Progression feedback where it happens: what THIS round did to XP/level/Skill Points, read
        // from the fold's own journaled audit row. Placed AFTER the mortality hand-off above — a
        // Hardcore death has already disposed the DB, and this read queries it. Null for a
        // character-free career.
        LastProgression = _session.RoundProgression(appliedRound);

        Summary = _session.Summary;
        Briefing.Refresh();
        // SmgpCareerOver is projected by the refreshed briefing rather than Home's mortality property.
        // Relay the combined predicate before command CanExecute is refreshed below.
        OnPropertyChanged(nameof(IsCareerTerminal));

        // SMGP promotion / demotion (3c-3): a seat change this round gets its own full-immersion
        // screen BEFORE the round advances — a pending two-wins offer to accept/decline, or a forced
        // relegation to acknowledge. Season end is held too until an offer resolves (3c-2). A
        // non-SMGP / character-free career yields null for both, so the shipped loop is unchanged.
        var promotion = _session.CurrentSmgpPromotion() ?? _session.CurrentSmgpDemotion(smgpTeamBefore);
        if (promotion is not null)
        {
            ShowPromotion(promotion);
            RefreshRoundCommands();
            return;
        }

        AdvanceAfterRound();
        RefreshRoundCommands();
    }

    /// <summary>Tears down the round's entry state (result / qualifying / grid / rival) once its
    /// result is applied — the qualifying order + captured races were consumed by the fold.</summary>
    private void ClearRoundEntryState()
    {
        if (_resultEntry is not null)
        {
            _resultEntry.PropertyChanged -= OnResultEntryPropertyChanged;
            _resultEntry = null;
        }
        if (_qualifyingEntry is not null)
        {
            _qualifyingEntry.PropertyChanged -= OnResultEntryPropertyChanged;
            _qualifyingEntry = null;
        }
        _capturedQualifyingOrder = null;
        _capturedRaces.Clear();
        _startingGrid = null;
        _rivalScreen = null;
        _sessionIntro = null;
        _rivalStepDone = false;
    }

    /// <summary>Move on from a finished round — the final standings review when the season is done,
    /// else the next round's briefing.</summary>
    private void AdvanceAfterRound()
    {
        _promotion = null;
        if (Summary.SeasonComplete)
        {
            // The 17-season SMGP campaign FINALE (Mike's "final final screen"): when the season that
            // just completed is a beaten campaign summit, show the locked special.jpg / ultimate.jpg
            // celebration ONCE before the review. Display-only (SmgpFinale() is a pure read — no fold,
            // no journal). Its Continue callback goes straight to the review, so it never re-enters
            // here. Null for every non-summit season, so the shipped end-of-season flow is unchanged.
            if (_finale is null && _session.SmgpFinale() is { } finale)
            {
                ShowFinale(finale);
                return;
            }
            ShowSeasonReview();
        }
        else if (_session.CurrentSitOut() is { } sitOut)
            // The next round is one the injured player must sit out — go straight to the auto-sim screen
            // (its Continue folds it and re-advances), never the briefing/manual entry. (§5.)
            ShowSitOut(sitOut);
        else
            CurrentContent = Briefing;
    }

    /// <summary>Builds the injured sit-out screen: its Continue folds the auto-simulated round and
    /// advances (chaining to the next sit-out, the briefing, or the season review). (§5.)</summary>
    private SitOutViewModel MakeSitOut(SitOutStatus status) =>
        new(status, AutoSimulateInjuredRound);

    private void ShowSitOut(SitOutStatus status)
    {
        ContentError = null;
        CurrentContent = MakeSitOut(status);
    }

    /// <summary>Fold the round the injured player sat out (the AI field is auto-simulated, the player is
    /// DNS — OPI-neutral), heal one race of a minor suspension, and advance. No manual result is entered.</summary>
    private void AutoSimulateInjuredRound()
    {
        try
        {
            _session.AutoSimulateRound();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException)
        {
            ContentError = ex.Message;
            return;
        }

        ClearRoundEntryState();
        Summary = _session.Summary; // an auto-sim never kills, so the DB is always live here
        Briefing.Refresh();
        AdvanceAfterRound();
        RefreshRoundCommands();
    }

    /// <summary>Show the 17-season campaign finale (Mike's "final final screen") — its own step before
    /// the final season review. Continue acknowledges it and advances into the review. The finale is
    /// a pure display projection, so no <c>ICareerSession</c> write happens here (unlike the promotion
    /// screen, which journals the offer decision).</summary>
    private void ShowFinale(SmgpFinaleModel model)
    {
        ContentError = null;
        _finale = new SmgpFinaleViewModel(
            model,
            onContinue: () =>
            {
                _finale = null;
                ShowSeasonReview();
                RefreshRoundCommands();
            });
        CurrentContent = _finale;
    }

    private void RefreshRoundCommands()
    {
        ShowBriefingCommand.NotifyCanExecuteChanged();
        EnterResultCommand.NotifyCanExecuteChanged();
        ConfirmResultCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Show the SMGP promotion / demotion screen (3c-3) — its own step after the confirm. Its
    /// buttons resolve the offer (or acknowledge the drop) and then advance the round.</summary>
    private void ShowPromotion(SmgpPromotionModel model)
    {
        ContentError = null;
        _promotion = new PromotionViewModel(
            model,
            onAccept: () => ResolvePromotion(model, accept: true),
            onDecline: () => ResolvePromotion(model, accept: false));
        CurrentContent = _promotion;
    }

    /// <summary>Answer the promotion screen: a two-wins offer commits the accept/decline to the fold
    /// (holding season end until now — 3c-2); a demotion is acknowledge-only. Then advance the round.</summary>
    private void ResolvePromotion(SmgpPromotionModel model, bool accept)
    {
        if (model.Kind == SmgpPromotionKind.PromotionOffer)
        {
            try
            {
                _session.ResolveSmgpOffer(accept);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException)
            {
                ContentError = ex.Message;
                return;
            }
        }

        Summary = _session.Summary;
        // Taking the seat moves the player's team/car but does NOT change the CareerSummary value
        // (same round, same season-start livery), so the observable setter above no-ops and the hub
        // never re-projects. Force the notification the hub listens on so the Driver + Skins lenses
        // pick up the new team (else they keep showing the pre-promotion seat).
        OnPropertyChanged(nameof(Summary));
        Briefing.Refresh();
        AdvanceAfterRound();
        RefreshRoundCommands();
    }

    [RelayCommand]
    private void ShowStandings()
    {
        ContentError = null;
        CurrentContent = NewStandings();
    }

    /// <summary>Standings with the settings seam attached: column visibility and the
    /// selected tab persist across openings (and across sessions).</summary>
    private StandingsViewModel NewStandings() =>
        new(_session.AllSnapshots(), _session.Pack, _settings, _session);

    /// <summary>Standings → back to whatever the round was doing (briefing, or the
    /// in-progress result entry); season review stays on the final standings.</summary>
    [RelayCommand]
    private void BackToRound()
    {
        ContentError = null;
        if (Summary.SeasonComplete)
        {
            ShowSeasonReview();
        }
        else if (_resultEntry is not null)
        {
            CurrentContent = _resultEntry;
        }
        else if (_qualifyingEntry is not null)
        {
            CurrentContent = _qualifyingEntry; // mid-qualifying: back to the grid entry, not the briefing
        }
        else if (_sessionIntro is not null)
        {
            CurrentContent = _sessionIntro;
        }
        else if (_startingGrid is not null)
        {
            CurrentContent = _startingGrid;
        }
        else if (_rivalScreen is not null && !_rivalStepDone)
        {
            CurrentContent = _rivalScreen;
        }
        else
        {
            Briefing.Refresh();
            CurrentContent = Briefing;
        }
    }

    /// <summary>Raised after the review's sign-and-continue persisted the next season: the
    /// session (and this Home) now point at the FINISHED season, so the shell must reopen
    /// the career file — it lands in the new season's round 1 briefing.</summary>
    public event EventHandler? NextSeasonStarted;

    /// <summary>Season completion navigates HERE: the review + offers screen (final
    /// standings, journal digest, offer letters, NAMeS restore, era sign-and-continue).</summary>
    private void ShowSeasonReview()
    {
        var review = new SeasonReviewViewModel(_session);
        review.SeasonSigned += (_, _) => NextSeasonStarted?.Invoke(this, EventArgs.Empty);
        CurrentContent = review;
    }

    /// <summary>Home's share of the shell-level Esc (non-destructive back only): standings →
    /// back to the round in progress; confirm → back to the result entry (the draft
    /// survives). Briefing, result entry (the grammar owns the keyboard there) and the
    /// season review have no "back" — Esc does nothing.</summary>
    public bool TryEscapeBack()
    {
        switch (CurrentContent)
        {
            case StandingsViewModel when !Summary.SeasonComplete:
                BackToRound();
                return true;

            case ConfirmViewModel confirm:
                confirm.BackCommand.Execute(null);
                return true;

            default:
                return false;
        }
    }

    private void OnResultEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) ||
            e.PropertyName == nameof(ResultEntryViewModel.IsComplete))
        {
            ConfirmResultCommand.NotifyCanExecuteChanged();
        }
    }

    public void Dispose()
    {
        if (_resultEntry is not null)
            _resultEntry.PropertyChanged -= OnResultEntryPropertyChanged;
        if (_qualifyingEntry is not null)
            _qualifyingEntry.PropertyChanged -= OnResultEntryPropertyChanged;
        (_watcher as IDisposable)?.Dispose();
        (_session as IDisposable)?.Dispose();
    }
}
