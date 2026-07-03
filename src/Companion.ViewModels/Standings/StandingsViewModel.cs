using CommunityToolkit.Mvvm.ComponentModel;
using Companion.Core.Packs;
using Companion.Core.Scoring;

namespace Companion.ViewModels.Standings;

/// <summary>One drivers- or constructors-table row: gross vs counted plus dropped markers.</summary>
public sealed record StandingsRow(
    string PositionText,
    string CompetitorId,
    string DisplayName,
    string CountedText,
    string GrossText,
    bool HasDroppedRounds,
    string DroppedRoundsText)
{
    /// <summary>True when gross differs from counted (drops or adjustments applied).</summary>
    public bool ShowGross => GrossText != CountedText;
}

/// <summary>One cell of the Wikipedia-style round matrix: the points that round, dropped-marked.</summary>
public sealed record RoundMatrixCell(string Text, bool IsDropped);

/// <summary>One driver row of the round matrix; cells align with <see cref="StandingsViewModel.RoundHeaders"/>.</summary>
public sealed record RoundMatrixRow(
    string DriverId,
    string DisplayName,
    IReadOnlyList<RoundMatrixCell> Cells);

/// <summary>
/// The standings screen: drivers + constructors tabs built from the latest snapshot (gross vs
/// counted points with dropped-round markers), the round matrix (driver × round → that round's
/// points, sourced per snapshot from the RoundScores the snapshot introduced), and the
/// rules-explainer chip derived from the pack's CatalogSeason.
/// </summary>
public sealed partial class StandingsViewModel : ObservableObject
{
    [ObservableProperty]
    private int selectedTabIndex;

    public StandingsViewModel(IReadOnlyList<StandingsSnapshot> snapshots, SeasonPack pack)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(pack);

        var driverNames = pack.Drivers.ToDictionary(d => d.Id, d => d.Name, StringComparer.Ordinal);
        var teamNames = pack.Teams.ToDictionary(t => t.Id, t => t.Name, StringComparer.Ordinal);

        var ordered = snapshots.OrderBy(s => s.AfterRound).ToArray();
        var latest = ordered.Length > 0 ? ordered[^1] : null;

        HasConstructors = pack.Season.PointsSystem.Constructors is not null;

        DriverRows = latest is null
            ? []
            : latest.Drivers
                .Select(d => Row(
                    d.Position, d.DriverId, driverNames.GetValueOrDefault(d.DriverId, d.DriverId),
                    d.CountedPoints, d.GrossPoints, d.Dropped))
                .ToArray();

        ConstructorRows = latest?.Constructors is not { } constructors
            ? []
            : constructors
                .Select(c => Row(
                    c.Position, c.ConstructorId, teamNames.GetValueOrDefault(c.ConstructorId, c.ConstructorId),
                    c.CountedPoints, c.GrossPoints, c.Dropped))
                .ToArray();

        RoundHeaders = ordered.Select(s => $"R{s.AfterRound}").ToArray();
        MatrixRows = BuildMatrix(ordered, latest, driverNames);

        int championshipRounds = pack.Season.Rounds.Count(r => r.Championship);
        RulesParts = BuildRulesParts(pack.Season.PointsSystem, championshipRounds);
        RulesChipText = string.Join(" · ", RulesParts);
    }

    /// <summary>Drivers-tab rows in championship order (excluded competitors last).</summary>
    public IReadOnlyList<StandingsRow> DriverRows { get; }

    /// <summary>Constructors-tab rows; empty when the season has no constructors championship.</summary>
    public IReadOnlyList<StandingsRow> ConstructorRows { get; }

    /// <summary>True when the pack's season runs a constructors championship (shows the tab).</summary>
    public bool HasConstructors { get; }

    /// <summary>Column headers of the round matrix: one per applied round ("R1", "R2", ...).</summary>
    public IReadOnlyList<string> RoundHeaders { get; }

    /// <summary>Driver × round matrix; rows ordered like <see cref="DriverRows"/>.</summary>
    public IReadOnlyList<RoundMatrixRow> MatrixRows { get; }

    /// <summary>The rules-explainer chip: points table, best-N, shared-drive policy, fastest lap.</summary>
    public string RulesChipText { get; }

    /// <summary>The chip's individual sentences, for views that render them as separate lines.</summary>
    public IReadOnlyList<string> RulesParts { get; }

    private static StandingsRow Row(
        int? position,
        string id,
        string name,
        Companion.Core.Numerics.Rational counted,
        Companion.Core.Numerics.Rational gross,
        IReadOnlyList<DroppedResult> dropped)
        => new(
            position?.ToString() ?? "–",
            id,
            name,
            counted.ToString(),
            gross.ToString(),
            dropped.Count > 0,
            string.Join(", ", dropped.Select(d => $"R{d.Round}")));

    /// <summary>Cell (driver, round k) comes from snapshot k — the RoundScore that snapshot
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
                    ? new RoundMatrixCell("", IsDropped: false)
                    : new RoundMatrixCell(
                        score.Points.ToString(),
                        droppedByDriver[standing.DriverId].Contains(snapshot.AfterRound)));
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
