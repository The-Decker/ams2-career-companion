using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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

        // After any drop (handled inside the behavior) the input box gets focus back.
        AddHandler(DragDrop.DropEvent, new DragEventHandler((_, _) => FocusInput()), handledEventsToo: true);
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

    /// <summary>Left-click releases only — a right-click must not steal focus from the
    /// context menu it is about to open.</summary>
    private void OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && !InsertPositionPopup.IsOpen)
            FocusInput();
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
        ViewModel?.SubmitCommand.Execute(null);
        e.Handled = true;
    }

    /// <summary>Double-click on a remaining driver: next open position (DNF in the DNF
    /// phase) — the same undoable primitives every other mouse path uses.</summary>
    private void OnRemainingDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is { } vm && DriverIdOf((sender as FrameworkElement)?.DataContext) is { } id)
        {
            if (vm.IsDnfPhase)
                vm.MarkDnf(id);
            else
                vm.InsertAt(id, vm.Classified.Count);
            e.Handled = true;
        }
    }

    /// <summary>The inline M/A/O picker on a freshly dropped DNF row. Picking m/a closes the
    /// picker; "o" keeps it open so a custom cause can be typed. Never blocks — the DNF is
    /// already committed and removable whether or not this is touched.</summary>
    private void OnReasonPickClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm &&
            sender is FrameworkElement { Tag: string reason } element &&
            DriverIdOf(element.DataContext) is { } id)
        {
            vm.SetDnfReason(id, reason);
            e.Handled = true;
            // "Other" keeps the row's picker open (custom box); m/a return focus to typing.
            if (reason != "o")
                FocusInput();
        }
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
            OnDnfDetailChanged(box, e);
            e.Handled = true;
            FocusInput();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            FocusInput();
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
            OnDsqReasonChanged(box, e);
            e.Handled = true;
            FocusInput();
        }
        else if (e.Key == Key.Escape)
        {
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
