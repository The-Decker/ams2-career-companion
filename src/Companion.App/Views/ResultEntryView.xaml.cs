using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Companion.App.Audio;
using Companion.Core.Grid;
using Companion.ViewModels.ResultEntry;
using Companion.ViewModels.Shell;

namespace Companion.App.Views;

/// <summary>
/// Result entry: the keyboard grammar AND full mouse parity over the same viewmodel (ux-round
/// contract §1). This code-behind is pure UI mechanics — key routing to viewmodel commands,
/// focus pinning on the input box (restored after every mouse action, so typing always
/// works), the 1 Hz footer-timer tick, and one-line translations from context-menu /
/// reason-picker / double-click gestures into the tested viewmodel mouse primitives. The
/// drag-and-drop mechanics live in Behaviors/ListDragDropBehavior; every grammar rule and
/// every mouse mutation lives (tested) in ResultEntryViewModel.
/// </summary>
public partial class ResultEntryView : UserControl
{
    private readonly DispatcherTimer _timer;

    private string? _insertPositionDriverId;

    public ResultEntryView()
    {
        InitializeComponent();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => (DataContext as ResultEntryViewModel)?.RefreshElapsed();

        Loaded += (_, _) => { _timer.Start(); FocusInput(); };
        Unloaded += (_, _) => _timer.Stop();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
                FocusInput();
        };

