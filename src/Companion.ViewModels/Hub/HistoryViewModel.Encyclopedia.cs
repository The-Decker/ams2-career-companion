using System.Collections.ObjectModel;
using System.Globalization;
using Companion.Core.Determinism;
using Companion.Core.HistoryArchive;
using Companion.ViewModels.Services;

namespace Companion.ViewModels.Hub;

public sealed record EraCardViewModel(
    string Key, string Name, string YearsLabel, string Overview,
    IReadOnlyList<string> Characteristics, string EngineTrends, string SafetyContext,
    IReadOnlyList<string> RegulationChanges, string Legacy, string SourcesLabel);

public sealed record SubjectCardViewModel(
    string Id, string Title, string CategoryLabel, string Summary,
    IReadOnlyList<string> Body, string YearsLabel, bool IsComplete, string SourcesLabel)
{
    public string CompletenessLabel => IsComplete ? "" : "PARTIALLY DOCUMENTED";
}

public sealed record TimelineEntryCardViewModel(
    int Year, string CategoryLabel, string Title, string Summary, string RelatedKey)
{
    public string ProvenanceLabel => "HISTORICAL RECORD";
}

public sealed record DriverProfileCardViewModel(
    string Name, string YearsLabel, string StatLine, string TitlesLabel, string StintsLabel)
{
    public bool HasTitles => TitlesLabel.Length > 0;
}

public sealed record TeamProfileCardViewModel(
    string Name, string YearsLabel, string StatLine, string LineageLabel, bool IsComplete)
{
    public bool HasLineage => LineageLabel.Length > 0;
    public string CompletenessLabel => IsComplete ? "" : "IDENTITY PARTIALLY DOCUMENTED";
}

public sealed record CircuitProfileCardViewModel(
    string LayoutId, string Name, string Place, string SpecLabel, int EditionCount,
    IReadOnlyList<string> Facts);

public sealed record DivergenceRowViewModel(
    string RoundLabel, string Venue, string KindLabel,
    string HistoricalLine, string CareerLine, bool IsAlternate);

/// <summary>
/// The interactive encyclopedia half of History: eras, subjects, verified timeline, entity
/// browsers, deterministic date-aware featured picks, the unified archive search, and the
/// real-history-vs-career-universe comparison. All read-side; the reference content is static
/// per session and built lazily once; divergence follows the archive refresh token.
/// </summary>
public sealed partial class HistoryViewModel
{
    private readonly ObservableCollection<EraCardViewModel> _eras = [];
    private readonly ObservableCollection<SubjectCardViewModel> _subjects = [];
    private readonly ObservableCollection<TimelineEntryCardViewModel> _timelineEntries = [];
    private readonly ObservableCollection<DriverProfileCardViewModel> _topDrivers = [];
    private readonly ObservableCollection<TeamProfileCardViewModel> _topTeams = [];
    private readonly ObservableCollection<CircuitProfileCardViewModel> _topCircuits = [];
    private readonly ObservableCollection<ArchiveSearchResult> _archiveSearchResults = [];
    private readonly ObservableCollection<DivergenceRowViewModel> _divergenceRows = [];
    private bool _encyclopediaBuilt;
    private ArchiveSearchIndex? _searchIndex;
    private string _archiveSearchText = "";
    private string _divergenceChampionLine = "";
    private EraCardViewModel? _featuredEra;
    private DriverProfileCardViewModel? _featuredDriver;
    private TeamProfileCardViewModel? _featuredTeam;

    public ObservableCollection<EraCardViewModel> Eras
    {
        get { EnsureEncyclopedia(); return _eras; }
    }

    public ObservableCollection<SubjectCardViewModel> Subjects
    {
        get { EnsureEncyclopedia(); return _subjects; }
    }

    public ObservableCollection<TimelineEntryCardViewModel> TimelineEntries
    {
        get { EnsureEncyclopedia(); return _timelineEntries; }
    }

    public ObservableCollection<DriverProfileCardViewModel> TopDrivers
    {
        get { EnsureEncyclopedia(); return _topDrivers; }
    }

    public ObservableCollection<TeamProfileCardViewModel> TopTeams
    {
        get { EnsureEncyclopedia(); return _topTeams; }
    }

