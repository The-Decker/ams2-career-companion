using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Companion.ViewModels.Services;

namespace Companion.ViewModels.Briefing;

/// <summary>How the staging outcome banner should read: green for staged/no-op, amber for
/// the expected community-file force gate (an informational choice, not a failure), red
/// only for real failures (preflight errors, no install, IO trouble).</summary>
public enum StageBannerTone
{
    None,
    Success,
    Info,
    Error,
}

/// <summary>
/// The Race Day briefing screen (ux-round contract, corrected briefing section): the setup
/// guide as a manual CHECK-OFF CHECKLIST — AMS2's custom-race settings are arrow-steppers,
/// nothing can be pasted, so every setting is one tickable row with a big glanceable value,
/// ordered to match the in-game custom-race screen flow (track → class → opponents → laps →
/// date → start time → weather slots → time progression → pit rules), with an "N of M set"
/// progress line. Tick state is keyed to the ROUND NUMBER and session-scoped: it survives
/// navigation within the career session and resets when the round advances (not persisted
/// across app restarts — v1). Per-row copy buttons are gone; one Copy summary action shares
/// the whole checklist outside the game via <see cref="CopyRequested"/> (the viewmodel stays
/// WPF-free — the view owns the real clipboard). <see cref="CompactChecklistOpen"/> drives
/// the App-layer always-on-top mini checklist window bound to this same instance.
/// Staging (backup-first outcome banner, force escape hatch, external-modification watcher)
/// is unchanged.
/// </summary>
public sealed partial class BriefingViewModel : ObservableObject
{
    private readonly ICareerSession _session;
    private readonly IFileWatcher? _watcher;

    /// <summary>Ticked labels per round number — the per-round reset rule falls out of the
    /// keying: a new round has no entry, so its checklist starts blank.</summary>
    private readonly Dictionary<int, HashSet<string>> _ticksByRound = [];

    private int _currentRoundNumber;

    public BriefingViewModel(ICareerSession session, IFileWatcher? stagedFileWatcher = null)
    {
        _session = session;
        _watcher = stagedFileWatcher;
        if (_watcher is not null)
            _watcher.Changed += OnWatchedFileChanged;
        Refresh();
    }

    /// <summary>Raised with the exact text the view should copy to the clipboard
    /// (the composed checklist summary).</summary>
    public event EventHandler<string>? CopyRequested;

    // ---------- briefing content ----------

    [ObservableProperty]
    private BriefingModel? _briefing;

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _venueDisplayName = "";

    /// <summary>The round's AMS2 track id (from the pack round's track ref), used to resolve the
    /// optional track-layout thumbnail (data/ams2/track-art/&lt;trackId&gt;). Empty when no round.</summary>
    [ObservableProperty]
    private string _trackId = "";

    [ObservableProperty]
    private bool _isPlaceholder;

    [ObservableProperty]
    private string? _setupNotes;

    /// <summary>The difficulty recommendation line (m5-fix-integration "App wiring"): the
    /// Opponent Skill the pace anchor suggests for this round. Null before calibration —
    /// the view falls back to the generic fixed-difficulty note.</summary>
    [ObservableProperty]
    private string? _difficultyRecommendation;

    /// <summary>True once every round has an applied result — there is nothing to brief.</summary>
    public bool SeasonComplete => Briefing is null;

    // ---------- Setup Gamble: the pre-race called shot (4b) ----------

    /// <summary>The sim's expected finish for this round (from the resolved grid), the yardstick the
    /// gamble is called against. Null when the player has no seat this round — then no gamble.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGamble), nameof(CalledShotSummary))]
    private int? _expectedFinish;

    /// <summary>The number of cars on this round's grid — the safe end of the call range.</summary>
    private int _gridSize;

    /// <summary>The finish the player has called (1-based), or null for no bet. Rides the round's raw
    /// envelope when the result is applied and is resolved by the fold only when it is a real gamble
    /// (bolder than <see cref="ExpectedFinish"/>). Reset each round.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCalledShot), nameof(CalledShotSummary))]
    private int? _calledShot;

    /// <summary>True when a gamble can be offered at all: the player has a seat and the expectation
    /// leaves room to call something bolder (you cannot out-call an expected pole).</summary>
    public bool CanGamble => !SeasonComplete && ExpectedFinish is > 1;