        // After any drop (handled inside the behavior) the input box gets focus back — but
        // never when a reason/detail/DSQ box is the active editor, or the drop would yank the
        // caret out of a box the user just started typing in (BUG A).
        AddHandler(DragDrop.DropEvent, new DragEventHandler((_, _) =>
        {
            if (!IsEditableTextEntryFocused())
                FocusInput();
        }), handledEventsToo: true);
    }

    private ResultEntryViewModel? ViewModel => DataContext as ResultEntryViewModel;

    // ---------- keyboard grammar routing (Enter / Tab / Esc / F8 / Ctrl+Z) ----------

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm)
            return;

        // Keys typed into the insert-position popover belong to the popover.
        if (InsertPositionPopup.IsOpen)
            return;

        // KEY-ROUTING GUARD (v0.3.3): when the caret sits in an editable box OTHER than the
        // grammar InputBox — the DNF custom-cause box or the DSQ reason box — the key belongs to
        // that box, not the grammar. OnPreviewKeyDown is a tunnel handler at the UserControl level
        // (root→leaf), so without this early return it would fire BEFORE OnDnfDetailKeyDown /
        // OnDsqReasonKeyDown and swallow Enter/Esc (executing Submit/Confirm) so the user "can't
        // press Enter" in those boxes. The v0.3.2 fix guarded the MOUSE pin (OnPreviewMouseUp) but
        // not this key route. Grammar keys still work when InputBox (or any non-text surface) holds
        // focus. (The insert-position box has its own KeyDown handler and is covered above.)
        if (IsEditableTextEntryFocused())
            return;

        switch (e.Key)
        {
            case Key.Enter:
                // A complete draft + empty input: Enter rolls straight into Confirm.
                if (vm.IsComplete && string.IsNullOrWhiteSpace(vm.Input) &&
                    FindHomeViewModel() is { } home && home.ConfirmResultCommand.CanExecute(null))
                {
                    home.ConfirmResultCommand.Execute(null);
                }
                else
                {
                    vm.SubmitCommand.Execute(null);
                }
                e.Handled = true;
                break;

            case Key.Tab:
                vm.CycleCandidateCommand.Execute(null);
                e.Handled = true; // Tab never leaves the input box on this screen
                break;

            case Key.Escape:
                vm.ClearInputCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.F8:
                vm.ToggleDnfPhaseCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Z when Keyboard.Modifiers.HasFlag(ModifierKeys.Control):
                vm.UndoCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    // ---------- focus pinning: the entry box always has focus ----------

    /// <summary>Left-click releases pin the grammar input back (typing must always work —
    /// decision 8), EXCEPT when the click lands in — or focus already sits within — an editable
    /// text-entry box other than InputBox (the DNF custom-cause box, the DSQ reason box, the
    /// insert-position box). Re-focusing those away instantly yanked the caret the user was
    /// trying to place (BUG A). A right-click must not steal focus from the context menu it is
    /// about to open, so only left releases pin.</summary>
    private void OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || InsertPositionPopup.IsOpen)
            return;

        // The click's own focus-set to a TextBox happens before this deferred re-focus would
        // run; if the target (or the currently-focused element) is an editable box other than
        // InputBox, leave the caret where the user put it.
        if (IsEditableTextEntryTarget(e.OriginalSource) || IsEditableTextEntryFocused())
            return;

        FocusInput();
    }

    /// <summary>True when keyboard focus currently sits in an editable TextBox that is NOT the
    /// grammar InputBox — the DNF custom-cause box, the DSQ reason box, or the insert-position
    /// box. Those own the caret; the grammar-input pin must stand down for them.</summary>
    private bool IsEditableTextEntryFocused() =>
        Keyboard.FocusedElement is TextBox { IsReadOnly: false } box && !ReferenceEquals(box, InputBox);

    /// <summary>True when a click's target is (or lives inside) an editable TextBox other than
    /// InputBox — checked on mouse-up before the box has necessarily taken keyboard focus.</summary>
    private bool IsEditableTextEntryTarget(object? source)
    {
        for (DependencyObject? node = source as DependencyObject; node is not null;
             node = node is Visual or System.Windows.Media.Media3D.Visual3D
                 ? VisualTreeHelper.GetParent(node)
                 : LogicalTreeHelper.GetParent(node))
        {
            if (node is TextBox { IsReadOnly: false } box)
                return !ReferenceEquals(box, InputBox);
        }
        return false;
    }

    private void FocusInput() =>
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            InputBox.Focus();
            Keyboard.Focus(InputBox);
        });

    // ---------- mouse gestures → viewmodel primitives ----------

    private void OnCandidateDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // The first click already moved SelectedCandidateIndex (two-way binding).
        if (ViewModel is { } vm)
        {
            string before = PlacementStateOf(vm);
            vm.SubmitCommand.Execute(null);

            // SubmitCommand is intentionally void and can decline incomplete/stale input. Give
            // the gesture tactile feedback only after the resolved result really changed.
            if (!string.Equals(before, PlacementStateOf(vm), StringComparison.Ordinal))
                SoundAssist.Play(SoundEffectCue.BucketPlace);
        }
        e.Handled = true;
    }

    /// <summary>Double-click on a remaining driver: next open position (DNF in the DNF
    /// phase) — the same undoable primitives every other mouse path uses.</summary>
    private void OnRemainingDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is { } vm && DriverIdOf((sender as FrameworkElement)?.DataContext) is { } id)
        {
            bool moved = vm.IsDnfPhase
                ? vm.MarkDnf(id)
                : vm.InsertAt(id, vm.Classified.Count);

            if (moved)
                SoundAssist.Play(SoundEffectCue.BucketPlace);
            e.Handled = true;
        }
    }

    /// <summary>A compact fingerprint of the result buckets and classified order. Candidate
    /// submission exposes a void command, so comparing this before/after distinguishes a real
    /// placement (including a reorder) from an invalid or same-position no-op without reaching
    /// into ViewModel implementation details.</summary>
    private static string PlacementStateOf(ResultEntryViewModel vm) => string.Join('\u001e',
    [
        string.Join('\u001f', vm.Classified.Select(s => s.DriverId)),
        string.Join('\u001f', vm.Dnfs.Select(d => d.Seat.DriverId)),
        string.Join('\u001f', vm.Disqualified.Select(s => s.DriverId)),
    ]);

    /// <summary>The inline M/A/O picker on a DNF row's editor. Picking m/a records a concrete
    /// cause and closes the editor (row → compact DISPLAY, via the VM clearing
    /// EditingReasonDriverId), returning focus to the grammar box. Picking "o" keeps the editor
    /// open and reveals the custom-cause box, into which the caret is dropped so the user can
    /// type straight away. Never blocks — the DNF is already committed and removable whether or
    /// not this is touched.</summary>
    private void OnReasonPickClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm ||
            sender is not FrameworkElement { Tag: string reason } element ||
            DriverIdOf(element.DataContext) is not { } id)
            return;

        vm.SetDnfReason(id, reason); // for m/a this also clears EditingReasonDriverId (editor hides)
        e.Handled = true;

        if (reason == "o")
        {
            // Reveal + focus the custom-cause box so typing works immediately. Background priority
            // (below the Input-priority pin) so the box, not the grammar InputBox, keeps the caret.
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                if (vm.EditingReasonDriverId == id)
                    FindEditorBoxFor(id)?.Focus();
            });
        }
        else
        {
            FocusInput();
        }
    }

    /// <summary>The editor's "Done" affordance — commit whatever is in the box and close the
    /// editor (row → compact DISPLAY). A mouse-only equivalent of pressing Enter in the box, for
    /// users who reach for a button. Commits EXPLICITLY (rather than relying on LostFocus, which
    /// may not have fired yet, and which never fires once the box is collapsed) so the typed text
    /// is never lost. Never blocks.</summary>
    private void OnReasonPickDone(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && DriverIdOf((sender as FrameworkElement)?.DataContext) is { } id)
        {
            if (FindEditorBoxFor(id) is { } box)
            {
                // Route through the same commit path the box's own handlers use.
                if (box.DataContext is DnfEntry)
                    OnDnfDetailChanged(box, e);
                else
                    OnDsqReasonChanged(box, e);
            }
            vm.StopEditingReason();
        }
        e.Handled = true;
        FocusInput();
    }

    /// <summary>The custom-cause text box on an "Other" DNF row — commits on Enter / lost
    /// focus. Independent of the DNF mark, so leaving it blank never traps the driver.</summary>
    private void OnDnfDetailChanged(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && sender is TextBox box && DriverIdOf(box.DataContext) is { } id)
            vm.SetDnfDetail(id, box.Text, DriverAttributedOf(box.DataContext));
    }

    private void OnDnfDetailKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox box)
        {
            // Enter COMMITS the custom cause AND closes the editor (row → compact DISPLAY): the
            // "when the values are done the text box must disappear" behaviour. Without the
            // StopEditingReason the box stayed put (Mike's report). The key-routing guard above
            // lets this handler see Enter at all.
            OnDnfDetailChanged(box, e);
            ViewModel?.StopEditingReason();
            e.Handled = true;
            FocusInput();
        }
        else if (e.Key == Key.Escape)
        {
            // Esc closes the editor too; the box was OneWay-bound to Detail, so any uncommitted
            // text is simply discarded (reverting is fine per the spec).
            ViewModel?.StopEditingReason();
            e.Handled = true;
            FocusInput();
        }
    }

    /// <summary>CLICK-TO-EDIT: a left-click on a resolved (DNF or DSQ) row's display area opens
    /// that row's inline reason editor, seeded with its current reason/detail — the "click on the
    /// driver, the box comes back up with the options so you can modify it" behaviour that
    /// replaces remove-then-readd. Only one row edits at a time (the VM enforces it). Does not
    /// mark the event handled, so drag-to-move and the context menu still work; the deferred
    /// focus-pin in OnPreviewMouseUp then lands on the freshly-realised editor box (a text-entry
    /// target), leaving the caret ready to type. A no-op if the row is somehow not resolved.</summary>
    private void OnResolvedRowClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is not { } vm || sender is not FrameworkElement element ||
            DriverIdOf(element.DataContext) is not { } id)
            return;

        // Already editing this row: leave it open (a second click must not toggle it shut, which
        // would fight the box the user is trying to click into).
        if (vm.EditingReasonDriverId == id)
            return;

        vm.BeginEditingReason(id);

        // After layout realises the editor, re-seed its box from the VM (so an undo/redo that
        // changed the stored reason without touching the box is reflected) and drop the caret in
        // so the user can type straight away (DNF custom-cause box only when the reason is already
        // "o"; DSQ reason box always). Deferred at Background priority — LOWER than the Input-
        // priority FocusInput pin OnPreviewMouseUp schedules on this same click — so the editor
        // box wins the caret last, not the grammar InputBox.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (vm.EditingReasonDriverId != id)
                return;
            if (FindEditorBoxFor(id) is { } box)
            {
                if (box.DataContext is not DnfEntry)
                    box.Text = vm.DsqReasonOf(id); // DNF box is Detail-bound; DSQ box is seeded here
                box.Focus();
                box.SelectAll();
            }
        });
    }

    /// <summary>The editable reason box for a driver's open editor — the DSQ reason box, or the
    /// DNF custom-cause box when the DNF reason is "o". Walks the realised visual tree; null when
    /// no box is currently shown (e.g. a DNF still on Mechanical/Accident).</summary>
    private TextBox? FindEditorBoxFor(string driverId)
    {
        foreach (var box in Descendants<TextBox>(this))
        {
            if (ReferenceEquals(box, InputBox) || box.IsReadOnly)
                continue;
            if (DriverIdOf(box.DataContext) == driverId)
                return box;
        }
        return null;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;
            foreach (var descendant in Descendants<T>(child))
                yield return descendant;
        }
    }

    /// <summary>The "driver's fault" toggle on an "Other" DNF row — flips the attribution the
    /// OPI blame model reads, keeping any custom text.</summary>
    private void OnDnfFaultToggle(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && sender is System.Windows.Controls.Primitives.ToggleButton toggle &&
            DriverIdOf(toggle.DataContext) is { } id)
        {
            vm.SetDnfDetail(id, DnfDetailTextOf(toggle.DataContext), toggle.IsChecked == true);
            FocusInput();
        }
    }

    /// <summary>The picker's own "Remove" affordance: the mistaken DNF goes straight back to
    /// Remaining — the fix for the "can't re-select until a reason is picked" trap. Available
    /// whether or not a reason was ever chosen.</summary>
    private void OnReasonPickRemove(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && DriverIdOf((sender as FrameworkElement)?.DataContext) is { } id)
        {
            vm.Unmark(id);
            e.Handled = true;
            FocusInput();
        }
    }

    /// <summary>Seed the DSQ reason box with the driver's stored reason when the row realises
    /// (so an undo/redo or a reopened row shows the right text).</summary>
    private void OnDsqReasonLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && sender is TextBox box && DriverIdOf(box.DataContext) is { } id)
            box.Text = vm.DsqReasonOf(id);
    }

    /// <summary>The per-row DSQ reason box (free text, e.g. "Underweight") — commit on Enter /
    /// lost focus. Independent of the DSQ mark; blank leaves no stated reason.</summary>
    private void OnDsqReasonChanged(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && sender is TextBox box && DriverIdOf(box.DataContext) is { } id)
            vm.SetDsqReason(id, box.Text);
    }

    private void OnDsqReasonKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox box)
        {
            // Enter COMMITS the DSQ reason AND closes the editor (row → compact DISPLAY, showing
            // the reason as a label): "you can't press enter to enter your dsq reasons" — before
            // the key-routing guard the grammar swallowed Enter and the box never committed nor
            // hid.
            OnDsqReasonChanged(box, e);
            ViewModel?.StopEditingReason();
            e.Handled = true;
            FocusInput();
        }
        else if (e.Key == Key.Escape)
        {
            ViewModel?.StopEditingReason();
            e.Handled = true;
            FocusInput();
        }
    }

    private static string? DnfDetailTextOf(object? item) =>
        item is DnfEntry entry ? entry.Detail : null;

    private static bool DriverAttributedOf(object? item) =>
        item is DnfEntry { DriverAttributed: true };

    // ---------- the per-driver context menu (same menu on every row) ----------

    private void OnCtxPlaceNext(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && MenuDriverId(sender) is { } id)
        {
            // A placed driver "places next" by moving to the end; everyone else inserts.
            if (!vm.InsertAt(id, vm.Classified.Count))
                vm.MoveTo(id, vm.Classified.Count - 1);
            FocusInput();
        }
    }

    private void OnCtxInsertAtPosition(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm || MenuDriverId(sender) is not { } id)
            return;

        _insertPositionDriverId = id;
        InsertPositionBox.Text = (vm.Classified.Count + 1).ToString();
        InsertPositionPopup.IsOpen = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            InsertPositionBox.Focus();
            InsertPositionBox.SelectAll();
        });
    }

    private void OnCtxDnf(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm &&
            sender is MenuItem { Tag: string reason } &&
            MenuDriverId(sender) is { } id)
        {
            // Already-DNF'd rows just change their reason.
            if (!vm.MarkDnf(id, reason))
                vm.SetDnfReason(id, reason);
            FocusInput();
        }
    }

    private void OnCtxDsq(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && MenuDriverId(sender) is { } id)
        {
            vm.MarkDsq(id);
            FocusInput();
        }
    }

    private void OnCtxRemove(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && MenuDriverId(sender) is { } id)
        {
            vm.Unmark(id);
            FocusInput();
        }
    }

    // ---------- the insert-at-position popover ----------

    private void OnInsertPositionKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitInsertPosition();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            InsertPositionPopup.IsOpen = false;
            FocusInput();
            e.Handled = true;
        }
    }

    private void OnInsertPositionConfirm(object sender, RoutedEventArgs e) => CommitInsertPosition();

    private void CommitInsertPosition()
    {
        if (ViewModel is { } vm && _insertPositionDriverId is { } id &&
            int.TryParse(InsertPositionBox.Text.Trim(), out int position) && position >= 1)
        {
            // 1-based position → 0-based index; placed drivers re-position instead.
            if (!vm.InsertAt(id, position - 1))
                vm.MoveTo(id, position - 1);
        }
        _insertPositionDriverId = null;
        InsertPositionPopup.IsOpen = false;
        FocusInput();
    }

    // ---------- helpers ----------

    /// <summary>Row items are GridSeat (remaining / order / DSQ) or DnfEntry (DNF zone).</summary>
    private static string? DriverIdOf(object? item) => item switch
    {
        GridSeat seat => seat.DriverId,
        DnfEntry entry => entry.Seat.DriverId,
        _ => null,
    };

    /// <summary>Menu items inherit the row item from the ContextMenu's PlacementTarget.</summary>
    private static string? MenuDriverId(object sender) =>
        DriverIdOf((sender as FrameworkElement)?.DataContext);

    /// <summary>The Confirm command lives on the Home conductor — walk up to it.</summary>
    private HomeViewModel? FindHomeViewModel()
    {
        DependencyObject? node = VisualTreeHelper.GetParent(this);
        while (node is not null)
        {
            if (node is FrameworkElement { DataContext: HomeViewModel home })
                return home;
            node = VisualTreeHelper.GetParent(node);
        }
        return null;
    }
}