    public ObservableCollection<CircuitProfileCardViewModel> TopCircuits
    {
        get { EnsureEncyclopedia(); return _topCircuits; }
    }

    public ObservableCollection<ArchiveSearchResult> ArchiveSearchResults => _archiveSearchResults;

    public ObservableCollection<DivergenceRowViewModel> DivergenceRows
    {
        get { EnsureEncyclopedia(); return _divergenceRows; }
    }

    public EraCardViewModel? FeaturedEra
    {
        get { EnsureEncyclopedia(); return _featuredEra; }
    }

    public DriverProfileCardViewModel? FeaturedDriver
    {
        get { EnsureEncyclopedia(); return _featuredDriver; }
    }

    public TeamProfileCardViewModel? FeaturedTeam
    {
        get { EnsureEncyclopedia(); return _featuredTeam; }
    }

    public bool HasEncyclopedia
    {
        get { EnsureEncyclopedia(); return _eras.Count > 0; }
    }

    public bool HasDivergence
    {
        get { EnsureEncyclopedia(); return _divergenceRows.Count > 0; }
    }

    public string DivergenceChampionLine
    {
        get { EnsureEncyclopedia(); return _divergenceChampionLine; }
    }

    public bool HasArchiveSearchResults => _archiveSearchResults.Count > 0;

    public bool IsArchiveSearchEmpty =>
        _archiveSearchText.Trim().Length >= 2 && _archiveSearchResults.Count == 0;

    /// <summary>The unified News+History search box (debounce lives in the view's binding;
    /// the index itself is built once per session).</summary>
    public string ArchiveSearchText
    {
        get => _archiveSearchText;
        set
        {
            value ??= "";
            if (!SetProperty(ref _archiveSearchText, value))
                return;

            EnsureEncyclopedia();
            _archiveSearchResults.Clear();
            if (_searchIndex is not null)
            {
                foreach (var result in _searchIndex.Search(value))
                    _archiveSearchResults.Add(result);
            }
            OnPropertyChanged(nameof(HasArchiveSearchResults));
            OnPropertyChanged(nameof(IsArchiveSearchEmpty));
        }
    }

    private void EnsureEncyclopedia()
    {
        if (_encyclopediaBuilt)
            return;
        _encyclopediaBuilt = true;

        var archive = _session.HistoryArchive();
        var reference = archive.Reference;

        foreach (var era in reference.Eras)
        {
            _eras.Add(new EraCardViewModel(
                era.Key, era.Name, $"{era.FromYear}-{era.ToYear}", era.Overview,
                era.DefiningCharacteristics, era.EngineTrends, era.SafetyContext,
                era.RegulationChanges, era.Legacy, SourcesLabel(era.Sources)));
        }

        foreach (var subject in reference.Subjects)
        {
            _subjects.Add(new SubjectCardViewModel(
                subject.Id, subject.Title, subject.Category.ToUpperInvariant(), subject.Summary,
                subject.Body,
                subject.ToYear is { } to ? $"{subject.FromYear}-{to}" : $"{subject.FromYear}-",
                subject.IsComplete, SourcesLabel(subject.Sources)));
        }

        foreach (var entry in archive.Timeline)
        {
            _timelineEntries.Add(new TimelineEntryCardViewModel(
                entry.Year, entry.Category.ToUpperInvariant(), entry.Title, entry.Summary,
                entry.RelatedKey));
        }

        foreach (var driver in archive.Drivers.Take(30))
        {
            _topDrivers.Add(DriverCard(driver));
        }

        foreach (var team in archive.Teams.Take(20))
        {
            _topTeams.Add(TeamCard(team));
        }

        foreach (var circuit in archive.Circuits.Take(24))
        {
            _topCircuits.Add(new CircuitProfileCardViewModel(
                circuit.LayoutId, circuit.Name, circuit.Place,
                circuit.LengthKm.Length > 0
                    ? $"{circuit.LengthKm} km{(circuit.Turns is { } t ? $" · {t} turns" : "")}"
                    : "",
                circuit.Editions.Count, circuit.Facts));
        }

        BuildFeatured(archive);
        BuildDivergence();

        _searchIndex = ArchiveSearchIndex.Build(_session.NewsroomFeed(), _session.StoryThreads(), archive);
    }

