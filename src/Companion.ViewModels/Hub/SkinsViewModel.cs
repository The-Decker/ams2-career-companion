using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Companion.Ams2.Skins;
using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.ViewModels.Services;
using Companion.ViewModels.Wizard;

namespace Companion.ViewModels.Hub;

/// <summary>The visual tone of a next-race livery status.</summary>
public enum SkinTone
{
    Good,
    Neutral,
    Warn,
}

/// <summary>One selectable, read-only car in the pre-qualifying grid preview.</summary>
public sealed record SkinRow
{
    public string DriverId { get; init; } = "";
    public required string DriverName { get; init; }
    public string TeamId { get; init; } = "";
    public required string TeamName { get; init; }
    public string? Number { get; init; }
    public string SkinSlot { get; init; } = "";
    public required string LiveryName { get; init; }
    public required bool IsPlayer { get; init; }
    public required string StatusLabel { get; init; }
    public required SkinTone Tone { get; init; }
    public string Detail { get; init; } = "";
    public string? PortraitKey { get; init; }
    public string? CarKey { get; init; }
    public string? TopCarKey => CarKey;
    public string? TeamLogoKey => string.IsNullOrEmpty(TeamId) ? null : TeamId;
}

/// <summary>A compact read-only identity for a car outside the qualifying cut.</summary>
public sealed record GridPreviewDnqRow(string DriverName, string TeamName, string? Number)
{
    public string NumberLabel => string.IsNullOrEmpty(Number) ? "" : "#" + Number;
}

/// <summary>
/// Read-only pre-qualifying reference for the next race. Selecting or cycling a car changes only the
/// preview; the feature has no configuration or external file-writing commands.
/// </summary>
public sealed partial class SkinsViewModel : ObservableObject
{
    private readonly ICareerSession _session;

    public SkinsViewModel(ICareerSession session)
    {
        _session = session;
        Refresh();
    }

    /// <summary>Cars that made the next-race field, in resolved grid order.</summary>
    public ObservableCollection<SkinRow> Cars { get; } = [];

    /// <summary>Cars outside the seeded qualifying cut for this round.</summary>
    public ObservableCollection<GridPreviewDnqRow> DidNotQualify { get; } = [];

    [ObservableProperty]
    private string _summary = "";

