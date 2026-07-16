using System.Globalization;
using Companion.Core.Smgp;
using Companion.ViewModels.Services;

namespace Companion.ViewModels.Hub;

/// <summary>Truthful categories that an existing dispatch source can prove without parsing prose.</summary>
public enum NewsStoryCategory
{
    Championship,
    Rivalry,
    Paddock,
    TeamMovement,
    Injury,
    Promotion,
    Records,
    RaceReport,
}

public enum NewsStoryImportance
{
    Standard,
    Major,
}

/// <summary>
/// One display-only story in the unified career wire. Optional facts stay empty when neither
/// source nor another existing read-side projection supplies them.
/// </summary>
public sealed record NewsStoryViewModel
{
    public required string Key { get; init; }
    public required int SeasonOrdinal { get; init; }
    public required int SeasonYear { get; init; }
    public int? Round { get; init; }
    public string DateLabel { get; init; } = "";
    public string RoundLabel { get; init; } = "";
    public string VenueName { get; init; } = "";
    public string TrackArtKey { get; init; } = "";
    public required NewsStoryCategory Category { get; init; }
    public string CategoryLabel => NewsStoryProjection.CategoryLabel(Category);
    public required NewsStoryImportance Importance { get; init; }
    public string ImportanceLabel => Importance == NewsStoryImportance.Major ? "MAJOR" : "DISPATCH";
    public required string Headline { get; init; }
    public string Standfirst { get; init; } = "";
    public string Body { get; init; } = "";
    public string WhyText { get; init; } = "";
    public string DriverName { get; init; } = "";
    public string TeamName { get; init; } = "";
    public string DriverPortraitKey { get; init; } = "";
    public string TeamArtKey { get; init; } = "";
    public string CarArtKey { get; init; } = "";
    public string HistoryEventKey { get; init; } = "";

    public bool HasDate => DateLabel.Length > 0;
    public bool HasRound => Round is not null;
    public bool HasVenue => VenueName.Length > 0;
    public bool HasTrackArt => TrackArtKey.Length > 0;
    public bool HasStandfirst => Standfirst.Length > 0;
    public bool HasBody => Body.Length > 0;
    public bool HasWhy => WhyText.Length > 0;
    public bool HasDriver => DriverName.Length > 0;
    public bool HasTeam => TeamName.Length > 0;
    public bool HasDriverPortrait => DriverPortraitKey.Length > 0;
    public bool HasTeamArt => TeamArtKey.Length > 0;
    public bool HasCarArt => CarArtKey.Length > 0;
    public bool HasHistoryLink => HistoryEventKey.Length > 0;
    public bool CanRead => HasStandfirst || HasBody || HasWhy;

