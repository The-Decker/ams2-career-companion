using Companion.Core.Newsroom;

namespace Companion.ViewModels.Services;

public enum DivergenceKind
{
    /// <summary>The career produced a different outcome than the historical record.</summary>
    AlternateOutcome,
    /// <summary>The career reproduced the historical outcome.</summary>
    UnchangedEvent,
    /// <summary>The round has not been raced in the career yet.</summary>
    NotYetRaced,
    /// <summary>The historical record has no documented result for this round.</summary>
    NotDocumented,
}

/// <summary>One round compared: what really happened vs what the career produced. The
/// historical side is VerifiedHistorical, the career side is CareerUniverse, the two are
/// carried as separate labeled fields and never blended.</summary>
public sealed record RoundDivergence
{
    public required int Round { get; init; }
    public required string Venue { get; init; }
    public required DivergenceKind Kind { get; init; }
    public string HistoricalWinner { get; init; } = "";
    public string HistoricalWinnerTeam { get; init; } = "";
    public string CareerWinner { get; init; } = "";
    public string CareerWinnerTeam { get; init; } = "";
    /// <summary>The career winner does not exist in the historical record at all (the
    /// player's own entrant, typically), the strongest form of divergence.</summary>
    public bool NonHistoricalWinner { get; init; }
}

/// <summary>
/// The season-level comparison between the player's universe and the historical record
/// (docs/dev/newsroom-history-overhaul.md D9). Computed only for historical-style careers;
/// the SMGP universe is fiction with its own almanac and never enters this comparison.
/// The user's career never alters real history, this report is explicitly framed as an
/// alternate timeline against the unchanged historical record.
/// </summary>
public sealed record SeasonDivergenceReport
{
    public required int SeasonYear { get; init; }
    public required int SeasonOrdinal { get; init; }
    public IReadOnlyList<RoundDivergence> Rounds { get; init; } = [];
    public string HistoricalChampion { get; init; } = "";
    public string HistoricalChampionTeam { get; init; } = "";
    public string CareerChampion { get; init; } = "";
    /// <summary>Null until the career season completes (no spoiler-free claim before).</summary>
    public bool? ChampionChanged { get; init; }
    public int AlternateOutcomes => Rounds.Count(r => r.Kind == DivergenceKind.AlternateOutcome);
    public int UnchangedEvents => Rounds.Count(r => r.Kind == DivergenceKind.UnchangedEvent);
    public string HistoricalSource { get; init; } = "";
}

