using System.Globalization;
using Companion.Core.Smgp;
using Companion.ViewModels.Services;

namespace Companion.ViewModels.Hub;

/// <summary>
/// Presentation-ready summary for the History hero. Every value is copied from an existing
/// read-side career projection; missing optional SMGP identity remains empty rather than being
/// inferred from prose or mutable pack data.
/// </summary>
public sealed record HistoryHeroViewModel
{
    public static HistoryHeroViewModel Empty { get; } = new();

    public string CareerName { get; init; } = "";
    public string SeriesName { get; init; } = "";
    public string EraLabel { get; init; } = "";
    public int SeasonYear { get; init; }
    public int SeasonOrdinal { get; init; }
    public string SeasonLabel { get; init; } = "";
    public string PlayerId { get; init; } = "";
    public string PlayerName { get; init; } = "";
    public string TeamName { get; init; } = "";
    public int? ChampionshipPosition { get; init; }
    public string StandingText { get; init; } = "";
    public string Trajectory { get; init; } = "";
    public string PortraitKey { get; init; } = "";
    public string CarKey { get; init; } = "";

    public string CurrentRivalId { get; init; } = "";
    public string CurrentRivalName { get; init; } = "";
    public string CurrentRivalTeamName { get; init; } = "";
    public string CurrentRivalPortraitKey { get; init; } = "";

    public int? Starts { get; init; }
    public int Wins { get; init; }
    public int Podiums { get; init; }
    public int? Poles { get; init; }
    public int Titles { get; init; }
    public int Championships => Titles;
    public double Points { get; init; }

    public bool HasTeam => TeamName.Length > 0;
    public bool HasPortrait => PortraitKey.Length > 0;
    public bool HasCar => CarKey.Length > 0;
    public bool HasCurrentRival => CurrentRivalName.Length > 0;
    public bool HasTrajectory => Trajectory.Length > 0;
    public bool HasStarts => Starts is not null;
    public bool HasPoles => Poles is not null;
}

public enum HistoryEventKind
{
    Race,
    CareerStart,
    Achievement,
    Promotion,
    Demotion,
    Rivalry,
    Injury,
    Championship,
    Season,
    Finale,
    Setback,
}

/// <summary>One truthful event in the combined career chronology.</summary>
public sealed record HistoryEventViewModel
{
    public required string Key { get; init; }
    public required HistoryEventKind Kind { get; init; }
    public required string Category { get; init; }
    public required int SeasonOrdinal { get; init; }
    public required int SeasonYear { get; init; }
    public int? Round { get; init; }
    public string RoundLabel => Round is { } round ? $"R{round}" : "";
    public required string WhenLabel { get; init; }
    public required string Title { get; init; }
    public required string Detail { get; init; }
    public string VenueName { get; init; } = "";
    public string SubjectDriverId { get; init; } = "";
    public string SubjectPortraitKey { get; init; } = "";
    public string RaceKey { get; init; } = "";
    public bool IsRace => Kind == HistoryEventKind.Race;
    public bool IsMajor { get; init; }
    public bool HasDetail => Detail.Length > 0;
    public bool HasVenue => VenueName.Length > 0;
    public bool HasSubjectPortrait => SubjectPortraitKey.Length > 0;
    public bool HasRaceLink => RaceKey.Length > 0;
}

public enum HistoryRaceStatus
{
    Classified,
    NotClassified,
}

/// <summary>
/// One flattened race in the career archive. The source currently distinguishes only a classified
/// finish from a missing classification, so the latter is deliberately labelled "Not classified"
/// rather than guessing DNF, DNS, or DSQ.
/// </summary>
public sealed record HistoryRaceArchiveItemViewModel
{
    public required string Key { get; init; }
    public required int SeasonOrdinal { get; init; }
    public required int SeasonYear { get; init; }
    public required int Round { get; init; }
    public string RoundLabel => $"R{Round}";
    public required string VenueName { get; init; }
    public int? PlayerFinish { get; init; }
    public required string FinishText { get; init; }
    public required HistoryRaceStatus Status { get; init; }
    public required string StatusLabel { get; init; }
    public required double PointsEarned { get; init; }
    public string PointsEarnedText => FormatPoints(PointsEarned);
    public required double PlayerPointsAfter { get; init; }
    public string PointsAfterText => FormatPoints(PlayerPointsAfter);
    public string RivalName { get; init; } = "";
    public int? RivalFinish { get; init; }
    public string RivalFinishText { get; init; } = "";
    public string ChampionAfter { get; init; } = "";
    public required string StoryContext { get; init; }

