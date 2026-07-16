using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.Input;

namespace Companion.ViewModels.Hub;

/// <summary>
/// The presentation-ready History archive contract. Kept as a partial so the established scrapbook,
/// historical-reference and inspector members remain untouched. The projection is invalidated by the
/// existing Seasons refresh collection, then rebuilt solely from the session's existing read seams.
/// </summary>
public sealed partial class HistoryViewModel
{
    private readonly ObservableCollection<HistoryEventViewModel> _events = [];
    private readonly ObservableCollection<HistoryRaceArchiveItemViewModel> _raceArchive = [];
    private readonly ObservableCollection<HistoryRaceArchiveItemViewModel> _filteredRaces = [];
    private readonly ObservableCollection<HistoryRaceFilterViewModel> _raceFilters = [];
    private readonly ObservableCollection<HistoryDispatchViewModel> _latestDispatches = [];

    private HistoryHeroViewModel _hero = HistoryHeroViewModel.Empty;
    private HistoryRaceFilterViewModel? _selectedRaceFilter;
    private string _searchText = "";
    private bool _isLegacyLimited;
    private bool _archiveBuilt;
    private bool _archiveRebuilding;
    private bool _archiveSubscribed;
    private ArchiveRefreshToken _archiveToken;

    public HistoryHeroViewModel Hero
    {
        get
        {
            EnsureArchiveProjection();
            return _hero;
        }
    }

    /// <summary>Every race and typed SMGP milestone in chronological order (oldest first).</summary>
    public ObservableCollection<HistoryEventViewModel> Events
    {
        get
        {
            EnsureArchiveProjection();
            return _events;
        }
    }

    /// <summary>Every recorded race in the career, flattened newest first.</summary>
    public ObservableCollection<HistoryRaceArchiveItemViewModel> RaceArchive
    {
        get
        {
            EnsureArchiveProjection();
            return _raceArchive;
        }
    }

    /// <summary>The archive after applying <see cref="SelectedRaceFilter"/> and
    /// <see cref="SearchText"/>.</summary>
    public ObservableCollection<HistoryRaceArchiveItemViewModel> FilteredRaces
    {
        get
        {
            EnsureArchiveProjection();
            return _filteredRaces;
        }
    }

    public ObservableCollection<HistoryRaceFilterViewModel> RaceFilters
    {
        get
        {
            EnsureArchiveProjection();
            return _raceFilters;
        }
    }

    public HistoryRaceFilterViewModel? SelectedRaceFilter
    {
        get
        {
            EnsureArchiveProjection();
            return _selectedRaceFilter;
        }
        set
        {
            EnsureArchiveProjection();
            if (SetProperty(ref _selectedRaceFilter, value))
                ApplyRaceFilters();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            value ??= "";
            if (SetProperty(ref _searchText, value))
                ApplyRaceFilters();
        }
    }

    public ObservableCollection<HistoryDispatchViewModel> LatestDispatches
    {
        get
        {
            EnsureArchiveProjection();
            return _latestDispatches;
        }
    }

    public bool HasAnyRace
    {
        get
        {
            EnsureArchiveProjection();
            return _raceArchive.Count > 0 || Seasons.Any(season => season.CanInspect);
        }
    }

    public bool IsFresh => !HasAnyRace;

    /// <summary>History is currently a synchronous read projection; the explicit false state is
    /// nevertheless part of the stable GUI contract.</summary>
    public bool IsLoading => false;

    public bool IsLegacyLimited
    {
        get
        {
            EnsureArchiveProjection();
            return _isLegacyLimited;
        }
    }

    public bool HasLatestDispatches
    {
        get
        {
            EnsureArchiveProjection();
            return _latestDispatches.Count > 0;
        }
    }

    public bool HasEvents
    {
        get
        {
            EnsureArchiveProjection();
            return _events.Count > 0;
        }
    }

    public bool HasActiveRaceFilter
    {
        get
        {
            EnsureArchiveProjection();
            return HasActiveRaceFilterCore();
        }
    }

    /// <summary>A populated archive whose active search/filter has no matches. Fresh careers use
    /// <see cref="IsFresh"/> instead.</summary>
    public bool IsRaceFilterEmpty
    {
        get
        {
            EnsureArchiveProjection();
            return _raceArchive.Count > 0 && HasActiveRaceFilterCore() && _filteredRaces.Count == 0;
        }
    }

    private bool CanClearRaceFilters() => HasActiveRaceFilter;

    [RelayCommand(CanExecute = nameof(CanClearRaceFilters))]
    private void ClearRaceFilters()
    {
        if (!HasActiveRaceFilterCore())
            return;

        _searchText = "";
        OnPropertyChanged(nameof(SearchText));
        _selectedRaceFilter = _raceFilters.FirstOrDefault(filter => filter.Kind == HistoryRaceFilterKind.All);
        OnPropertyChanged(nameof(SelectedRaceFilter));
        ApplyRaceFilters();
    }

