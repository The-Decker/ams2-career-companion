using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Companion.ViewModels.Hub;

namespace Companion.App.Views;

/// <summary>The Paddock lens: presentation-only tab and cross-link behavior over the read-only model.</summary>
public partial class PaddockView : UserControl
{
    private PaddockViewModel? _viewModel;

    public PaddockView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
            return;

        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        SyncDriverTeamTab();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = e.NewValue as PaddockViewModel;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        SyncDriverTeamTab();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PaddockViewModel.ShowTeams))
            SyncDriverTeamTab();
    }

    private void SyncDriverTeamTab()
    {
        if (_viewModel is not null && PaddockTabs.SelectedIndex != 2)
            PaddockTabs.SelectedIndex = _viewModel.ShowTeams ? 1 : 0;
    }

    private void OnPaddockTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not PaddockViewModel vm || sender is not TabControl tabs)
            return;

        if (tabs.SelectedIndex == 0)
            vm.ShowDriversCommand.Execute(null);
        else if (tabs.SelectedIndex == 1)
            vm.ShowTeamsListCommand.Execute(null);
    }

    private void OnViewTeamClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PaddockViewModel vm || sender is not Button { Tag: string teamId })
            return;

        vm.ViewTeamCommand.Execute(teamId);
        PaddockTabs.SelectedIndex = 1;
    }

    private void OnViewDriverClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PaddockViewModel vm || sender is not Button { Tag: string driverName })
            return;

        vm.SelectedDriver = vm.Drivers.FirstOrDefault(driver =>
            string.Equals(driver.Name, driverName, StringComparison.Ordinal));
        PaddockTabs.SelectedIndex = 0;
    }

    private void OnViewSponsorTeamClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PaddockViewModel vm || sender is not Button { Tag: string teamName })
            return;

        vm.SelectedTeam = vm.Teams.FirstOrDefault(team =>
            string.Equals(team.Name, teamName, StringComparison.Ordinal));
        if (vm.SelectedTeam is not null)
            PaddockTabs.SelectedIndex = 1;
    }
}