    /// <summary>True when the player has committed a call this round.</summary>
    public bool HasCalledShot => CalledShot is not null;

    /// <summary>One legible line describing the current call and its reputation stake — what the view
    /// shows so the gamble is never a silent no-op.</summary>
    public string CalledShotSummary
    {
        get
        {
            if (ExpectedFinish is not { } expected)
                return "";
            if (CalledShot is not { } called)
                return $"The sim expects you around P{expected}. Call a bolder finish to stake reputation on it.";
            if (!Companion.Core.Career.CalledShotMath.IsGamble(called, expected))
                return $"P{called} isn't a gamble — call better than P{expected} to put reputation on the line.";
            double stake = Companion.Core.Career.CalledShotMath.Stake(called, expected);
            return $"Called P{called}: staking {stake:0.#} reputation — hit it for +{stake:0.#}, miss for −{stake:0.#}.";
        }
    }

    /// <summary>Call a bolder finish (a lower P number). From no call, starts one place better than
    /// the expected finish — the least-ambitious real gamble. Clamped at P1.</summary>
    [RelayCommand]
    private void CallBolder()
    {
        if (ExpectedFinish is not { } expected || expected <= 1)
            return;
        CalledShot = CalledShot is { } c ? Math.Max(1, c - 1) : expected - 1;
    }

    /// <summary>Ease the call one place (a higher P number). Past the expected finish it stops being a
    /// gamble; drop it entirely with <see cref="ClearCallCommand"/>.</summary>
    [RelayCommand]
    private void CallSafer()
    {
        if (CalledShot is not { } c)
            return;
        CalledShot = Math.Min(_gridSize > 0 ? _gridSize : c + 1, c + 1);
    }

    /// <summary>Withdraw the bet — no gamble this round.</summary>
    [RelayCommand]
    private void ClearCall() => CalledShot = null;

    /// <summary>The check-off rows, in in-game custom-race screen order.</summary>
    public ObservableCollection<BriefingChecklistItem> Settings { get; } = [];

    /// <summary>Re-reads the current round's briefing from the session (call after Apply).
    /// Rebuilds the checklist and restores this round's ticks, if any.</summary>
    public void Refresh()
    {
        Briefing = _session.CurrentBriefing();

        foreach (var old in Settings)
            old.PropertyChanged -= OnChecklistItemChanged;
        Settings.Clear();

        if (Briefing is { } briefing)
        {
            _currentRoundNumber = briefing.Round.Round;
            var ticked = _ticksByRound.GetValueOrDefault(_currentRoundNumber);
            foreach (var setting in briefing.Settings.OrderBy(s => InGameOrderRank(s.Label)))
            {
                var item = new BriefingChecklistItem(setting.Label, setting.Value)
                {
                    IsChecked = ticked?.Contains(setting.Label) == true,
                };
                item.PropertyChanged += OnChecklistItemChanged;
                Settings.Add(item);
            }

            Title = BriefingComposer.ComposeTitle(briefing);
            VenueDisplayName = briefing.VenueDisplayName;
            TrackId = briefing.Round.Track.Id;
            IsPlaceholder = briefing.IsPlaceholder;
            SetupNotes = briefing.SetupNotes;
            DifficultyRecommendation = briefing.RecommendedSlider is { } slider
                ? $"Recommended Opponent Skill: {slider}% — calibrated from your pace so far (never auto-applied)."
                : null;

            // Setup Gamble: a fresh round starts with no bet; expose the expectation the call is made
            // against (and the grid size that bounds a safe call).
            ExpectedFinish = _session.CurrentExpectedFinish();
            _gridSize = _session.CurrentGrid().Count;
            CalledShot = null;
        }
        else
        {
            _currentRoundNumber = 0;
            Title = "";
            VenueDisplayName = "";
            TrackId = "";
            IsPlaceholder = false;
            SetupNotes = null;
            DifficultyRecommendation = null;
            ExpectedFinish = null;
            _gridSize = 0;
            CalledShot = null;
            CompactChecklistOpen = false; // nothing left to tick — close the overlay
        }
        OnPropertyChanged(nameof(SeasonComplete));
        OnPropertyChanged(nameof(CanGamble));
        OnPropertyChanged(nameof(CalledShotSummary));
        RaiseProgressChanged();
    }

