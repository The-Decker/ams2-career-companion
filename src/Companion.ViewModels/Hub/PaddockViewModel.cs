using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Companion.ViewModels.Services;

namespace Companion.ViewModels.Hub;

/// <summary>
/// The hub's Paddock lens (SMGP driver/team preview): the whole grid's drivers — each with a short bio,
/// predetermined career stats and their team — and every team with its motto, history and quotes. A
/// thin read-only wrapper over <see cref="ICareerSession.SmgpPaddock"/>, re-projected after every applied
/// round like the other lenses. Present as a rail tab only for an SMGP career with the reference data
/// loaded (<see cref="HasPaddock"/>).
/// </summary>
public sealed partial class PaddockViewModel : ObservableObject
{
    private readonly ICareerSession _session;

    public PaddockViewModel(ICareerSession session)
    {
        _session = session;
        Refresh();
    }

    /// <summary>The grid's drivers, most-storied first.</summary>
    public ObservableCollection<SmgpDriverCard> Drivers { get; } = [];

    /// <summary>The grid's teams, highest prestige first.</summary>
    public ObservableCollection<SmgpTeamCard> Teams { get; } = [];

    /// <summary>True when there is a paddock to show — the hub adds the tab only then.</summary>
    public bool HasPaddock => Drivers.Count > 0;

    /// <summary>The view mode: false = the DRIVERS list, true = the TEAMS list.</summary>
    [ObservableProperty]
    private bool _showTeams;

    /// <summary>The driver whose dossier the detail pane shows.</summary>
    [ObservableProperty]
    private SmgpDriverCard? _selectedDriver;

    /// <summary>The team whose dossier the detail pane shows.</summary>
    [ObservableProperty]
    private SmgpTeamCard? _selectedTeam;

    public void Refresh()
    {
        var model = _session.SmgpPaddock();

        // Preserve the current selection across a refresh (by id) so a re-project after a round doesn't
        // yank the pane back to the top of the list.
        string? keepDriver = SelectedDriver?.DriverId;
        string? keepTeam = SelectedTeam?.TeamId;

        Drivers.Clear();
        Teams.Clear();
        if (model is not null)
        {
            foreach (var d in model.Drivers) Drivers.Add(d);
            foreach (var t in model.Teams) Teams.Add(t);
        }

        SelectedDriver = Drivers.FirstOrDefault(d => string.Equals(d.DriverId, keepDriver, StringComparison.Ordinal))
            ?? Drivers.FirstOrDefault();
        SelectedTeam = Teams.FirstOrDefault(t => string.Equals(t.TeamId, keepTeam, StringComparison.Ordinal))
            ?? Teams.FirstOrDefault();
        OnPropertyChanged(nameof(HasPaddock));
    }

    /// <summary>Switch the list to the DRIVERS view.</summary>
    [RelayCommand]
    private void ShowDrivers() => ShowTeams = false;

    /// <summary>Switch the list to the TEAMS view.</summary>
    [RelayCommand]
    private void ShowTeamsList() => ShowTeams = true;

    /// <summary>Jump from a driver's dossier to their team's card (the "their team" link).</summary>
    [RelayCommand]
    private void ViewTeam(string? teamId)
    {
        if (string.IsNullOrEmpty(teamId))
            return;
        var team = Teams.FirstOrDefault(t => string.Equals(t.TeamId, teamId, StringComparison.Ordinal));
        if (team is not null)
        {
            SelectedTeam = team;
            ShowTeams = true;
        }
    }
}