    /// <summary>
    /// Explicitly the player's current identity, not an invented claim about their historical seat
    /// in this race. Historical per-round team/car identity is not present in the existing seam.
    /// </summary>
    public string CurrentTeamName { get; init; } = "";
    public string CurrentCarKey { get; init; } = "";
    public string PlayerPortraitKey { get; init; } = "";

    public bool IsWin => PlayerFinish == 1;
    public bool IsPodium => PlayerFinish is >= 1 and <= 3;
    public bool ScoredPoints => PointsEarned > 0.0;
    public bool HasRival => RivalName.Length > 0;
    public bool HasChampionContext => ChampionAfter.Length > 0;
    public bool HasCurrentTeam => CurrentTeamName.Length > 0;
    public bool HasCurrentCar => CurrentCarKey.Length > 0;
    public bool HasPlayerPortrait => PlayerPortraitKey.Length > 0;

    internal bool MatchesSearch(string search)
    {
        if (search.Length == 0)
            return true;

        return Key.Contains(search, StringComparison.OrdinalIgnoreCase)
            || SeasonYear.ToString(CultureInfo.InvariantCulture).Contains(search, StringComparison.OrdinalIgnoreCase)
            || SeasonOrdinal.ToString(CultureInfo.InvariantCulture).Contains(search, StringComparison.OrdinalIgnoreCase)
            || RoundLabel.Contains(search, StringComparison.OrdinalIgnoreCase)
            || VenueName.Contains(search, StringComparison.OrdinalIgnoreCase)
            || FinishText.Contains(search, StringComparison.OrdinalIgnoreCase)
            || StatusLabel.Contains(search, StringComparison.OrdinalIgnoreCase)
            || RivalName.Contains(search, StringComparison.OrdinalIgnoreCase)
            || RivalFinishText.Contains(search, StringComparison.OrdinalIgnoreCase)
            || ChampionAfter.Contains(search, StringComparison.OrdinalIgnoreCase)
            || StoryContext.Contains(search, StringComparison.OrdinalIgnoreCase)
            || CurrentTeamName.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatPoints(double points) =>
        points.ToString("0.##", CultureInfo.InvariantCulture);
}

public enum HistoryRaceFilterKind
{
    All,
    Wins,
    Podiums,
    Points,
    Rivalries,
    NotClassified,
}

public sealed record HistoryRaceFilterViewModel(
    HistoryRaceFilterKind Kind,
    string Key,
    string Label,
    int Count)
{
    public string DisplayText => $"{Label} {Count}";
    public bool IsAll => Kind == HistoryRaceFilterKind.All;
}

public enum HistoryDispatchImportance
{
    Standard,
    Major,
}

/// <summary>One compact read-only story for History's latest-dispatches section.</summary>
public sealed record HistoryDispatchViewModel
{
    public required string Key { get; init; }
    public string HistoryEventKey { get; init; } = "";
    public required int SeasonOrdinal { get; init; }
    public required int SeasonYear { get; init; }
    public int? Round { get; init; }
    public string RoundLabel => Round is { } round ? $"R{round}" : "";
    public required string WhenLabel { get; init; }
    public required string Category { get; init; }
    public required HistoryDispatchImportance Importance { get; init; }
    public string ImportanceLabel => Importance == HistoryDispatchImportance.Major ? "MAJOR" : "DISPATCH";
    public required string Headline { get; init; }
    public required string Body { get; init; }
    public required string WhyText { get; init; }
    public string DriverPortraitKey { get; init; } = "";
    public string TeamArtKey { get; init; } = "";

    public bool HasHistoryLink => HistoryEventKey.Length > 0;
    public bool HasBody => Body.Length > 0;
    public bool HasWhy => WhyText.Length > 0;
    public bool HasDriverPortrait => DriverPortraitKey.Length > 0;
    public bool HasTeamArt => TeamArtKey.Length > 0;
}

internal static class HistoryArchiveProjection
{
    private const int LatestDispatchLimit = 6;