    /// <summary>The in-game custom-race screen flow (ux-round contract): track → class →
    /// opponents → laps → date → start time → weather slots → time progression → pit rules.
    /// The sort is stable, so "Weather slot 1..n" keep their relative order.</summary>
    internal static int InGameOrderRank(string label) => label switch
    {
        "Track" => 0,
        "Class" => 1,
        "Opponents" => 2,
        "Laps" => 3,
        "Date" => 4,
        "Start time" => 5,
        _ when label.StartsWith("Weather slot", StringComparison.Ordinal) => 6,
        "Time progression" => 7,
        "Mandatory pit stop" => 8,
        _ => 9,
    };

    // ---------- checklist ticks & progress ----------

    /// <summary>The "N of M set" progress line.</summary>
    public string ChecklistProgressText =>
        $"{Settings.Count(i => i.IsChecked)} of {Settings.Count} set";

    /// <summary>True when every row is ticked — the view flips the progress chip green.</summary>
    public bool AllSet => Settings.Count > 0 && Settings.All(i => i.IsChecked);

    /// <summary>Row-click toggle (the whole row is the hit target, not just the checkbox).</summary>
    [RelayCommand]
    private void ToggleItem(BriefingChecklistItem? item) => item?.Toggle();

    private void OnChecklistItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BriefingChecklistItem.IsChecked) ||
            sender is not BriefingChecklistItem item)
        {
            return;
        }

        if (!_ticksByRound.TryGetValue(_currentRoundNumber, out var ticked))
            _ticksByRound[_currentRoundNumber] = ticked = new HashSet<string>(StringComparer.Ordinal);
        if (item.IsChecked)
            ticked.Add(item.Label);
        else
            ticked.Remove(item.Label);

        RaiseProgressChanged();
    }

    private void RaiseProgressChanged()
    {
        OnPropertyChanged(nameof(ChecklistProgressText));
        OnPropertyChanged(nameof(AllSet));
    }

    // ---------- copy summary (the one remaining copy action — for sharing, not pasting) ----------

    [RelayCommand]
    private void CopySummary() => CopyRequested?.Invoke(this, ComposeSummary());

    /// <summary>Title + every "Label: Value" line in checklist order (+ setup notes).</summary>
    public string ComposeSummary()
    {
        var text = new StringBuilder();
        if (Title.Length > 0)
            text.AppendLine(Title);
        foreach (var item in Settings)
            text.AppendLine($"{item.Label}: {item.Value}");
        if (SetupNotes is { Length: > 0 } notes)
        {
            text.AppendLine();
            text.AppendLine(notes);
        }
        return text.ToString().TrimEnd();
    }

    // ---------- compact always-on-top mode ----------

    /// <summary>True while the small floating checklist window is open. The App layer
    /// observes this and opens/closes a Topmost window bound to this same viewmodel, so the
    /// user can tick settings off while clicking through AMS2 in windowed/borderless mode.</summary>
    [ObservableProperty]
    private bool _compactChecklistOpen;

    [RelayCommand]
    private void ToggleCompactChecklist() => CompactChecklistOpen = !CompactChecklistOpen;

    // ---------- staging ----------

    [ObservableProperty]
    private StageOutcome? _lastStageOutcome;

    [ObservableProperty]
    private bool _stageSucceeded;

    [ObservableProperty]
    private string _stageBanner = "";

    /// <summary>⚠ state: the staged XML changed on disk after we wrote it (the user or
    /// another tool touched it) — re-stage before racing.</summary>
    [ObservableProperty]
    private bool _stagedFileTouchedExternally;

    public IReadOnlyList<string> StageMessages => LastStageOutcome?.Messages ?? [];

    /// <summary>The banner color the view renders: green (staged or no-op match), amber for
    /// the expected community-file force gate, red only for real failures.</summary>
    public StageBannerTone BannerTone => LastStageOutcome switch
    {
        null => StageBannerTone.None,
        { Success: true } => StageBannerTone.Success,
        { BlockedByForceGate: true } => StageBannerTone.Info,
        _ => StageBannerTone.Error,
    };

    /// <summary>Per-file detail lines (e.g. the livery scan's unreadable files) behind the
    /// aggregate <see cref="StageMessages"/> — collapsed by default.</summary>
    public IReadOnlyList<string> StageDetails => LastStageOutcome?.Details ?? [];

    public bool HasStageDetails => StageDetails.Count > 0;

    /// <summary>The expander state for <see cref="StageDetails"/>; resets to collapsed on
    /// every new staging outcome.</summary>
    [ObservableProperty]
    private bool _stageDetailsExpanded;

    [RelayCommand]
    private void ToggleStageDetails() => StageDetailsExpanded = !StageDetailsExpanded;

    /// <summary>True when the session supports the explicit force-stage escape hatch (staging
    /// over a file the app did not generate, e.g. a curated community NAMeS file).</summary>
    public bool CanForceStage => _session is IForceStaging;

    [RelayCommand]
    private void StageGrid() => RunStage(force: false);

    [RelayCommand]
    private void ForceStageGrid()
    {
        if (_session is IForceStaging forceStaging)
            ApplyOutcome(forceStaging.StageCurrentGrid(force: true));
    }

    private void RunStage(bool force)
    {
        if (force && _session is IForceStaging forceStaging)
            ApplyOutcome(forceStaging.StageCurrentGrid(force: true));
        else
            ApplyOutcome(_session.StageCurrentGrid());
    }

    private void ApplyOutcome(StageOutcome outcome)
    {
        LastStageOutcome = outcome;
        StageSucceeded = outcome.Success;
        StagedFileTouchedExternally = false;
        StageDetailsExpanded = false;
        StageBanner = ComposeBanner(outcome);
        OnPropertyChanged(nameof(StageMessages));
        OnPropertyChanged(nameof(BannerTone));
        OnPropertyChanged(nameof(StageDetails));
        OnPropertyChanged(nameof(HasStageDetails));

        if (outcome.Success && outcome.WrittenPath is { Length: > 0 } path)
            _watcher?.Watch(path);
        else
            _watcher?.Stop();
    }

    /// <summary>Always states which of the staging outcomes happened: no-op (installed file
    /// already matches) / staged (with backup path) / gated behind the explicit Stage-anyway
    /// choice (informational, amber) / aborted (red).</summary>
    private static string ComposeBanner(StageOutcome outcome)
    {
        if (outcome.BlockedByForceGate)
            // Not a failure — a deliberate safety pause. Tell the user exactly what to click and
            // that nothing is at risk, so the community-file gate never reads as an error.
            return "Your installed AI is a community file, so the app only overwrites it when you " +
                   "confirm — click “Overwrite anyway (backup first)” to set up this race. A timestamped " +
                   "backup is taken first, so nothing is lost.";

        if (!outcome.Success)
            return outcome.Messages.Count > 0
                ? $"Couldn't set up the race — {outcome.Messages[^1]}"
                : "Couldn't set up the race.";

        if (outcome.NoOpAlreadyMatches)
            return "✔ AMS2 is already set up for this race — your installed drivers + skins match, nothing to change.";

        // Surface WHAT was done (this race's drivers written, its skins activated, any bubble-car swap,
        // the base-game fallback) so each reason is visible, then where it went + the backup + what next.
        var lines = new List<string> { "✔ AMS2 is set up for this race." };
        lines.AddRange(outcome.Messages);
        if (outcome.WrittenPath is { Length: > 0 } written)
            lines.Add($"Written into your live AMS2 file: {written}.");
        lines.Add(outcome.BackupPath is { Length: > 0 } backup
            ? $"Your previous file was backed up to {backup} — a safety copy AMS2 ignores; season-end can restore it."
            : "No previous file existed, so nothing was backed up.");
        lines.Add("Close AMS2 first if it's open, then launch and race.");
        return string.Join("\n\n", lines);
    }

    private void OnWatchedFileChanged(object? sender, string path)
    {
        if (LastStageOutcome is { Success: true, WrittenPath: { } written } &&
            string.Equals(path, written, StringComparison.OrdinalIgnoreCase))
        {
            StagedFileTouchedExternally = true;
        }
    }
}
