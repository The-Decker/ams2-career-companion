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
/// Result entry: one text box, one list, the whole keyboard grammar. This code-behind is
/// pure UI mechanics — key routing to viewmodel commands, focus pinning on the input box,
/// the 1 Hz footer-timer tick, and the double-click mouse fallback. Every grammar rule
/// lives (tested) in ResultEntryViewModel.
/// </summary>
public partial class ResultEntryView : UserControl
{
    private readonly DispatcherTimer _timer;

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
    }

    private ResultEntryViewModel? ViewModel => DataContext as ResultEntryViewModel;

    // ---------- keyboard grammar routing (Enter / Tab / Esc / F8 / Ctrl+Z) ----------

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm)
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

    private void OnPreviewMouseUp(object sender, MouseButtonEventArgs e) => FocusInput();

    private void FocusInput() =>
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            InputBox.Focus();
            Keyboard.Focus(InputBox);
        });

    // ---------- mouse fallback: double-click assigns / marks ----------

    private void OnCandidateDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // The first click already moved SelectedCandidateIndex (two-way binding).
        ViewModel?.SubmitCommand.Execute(null);
        e.Handled = true;
    }

    private void OnRemainingDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is not { } vm ||
            (sender as FrameworkElement)?.DataContext is not GridSeat seat)
        {
            return;
        }

        // Type the seat's most specific token for it, select it among the candidates, submit.
        vm.Input = seat.Number is { Length: > 0 } number ? number : Surname(seat.DriverName);
        for (int i = 0; i < vm.Candidates.Count; i++)
        {
            if (vm.Candidates[i].DriverId == seat.DriverId)
            {
                vm.SelectedCandidateIndex = i;
                vm.SubmitCommand.Execute(null);
                break;
            }
        }
        e.Handled = true;
    }

    private static string Surname(string driverName)
    {
        int i = driverName.LastIndexOf(' ');
        return i < 0 ? driverName : driverName[(i + 1)..];
    }

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