    [ObservableProperty]
    private string _gridLabel = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedCar))]
    private SkinRow? _selectedCar;

    [ObservableProperty]
    private string _selectedPositionLabel = "";

    public bool HasSelectedCar => SelectedCar is not null;

    public bool HasDnq => DidNotQualify.Count > 0;

    public string DnqHeader => $"DID NOT QUALIFY  •  {DidNotQualify.Count}";

    /// <summary>Legacy grid-editor overrides (renamed drivers / rebound liveries) this career still
    /// carries from before the read-only Grid Preview. Staging still applies them, so they must be
    /// visible and clearable — never a silent hidden edit to the staged AI file.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStagingOverrides), nameof(StagingOverridesNote))]
    private int _stagingOverrideCount;

    public bool HasStagingOverrides => StagingOverrideCount > 0;

    public string StagingOverridesNote => StagingOverrideCount == 0
        ? ""
        : $"{StagingOverrideCount} legacy grid edit{(StagingOverrideCount == 1 ? "" : "s")} still apply at staging (renamed drivers / rebound skins from the old grid editor).";

    /// <summary>Removes every legacy grid-editor override, so the next stage writes the pack's
    /// authored grid untouched.</summary>
    [RelayCommand]
    private void ClearStagingOverrides()
    {
        foreach (var key in _session.SeatStagingOverrides().Keys.ToList())
            _session.SetSeatStagingOverride(key, new SeatStagingOverride());
        Refresh();
    }

    public void Refresh()
    {
        string? selectedLivery = SelectedCar?.LiveryName;
        var plan = _session.CurrentSkinAssignments();
        var authoredDriversByLivery = BuildAuthoredDriversByLivery();

        Cars.Clear();
        foreach (var assignment in plan.Assignments)
            Cars.Add(ToRow(assignment, authoredDriversByLivery));

        int currentRound = _session.Summary.CurrentRound;
        DidNotQualify.Clear();
        var schedule = _session.SeasonSchedule().FirstOrDefault(entry => entry.Round == currentRound);
        if (schedule is not null)
        {
            foreach (var entry in schedule.Dnq)
                DidNotQualify.Add(new GridPreviewDnqRow(entry.Name, entry.TeamName, entry.Number));
        }

        Summary = plan.IsEmpty
            ? "No next-race grid is available to preview."
            : DidNotQualify.Count > 0
                ? $"{plan.Assignments.Count} cars made the next-race field; {DidNotQualify.Count} missed the cut."
                : $"{plan.Assignments.Count} cars are expected on the next-race grid.";
        GridLabel = plan.IsEmpty
            ? ""
            : $"ROUND {currentRound}  •  {plan.Assignments.Count} GRID CARS  •  {plan.Ams2Class}";

        SelectedCar = Cars.FirstOrDefault(car =>
                string.Equals(car.LiveryName, selectedLivery, StringComparison.Ordinal))
            ?? Cars.FirstOrDefault(car => car.IsPlayer)
            ?? Cars.FirstOrDefault();

        UpdateSelectedPositionLabel();
        StagingOverrideCount = _session.SeatStagingOverrides().Count;
        OnPropertyChanged(nameof(HasSelectedCar));
        OnPropertyChanged(nameof(HasDnq));
        OnPropertyChanged(nameof(DnqHeader));
    }

    partial void OnSelectedCarChanged(SkinRow? value) => UpdateSelectedPositionLabel();

    [RelayCommand]
    private void PreviousCar() => MoveSelection(-1);

    [RelayCommand]
    private void NextCar() => MoveSelection(1);

    private void MoveSelection(int delta)
    {
        if (Cars.Count == 0)
            return;

        int current = SelectedCar is null ? 0 : Cars.IndexOf(SelectedCar);
        if (current < 0)
            current = 0;
        SelectedCar = Cars[(current + delta + Cars.Count) % Cars.Count];
    }

    private void UpdateSelectedPositionLabel()
    {
        int index = SelectedCar is null ? -1 : Cars.IndexOf(SelectedCar);
        SelectedPositionLabel = index < 0 ? "" : $"CAR {index + 1} OF {Cars.Count}";
    }

    private IReadOnlyDictionary<string, string> BuildAuthoredDriversByLivery()
    {
        int round = _session.Summary.CurrentRound;
        return _session.Pack.Entries
            .GroupBy(entry => entry.Ams2LiveryName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (group.FirstOrDefault(entry =>
                    RoundsRange.TryParse(entry.Rounds, out var range) && range.Contains(round))
                    ?? group.First()).DriverId,
                StringComparer.Ordinal);
    }

    private static SkinRow ToRow(
        SkinAssignment assignment,
        IReadOnlyDictionary<string, string> authoredDriversByLivery)
    {
        var (label, tone, detail) = assignment.Status switch
        {
            SkinStatus.CustomSkin => (
                "Custom skin",
                SkinTone.Good,
                assignment.VehicleFolder is { Length: > 0 } folder ? $"Installed under {folder}" : "Active custom livery"),
            SkinStatus.InstalledInactive => (
                "Installed inactive",
                SkinTone.Warn,
                "Skin files are present, but this livery has no active in-game slot"),
            SkinStatus.StockDefault => (
                "Default livery",
                SkinTone.Neutral,
                "AMS2 built-in paint; no custom skin is associated with this entry"),
            SkinStatus.NameOnly => (
                "Default skin",
                SkinTone.Neutral,
                "The driver name binds, but no matching custom skin is installed"),
            SkinStatus.Unbound => (
                "Unbound livery",
                SkinTone.Warn,
                assignment.NearMiss is { Length: > 0 } near
                    ? $"No exact installed match; nearest name is “{near}”"
                    : "No matching installed livery, driver-name entry, or stock name"),
            _ => ("Unknown", SkinTone.Neutral, "No skin status is available"),
        };

        return new SkinRow
        {
            DriverId = assignment.DriverId,
            DriverName = assignment.DriverName,
            TeamId = assignment.TeamId,
            TeamName = assignment.TeamName,
            Number = assignment.Number,
            SkinSlot = assignment.SkinSlot,
            LiveryName = assignment.LiveryName,
            IsPlayer = assignment.IsPlayer,
            StatusLabel = label,
            Tone = tone,
            Detail = detail,
            PortraitKey = assignment.IsPlayer
                ? GridSeatChoice.PlayerImageKey(assignment.TeamId)
                : string.IsNullOrEmpty(assignment.DriverId) ? null : assignment.DriverId,
            CarKey = ResolveCarKey(assignment, authoredDriversByLivery),
        };
    }

    private static string? ResolveCarKey(
        SkinAssignment assignment,
        IReadOnlyDictionary<string, string> authoredDriversByLivery)
    {
        if (!string.IsNullOrEmpty(assignment.DriverId) &&
            !string.Equals(
                assignment.DriverId,
                RoundGridResolver.SyntheticPlayerDriverId,
                StringComparison.Ordinal))
        {
            return assignment.DriverId;
        }

        return authoredDriversByLivery.GetValueOrDefault(assignment.LiveryName);
    }
}
