using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Companion.Core.Grid;
using Companion.ViewModels.Services;

namespace Companion.ViewModels.ResultEntry;

/// <summary>Which outcome the Enter key currently records.</summary>
public enum ResultEntryPhase
{
    /// <summary>Matches assign the next open finishing position.</summary>
    Classified,

    /// <summary>After F8: matches (or bare Enter, in list order) mark DNFs.</summary>
    Dnf,
}

/// <summary>A retired seat with its one-letter reason ("m" mechanical, "a" accident, "o" other)
/// and, for a customised "other", optional free text plus whether the cause is the driver's
/// fault. <see cref="Detail"/> is only ever set alongside reason "o"; marking DNF and editing
/// the reason/detail are independent, separately-undoable steps.</summary>
public sealed record DnfEntry(GridSeat Seat, string Reason, string? Detail = null, bool DriverAttributed = false);

/// <summary>
/// The result-entry keyboard grammar as a pure, WPF-free state machine
/// (docs/dev/app-shell.md "Result-entry keyboard grammar"). One text box
/// (<see cref="Input"/>), one list; the view forwards Enter/Tab/Esc/F8/Ctrl+Z to the
/// commands. Every rule of the grammar lives here so it is unit-testable:
///
/// <list type="bullet">
/// <item>car number (exact, then prefix) or ≥2-letter surname prefix — UNPLACED drivers only;</item>
/// <item>"me" is reserved for the player (never a surname prefix);</item>
/// <item>unambiguous match auto-selects; ambiguous shows <see cref="Candidates"/>, Tab cycles,
///   Enter commits the highlighted candidate;</item>
/// <item>F8 toggles the DNF phase: remaining drivers are the candidates, bare Enter marks them
///   in list order (↵↵↵ bulk works), optional " m"/" a"/" o" reason after the match text;</item>
/// <item>trailing 'q' on a match — DSQ (unplaced or placed; a placed driver is pulled out of
///   the classification);</item>
/// <item>digits after a PLACED driver's match re-position it (penalty), shifting the others;</item>
/// <item>Ctrl+Z unlimited undo across every mutation kind; Esc clears the input;</item>
/// <item>footer: live timer (injectable clock) + "14/26 placed" progress.</item>
/// </list>
/// </summary>
public sealed partial class ResultEntryViewModel : ObservableObject
{
    /// <summary>Reason recorded when a DNF is marked without an explicit letter.</summary>
    public const string DefaultDnfReason = "o";

    private readonly IReadOnlyList<GridSeat> _grid;
    private readonly string _playerDriverId;
    private readonly TimeProvider _clock;

    // Result state. Positions are implied by _classified order (index 0 = P1).
    private readonly List<GridSeat> _classified = [];
    private readonly List<DnfEntry> _dnfs = [];
    private readonly List<GridSeat> _disqualified = [];

    // Optional free-text DSQ reason per disqualified driver (e.g. "Underweight"). Parallel to
    // _disqualified so the Disqualified list itself stays a plain GridSeat list; snapshotted
    // with it so custom reasons undo/redo in lockstep.
    private readonly Dictionary<string, string> _dsqReasons = new(StringComparer.Ordinal);

    private readonly Stack<Snapshot> _undoStack = new();

    private DateTimeOffset? _startedAt;

    private PendingKind _pendingKind = PendingKind.None;
    private string _pendingReason = DefaultDnfReason;
    private int _pendingPosition;

