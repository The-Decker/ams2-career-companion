using CommunityToolkit.Mvvm.ComponentModel;
using Companion.Core.Grid;

namespace Companion.ViewModels.ResultEntry;

/// <summary>
/// Mouse-oriented primitives for the result-entry screen (ux-round contract, locked decision
/// #8): drag-and-drop and context-menu operations as first-class viewmodel methods. The
/// keyboard grammar in <c>ResultEntryViewModel.cs</c> is untouched — these methods are peers
/// over the same state, push the SAME undo snapshots (<see cref="Undo"/> unwinds keyboard and
/// mouse mutations interchangeably), share the progress counter, the timer, and the
/// <see cref="IsComplete"/> rule. The WPF drag/adorner layer stays thin: every drop resolves
/// to one call here, so the interactions are unit-testable without WPF.
///
/// Semantics:
/// <list type="bullet">
/// <item><see cref="InsertAt"/> — insert-BEFORE the given index (never replaces); followers
///   shift down; also pulls a DNF/DSQ driver back into the order in one undoable step;</item>
/// <item><see cref="MoveTo"/> — reorder within the classification == the grammar's penalty
///   re-position (index is the driver's FINAL 0-based position);</item>
/// <item><see cref="MarkDnf"/>/<see cref="MarkDsq"/> — mark from anywhere (a placed driver is
///   pulled out of the classification, like the grammar's trailing-'q' rule);</item>
/// <item><see cref="Unmark"/> — back to Remaining from any resolved state (drag out of a
///   zone, or the context menu's Remove);</item>
/// <item><see cref="MarkDnfBulk"/> — one drag gesture, ONE undo snapshot;</item>
/// <item>multi-select (<see cref="ToggleSelected"/>/<see cref="ClearSelection"/>) is UI
///   state: never part of the draft, never undone.</item>
/// </list>
/// </summary>
public sealed partial class ResultEntryViewModel
{
    private static readonly string[] ValidDnfReasons = ["m", "a", "o"];

    // ---------- multi-select state (Ctrl/Shift-click; feeds bulk drag) ----------

    private readonly HashSet<string> _selection = new(StringComparer.Ordinal);

    /// <summary>Driver ids currently multi-selected in the Remaining list.</summary>
    public IReadOnlyCollection<string> SelectedDriverIds => _selection;

    public bool IsSelected(string driverId) => _selection.Contains(driverId);

    public void ToggleSelected(string driverId)
    {
        if (FindSeat(driverId) is null)
            return;
        if (!_selection.Add(driverId))
            _selection.Remove(driverId);
        OnPropertyChanged(nameof(SelectedDriverIds));
    }

    public void ClearSelection()
    {
        if (_selection.Count == 0)
            return;
        _selection.Clear();
        OnPropertyChanged(nameof(SelectedDriverIds));
    }

    // ---------- inline DNF reason picker (view state; set by the drop handler) ----------

    /// <summary>The driver whose freshly-dropped DNF row shows the inline reason picker
    /// (Mechanical / Accident / Other). Cleared by <see cref="SetDnfReason"/> and
    /// <see cref="Unmark"/>; null hides every picker.</summary>
    [ObservableProperty]
    private string? reasonPickerDriverId;

    // ---------- mouse primitives (each pushes the same undo snapshot the grammar uses) ----------

    /// <summary>Insert an unplaced (or DNF'd/DSQ'd) driver into the finishing order BEFORE
    /// <paramref name="index"/> (0-based; clamped to the ends). Followers shift down —
    /// dropping onto a filled slot never replaces it. False when the driver is unknown or
    /// already classified (use <see cref="MoveTo"/> for placed drivers).</summary>
    public bool InsertAt(string driverId, int index)
    {
        var seat = FindSeat(driverId);
        if (seat is null || _classified.Any(s => s.DriverId == driverId))
            return false;

        BeginMouseMutation();
        _dnfs.RemoveAll(d => d.Seat.DriverId == driverId);
        _disqualified.RemoveAll(s => s.DriverId == driverId);
        _dsqReasons.Remove(driverId);
        _classified.Insert(Math.Clamp(index, 0, _classified.Count), seat);
        CompleteMouseMutation(driverId);
        return true;
    }