    internal bool MatchesSearch(string search)
    {
        if (search.Length == 0)
            return true;

        return Key.Contains(search, StringComparison.OrdinalIgnoreCase)
            || SeasonOrdinal.ToString(CultureInfo.InvariantCulture).Contains(search, StringComparison.OrdinalIgnoreCase)
            || SeasonYear.ToString(CultureInfo.InvariantCulture).Contains(search, StringComparison.OrdinalIgnoreCase)
            || RoundLabel.Contains(search, StringComparison.OrdinalIgnoreCase)
            || DateLabel.Contains(search, StringComparison.OrdinalIgnoreCase)
            || VenueName.Contains(search, StringComparison.OrdinalIgnoreCase)
            || CategoryLabel.Contains(search, StringComparison.OrdinalIgnoreCase)
            || Headline.Contains(search, StringComparison.OrdinalIgnoreCase)
            || Standfirst.Contains(search, StringComparison.OrdinalIgnoreCase)
            || Body.Contains(search, StringComparison.OrdinalIgnoreCase)
            || WhyText.Contains(search, StringComparison.OrdinalIgnoreCase)
            || DriverName.Contains(search, StringComparison.OrdinalIgnoreCase)
            || TeamName.Contains(search, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record NewsCategoryFilterViewModel(
    NewsStoryCategory? Category,
    string Key,
    string Label,
    int Count)
{
    public string DisplayText => $"{Label} {Count}";
    public bool IsAll => Category is null;
}

internal static class NewsStoryProjection
{
    public static IReadOnlyList<NewsStoryViewModel> Build(
        IReadOnlyList<SmgpDispatch> smgpDispatches,
        IReadOnlyList<NewsDispatch> journalFeed,
        CareerTimeline timeline,
        SmgpPaddockModel? paddock,
        bool showBody)
    {
        var candidates = new List<StoryCandidate>();
        var yearBySeason = timeline.Seasons
            .Select((season, index) => (Ordinal: index + 1, season.SeasonYear))
            .ToDictionary(item => item.Ordinal, item => item.SeasonYear);
        var raceLines = timeline.Seasons
            .SelectMany((season, index) => season.RoundLines.Select(line =>
                (Coordinate: (Season: index + 1, line.Round), Line: line)))
            .GroupBy(item => item.Coordinate)
            .ToDictionary(group => group.Key, group => group.First().Line);
        var driverById = paddock?.Drivers
            .Where(driver => driver.DriverId.Length > 0)
            .GroupBy(driver => driver.DriverId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal)
            ?? new Dictionary<string, SmgpDriverCard>(StringComparer.Ordinal);
        var teamById = paddock?.Teams
            .Where(team => team.TeamId.Length > 0)
            .GroupBy(team => team.TeamId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal)
            ?? new Dictionary<string, SmgpTeamCard>(StringComparer.Ordinal);

        for (int index = 0; index < smgpDispatches.Count; index++)
        {
            var dispatch = smgpDispatches[index];
            int? round = IsRecordedRound(dispatch.SortRound) ? dispatch.SortRound : null;
            driverById.TryGetValue(dispatch.DriverArtKey, out var driver);
            teamById.TryGetValue(dispatch.TeamArtKey, out var team);
            raceLines.TryGetValue((dispatch.SortSeason, round ?? -1), out var raceLine);
            int seasonYear = yearBySeason.GetValueOrDefault(dispatch.SortSeason);
            string historyKey = raceLine is not null
                ? HistoryArchiveProjection.RaceKey(dispatch.SortSeason, raceLine.Round)
                : "";

            candidates.Add(new StoryCandidate(
                dispatch.SortSeason,
                dispatch.SortRound,
                dispatch.SortSeq,
                SourcePriority: 1,
                SourceIndex: index,
                new NewsStoryViewModel
                {
                    Key = $"smgp:{dispatch.SortSeason}:{dispatch.SortRound}:{dispatch.SortSeq}",
                    SeasonOrdinal = dispatch.SortSeason,
                    SeasonYear = seasonYear,
                    DateLabel = seasonYear > 0 ? seasonYear.ToString(CultureInfo.InvariantCulture) : "",
                    Round = round,
                    RoundLabel = round is { } value ? $"Round {value}" : "",
                    VenueName = raceLine?.Venue ?? "",
                    Category = CategoryFor(dispatch.Kind),
                    Importance = ImportanceFor(dispatch.Kind),
                    Headline = dispatch.Headline,
                    Body = showBody ? dispatch.Body : "",
                    DriverName = driver?.Name ?? "",
                    TeamName = team?.Name ?? "",
                    DriverPortraitKey = dispatch.DriverArtKey,
                    TeamArtKey = dispatch.TeamArtKey,
                    CarArtKey = driver?.CarKey ?? "",
                    HistoryEventKey = historyKey,
                }));
        }

        int currentSeasonOrdinal = ResolveCurrentSeasonOrdinal(timeline, journalFeed);
        for (int index = 0; index < journalFeed.Count; index++)
        {
            var dispatch = journalFeed[index];
            int sortRound = dispatch.Round ?? SmgpDispatch.SeasonEndRound;
            raceLines.TryGetValue((currentSeasonOrdinal, dispatch.Round ?? -1), out var raceLine);
            string historyKey = raceLine is not null
                ? HistoryArchiveProjection.RaceKey(currentSeasonOrdinal, raceLine.Round)
                : "";

            candidates.Add(new StoryCandidate(
                currentSeasonOrdinal,
                sortRound,
                SortSequence: -index,
                SourcePriority: 0,
                SourceIndex: index,
                new NewsStoryViewModel
                {
                    Key = $"journal:{dispatch.SeasonYear}:{dispatch.Round?.ToString(CultureInfo.InvariantCulture) ?? "season"}:{index}",
                    SeasonOrdinal = currentSeasonOrdinal,
                    SeasonYear = dispatch.SeasonYear,
                    DateLabel = dispatch.SeasonYear > 0 ? dispatch.SeasonYear.ToString(CultureInfo.InvariantCulture) : "",
                    Round = dispatch.Round,
                    RoundLabel = dispatch.Round is { } value ? $"Round {value}" : "",
                    VenueName = raceLine?.Venue ?? "",
                    Category = CategoryFor(dispatch.Kind),
                    Importance = dispatch.Round is null
                        ? NewsStoryImportance.Major
                        : NewsStoryImportance.Standard,
                    Headline = dispatch.Headline,
                    Body = showBody ? dispatch.Body : "",
                    WhyText = dispatch.WhyText,
                    HistoryEventKey = historyKey,
                }));
        }

        return candidates
            .OrderByDescending(candidate => candidate.SeasonOrdinal)
            .ThenByDescending(candidate => candidate.SortRound)
            .ThenByDescending(candidate => candidate.SourcePriority)
            .ThenByDescending(candidate => candidate.SortSequence)
            .ThenBy(candidate => candidate.SourceIndex)
            .Select(candidate => candidate.Story)
            .ToList();
    }

    public static IReadOnlyList<NewsCategoryFilterViewModel> BuildCategories(
        IReadOnlyCollection<NewsStoryViewModel> stories)
    {
        var categories = new List<NewsCategoryFilterViewModel>
        {
            new(null, "all", "All stories", stories.Count),
        };

        foreach (var category in Enum.GetValues<NewsStoryCategory>())
        {
            int count = stories.Count(story => story.Category == category);
            if (count > 0)
                categories.Add(new(category, CategoryKey(category), CategoryLabel(category), count));
        }

        return categories;
    }

    public static string CategoryLabel(NewsStoryCategory category) => category switch
    {
        NewsStoryCategory.TeamMovement => "Team movement",
        NewsStoryCategory.RaceReport => "Race report",
        _ => category.ToString(),
    };

    private static string CategoryKey(NewsStoryCategory category) => category switch
    {
        NewsStoryCategory.TeamMovement => "team-movement",
        NewsStoryCategory.RaceReport => "race-report",
        _ => category.ToString().ToLowerInvariant(),
    };

    private static NewsStoryCategory CategoryFor(SmgpDispatchKind kind) => kind switch
    {
        SmgpDispatchKind.TitleRace => NewsStoryCategory.Championship,
        SmgpDispatchKind.RivalWatch => NewsStoryCategory.Rivalry,
        SmgpDispatchKind.RaceResult => NewsStoryCategory.RaceReport,
        // Milestone/setback/digest do not expose their finer subject as structured data. Paddock is
        // the truthful broad category; headline parsing would invent a stronger classification.
        _ => NewsStoryCategory.Paddock,
    };

    private static NewsStoryCategory CategoryFor(string kind) => kind switch
    {
        "championship" or "title" => NewsStoryCategory.Championship,
        "rivalry" => NewsStoryCategory.Rivalry,
        "offer" or "team" or "teamMovement" or "team-movement" => NewsStoryCategory.TeamMovement,
        "injury" => NewsStoryCategory.Injury,
        "promotion" => NewsStoryCategory.Promotion,
        "record" or "records" => NewsStoryCategory.Records,
        "race" => NewsStoryCategory.RaceReport,
        _ => NewsStoryCategory.Paddock,
    };

    private static NewsStoryImportance ImportanceFor(SmgpDispatchKind kind) => kind switch
    {
        SmgpDispatchKind.RaceResult or SmgpDispatchKind.RivalWatch => NewsStoryImportance.Standard,
        _ => NewsStoryImportance.Major,
    };

    private static bool IsRecordedRound(int sortRound) =>
        sortRound is > SmgpDispatch.SeasonStartRound and < SmgpDispatch.SeasonEndRound;

    private static int ResolveCurrentSeasonOrdinal(
        CareerTimeline timeline,
        IReadOnlyList<NewsDispatch> journalFeed)
    {
        if (timeline.Seasons.Count == 0 || journalFeed.Count == 0)
            return 0;

        int year = journalFeed[0].SeasonYear;
        for (int index = timeline.Seasons.Count - 1; index >= 0; index--)
        {
            if (timeline.Seasons[index].SeasonYear == year)
                return index + 1;
        }

        return 0;
    }

    private sealed record StoryCandidate(
        int SeasonOrdinal,
        int SortRound,
        int SortSequence,
        int SourcePriority,
        int SourceIndex,
        NewsStoryViewModel Story);
}