    public static HistoryHeroViewModel BuildHero(
        CareerSummary summary,
        CareerTimeline timeline,
        SmgpDriverCard? player,
        SmgpDriverCard? rival)
    {
        int seasonOrdinal = timeline.Seasons.Count;
        int decade = summary.SeasonYear > 0 ? summary.SeasonYear / 10 * 10 : 0;
        var career = player?.Career;

        return new HistoryHeroViewModel
        {
            CareerName = summary.CareerName,
            SeriesName = summary.SeriesName,
            EraLabel = decade > 0 ? $"{decade}s" : summary.SeriesName,
            SeasonYear = summary.SeasonYear,
            SeasonOrdinal = seasonOrdinal,
            SeasonLabel = seasonOrdinal > 0
                ? $"Season {seasonOrdinal} · {summary.SeasonYear}"
                : summary.SeasonYear.ToString(CultureInfo.InvariantCulture),
            PlayerId = player?.DriverId ?? summary.PlayerDriverId,
            PlayerName = player?.Name ?? summary.PlayerDriverId,
            TeamName = player?.TeamName ?? "",
            ChampionshipPosition = summary.PlayerPosition,
            StandingText = summary.PlayerPosition is { } position ? $"P{position}" : "Not yet classified",
            Trajectory = player?.NarrativeIntro ?? "",
            PortraitKey = player?.PortraitKey ?? "",
            CarKey = player?.CarKey ?? "",
            CurrentRivalId = rival?.DriverId ?? "",
            CurrentRivalName = rival?.Name ?? "",
            CurrentRivalTeamName = rival?.TeamName ?? "",
            CurrentRivalPortraitKey = rival?.PortraitKey ?? "",
            Starts = career?.Starts,
            Wins = career?.Wins ?? timeline.Records.Wins,
            Podiums = career?.Podiums ?? timeline.Records.Podiums,
            Poles = career?.Poles,
            Titles = career?.Titles ?? timeline.Records.Championships,
            Points = career?.Points ?? timeline.Records.TotalPoints,
        };
    }

    public static IReadOnlyList<HistoryRaceArchiveItemViewModel> BuildRaceArchive(
        CareerTimeline timeline,
        SmgpDriverCard? player)
    {
        var races = new List<HistoryRaceArchiveItemViewModel>();

        for (int seasonIndex = 0; seasonIndex < timeline.Seasons.Count; seasonIndex++)
        {
            var season = timeline.Seasons[seasonIndex];
            int seasonOrdinal = seasonIndex + 1;
            double previousPoints = 0.0;

            foreach (var line in season.RoundLines.OrderBy(r => r.Round))
            {
                double earned = RoundPoints(line.PlayerPointsAfter - previousPoints);
                previousPoints = line.PlayerPointsAfter;
                bool classified = line.PlayerFinish is not null;
                string finishText = line.PlayerFinish is { } finish ? $"P{finish}" : "NC";
                string rivalFinish = line.RivalName is null
                    ? ""
                    : line.RivalFinish is { } rivalPosition ? $"P{rivalPosition}" : "NC";

                races.Add(new HistoryRaceArchiveItemViewModel
                {
                    Key = RaceKey(seasonOrdinal, line.Round),
                    SeasonOrdinal = seasonOrdinal,
                    SeasonYear = season.SeasonYear,
                    Round = line.Round,
                    VenueName = line.Venue,
                    PlayerFinish = line.PlayerFinish,
                    FinishText = finishText,
                    Status = classified ? HistoryRaceStatus.Classified : HistoryRaceStatus.NotClassified,
                    StatusLabel = classified ? "CLASSIFIED" : "NOT CLASSIFIED",
                    PointsEarned = earned,
                    PlayerPointsAfter = line.PlayerPointsAfter,
                    RivalName = line.RivalName ?? "",
                    RivalFinish = line.RivalFinish,
                    RivalFinishText = rivalFinish,
                    ChampionAfter = line.ChampionAfter ?? "",
                    StoryContext = RaceContext(line, earned, finishText, rivalFinish),
                    CurrentTeamName = player?.TeamName ?? "",
                    CurrentCarKey = player?.CarKey ?? "",
                    PlayerPortraitKey = player?.PortraitKey ?? "",
                });
            }
        }

        return races
            .OrderByDescending(r => r.SeasonOrdinal)
            .ThenByDescending(r => r.Round)
            .ToList();
    }