    private void EnsureArchiveProjection()
    {
        if (_archiveRebuilding)
            return;

        EnsureArchiveSubscription();
        var summary = _session.Summary;
        var token = new ArchiveRefreshToken(
            summary.SeasonYear,
            summary.CurrentRound,
            summary.SeasonComplete,
            summary.PlayerPosition,
            summary.Reputation,
            summary.Opi);
        if (_archiveBuilt && token == _archiveToken)
            return;

        _archiveRebuilding = true;
        try
        {
            BuildArchiveProjection(summary);
            _archiveToken = token;
            _archiveBuilt = true;
        }
        finally
        {
            _archiveRebuilding = false;
        }
    }

    private void EnsureArchiveSubscription()
    {
        if (_archiveSubscribed)
            return;
        Seasons.CollectionChanged += OnArchiveSourceCollectionChanged;
        ArchivedArticles.CollectionChanged += OnArchiveSourceCollectionChanged;
        _archiveSubscribed = true;
    }

    private void OnArchiveSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _archiveBuilt = false;
        NotifyArchivePropertiesChanged();
    }

    private void BuildArchiveProjection(Services.CareerSummary summary)
    {
        var timeline = _session.CareerTimeline();
        var paddock = _session.SmgpPaddock();
        var player = paddock?.Drivers.FirstOrDefault(driver => driver.IsPlayer);
        string? rivalId = _session.CurrentSmgpRivalDriverId();
        var rival = rivalId is null
            ? null
            : paddock?.Drivers.FirstOrDefault(driver =>
                string.Equals(driver.DriverId, rivalId, StringComparison.Ordinal));
        var smgpDispatches = _session.SmgpDispatches();
        var journalFeed = _session.ReadFeed();

        _hero = HistoryArchiveProjection.BuildHero(summary, timeline, player, rival);
        Replace(_raceArchive, HistoryArchiveProjection.BuildRaceArchive(timeline, player));
        Replace(_events, HistoryArchiveProjection.BuildEvents(
            timeline,
            _raceArchive,
            player?.Timeline ?? [],
            paddock?.Drivers ?? []));
        Replace(_latestDispatches, HistoryArchiveProjection.BuildLatestDispatches(
            smgpDispatches,
            journalFeed,
            timeline,
            summary.SeasonYear));

        _isLegacyLimited = _raceArchive.Count > 0 && (player is null || player.Career is null);
        RebuildRaceFilters();
        NotifyArchivePropertiesChanged();
    }

    private void RebuildRaceFilters()
    {
        string selectedKey = _selectedRaceFilter?.Key ?? "all";
        Replace(_raceFilters, HistoryArchiveProjection.BuildRaceFilters(_raceArchive));
        _selectedRaceFilter = _raceFilters.FirstOrDefault(filter =>
                                  string.Equals(filter.Key, selectedKey, StringComparison.Ordinal))
                              ?? _raceFilters.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedRaceFilter));
        ApplyRaceFilters();
    }

    private void ApplyRaceFilters()
    {
        string search = _searchText.Trim();
        var filter = _selectedRaceFilter?.Kind ?? HistoryRaceFilterKind.All;
        _filteredRaces.Clear();
        foreach (var race in _raceArchive)
        {
            if (HistoryArchiveProjection.MatchesFilter(race, filter) && race.MatchesSearch(search))
                _filteredRaces.Add(race);
        }

        OnPropertyChanged(nameof(HasActiveRaceFilter));
        OnPropertyChanged(nameof(IsRaceFilterEmpty));
        ClearRaceFiltersCommand.NotifyCanExecuteChanged();
    }

    private bool HasActiveRaceFilterCore() =>
        !string.IsNullOrWhiteSpace(_searchText)
        || _selectedRaceFilter is { Kind: not HistoryRaceFilterKind.All };

    private void NotifyArchivePropertiesChanged()
    {
        OnPropertyChanged(nameof(Hero));
        OnPropertyChanged(nameof(Events));
        OnPropertyChanged(nameof(RaceArchive));
        OnPropertyChanged(nameof(FilteredRaces));
        OnPropertyChanged(nameof(RaceFilters));
        OnPropertyChanged(nameof(LatestDispatches));
        OnPropertyChanged(nameof(HasAnyRace));
        OnPropertyChanged(nameof(IsFresh));
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(IsLegacyLimited));
        OnPropertyChanged(nameof(HasLatestDispatches));
        OnPropertyChanged(nameof(HasEvents));
        OnPropertyChanged(nameof(HasActiveRaceFilter));
        OnPropertyChanged(nameof(IsRaceFilterEmpty));
        ClearRaceFiltersCommand.NotifyCanExecuteChanged();
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
            target.Add(item);
    }

    private readonly record struct ArchiveRefreshToken(
        int SeasonYear,
        int CurrentRound,
        bool SeasonComplete,
        int? PlayerPosition,
        double? Reputation,
        double? Opi);
}