    /// <summary>Reorder a placed driver to a FINAL 0-based index (clamped) — the drag-within-
    /// order gesture, identical in effect to the grammar's penalty re-position. Dropping a
    /// driver back on its own slot is a successful no-op that pushes nothing to undo.</summary>
    public bool MoveTo(string driverId, int newIndex)
    {
        int current = _classified.FindIndex(s => s.DriverId == driverId);
        if (current < 0)
            return false;

        int target = Math.Clamp(newIndex, 0, _classified.Count - 1);
        if (target == current)
            return true;

        BeginMouseMutation();
        var seat = _classified[current];
        _classified.RemoveAt(current);
        _classified.Insert(target, seat);
        CompleteMouseMutation(driverId);
        return true;
    }

    /// <summary>Mark a driver DNF (default reason "o" = Other, matching the grammar). Works
    /// from Remaining, the classification (the driver is pulled out), or DSQ. False when
    /// already DNF (use <see cref="SetDnfReason"/>) or the reason letter is invalid.</summary>
    public bool MarkDnf(string driverId, string reason = DefaultDnfReason)
    {
        var seat = FindSeat(driverId);
        if (seat is null || !ValidDnfReasons.Contains(reason) ||
            _dnfs.Any(d => d.Seat.DriverId == driverId))
        {
            return false;
        }

        BeginMouseMutation();
        _classified.RemoveAll(s => s.DriverId == driverId);
        _disqualified.RemoveAll(s => s.DriverId == driverId);
        _dsqReasons.Remove(driverId);
        _dnfs.Add(new DnfEntry(seat, reason));
        CompleteMouseMutation(driverId);
        return true;
    }

    /// <summary>Change an existing DNF's reason ("m"/"a"/"o") — the inline picker and the
    /// context menu's reason submenu. Undoable; picking the current reason is a no-op that
    /// still dismisses the picker. Switching AWAY from "o" drops any custom detail (mechanical
    /// and accident have fixed meanings); the picker stays open on "o" so the user can type a
    /// custom cause, and closes on m/a.</summary>
    public bool SetDnfReason(string driverId, string reason)
    {
        if (!ValidDnfReasons.Contains(reason))
            return false;
        int i = _dnfs.FindIndex(d => d.Seat.DriverId == driverId);
        if (i < 0)
            return false;

        if (_dnfs[i].Reason == reason)
        {
            // Same reason: no state change, but m/a dismiss the picker (nothing more to say);
            // "o" keeps it open for the custom-text box.
            if (ReasonPickerDriverId == driverId && reason != "o")
                ReasonPickerDriverId = null;
            return true;
        }

        BeginMouseMutation();
        _dnfs[i] = reason == "o"
            ? _dnfs[i] with { Reason = "o" }
            : _dnfs[i] with { Reason = reason, Detail = null, DriverAttributed = false };
        // Selecting a concrete cause (mechanical/accident) closes the picker; "o" leaves it
        // open so free text can follow — never blocking either way.
        if (reason != "o")
            ReasonPickerDriverId = null;
        CompleteMouseMutation(clearPickerFor: null);
        return true;
    }

    /// <summary>Set (or clear) the free-text detail on an "other" DNF and whether the cause is
    /// the driver's fault — the inline "Other" text box + a "driver's fault" toggle. Forces the
    /// reason to "o" (custom text only ever qualifies Other). Undoable and fully independent of
    /// marking the DNF, so a mistaken DNF is always removable whether or not a detail was ever
    /// typed. False when the driver is not currently DNF'd.</summary>
    public bool SetDnfDetail(string driverId, string? text, bool driverAttributed = false)
    {
        int i = _dnfs.FindIndex(d => d.Seat.DriverId == driverId);
        if (i < 0)
            return false;

        string normalized = text?.Trim() ?? "";
        string detail = normalized.Length == 0 ? "" : normalized;
        var current = _dnfs[i];
        bool unchanged = current.Reason == "o" &&
            string.Equals(current.Detail ?? "", detail, StringComparison.Ordinal) &&
            current.DriverAttributed == driverAttributed;
        if (unchanged)
            return true;

        BeginMouseMutation();
        _dnfs[i] = current with
        {
            Reason = "o",
            Detail = detail.Length == 0 ? null : detail,
            DriverAttributed = driverAttributed,
        };
        CompleteMouseMutation(clearPickerFor: null);
        return true;
    }

