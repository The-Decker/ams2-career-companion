using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
        nameof(HeaderTitle), nameof(RoundText), nameof(StandingText), nameof(IsSeasonReview),
        nameof(FormText), nameof(HasForm))]
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
        nameof(IsConfirmState), nameof(IsStandingsState), nameof(IsSeasonReviewState))]
    private ObservableObject? _currentContent;

    [ObservableProperty]
    private string? _contentError;

    public bool IsBriefingState => CurrentContent is BriefingViewModel;
    public bool IsResultEntryState => CurrentContent is ResultEntryViewModel;
    public bool IsConfirmState => CurrentContent is ConfirmViewModel;
    public bool IsStandingsState => CurrentContent is StandingsViewModel;
    public bool IsSeasonReviewState => CurrentContent is SeasonReviewViewModel;

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
        if (_resultEntry is null)
        {
            var grid = _session.CurrentGrid();
            if (grid.Count == 0)
            {
                ContentError = "This round has no grid to score.";
                return;
            }
            _resultEntry = new ResultEntryViewModel(grid, Summary.PlayerDriverId, _clock)
            {
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

    private bool CanConfirmResult => _resultEntry is { IsComplete: true };

    /// <summary>Result entry → Confirm interstitial: score the draft without committing.</summary>
    [RelayCommand(CanExecute = nameof(CanConfirmResult))]
    private void ConfirmResult()
    {
        if (_resultEntry is not { IsComplete: true } entry)
            return;

        var draft = entry.BuildDraft();
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
            displayName: DriverDisplayName,
            minimalNarrative: _settings?.Current.MinimalNarrative ?? false);
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
        else
        {
            Briefing.Refresh();
            CurrentContent = Briefing;
        }
    }

    /// <summary>Season completion navigates HERE: the review + offers screen (final
    /// standings, journal digest, offer letters, NAMeS restore, era-transition note).</summary>
    private void ShowSeasonReview() =>
        CurrentContent = new SeasonReviewViewModel(_session);

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

    private string DriverDisplayName(string driverId) =>
        _session.Pack.Drivers.FirstOrDefault(d => d.Id == driverId)?.Name ?? driverId;

    public void Dispose()
    {
        if (_resultEntry is not null)
            _resultEntry.PropertyChanged -= OnResultEntryPropertyChanged;
        (_watcher as IDisposable)?.Dispose();
        (_session as IDisposable)?.Dispose();
    }
}