    public static IReadOnlyList<HistoryEventViewModel> BuildEvents(
        CareerTimeline timeline,
        IReadOnlyList<HistoryRaceArchiveItemViewModel> races,
        IReadOnlyList<SmgpCareerBeat> beats,
        IReadOnlyList<SmgpDriverCard> drivers)
    {
        var candidates = new List<EventCandidate>();
        var raceByCoordinate = races.ToDictionary(r => (r.SeasonOrdinal, r.Round));
        var yearBySeason = timeline.Seasons
            .Select((season, index) => (Ordinal: index + 1, season.SeasonYear))
            .ToDictionary(x => x.Ordinal, x => x.SeasonYear);
        var portraitByDriver = drivers
            .Where(driver => driver.DriverId.Length > 0)
            .GroupBy(driver => driver.DriverId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().PortraitKey, StringComparer.Ordinal);

        foreach (var race in races)
        {
            candidates.Add(new EventCandidate(
                race.SeasonOrdinal,
                race.Round,
                0,
                new HistoryEventViewModel
                {
                    Key = race.Key,
                    Kind = HistoryEventKind.Race,
                    Category = "Race",
                    SeasonOrdinal = race.SeasonOrdinal,
                    SeasonYear = race.SeasonYear,
                    Round = race.Round,
                    WhenLabel = $"Season {race.SeasonOrdinal} · {race.VenueName}",
                    Title = $"{race.FinishText} · {race.VenueName}",
                    Detail = race.StoryContext,
                    VenueName = race.VenueName,
                    RaceKey = race.Key,
                    IsMajor = race.IsWin,
                }));
        }

        var duplicateKeys = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index < beats.Count; index++)
        {
            var beat = beats[index];
            int seasonOrdinal = beat.Season > 0 ? beat.Season : 1;
            int sortRound = beat.Round;
            int? round = sortRound is > SmgpDispatch.SeasonStartRound and < SmgpDispatch.SeasonEndRound
                ? sortRound
                : null;
            raceByCoordinate.TryGetValue((seasonOrdinal, sortRound), out var race);
            string keyBase = $"beat:{seasonOrdinal}:{sortRound}:{beat.Kind}:{beat.SubjectId}";
            int duplicate = duplicateKeys.GetValueOrDefault(keyBase);
            duplicateKeys[keyBase] = duplicate + 1;
            string key = duplicate == 0 ? keyBase : $"{keyBase}:{duplicate}";
            var (kind, category, major) = EventIdentity(beat.Kind);

            candidates.Add(new EventCandidate(
                seasonOrdinal,
                sortRound,
                index + 1,
                new HistoryEventViewModel
                {
                    Key = key,
                    Kind = kind,
                    Category = category,
                    SeasonOrdinal = seasonOrdinal,
                    SeasonYear = yearBySeason.GetValueOrDefault(seasonOrdinal),
                    Round = round,
                    WhenLabel = beat.WhenLabel,
                    Title = beat.Headline,
                    Detail = beat.Detail,
                    VenueName = race?.VenueName ?? "",
                    SubjectDriverId = beat.SubjectId,
                    SubjectPortraitKey = portraitByDriver.GetValueOrDefault(beat.SubjectId) ?? "",
                    RaceKey = race?.Key ?? "",
                    IsMajor = major,
                }));
        }

        return candidates
            .OrderBy(candidate => candidate.SeasonOrdinal)
            .ThenBy(candidate => candidate.SortRound)
            .ThenBy(candidate => candidate.Sequence)
            .Select(candidate => candidate.Event)
            .ToList();
    }