    /// <summary>Disqualify a driver from anywhere (a placed driver is pulled out of the
    /// classification — the grammar's trailing-'q' semantics). False when already DSQ.</summary>
    public bool MarkDsq(string driverId)
    {
        var seat = FindSeat(driverId);
        if (seat is null || _disqualified.Any(s => s.DriverId == driverId))
            return false;

        BeginMouseMutation();
        _classified.RemoveAll(s => s.DriverId == driverId);
        _dnfs.RemoveAll(d => d.Seat.DriverId == driverId);
        _dsqReasons.Remove(driverId); // fresh DSQ starts with no stated reason
        _disqualified.Add(seat);
        CompleteMouseMutation(driverId);
        return true;
    }

    /// <summary>Set (or clear) the free-text DSQ reason (e.g. "Underweight", "Illegal wing")
    /// for a disqualified driver. Undoable and independent of the DSQ mark itself. False when
    /// the driver is not currently DSQ'd.</summary>
    public bool SetDsqReason(string driverId, string? reason)
    {
        if (!_disqualified.Any(s => s.DriverId == driverId))
            return false;

        string detail = reason?.Trim() ?? "";
        string current = _dsqReasons.TryGetValue(driverId, out string? r) ? r : "";
        if (string.Equals(current, detail, StringComparison.Ordinal))
            return true;

        BeginMouseMutation();
        if (detail.Length == 0)
            _dsqReasons.Remove(driverId);
        else
            _dsqReasons[driverId] = detail;
        CompleteMouseMutation(clearPickerFor: null);
        return true;
    }

    /// <summary>Return a resolved driver (classified, DNF, or DSQ) to Remaining — drag out of
    /// a zone, or the context menu's Remove. False when the driver is already unresolved.</summary>
    public bool Unmark(string driverId)
    {
        if (FindSeat(driverId) is null || !IsResolved(driverId))
            return false;

        BeginMouseMutation();
        _classified.RemoveAll(s => s.DriverId == driverId);
        _dnfs.RemoveAll(d => d.Seat.DriverId == driverId);
        _disqualified.RemoveAll(s => s.DriverId == driverId);
        _dsqReasons.Remove(driverId);
        CompleteMouseMutation(driverId);
        return true;
    }

    /// <summary>Bulk retirement — multi-select dragged to the DNF zone, mirroring the DNF
    /// phase's ↵↵↵. One gesture pushes ONE undo snapshot (a single Ctrl+Z restores the whole
    /// bulk). Drivers are marked in the order given; ids already DNF'd are skipped; the
    /// multi-selection is cleared afterwards. False when nothing was markable.</summary>
    public bool MarkDnfBulk(IEnumerable<string> driverIds, string reason = DefaultDnfReason)
    {
        ArgumentNullException.ThrowIfNull(driverIds);
        if (!ValidDnfReasons.Contains(reason))
            return false;

        var seats = driverIds
            .Distinct(StringComparer.Ordinal)
            .Select(FindSeat)
            .Where(s => s is not null && !_dnfs.Any(d => d.Seat.DriverId == s.DriverId))
            .Select(s => s!)
            .ToList();
        if (seats.Count == 0)
            return false;

        BeginMouseMutation();
        foreach (var seat in seats)
        {
            _classified.RemoveAll(s => s.DriverId == seat.DriverId);
            _disqualified.RemoveAll(s => s.DriverId == seat.DriverId);
            _dsqReasons.Remove(seat.DriverId);
            _dnfs.Add(new DnfEntry(seat, reason));
        }
        ClearSelection();
        CompleteMouseMutation(clearPickerFor: null);
        return true;
    }

    // ---------- shared plumbing ----------

    private GridSeat? FindSeat(string driverId) =>
        _grid.FirstOrDefault(s => string.Equals(s.DriverId, driverId, StringComparison.Ordinal));

    /// <summary>Every mouse mutation counts as an interaction (starts the entry timer) and
    /// pushes the same snapshot type the grammar pushes — one shared undo stack.</summary>
    private void BeginMouseMutation()
    {
        StartTimerIfNeeded();
        PushUndo();
    }

    private void CompleteMouseMutation(string? clearPickerFor)
    {
        if (clearPickerFor is not null && ReasonPickerDriverId == clearPickerFor)
            ReasonPickerDriverId = null;
        ErrorText = null;
        RaiseStateChanged();
    }
}
