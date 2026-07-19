using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.ViewModels.Hub;
using Companion.ViewModels.Services;
using Companion.ViewModels.Settings;

namespace Companion.ViewModels.Standings;

/// <summary>One drivers- or constructors-table row: gross vs counted plus dropped markers,
/// with the raw sortable values behind the display strings.</summary>
public sealed record StandingsRow(
    string PositionText,
    string CompetitorId,
    string DisplayName,
    string CountedText,
    string GrossText,
    bool HasDroppedRounds,
    string DroppedRoundsText,
    int? Position,
    double CountedValue,
    double GrossValue,
    int DroppedCount,
    string PerRoundText,
    double PerRoundValue)
{
    /// <summary>True when gross differs from counted (drops or adjustments applied).</summary>
    public bool ShowGross => GrossText != CountedText;

    /// <summary>True for the player's own row, the table highlights it. Init-only (not a positional param,
    /// so the record's deconstruction arity is unchanged). Display-only.</summary>
    public bool IsPlayer { get; init; }

    /// <summary>True for the player's currently named SMGP rival's row, highlighted (with the red rival
    /// accent) so the two are easy to track down the table. False off-SMGP. Display-only.</summary>
    public bool IsRival { get; init; }
}

/// <summary>One cell of the Wikipedia-style round matrix: the points that round, dropped-marked,
/// plus the (driver, round) coordinates that make the cell a clickable "Why?" walk-back target.
/// <see cref="DriverId"/>/<see cref="Round"/> are additive with defaults so the existing positional
/// construction (text + dropped) still compiles; an empty driver id marks a cell with no journal
/// behind it (a blank / not-yet-raised cell).</summary>
public sealed record RoundMatrixCell(
    string Text, bool IsDropped, string DriverId = "", int Round = 0)
{
    /// <summary>The click target for this cell's "Why?" inspector, the driver + round it belongs
    /// to. Null when the cell has no driver/round (a blank cell), so the number renders plain.</summary>
    public RoundMatrixCellRef? InspectorRef =>
        DriverId.Length > 0 && Round > 0 ? new RoundMatrixCellRef(DriverId, Round) : null;
}

/// <summary>The coordinates a round-matrix cell click passes to
/// <see cref="StandingsViewModel.OpenCellInspectorCommand"/>: which driver's journal to walk and
/// which round to narrow it to.</summary>
public sealed record RoundMatrixCellRef(string DriverId, int Round);

/// <summary>One driver row of the round matrix; cells align with <see cref="StandingsViewModel.RoundHeaders"/>.</summary>
public sealed record RoundMatrixRow(
    string DriverId,
    string DisplayName,
    IReadOnlyList<RoundMatrixCell> Cells);

/// <summary>Which standings table a tab shows. The int values are the persisted
/// <c>StandingsTabIndex</c> slots, Drivers 0, Constructors 1, Round matrix 2, so a saved
/// index maps straight onto a tab kind even when a hidden tab shifts the visible order.</summary>
public enum StandingsTabKind
{
    Drivers = 0,
    Constructors = 1,
    Matrix = 2,
}

/// <summary>One tab of the standings screen: its kind, header text, and the fixed selection
/// index the view's TabControl uses. Only the tabs that actually apply to this season appear
/// in <see cref="StandingsViewModel.Tabs"/>, the Constructors tab is present only when the
/// season runs a constructors championship (pre-1958 seasons legitimately omit it), and every
/// tab is present only once at least one round has been applied.</summary>
public sealed record StandingsTab(StandingsTabKind Kind, string Header, int Index);

/// <summary>
/// The standings screen: drivers + constructors tabs built from the latest snapshot (gross vs
/// counted points with dropped-round markers), the round matrix (driver × round → that round's
/// points, sourced per snapshot from the RoundScores the snapshot introduced), and the
/// rules-explainer chip derived from the pack's CatalogSeason.
///
/// UX-round additions (contract section 2): click a column header to sort (click again to
/// flip), right-click the header for the column chooser (counted/gross/dropped/points-per-
/// round), both tables share the sort, and the column visibility + selected tab persist
/// through the settings seam.
/// </summary>
public sealed partial class StandingsViewModel : InspectorHostViewModel
{
    /// <summary>Column keys for <see cref="SortByCommand"/> and the header glyphs.</summary>
    public const string ColumnPosition = "position";
    public const string ColumnName = "name";
    public const string ColumnCounted = "counted";
    public const string ColumnGross = "gross";
    public const string ColumnDropped = "dropped";
    public const string ColumnPerRound = "perRound";

