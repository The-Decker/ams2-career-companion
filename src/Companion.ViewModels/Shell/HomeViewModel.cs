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

        if (_summary.SeasonComplete)
            ShowSeasonReview();
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
        nameof(IsSeasonReview), nameof(FormText), nameof(HasForm))]
    private CareerSummary _summary;

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
        nameof(IsQualifyingStep), nameof(ConfirmButtonText))]
    private ObservableObject? _currentContent;

    [ObservableProperty]
    private string? _contentError;

    public bool IsBriefingState => CurrentContent is BriefingViewModel;
    public bool IsResultEntryState => CurrentContent is ResultEntryViewModel;
    public bool IsConfirmState => CurrentContent is ConfirmViewModel;
    public bool IsStandingsState => CurrentContent is StandingsViewModel;
    public bool IsSeasonReviewState => CurrentContent is SeasonReviewViewModel;

    /// <summary>True while the CURRENT content is the weekend qualifying-order step (not the race
    /// result) — both reuse the result-entry grammar, so this drives the primary action's label
    /// and the "which step am I on" cues. Always false on single-race rounds.</summary>
    public bool IsQualifyingStep => _qualifyingEntry is not null
        && ReferenceEquals(CurrentContent, _qualifyingEntry);

    /// <summary>The primary confirm button's label: the qualifying step locks the grid; a race that
    /// is not the round's last advances to the next race; the last (or only) race scores the round.</summary>
    public string ConfirmButtonText =>
        IsQualifyingStep ? "Set the grid  ⏎"
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

    private bool RoundInProgress => !Summary.SeasonComplete;

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

        // Weekend qualifying step (Increment 2b.3): on a round whose weekend declares a qualifying
        // session, capture the grid order (pole first) BEFORE the race — once per round. A round
        // with no weekend / no qualifying skips straight to the race, so the shipped single-race
        // loop is byte-identical.
        if (QualifyingSession is not null && _capturedQualifyingOrder is null)
        {
            ShowQualifyingEntry();
            return;
        }

        ShowRaceEntry();
    }

    /// <summary>The current round's qualifying session when its weekend declares one present; null
    /// on a single-race round (the byte-identical default — every bundled pack).</summary>
    private PackWeekendSession? QualifyingSession =>
        _session.CurrentWeekend()?.Qualifying is { Present: true } qualifying ? qualifying : null;

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
                return;
            }
            _qualifyingEntry = new ResultEntryViewModel(grid, Summary.PlayerDriverId, _clock)
            {
                SessionLabel = QualifyingSession?.Label ?? "Qualifying",
            };
            _qualifyingEntry.PropertyChanged += OnResultEntryPropertyChanged;
            ConfirmResultCommand.NotifyCanExecuteChanged();
        }
        CurrentContent = _qualifyingEntry;
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
                return;
            }
            _resultEntry = new ResultEntryViewModel(
                OrderByQualifying(grid, _capturedQualifyingOrder), Summary.PlayerDriverId, _clock)
            {
                // Name the race only on a two-race weekend (Feature/Sprint); a single race keeps
                // the null label, so its screen is byte-identical to the shipped loop.
                SessionLabel = WeekendRaceCount > 1 ? WeekendRaces?[CurrentRaceIndex].Label : null,
                // Prefill the slider prompt with the pace-anchor recommendation (the same
                // value the briefing showed); before the anchor calibrates, the settings
                // screen's default difficulty (neutral 100 out of the box).
                SliderUsed = _session.CurrentSliderRecommendation()
                    ?? _settings?.Current.DefaultDifficulty
                    ?? ResultEntryViewModel.NeutralSlider,
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

    private bool CanConfirmResult => CurrentContent is ResultEntryViewModel { IsComplete: true };

    /// <summary>The result-entry primary action. On the qualifying step it locks the entered grid
    /// (no scoring) and advances to the race; on the race step it scores the draft into the Confirm
    /// interstitial without committing. The captured qualifying order rides on the race draft.</summary>
    [RelayCommand(CanExecute = nameof(CanConfirmResult))]
    private void ConfirmResult()
    {
        // Qualifying step: lock the grid, hold the order, advance to the race (no confirm screen).
        if (IsQualifyingStep && _qualifyingEntry is { IsComplete: true } qualifying)
        {
            _capturedQualifyingOrder = qualifying.BuildDraft().Classified;
            _qualifyingEntry.PropertyChanged -= OnResultEntryPropertyChanged;
            _qualifyingEntry = null;
            ContentError = null;
            ShowRaceEntry();
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
            ShowRaceEntry();
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
            displayName: PackDisplayNames.ResolverFor(_session.Pack),
            minimalNarrative: _settings?.Current.MinimalNarrative ?? false);
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
        try
        {
            _session.Apply(draft);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException)
        {
            ContentError = ex.Message;
            return;
        }

        if (_resultEntry is not null)
        {
            _resultEntry.PropertyChanged -= OnResultEntryPropertyChanged;
            _resultEntry = null;
        }

        // The weekend's qualifying order + captured races were consumed by this Apply — clear them.
        if (_qualifyingEntry is not null)
        {
            _qualifyingEntry.PropertyChanged -= OnResultEntryPropertyChanged;
            _qualifyingEntry = null;
        }
        _capturedQualifyingOrder = null;
        _capturedRaces.Clear();

        Summary = _session.Summary;
        Briefing.Refresh();

        if (Summary.SeasonComplete)
            ShowSeasonReview();
        else
            CurrentContent = Briefing;

        ShowBriefingCommand.NotifyCanExecuteChanged();
        EnterResultCommand.NotifyCanExecuteChanged();
        ConfirmResultCommand.NotifyCanExecuteChanged();
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
        new(_session.AllSnapshots(), _session.Pack, _settings);

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
