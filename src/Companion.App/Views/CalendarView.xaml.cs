using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Companion.ViewModels.Hub;
using Companion.ViewModels.Services;

namespace Companion.App.Views;

/// <summary>The Calendar lens: a compact selectable season route beside one full round dossier.
/// Selection is presentation state, kept in the view so the shared Calendar VM/data contract stays
/// untouched. A refresh preserves the viewed round; a fresh view opens on the upcoming round.</summary>
public partial class CalendarView : UserControl
{
    private CalendarViewModel? _calendar;
    private string? _selectedRoundLabel;
    private bool _selectionQueued;

    public CalendarView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Attach(DataContext as CalendarViewModel);
        QueueSelection();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => Attach(null);

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsLoaded)
        {
            Attach(e.NewValue as CalendarViewModel);
            QueueSelection();
        }
    }

    private void Attach(CalendarViewModel? calendar)
    {
        if (ReferenceEquals(_calendar, calendar))
            return;

        if (_calendar is not null)
            _calendar.Rounds.CollectionChanged -= OnRoundsChanged;

        _calendar = calendar;
        if (_calendar is not null)
            _calendar.Rounds.CollectionChanged += OnRoundsChanged;
    }

    private void OnRoundsChanged(object? sender, NotifyCollectionChangedEventArgs e) => QueueSelection();

    private void OnRoundSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RoundList.SelectedItem is CalendarRoundViewModel selected)
            _selectedRoundLabel = selected.RoundLabel;
    }

    private void QueueSelection()
    {
        if (_selectionQueued)
            return;

        _selectionQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _selectionQueued = false;
            EnsureSelection();
        }));
    }

    private void EnsureSelection()
    {
        if (RoundList.SelectedItem is CalendarRoundViewModel selected)
        {
            _selectedRoundLabel = selected.RoundLabel;
            return;
        }

        if (_calendar?.Rounds.Count is not > 0)
            return;

        CalendarRoundViewModel? target = _selectedRoundLabel is { Length: > 0 } remembered
            ? _calendar.Rounds.FirstOrDefault(round => string.Equals(
                round.RoundLabel, remembered, StringComparison.Ordinal))
            : null;

        // A newly opened Calendar starts at the current race rather than forcing the player to
        // walk past completed rounds. The view-only lookup is also valid in a torn-off window.
        if (target is null && FindSession() is { } session)
        {
            var upcoming = session.SeasonSchedule().FirstOrDefault(
                round => round.Status == SeasonRoundStatus.Next);
            if (upcoming is not null)
            {
                string label = $"R{upcoming.Round}";
                target = _calendar.Rounds.FirstOrDefault(round => string.Equals(
                    round.RoundLabel, label, StringComparison.Ordinal));
            }
        }

        target ??= _calendar.Rounds[0];
        RoundList.SelectedItem = target;
        RoundList.ScrollIntoView(target);
    }

    private ICareerSession? FindSession()
    {
        for (DependencyObject? current = this; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is HubView { DataContext: HubViewModel hub })
                return hub.Home.Session;
        }

        return Window.GetWindow(this)?.Tag is HubViewModel tearOff ? tearOff.Home.Session : null;
    }
}