    private readonly ISettingsService? _settings;
    private readonly ICareerSession? _session;
    private readonly IReadOnlyList<StandingsRow> _driverRowsBase;
    private readonly IReadOnlyList<StandingsRow> _constructorRowsBase;

    private bool _showCountedColumn;
    private bool _showGrossColumn;
    private bool _showDroppedColumn;
    private bool _showPerRoundColumn;

    public StandingsViewModel(
        IReadOnlyList<StandingsSnapshot> snapshots,
        SeasonPack pack,
        ISettingsService? settings = null,
        ICareerSession? session = null)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(pack);
        _settings = settings;
        _session = session;

        var driverNames = pack.Drivers.ToDictionary(d => d.Id, d => d.Name, StringComparer.Ordinal);
        // Render the player's chosen character name on their row (the drivers tab AND the round matrix
        // both read this map), instead of the historical driver they seated. Null identity = the
        // historical name, exactly as before.
        if (session?.PlayerIdentity() is { } player)
            driverNames[player.DriverId] = player.DisplayName;
        var teamNames = pack.Teams.ToDictionary(t => t.Id, t => t.Name, StringComparer.Ordinal);

        // Flag the player's row and the currently-named SMGP rival's row so the table can highlight both.
        string? playerRowId = session?.PlayerIdentity()?.DriverId;
        string? rivalRowId = session?.CurrentSmgpRivalDriverId();

        var ordered = snapshots.OrderBy(s => s.AfterRound).ToArray();
        var latest = ordered.Length > 0 ? ordered[^1] : null;
        int appliedRounds = ordered.Length;

        HasConstructors = pack.Season.PointsSystem.Constructors is not null;

        _driverRowsBase = latest is null
            ? []
            : latest.Drivers
                .Select(d => Row(
                    d.Position, d.DriverId, driverNames.GetValueOrDefault(d.DriverId, d.DriverId),
                    d.CountedPoints, d.GrossPoints, d.Dropped, appliedRounds) with
                {
                    IsPlayer = playerRowId is { } p && string.Equals(d.DriverId, p, StringComparison.Ordinal),
                    IsRival = rivalRowId is { } r && string.Equals(d.DriverId, r, StringComparison.Ordinal),
                })
                .ToArray();

        _constructorRowsBase = latest?.Constructors is not { } constructors
            ? []
            : constructors
                .Select(c => Row(
                    c.Position, c.ConstructorId, teamNames.GetValueOrDefault(c.ConstructorId, c.ConstructorId),
                    c.CountedPoints, c.GrossPoints, c.Dropped, appliedRounds))
                .ToArray();

        RoundHeaders = ordered.Select(s => $"R{s.AfterRound}").ToArray();
        MatrixRows = BuildMatrix(ordered, latest, driverNames);

        int championshipRounds = pack.Season.Rounds.Count(r => r.Championship);
        RulesParts = BuildRulesParts(pack.Season.PointsSystem, championshipRounds);
        RulesChipText = string.Join(" · ", RulesParts);

        var columns = settings?.Current.StandingsColumns ?? new StandingsColumnSettings();
        _showCountedColumn = columns.ShowCounted;
        _showGrossColumn = columns.ShowGross;
        _showDroppedColumn = columns.ShowDropped;
        _showPerRoundColumn = columns.ShowPerRound;

        // The tab set for this season: Drivers + Round matrix once a round is applied, plus
        // Constructors only when the season runs a constructors championship. The view binds
        // each TabItem's visibility to Show*Tab (all derived from this list) so a tab is never
        // hard-hidden in XAML, the VM alone decides which tabs exist, and the host tests can
        // read exactly what the user can reach. Indexes stay the fixed persistence slots
        // (Drivers 0 / Constructors 1 / Matrix 2) so a hidden Constructors tab leaves gaps
        // rather than renumbering the others.
        bool hasRounds = _driverRowsBase.Count > 0;
        var tabs = new List<StandingsTab>();
        if (hasRounds)
        {
            tabs.Add(new StandingsTab(StandingsTabKind.Drivers, "Drivers", (int)StandingsTabKind.Drivers));
            if (HasConstructors)
                tabs.Add(new StandingsTab(StandingsTabKind.Constructors, "Constructors", (int)StandingsTabKind.Constructors));
            tabs.Add(new StandingsTab(StandingsTabKind.Matrix, "Round matrix", (int)StandingsTabKind.Matrix));
        }
        Tabs = tabs;