    /// <summary>Deterministic, date-aware featured rotation: seeded by career identity + the
    /// calendar DAY, so the page holds still all day and quietly rotates tomorrow.</summary>
    private void BuildFeatured(HistoryArchiveIndex archive)
    {
        if (archive.Reference.Eras.Count == 0)
            return;

        var daySeed = StableHash.Fnv1a64(string.Create(CultureInfo.InvariantCulture,
            $"{_session.Summary.CareerName}|{DateTime.Today:yyyy-MM-dd}"));

        _featuredEra = _eras.Count > 0 ? _eras[(int)(daySeed % (ulong)_eras.Count)] : null;
        if (archive.Drivers.Count > 0)
        {
            var pool = Math.Min(archive.Drivers.Count, 40);
            _featuredDriver = DriverCard(archive.Drivers[(int)(daySeed / 7 % (ulong)pool)]);
        }
        if (archive.Teams.Count > 0)
        {
            var pool = Math.Min(archive.Teams.Count, 25);
            _featuredTeam = TeamCard(archive.Teams[(int)(daySeed / 13 % (ulong)pool)]);
        }
    }

    private void BuildDivergence()
    {
        var ordinal = _session.CareerTimeline().Seasons.Count;
        if (ordinal == 0 || _session.SeasonDivergence(ordinal) is not { } report)
            return;

        foreach (var row in report.Rounds)
        {
            _divergenceRows.Add(new DivergenceRowViewModel(
                $"R{row.Round}",
                row.Venue,
                row.Kind switch
                {
                    DivergenceKind.AlternateOutcome => "ALTERNATE OUTCOME",
                    DivergenceKind.UnchangedEvent => "UNCHANGED",
                    DivergenceKind.NotYetRaced => "NOT YET RACED",
                    _ => "NOT DOCUMENTED",
                },
                row.HistoricalWinner.Length > 0
                    ? $"{row.HistoricalWinner}{(row.HistoricalWinnerTeam.Length > 0 ? $" · {row.HistoricalWinnerTeam}" : "")}"
                    : "—",
                row.CareerWinner.Length > 0
                    ? $"{row.CareerWinner}{(row.CareerWinnerTeam.Length > 0 ? $" · {row.CareerWinnerTeam}" : "")}"
                    : "—",
                row.Kind == DivergenceKind.AlternateOutcome));
        }

        _divergenceChampionLine = report.ChampionChanged switch
        {
            true => $"Divergence point: this universe crowned {report.CareerChampion}; the historical record shows {report.HistoricalChampion}.",
            false => $"History held: {report.HistoricalChampion} champion in both timelines.",
            null => report.HistoricalChampion.Length > 0
                ? $"Historical record: {report.HistoricalChampion} took the {report.SeasonYear} title. This universe's verdict is still being written."
                : "",
        };
    }

    private static DriverProfileCardViewModel DriverCard(DriverHistoryProfile driver) => new(
        driver.Name,
        $"{driver.FirstYear}-{driver.LastYear}",
        $"{driver.Starts} starts · {driver.Wins} wins · {driver.Podiums} podiums · {driver.FastestLaps} fastest laps",
        driver.ChampionshipYears.Count > 0
            ? "World Champion " + string.Join(", ", driver.ChampionshipYears)
            : "",
        string.Join(" · ", driver.Stints.Select(s =>
            s.FirstYear == s.LastYear ? $"{s.Team} ({s.FirstYear})" : $"{s.Team} ({s.FirstYear}-{s.LastYear})")));

    private static TeamProfileCardViewModel TeamCard(TeamHistoryProfile team) => new(
        team.Canonical,
        $"{team.FirstYear}-{team.LastYear}",
        $"{team.Wins} wins · {team.DriversFielded} drivers"
            + (team.ConstructorsChampionshipYears.Count > 0
                ? $" · constructors' titles {team.ConstructorsChampionshipYears.Count}"
                : ""),
        string.Join("; ", team.Lineage.Select(l => $"{l.Relationship} → {l.RelatedTo}")),
        team.IsComplete);

    private static string SourcesLabel(IReadOnlyList<string> sources) =>
        sources.Count == 0 ? "" : "Sources: " + string.Join(" · ", sources);
}