    public static IReadOnlyList<HistoryDispatchViewModel> BuildLatestDispatches(
        IReadOnlyList<SmgpDispatch> smgp,
        IReadOnlyList<NewsDispatch> journal,
        CareerTimeline timeline,
        int currentSeasonYear)
    {
        var candidates = new List<DispatchCandidate>();
        int ordinal = Math.Max(1, timeline.Seasons.Count);
        var yearBySeason = timeline.Seasons
            .Select((season, index) => (Ordinal: index + 1, season.SeasonYear))
            .ToDictionary(item => item.Ordinal, item => item.SeasonYear);

        for (int index = 0; index < smgp.Count; index++)
        {
            var dispatch = smgp[index];
            int? round = dispatch.SortRound is > SmgpDispatch.SeasonStartRound and < SmgpDispatch.SeasonEndRound
                ? dispatch.SortRound
                : null;
            var (category, importance) = DispatchIdentity(dispatch.Kind);
            candidates.Add(new DispatchCandidate(
                dispatch.SortSeason,
                dispatch.SortRound,
                SourcePriority: 1,
                SourceIndex: index,
                new HistoryDispatchViewModel
                {
                    Key = $"smgp:{dispatch.SortSeason}:{dispatch.SortRound}:{dispatch.SortSeq}",
                    HistoryEventKey = round is { } raceRound ? RaceKey(dispatch.SortSeason, raceRound) : "",
                    SeasonOrdinal = dispatch.SortSeason,
                    SeasonYear = yearBySeason.TryGetValue(dispatch.SortSeason, out int seasonYear)
                        ? seasonYear
                        : dispatch.SortSeason == ordinal ? currentSeasonYear : 0,
                    Round = round,
                    WhenLabel = dispatch.WhenLabel,
                    Category = category,
                    Importance = importance,
                    Headline = dispatch.Headline,
                    Body = dispatch.Body,
                    WhyText = "",
                    DriverPortraitKey = dispatch.DriverArtKey,
                    TeamArtKey = dispatch.TeamArtKey,
                }));
        }

        for (int index = 0; index < journal.Count; index++)
        {
            var dispatch = journal[index];
            int sortRound = dispatch.Round ?? SmgpDispatch.SeasonEndRound;
            string category = string.Equals(dispatch.Kind, "race", StringComparison.Ordinal)
                ? "Race report"
                : string.Equals(dispatch.Kind, "season", StringComparison.Ordinal)
                    ? "Season"
                    : "Dispatch";
            candidates.Add(new DispatchCandidate(
                ordinal,
                sortRound,
                SourcePriority: 0,
                SourceIndex: index,
                new HistoryDispatchViewModel
                {
                    Key = $"journal:{dispatch.SeasonYear}:{dispatch.Round?.ToString(CultureInfo.InvariantCulture) ?? "season"}:{index}",
                    HistoryEventKey = dispatch.Round is { } raceRound ? RaceKey(ordinal, raceRound) : "",
                    SeasonOrdinal = ordinal,
                    SeasonYear = dispatch.SeasonYear,
                    Round = dispatch.Round,
                    WhenLabel = dispatch.Round is { } round
                        ? $"{dispatch.SeasonYear} · Round {round}"
                        : dispatch.SeasonYear.ToString(CultureInfo.InvariantCulture),
                    Category = category,
                    Importance = dispatch.Round is null
                        ? HistoryDispatchImportance.Major
                        : HistoryDispatchImportance.Standard,
                    Headline = dispatch.Headline,
                    Body = dispatch.Body,
                    WhyText = dispatch.WhyText,
                }));
        }

        return candidates
            .OrderByDescending(candidate => candidate.SeasonOrdinal)
            .ThenByDescending(candidate => candidate.SortRound)
            .ThenByDescending(candidate => candidate.SourcePriority)
            .ThenBy(candidate => candidate.SourceIndex)
            .Take(LatestDispatchLimit)
            .Select(candidate => candidate.Dispatch)
            .ToList();
    }

    public static IReadOnlyList<HistoryRaceFilterViewModel> BuildRaceFilters(
        IReadOnlyList<HistoryRaceArchiveItemViewModel> races) =>
    [
        new(HistoryRaceFilterKind.All, "all", "All races", races.Count),
        new(HistoryRaceFilterKind.Wins, "wins", "Wins", races.Count(race => race.IsWin)),
        new(HistoryRaceFilterKind.Podiums, "podiums", "Podiums", races.Count(race => race.IsPodium)),
        new(HistoryRaceFilterKind.Points, "points", "Points", races.Count(race => race.ScoredPoints)),
        new(HistoryRaceFilterKind.Rivalries, "rivalries", "Rival battles", races.Count(race => race.HasRival)),
        new(HistoryRaceFilterKind.NotClassified, "not-classified", "Not classified",
            races.Count(race => race.Status == HistoryRaceStatus.NotClassified)),
    ];