    public ResultEntryViewModel(
        IReadOnlyList<GridSeat> grid,
        string playerDriverId,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentException.ThrowIfNullOrEmpty(playerDriverId);
        if (grid.Count == 0)
            throw new ArgumentException("The grid must contain at least one seat.", nameof(grid));

        _grid = grid;
        _playerDriverId = playerDriverId;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>Optional per-session heading shown above the grammar (Increment 2b.3): set to
    /// "Qualifying" when this entry captures the weekend's qualifying grid order rather than a race
    /// result. Null on a plain race entry — the screen then renders exactly as the shipped
    /// single-race loop. Display only; the grammar itself is identical for every session.</summary>
    public string? SessionLabel { get; init; }

    // ---------- observable state ----------

    /// <summary>Slider value assumed before any recommendation exists (neutral 100%).</summary>
    public const double NeutralSlider = 100.0;

    /// <summary>Lowest/highest in-game Opponent Skill values (contract: editable 70–120).</summary>
    public const double MinSlider = 70.0;
    public const double MaxSlider = 120.0;

    /// <summary>The in-game Opponent Skill the round was actually driven at. Prefilled by the
    /// shell with the pace-anchor recommendation; the player edits it when they raced at
    /// something else. Stored in the round's raw-result envelope on Apply.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SliderUsedText))]
    private double sliderUsed = NeutralSlider;

    public string SliderUsedText => $"{SliderUsed:0}%";

    partial void OnSliderUsedChanged(double value)
    {
        double clamped = Math.Clamp(value, MinSlider, MaxSlider);
        if (clamped != value)
            SliderUsed = clamped;
    }

    /// <summary>Whether the round was run in the WET — captured for the weather-conditional perks
    /// (Rain Man, Sunshine Specialist). Defaults dry; stored in the raw envelope on Apply. Harmless
    /// for a character-free career (the fold never reads it).</summary>
    [ObservableProperty]
    private bool isWet;

    [ObservableProperty]
    private string input = "";

    [ObservableProperty]
    private ResultEntryPhase phase = ResultEntryPhase.Classified;

    /// <summary>Seats the current input matches, in list (grid) order. When the input is
    /// empty in the DNF phase this is the remaining drivers — bare Enter marks the selected
    /// one, which is how ↵↵↵ bulk-confirm works.</summary>
    [ObservableProperty]
    private IReadOnlyList<GridSeat> candidates = [];

    /// <summary>-1 when there are no candidates; Tab cycles it.</summary>
    [ObservableProperty]
    private int selectedCandidateIndex = -1;

    /// <summary>Set when Enter could not be applied; cleared on the next input change.</summary>
    [ObservableProperty]
    private string? errorText;

    public GridSeat? SelectedCandidate =>
        SelectedCandidateIndex >= 0 && SelectedCandidateIndex < Candidates.Count
            ? Candidates[SelectedCandidateIndex]
            : null;

    public bool IsAmbiguous => Candidates.Count > 1;

    public bool IsDnfPhase => Phase == ResultEntryPhase.Dnf;

    /// <summary>Classified seats in finishing order (index 0 = P1).</summary>
    public IReadOnlyList<GridSeat> Classified => _classified.ToArray();

    /// <summary>Unresolved seats, in grid order — the DNF phase's list.</summary>
    public IReadOnlyList<GridSeat> Remaining => _grid.Where(s => !IsResolved(s.DriverId)).ToArray();

    /// <summary>DNF'd seats in the order they were marked, each with its reason.</summary>
    public IReadOnlyList<DnfEntry> Dnfs => _dnfs.ToArray();

    public IReadOnlyList<GridSeat> Disqualified => _disqualified.ToArray();

    /// <summary>The free-text DSQ reason for a disqualified driver, or "" when none is set /
    /// the driver is not DSQ'd. Bound by the DSQ row's reason box.</summary>
    public string DsqReasonOf(string driverId) =>
        _dsqReasons.TryGetValue(driverId, out string? reason) ? reason : "";

    public int ResolvedCount => _classified.Count + _dnfs.Count + _disqualified.Count;

    /// <summary>Footer progress, e.g. "14/26 placed".</summary>
    public string ProgressText => $"{ResolvedCount}/{_grid.Count} placed";

    /// <summary>True when every seat is classified, DNF'd, or DSQ'd — the draft is complete.</summary>
    public bool IsComplete => ResolvedCount == _grid.Count;

    public bool CanUndo => _undoStack.Count > 0;

    // ---------- timer (starts on the first interaction; injectable clock) ----------

    public TimeSpan Elapsed => _startedAt is { } started ? _clock.GetUtcNow() - started : TimeSpan.Zero;

    public string ElapsedText => Elapsed.ToString(@"m\:ss");

    /// <summary>Called by the view's tick timer so the footer clock stays live.</summary>
    public void RefreshElapsed()
    {
        OnPropertyChanged(nameof(Elapsed));
        OnPropertyChanged(nameof(ElapsedText));
    }

    private void StartTimerIfNeeded() => _startedAt ??= _clock.GetUtcNow();

    // ---------- draft ----------

    /// <summary>The screen's product: classified order, DNF reasons, disqualifications, and
    /// the Opponent Skill slider the round was driven at (whole percent, clamped 70–120).</summary>
    public ResultDraft BuildDraft() => new()
    {
        Classified = _classified.Select(s => s.DriverId).ToArray(),
        // The letter map stays pure m/a/o — the stable seam every existing consumer reads.
        DidNotFinish = _dnfs.ToDictionary(d => d.Seat.DriverId, d => d.Reason, StringComparer.Ordinal),
        // Custom text / driver-error attribution rides alongside, only for the "o" rows that
        // actually carry it — untouched DNFs never appear here.
        DidNotFinishDetail = _dnfs
            .Where(d => !string.IsNullOrEmpty(d.Detail) || d.DriverAttributed)
            .ToDictionary(
                d => d.Seat.DriverId,
                d => new DnfDetail { Text = d.Detail ?? "", DriverAttributed = d.DriverAttributed },
                StringComparer.Ordinal),
        Disqualified = _disqualified.Select(s => s.DriverId).ToArray(),
        DisqualifiedDetail = _disqualified
            .Where(s => _dsqReasons.TryGetValue(s.DriverId, out string? r) && !string.IsNullOrEmpty(r))
            .ToDictionary(s => s.DriverId, s => _dsqReasons[s.DriverId], StringComparer.Ordinal),
        SliderUsed = Math.Clamp(
            Math.Round(SliderUsed, MidpointRounding.AwayFromZero), MinSlider, MaxSlider),
        IsWet = IsWet,
    };

    // ---------- commands (view maps Enter/Tab/Esc/F8/Ctrl+Z to these) ----------

    /// <summary>Enter: commit the selected candidate under the pending command.</summary>
    [RelayCommand]
    private void Submit()
    {
        StartTimerIfNeeded();

        if (_pendingKind == PendingKind.None || SelectedCandidate is not { } seat)
        {
            if (Input.Trim().Length > 0)
                ErrorText = $"No match for '{Input.Trim()}'";
            return;
        }

        PushUndo();
        switch (_pendingKind)
        {
            case PendingKind.Assign:
                _classified.Add(seat);
                break;

            case PendingKind.MarkDnf:
                _dnfs.Add(new DnfEntry(seat, _pendingReason));
                break;

            case PendingKind.Disqualify:
                _classified.Remove(seat); // no-op when the driver was unplaced
                _dsqReasons.Remove(seat.DriverId); // fresh DSQ carries no stated reason
                _disqualified.Add(seat);
                break;

            case PendingKind.Reposition:
                _classified.Remove(seat);
                _classified.Insert(Math.Min(_pendingPosition - 1, _classified.Count), seat);
                break;
        }

        ErrorText = null;
        Input = "";
        RaiseStateChanged();
    }

    /// <summary>Tab: cycle the highlighted candidate.</summary>
    [RelayCommand]
    private void CycleCandidate()
    {
        StartTimerIfNeeded();
        if (Candidates.Count == 0)
            return;
        SelectedCandidateIndex = (SelectedCandidateIndex + 1) % Candidates.Count;
    }

    /// <summary>Esc: clear the input (and with it the candidates).</summary>
    [RelayCommand]
    private void ClearInput()
    {
        Input = "";
        ErrorText = null;
        Recompute();
    }

    /// <summary>F8: toggle the DNF phase.</summary>
    [RelayCommand]
    private void ToggleDnfPhase()
    {
        StartTimerIfNeeded();
        Phase = Phase == ResultEntryPhase.Classified ? ResultEntryPhase.Dnf : ResultEntryPhase.Classified;
        Input = "";
        Recompute(); // Input may already have been empty, which raises no change
    }

    /// <summary>Ctrl+Z: unlimited undo across every mutation kind.</summary>
    [RelayCommand]
    private void Undo()
    {
        if (_undoStack.Count == 0)
            return;

        var snapshot = _undoStack.Pop();
        _classified.Clear();
        _classified.AddRange(snapshot.Classified);
        _dnfs.Clear();
        _dnfs.AddRange(snapshot.Dnfs);
        _disqualified.Clear();
        _disqualified.AddRange(snapshot.Disqualified);
        _dsqReasons.Clear();
        foreach (var pair in snapshot.DsqReasons)
            _dsqReasons[pair.Key] = pair.Value;

        RaiseStateChanged();
    }

    // ---------- grammar: parsing + matching ----------

    private enum PendingKind { None, Assign, MarkDnf, Disqualify, Reposition }

    partial void OnInputChanged(string value)
    {
        ErrorText = null;
        if (value.Length > 0)
            StartTimerIfNeeded();
        Recompute();
    }

    partial void OnPhaseChanged(ResultEntryPhase value)
    {
        OnPropertyChanged(nameof(IsDnfPhase));
        Recompute();
    }

    partial void OnCandidatesChanged(IReadOnlyList<GridSeat> value)
    {
        OnPropertyChanged(nameof(SelectedCandidate));
        OnPropertyChanged(nameof(IsAmbiguous));
    }

    partial void OnSelectedCandidateIndexChanged(int value) =>
        OnPropertyChanged(nameof(SelectedCandidate));

    /// <summary>Re-derives the pending command and candidate list from the current input.
    /// Resolution order: bare input (DNF-phase bulk) → plain match against unplaced →
    /// trailing 'q' DSQ → DNF reason letter (DNF phase, space-separated) → trailing digits
    /// re-position against placed.</summary>
    private void Recompute()
    {
        string text = Input.Trim();

        if (text.Length == 0)
        {
            if (Phase == ResultEntryPhase.Dnf)
            {
                // Remaining drivers ARE the candidates: Enter marks them in list order.
                SetPending(PendingKind.MarkDnf, Remaining, DefaultDnfReason);
            }
            else
            {
                SetPending(PendingKind.None, []);
            }
            return;
        }

        // 1. The whole text as a match against UNPLACED drivers (the dominant flow).
        var plain = Match(text, Remaining);
        if (plain.Count > 0)
        {
            SetPending(
                Phase == ResultEntryPhase.Dnf ? PendingKind.MarkDnf : PendingKind.Assign,
                plain,
                DefaultDnfReason);
            return;
        }

        // 2. Trailing 'q' — DSQ. Unplaced first, then placed (a placed driver is pulled out).
        if (text.Length >= 2 && char.ToLowerInvariant(text[^1]) == 'q')
        {
            string rest = text[..^1].TrimEnd();
            if (rest.Length > 0)
            {
                var scope = Remaining.Concat(_classified).ToArray();
                var dsq = Match(rest, scope);
                if (dsq.Count > 0)
                {
                    SetPending(PendingKind.Disqualify, dsq);
                    return;
                }
            }
        }

        // 3. DNF phase: "<match> <m|a|o>" — the space is required so the reason letter can
        // never be mistaken for part of a surname prefix.
        if (Phase == ResultEntryPhase.Dnf && text.Length >= 3)
        {
            char last = char.ToLowerInvariant(text[^1]);
            if (last is 'm' or 'a' or 'o' && char.IsWhiteSpace(text[^2]))
            {
                string rest = text[..^2].TrimEnd();
                var dnf = Match(rest, Remaining);
                if (dnf.Count > 0)
                {
                    SetPending(PendingKind.MarkDnf, dnf, last.ToString());
                    return;
                }
            }
        }

        // 4. Digits after a PLACED driver's match — penalty re-position.
        if (TryParseReposition(text, out string matchText, out int position))
        {
            var placed = Match(matchText, _classified);
            if (placed.Count > 0)
            {
                SetPending(PendingKind.Reposition, placed, position: position);
                return;
            }
        }

        SetPending(PendingKind.None, []);
    }

    private void SetPending(
        PendingKind kind,
        IReadOnlyList<GridSeat> candidates,
        string reason = DefaultDnfReason,
        int position = 0)
    {
        _pendingKind = kind;
        _pendingReason = reason;
        _pendingPosition = position;
        Candidates = candidates;
        SelectedCandidateIndex = candidates.Count > 0 ? 0 : -1;
    }

    /// <summary>Match text → seats within a scope. "me" is reserved for the player and never
    /// falls through to surname matching. A leading digit means car-number matching (exact
    /// number wins outright; otherwise number prefixes). Two or more letters match as a
    /// surname prefix. A single letter never matches (too ambiguous; also keeps the DNF
    /// reason letters unambiguous).</summary>
    private IReadOnlyList<GridSeat> Match(string token, IReadOnlyList<GridSeat> scope)
    {
        if (token.Equals("me", StringComparison.OrdinalIgnoreCase))
            return scope.Where(s => string.Equals(s.DriverId, _playerDriverId, StringComparison.Ordinal))
                .Take(1)
                .ToArray();

        if (char.IsAsciiDigit(token[0]))
        {
            var exact = scope
                .Where(s => s.Number is { } n && n.Equals(token, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (exact.Length > 0)
                return exact;

            return scope
                .Where(s => s.Number is { } n && n.StartsWith(token, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        if (token.Length >= 2 && token.All(char.IsLetter))
            return scope
                .Where(s => StartsWithLoose(Surname(s.DriverName), token))
                .ToArray();

        return [];
    }

    /// <summary>Case- AND accent-insensitive prefix test: a surname typed WITHOUT its diacritics
    /// still matches (e.g. "perez" → "Pérez-Sala", "raik" → "Räikkönen", "hakk" → "Häkkinen"), since
    /// most keyboards can't type the accent. IgnoreNonSpace folds combining marks; IgnoreCase folds
    /// case. Typing the accent works too — the fold is symmetric.</summary>
    private static bool StartsWithLoose(string source, string prefix) =>
        CultureInfo.InvariantCulture.CompareInfo.IsPrefix(
            source, prefix, CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreCase);

    /// <summary>Splits "cla3" / "cla 3" / "1 2" into match text + target position. Without a
    /// space the match text must contain a non-digit, so a pure number like "12" is always a
    /// car-number match, never an accidental re-position of car 1.</summary>
    private static bool TryParseReposition(string text, out string matchText, out int position)
    {
        matchText = "";
        position = 0;

        string left, digits;
        int lastSpace = text.LastIndexOf(' ');
        if (lastSpace > 0)
        {
            left = text[..lastSpace].TrimEnd();
            digits = text[(lastSpace + 1)..];
        }
        else
        {
            int i = text.Length;
            while (i > 0 && char.IsAsciiDigit(text[i - 1]))
                i--;
            if (i == 0 || i == text.Length)
                return false; // pure digits, or no digit suffix at all
            left = text[..i];
            digits = text[i..];
        }

        if (left.Length == 0 || digits.Length == 0 || !digits.All(char.IsAsciiDigit))
            return false;
        if (!int.TryParse(digits, out position) || position < 1)
            return false;

        matchText = left;
        return true;
    }

    /// <summary>Last whitespace-separated token of the driver's display name.</summary>
    private static string Surname(string driverName)
    {
        int i = driverName.LastIndexOf(' ');
        return i < 0 ? driverName : driverName[(i + 1)..];
    }

    // ---------- state bookkeeping ----------

    private sealed record Snapshot(
        GridSeat[] Classified,
        DnfEntry[] Dnfs,
        GridSeat[] Disqualified,
        KeyValuePair<string, string>[] DsqReasons);

    private void PushUndo() =>
        _undoStack.Push(new Snapshot(
            _classified.ToArray(),
            _dnfs.ToArray(),
            _disqualified.ToArray(),
            _dsqReasons.ToArray()));

    private bool IsResolved(string driverId) =>
        _classified.Any(s => s.DriverId == driverId) ||
        _dnfs.Any(d => d.Seat.DriverId == driverId) ||
        _disqualified.Any(s => s.DriverId == driverId);

    private void RaiseStateChanged()
    {
        Recompute();
        OnPropertyChanged(nameof(Classified));
        OnPropertyChanged(nameof(Remaining));
        OnPropertyChanged(nameof(Dnfs));
        OnPropertyChanged(nameof(Disqualified));
        OnPropertyChanged(nameof(ResolvedCount));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(IsComplete));
        OnPropertyChanged(nameof(CanUndo));
    }
}