        // Tab memory: reopen on the tab last used (0 drivers, 1 constructors, 2 matrix), but
        // never land on a tab this season does not show, a remembered Constructors tab (or a
        // hand-edited out-of-range index) degrades to Drivers, and the round matrix stays
        // reachable. Both mouse and keyboard drive SelectedTabIndex, so this one guard keeps
        // every applicable tab switchable by either input (locked decision 8).
        int savedTab = settings?.Current.StandingsTabIndex ?? 0;
        selectedTabIndex = ResolveInitialTab(savedTab);
    }

    // ---------- tabs (persisted) ----------

    /// <summary>The tabs this season actually shows, in display order: Drivers, then
    /// Constructors (only with a constructors championship), then Round matrix, and only once
    /// a round has been applied. The view renders exactly these; host tests assert against
    /// them.</summary>
    public IReadOnlyList<StandingsTab> Tabs { get; }

    /// <summary>The Drivers tab shows once a round has been applied.</summary>
    public bool ShowDriversTab => Tabs.Any(t => t.Kind == StandingsTabKind.Drivers);

    /// <summary>The Constructors tab shows only for a season with a constructors championship
    /// (and once a round has been applied). Pre-1958 seasons legitimately omit it.</summary>
    public bool ShowConstructorsTab => Tabs.Any(t => t.Kind == StandingsTabKind.Constructors);

    /// <summary>The Round-matrix tab shows once a round has been applied.</summary>
    public bool ShowMatrixTab => Tabs.Any(t => t.Kind == StandingsTabKind.Matrix);

    /// <summary>Snaps a candidate persistence index onto a tab this season shows: the exact
    /// tab when it is present, otherwise the first shown tab (Drivers), otherwise 0.</summary>
    private int ResolveInitialTab(int candidate)
    {
        if (Tabs.Any(t => t.Index == candidate))
            return candidate;
        return Tabs.Count > 0 ? Tabs[0].Index : 0;
    }

    [ObservableProperty]
    private int selectedTabIndex;

    partial void OnSelectedTabIndexChanged(int value) =>
        _settings?.Update(s => s with { StandingsTabIndex = value });

    // ---------- rows (sorted views over the engine order) ----------

    /// <summary>Drivers-tab rows in the current sort order (default: championship order).</summary>
    public IReadOnlyList<StandingsRow> DriverRows => Sorted(_driverRowsBase);

    /// <summary>Constructors-tab rows; empty when the season has no constructors championship.</summary>
    public IReadOnlyList<StandingsRow> ConstructorRows => Sorted(_constructorRowsBase);

    /// <summary>True when the pack's season runs a constructors championship (shows the tab).</summary>
    public bool HasConstructors { get; }

    /// <summary>Column headers of the round matrix: one per applied round ("R1", "R2", ...).</summary>
    public IReadOnlyList<string> RoundHeaders { get; }

    /// <summary>Driver × round matrix; rows ordered like the unsorted championship order.</summary>
    public IReadOnlyList<RoundMatrixRow> MatrixRows { get; }

    /// <summary>The rules-explainer chip: points table, best-N, shared-drive policy, fastest lap.</summary>
    public string RulesChipText { get; }

    /// <summary>The chip's individual sentences, for views that render them as separate lines.</summary>
    public IReadOnlyList<string> RulesParts { get; }

    // ---------- sorting (click a header; click again to flip) ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(DriverRows), nameof(ConstructorRows),
        nameof(PositionHeader), nameof(NameHeader), nameof(CountedHeader),
        nameof(GrossHeader), nameof(DroppedHeader), nameof(PerRoundHeader))]
    private string sortColumn = ColumnPosition;

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(DriverRows), nameof(ConstructorRows),
        nameof(PositionHeader), nameof(NameHeader), nameof(CountedHeader),
        nameof(GrossHeader), nameof(DroppedHeader), nameof(PerRoundHeader))]
    private bool sortDescending;

    /// <summary>Header click: sort by the column, or flip the direction when it is already
    /// the sort column. Numeric columns start descending (biggest first), text ascending.</summary>
    [RelayCommand]
    private void SortBy(string? column)
    {
        if (column is not (ColumnPosition or ColumnName or ColumnCounted
            or ColumnGross or ColumnDropped or ColumnPerRound))
            return;

        if (string.Equals(SortColumn, column, StringComparison.Ordinal))
        {
            SortDescending = !SortDescending;
            return;
        }

        // Setting both properties raises the row-refresh notifications via the attributes.
        SortColumn = column;
        SortDescending = column is ColumnCounted or ColumnGross or ColumnDropped or ColumnPerRound;
    }

    // ---------- clickable numbers → the "Why?" inspector (decisions 4 + 5) ----------

    /// <summary>True when a career session is wired, so the standings numbers are clickable
    /// walk-back targets (the view enables the number hyperlinks / accelerators on this). Without a
    /// session, the season-review's final-standings snapshot, or a test built from raw snapshots —
    /// the numbers render plain and the command no-ops, so nothing depends on the session.</summary>
    public bool CanInspect => _session is not null;

    /// <summary>Open the inspector for a driver's championship number (points/position cell): walks
    /// the whole season's journal for that driver. The parameter is the row's competitor id, the
    /// DRIVER id for a drivers-table row. A constructor row (or a null/unknown id, or no session)
    /// simply does not open a panel. Mouse (click the number) and keyboard (a bound accelerator on
    /// the focused row) both invoke this, locked decision 8's parity.</summary>
    [RelayCommand]
    private void OpenInspector(string? driverId)
    {
        if (_session is null || string.IsNullOrEmpty(driverId))
            return;
        ShowInspector(_session.JournalFor(driverId));
    }

    /// <summary>Open the inspector for one round-matrix cell (driver × round): walks that driver's
    /// journal narrowed to the round. The parameter pairs the row's driver id with the applied
    /// round number the cell belongs to. No session (or a null id) no-ops.</summary>
    [RelayCommand]
    private void OpenCellInspector(RoundMatrixCellRef? cell)
    {
        if (_session is null || cell is null || string.IsNullOrEmpty(cell.DriverId))
            return;
        ShowInspector(_session.JournalFor(cell.DriverId, cell.Round));
    }

    public string PositionHeader => Header("Pos", ColumnPosition);
    public string NameHeader => Header("Name", ColumnName);
    public string CountedHeader => Header("Points", ColumnCounted);
    public string GrossHeader => Header("Gross", ColumnGross);
    public string DroppedHeader => Header("Drops", ColumnDropped);
    public string PerRoundHeader => Header("Pts/round", ColumnPerRound);

    private string Header(string label, string column) =>
        string.Equals(SortColumn, column, StringComparison.Ordinal)
            ? $"{label} {(SortDescending ? "▼" : "▲")}"
            : label;

    private IReadOnlyList<StandingsRow> Sorted(IReadOnlyList<StandingsRow> rows)
    {
        IOrderedEnumerable<StandingsRow> sorted = SortColumn switch
        {
            ColumnName => rows.OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase),
            ColumnCounted => rows.OrderBy(r => r.CountedValue),
            ColumnGross => rows.OrderBy(r => r.GrossValue),
            ColumnDropped => rows.OrderBy(r => r.DroppedCount),
            ColumnPerRound => rows.OrderBy(r => r.PerRoundValue),
            _ => rows.OrderBy(r => r.Position ?? int.MaxValue),
        };
        return (SortDescending ? sorted.Reverse() : sorted).ToArray();
    }

    // ---------- column chooser (right-click the header; persisted in settings) ----------

    public bool ShowCountedColumn
    {
        get => _showCountedColumn;
        set => SetColumn(ref _showCountedColumn, value, c => c with { ShowCounted = value });
    }

    public bool ShowGrossColumn
    {
        get => _showGrossColumn;
        set => SetColumn(ref _showGrossColumn, value, c => c with { ShowGross = value });
    }

    public bool ShowDroppedColumn
    {
        get => _showDroppedColumn;
        set => SetColumn(ref _showDroppedColumn, value, c => c with { ShowDropped = value });
    }

    public bool ShowPerRoundColumn
    {
        get => _showPerRoundColumn;
        set => SetColumn(ref _showPerRoundColumn, value, c => c with { ShowPerRound = value });
    }

    private void SetColumn(
        ref bool field, bool value,
        Func<StandingsColumnSettings, StandingsColumnSettings> mutate,
        [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (field == value)
            return;
        field = value;
        OnPropertyChanged(propertyName);
        _settings?.Update(s => s with { StandingsColumns = mutate(s.StandingsColumns) });
    }

    // ---------- row construction ----------

    private static StandingsRow Row(
        int? position,
        string id,
        string name,
        Companion.Core.Numerics.Rational counted,
        Companion.Core.Numerics.Rational gross,
        IReadOnlyList<DroppedResult> dropped,
        int appliedRounds)
    {
        double countedValue = counted.ToDouble();
        double perRound = appliedRounds > 0 ? countedValue / appliedRounds : 0.0;
        return new StandingsRow(
            position?.ToString() ?? "–",
            id,
            name,
            counted.ToString(),
            gross.ToString(),
            dropped.Count > 0,
            string.Join(", ", dropped.Select(d => $"R{d.Round}")),
            position,
            countedValue,
            gross.ToDouble(),
            dropped.Count,
            perRound.ToString("0.##", CultureInfo.InvariantCulture),
            perRound);
    }

    /// <summary>Cell (driver, round k) comes from snapshot k, the RoundScore that snapshot
    /// introduced for its own round. Dropped flags come from the LATEST snapshot, so the
    /// markers always reflect the current best-N state.</summary>
    private static IReadOnlyList<RoundMatrixRow> BuildMatrix(
        IReadOnlyList<StandingsSnapshot> ordered,
        StandingsSnapshot? latest,
        IReadOnlyDictionary<string, string> driverNames)
    {
        if (latest is null)
            return [];

        var droppedByDriver = latest.Drivers.ToDictionary(
            d => d.DriverId,
            d => d.Dropped.Select(x => x.Round).ToHashSet(),
            StringComparer.Ordinal);

        var rows = new List<RoundMatrixRow>(latest.Drivers.Count);
        foreach (var standing in latest.Drivers)
        {
            var cells = new List<RoundMatrixCell>(ordered.Count);
            foreach (var snapshot in ordered)
            {
                var inSnapshot = snapshot.Drivers.FirstOrDefault(d => d.DriverId == standing.DriverId);
                var score = inSnapshot?.RoundScores.FirstOrDefault(rs => rs.Round == snapshot.AfterRound);
                cells.Add(score is null
                    ? new RoundMatrixCell("", IsDropped: false, standing.DriverId, snapshot.AfterRound)
                    : new RoundMatrixCell(
                        score.Points.ToString(),
                        droppedByDriver[standing.DriverId].Contains(snapshot.AfterRound),
                        standing.DriverId,
                        snapshot.AfterRound));
            }

            rows.Add(new RoundMatrixRow(
                standing.DriverId,
                driverNames.GetValueOrDefault(standing.DriverId, standing.DriverId),
                cells));
        }

        return rows;
    }

    private static IReadOnlyList<string> BuildRulesParts(CatalogSeason season, int championshipRounds)
    {
        var parts = new List<string>
        {
            $"Points {string.Join("-", season.RacePoints)}",
            DescribeBestN(season.DriversBestN, championshipRounds),
            season.SharedDrivePolicy == SharedDrivePolicy.Split
                ? "shared drives split points"
                : "shared drives score no points",
            season.FastestLap is { } fl
                ? $"fastest lap +{fl.Points}" +
                  (fl.Eligibility == FastestLapEligibility.ClassifiedTopTen ? " (top 10 only)" : "")
                : "no fastest-lap point",
        };
        return parts;
    }

    private static string DescribeBestN(CatalogBestN? bestN, int championshipRounds)
    {
        if (bestN is null)
            return "all rounds count";

        if (bestN.WholeSeason is { } n)
            return $"best {n} of {championshipRounds} results count";

        if (bestN.Split is { } split)
            return $"best {split.FirstCount} of rounds 1–{split.FirstRounds} + " +
                   $"best {split.SecondCount} of rounds {split.FirstRounds + 1}–" +
                   $"{split.FirstRounds + split.SecondRounds} count";

        return "all rounds count";
    }
}