    public static bool MatchesFilter(
        HistoryRaceArchiveItemViewModel race,
        HistoryRaceFilterKind filter) => filter switch
    {
        HistoryRaceFilterKind.All => true,
        HistoryRaceFilterKind.Wins => race.IsWin,
        HistoryRaceFilterKind.Podiums => race.IsPodium,
        HistoryRaceFilterKind.Points => race.ScoredPoints,
        HistoryRaceFilterKind.Rivalries => race.HasRival,
        HistoryRaceFilterKind.NotClassified => race.Status == HistoryRaceStatus.NotClassified,
        _ => true,
    };

    public static string RaceKey(int seasonOrdinal, int round) =>
        $"race:{seasonOrdinal}:{round}";

    private static string RaceContext(
        CareerSeasonRoundLine line,
        double earned,
        string finishText,
        string rivalFinish)
    {
        var facts = new List<string>
        {
            line.PlayerFinish is null ? "Not classified" : $"Finished {finishText}",
            earned > 0.0
                ? $"{earned.ToString("0.##", CultureInfo.InvariantCulture)} points gained"
                : earned < 0.0
                    ? $"{earned.ToString("0.##", CultureInfo.InvariantCulture)} counted-points movement"
                    : "No counted-points gain",
        };
        if (line.RivalName is { Length: > 0 } rival)
            facts.Add($"Rival {rival}: {rivalFinish}");
        if (line.ChampionAfter is { Length: > 0 } leader)
            facts.Add($"Championship leader: {leader}");
        facts.Add($"Season total: {line.PlayerPointsAfter.ToString("0.##", CultureInfo.InvariantCulture)}");
        return string.Join(" · ", facts);
    }

    private static double RoundPoints(double value) =>
        Math.Round(value, 6, MidpointRounding.AwayFromZero);

    private static (HistoryEventKind Kind, string Category, bool Major) EventIdentity(SmgpBeatKind kind) =>
        kind switch
        {
            SmgpBeatKind.Arrived => (HistoryEventKind.CareerStart, "Career start", true),
            SmgpBeatKind.FirstStart or SmgpBeatKind.FirstPoints or SmgpBeatKind.FirstTop5
                or SmgpBeatKind.FirstPole or SmgpBeatKind.FirstPodium or SmgpBeatKind.FirstWin =>
                (HistoryEventKind.Achievement, "Achievement", true),
            SmgpBeatKind.Promotion => (HistoryEventKind.Promotion, "Promotion", true),
            SmgpBeatKind.Demotion => (HistoryEventKind.Demotion, "Team movement", true),
            SmgpBeatKind.RivalryEarned or SmgpBeatKind.RivalryLost =>
                (HistoryEventKind.Rivalry, "Rivalry", true),
            SmgpBeatKind.Injured or SmgpBeatKind.SeasonEndingInjury or SmgpBeatKind.Died =>
                (HistoryEventKind.Injury, "Injury", true),
            SmgpBeatKind.Title => (HistoryEventKind.Championship, "Championship", true),
            SmgpBeatKind.SeasonMilestone => (HistoryEventKind.Season, "Season", false),
            SmgpBeatKind.Finale => (HistoryEventKind.Finale, "Finale", true),
            SmgpBeatKind.NearMiss => (HistoryEventKind.Setback, "Setback", true),
            _ => (HistoryEventKind.Achievement, "Milestone", false),
        };

    private static (string Category, HistoryDispatchImportance Importance) DispatchIdentity(
        SmgpDispatchKind kind) => kind switch
    {
        SmgpDispatchKind.Milestone => ("Milestone", HistoryDispatchImportance.Major),
        SmgpDispatchKind.RaceResult => ("Race report", HistoryDispatchImportance.Standard),
        SmgpDispatchKind.Setback => ("Setback", HistoryDispatchImportance.Major),
        SmgpDispatchKind.RivalWatch => ("Rivalry", HistoryDispatchImportance.Standard),
        SmgpDispatchKind.TitleRace => ("Championship", HistoryDispatchImportance.Major),
        SmgpDispatchKind.SeasonDigest => ("Season", HistoryDispatchImportance.Major),
        _ => ("Dispatch", HistoryDispatchImportance.Standard),
    };

    private sealed record EventCandidate(
        int SeasonOrdinal,
        int SortRound,
        int Sequence,
        HistoryEventViewModel Event);

    private sealed record DispatchCandidate(
        int SeasonOrdinal,
        int SortRound,
        int SourcePriority,
        int SourceIndex,
        HistoryDispatchViewModel Dispatch);
}