/// <summary>
/// Pure comparison over the already-shaped career season and the loaded historical record.
/// Identity is the NAME STRING: faithful packs carry f1db driver names verbatim, so a real
/// driver matches exactly; the player's synthetic entrant matches nothing and surfaces as a
/// non-historical winner. Deterministic, display-only, never a fold input.
/// </summary>
public static class CareerDivergence
{
    public static SeasonDivergenceReport Compare(NewsroomSeason career, HistoricalSeason historical)
    {
        var rounds = new List<RoundDivergence>();
        var historicalByRound = historical.Rounds.ToDictionary(r => r.Round);
        var careerByRound = career.Rounds.ToDictionary(r => r.Round);
        var historicalWinners = historical.Rounds
            .SelectMany(r => r.Results.Select(x => x.Driver))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var allRounds = historicalByRound.Keys.Union(careerByRound.Keys).OrderBy(r => r);
        foreach (var roundNumber in allRounds)
        {
            var hist = historicalByRound.GetValueOrDefault(roundNumber);
            var raced = careerByRound.GetValueOrDefault(roundNumber);
            var venue = raced?.Venue ?? hist?.Name ?? $"Round {roundNumber}";

            if (hist is null || string.IsNullOrEmpty(hist.Winner))
            {
                rounds.Add(new RoundDivergence
                {
                    Round = roundNumber,
                    Venue = venue,
                    Kind = DivergenceKind.NotDocumented,
                    CareerWinner = raced?.WinnerName ?? "",
                    CareerWinnerTeam = raced?.WinnerTeamName ?? "",
                });
                continue;
            }

            if (raced is null || raced.WinnerName.Length == 0)
            {
                rounds.Add(new RoundDivergence
                {
                    Round = roundNumber,
                    Venue = venue,
                    Kind = DivergenceKind.NotYetRaced,
                    HistoricalWinner = hist.Winner,
                    HistoricalWinnerTeam = hist.WinnerTeam ?? "",
                });
                continue;
            }

            var sameWinner = string.Equals(
                Normalize(raced.WinnerName), Normalize(hist.Winner), StringComparison.OrdinalIgnoreCase);

            rounds.Add(new RoundDivergence
            {
                Round = roundNumber,
                Venue = venue,
                Kind = sameWinner ? DivergenceKind.UnchangedEvent : DivergenceKind.AlternateOutcome,
                HistoricalWinner = hist.Winner,
                HistoricalWinnerTeam = hist.WinnerTeam ?? "",
                CareerWinner = raced.WinnerName,
                CareerWinnerTeam = raced.WinnerTeamName,
                NonHistoricalWinner = !sameWinner
                    && !historicalWinners.Contains(Normalize(raced.WinnerName)),
            });
        }

        bool? championChanged = null;
        if (career.Complete && career.ChampionName.Length > 0
            && historical.DriversChampion?.Driver is { Length: > 0 } historicalChampion)
        {
            championChanged = !string.Equals(
                Normalize(career.ChampionName), Normalize(historicalChampion),
                StringComparison.OrdinalIgnoreCase);
        }

        return new SeasonDivergenceReport
        {
            SeasonYear = career.Year,
            SeasonOrdinal = career.Ordinal,
            Rounds = rounds,
            HistoricalChampion = historical.DriversChampion?.Driver ?? "",
            HistoricalChampionTeam = historical.DriversChampion?.Team ?? "",
            CareerChampion = career.Complete ? career.ChampionName : "",
            ChampionChanged = championChanged,
            HistoricalSource = historical.Source ?? "",
        };
    }

    /// <summary>Divergence NEWS events for the spine: one per changed race outcome, plus the
    /// season verdict (champion changed → the big alternate-history story; champion held →
    /// one modest history-held note). Never emitted for undocumented/unraced rounds.</summary>
    public static IReadOnlyList<NewsEvent> ToNewsEvents(SeasonDivergenceReport report)
    {
        var events = new List<NewsEvent>();

        foreach (var round in report.Rounds.Where(r =>
            r.Kind == DivergenceKind.AlternateOutcome))
        {
            events.Add(new NewsEvent
            {
                Kind = NewsEventKind.HistoryDiverged,
                SeasonOrdinal = report.SeasonOrdinal,
                SeasonYear = report.SeasonYear,
                Round = round.Round,
                SubjectId = round.NonHistoricalWinner ? "player" : round.CareerWinner,
                SubjectName = round.CareerWinner,
                SubjectTeamName = round.CareerWinnerTeam,
                VenueName = round.Venue,
                Facts = new NewsEventFacts
                {
                    WinnerName = round.CareerWinner,
                    WinnerTeamName = round.CareerWinnerTeam,
                    RivalName = round.HistoricalWinner, // the displaced historical fact
                },
            });
        }

        if (report.ChampionChanged is { } changed)
        {
            events.Add(new NewsEvent
            {
                Kind = changed ? NewsEventKind.HistoryDiverged : NewsEventKind.HistoryHeld,
                SeasonOrdinal = report.SeasonOrdinal,
                SeasonYear = report.SeasonYear,
                Round = CareerNewsEvents.SeasonEndRound,
                SubjectId = "season",
                SubjectName = report.CareerChampion,
                Facts = new NewsEventFacts
                {
                    WinnerName = report.CareerChampion,
                    RivalName = report.HistoricalChampion,
                    ClinchedTitle = changed,
                },
                Discriminator = "champion",
            });
        }

        return events;
    }

    private static string Normalize(string name) => name.Trim();
}
