using System.Globalization;
using Companion.Core.Newsroom;
using Companion.Core.Smgp;
using Companion.Data;
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

    // Newsroom editorial surface (empty for legacy journal/SMGP-dispatch stories).
    public string Deck { get; init; } = "";
    public string DeskName { get; init; } = "";
    public string DeskMonogram { get; init; } = "";
    /// <summary>Editorial status badge text ("CONFIRMED", "RUMOUR", "ANALYSIS"...).</summary>
    public string StatusLabel { get; init; } = "";
    /// <summary>Provenance badge ("CAREER UNIVERSE" / "HISTORICAL RECORD"...). Present on
    /// every newsroom story so simulated coverage is never mistaken for real history.</summary>
    public string ProvenanceLabel { get; init; } = "";
    /// <summary>The fine-grained editorial category ("Championship analysis", "Mechanical
    /// reliability"...); <see cref="Category"/> stays the coarse filter bucket.</summary>
    public string CategoryDetail { get; init; } = "";
    /// <summary>Layout tier ("LEAD" / "FEATURED" / "STANDARD" / "BRIEF"); empty = legacy.</summary>
    public string TierLabel { get; init; } = "";
    public int ReadingSeconds { get; init; }
    public bool IsRead { get; init; }
    public bool IsBookmarked { get; init; }
    /// <summary>The developing-story thread this article belongs to; empty = standalone.</summary>
    public string ThreadKey { get; init; } = "";

    public bool HasDeck => Deck.Length > 0;
    public bool HasDesk => DeskName.Length > 0;
    public bool HasStatus => StatusLabel.Length > 0;
    public bool HasProvenance => ProvenanceLabel.Length > 0;
    public bool HasCategoryDetail => CategoryDetail.Length > 0;
    public bool HasTier => TierLabel.Length > 0;
    public bool HasThread => ThreadKey.Length > 0;
    public bool IsUnread => !IsRead && CanRead;
    public string ReadingTimeLabel => ReadingSeconds <= 0 ? ""
        : ReadingSeconds < 60 ? $"{ReadingSeconds}s read"
        : $"{(ReadingSeconds + 30) / 60} min read";

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
            || TeamName.Contains(search, StringComparison.OrdinalIgnoreCase)
            || Deck.Contains(search, StringComparison.OrdinalIgnoreCase)
            || DeskName.Contains(search, StringComparison.OrdinalIgnoreCase)
            || CategoryDetail.Contains(search, StringComparison.OrdinalIgnoreCase);
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
    /// <summary>Newsroom kinds the SMGP dispatch feed already voices with richer art and
    /// canon language, suppressed for SMGP careers so one happening never appears twice.</summary>
    private static readonly HashSet<NewsEventKind> SmgpCoveredKinds =
    [
        NewsEventKind.CareerCreated, NewsEventKind.SeasonStarted, NewsEventKind.SeasonCompleted,
        NewsEventKind.FirstStart, NewsEventKind.FirstPoints, NewsEventKind.FirstTop5,
        NewsEventKind.FirstPodium, NewsEventKind.FirstWin, NewsEventKind.FirstPole,
        NewsEventKind.ChampionCrowned, NewsEventKind.PlayerInjured,
        NewsEventKind.SeasonEndingInjury, NewsEventKind.PlayerDied,
        NewsEventKind.PlayerTeamChanged, NewsEventKind.RivalryDeveloped, NewsEventKind.DnqDrama,
    ];

    /// <summary>Newsroom kinds that constitute THE weekend result story, when one exists for a
    /// round, the legacy journal headline for that round is superseded (never duplicated).</summary>
    private static readonly HashSet<NewsEventKind> ResultFamilyKinds =
    [
        NewsEventKind.RaceWon, NewsEventKind.PodiumFinish, NewsEventKind.PointsFinish,
        NewsEventKind.MidfieldResult, NewsEventKind.Overperformed, NewsEventKind.Underperformed,
        NewsEventKind.RetiredMechanical, NewsEventKind.RetiredDriverError, NewsEventKind.SatOutRound,
    ];

    public static IReadOnlyList<NewsStoryViewModel> Build(
        IReadOnlyList<SmgpDispatch> smgpDispatches,
        IReadOnlyList<NewsDispatch> journalFeed,
        CareerTimeline timeline,
        SmgpPaddockModel? paddock,
        bool showBody) =>
        Build(smgpDispatches, journalFeed, timeline, paddock, showBody,
            newsroomArticles: [],
            readingState: new Dictionary<string, NewsReadingState>(),
            threadKeyByStory: new Dictionary<string, string>(),
            smgpMode: false);

    public static IReadOnlyList<NewsStoryViewModel> Build(
        IReadOnlyList<SmgpDispatch> smgpDispatches,
        IReadOnlyList<NewsDispatch> journalFeed,
        CareerTimeline timeline,
        SmgpPaddockModel? paddock,
        bool showBody,
        IReadOnlyList<NewsroomArticle> newsroomArticles,
        IReadOnlyDictionary<string, NewsReadingState> readingState,
        IReadOnlyDictionary<string, string> threadKeyByStory,
        bool smgpMode)
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

        // The rendered newsroom: the richest source, and the one that supersedes the legacy
        // journal headline for any round it covers with a result-family article.
        var resultCoveredRounds = new HashSet<(int Ordinal, int Round)>();
        var completedOrdinals = new HashSet<int>();
        for (int index = 0; index < newsroomArticles.Count; index++)
        {
            var article = newsroomArticles[index];
            if (smgpMode && SmgpCoveredKinds.Contains(article.EventKind))
            {
                continue; // the SMGP dispatch feed already tells this one, in canon voice
            }

            int? round = article.Round is > 0 and < CareerNewsEvents.SeasonEndRound
                ? article.Round
                : null;
            if (round is { } r && ResultFamilyKinds.Contains(article.EventKind))
            {
                resultCoveredRounds.Add((article.SeasonOrdinal, r));
            }
            if (article.EventKind == NewsEventKind.SeasonCompleted)
            {
                completedOrdinals.Add(article.SeasonOrdinal);
            }
            raceLines.TryGetValue((article.SeasonOrdinal, round ?? -1), out var raceLine);

            candidates.Add(new StoryCandidate(
                article.SeasonOrdinal,
                article.Round,
                SortSequence: 2_000_000 + article.ImportanceScore,
                SourcePriority: 2,
                SourceIndex: index,
                new NewsStoryViewModel
                {
                    Key = article.Key,
                    SeasonOrdinal = article.SeasonOrdinal,
                    SeasonYear = article.SeasonYear,
                    DateLabel = article.SeasonYear > 0
                        ? article.SeasonYear.ToString(CultureInfo.InvariantCulture)
                        : "",
                    Round = round,
                    RoundLabel = round is { } value ? $"Round {value}" : "",
                    VenueName = article.VenueName.Length > 0 ? article.VenueName : raceLine?.Venue ?? "",
                    Category = CoarseCategory(article.Category),
                    Importance = article.Tier <= Companion.Core.Newsroom.EditorialTier.Featured
                        ? NewsStoryImportance.Major
                        : NewsStoryImportance.Standard,
                    Headline = article.Headline,
                    Standfirst = article.Summary,
                    Body = showBody ? article.Body : "",
                    DriverName = article.SubjectName,
                    TeamName = article.TeamName,
                    HistoryEventKey = raceLine is not null
                        ? HistoryArchiveProjection.RaceKey(article.SeasonOrdinal, raceLine.Round)
                        : "",
                    Deck = article.Deck,
                    DeskName = article.DeskName,
                    DeskMonogram = article.DeskMonogram,
                    StatusLabel = StatusLabel(article.Status),
                    ProvenanceLabel = ProvenanceLabel(article.Provenance),
                    CategoryDetail = DetailLabel(article.Category),
                    TierLabel = article.Tier.ToString().ToUpperInvariant(),
                    ReadingSeconds = article.ReadingSeconds,
                }));
        }

        int currentSeasonOrdinal = ResolveCurrentSeasonOrdinal(timeline, journalFeed);
        for (int index = 0; index < journalFeed.Count; index++)
        {
            var dispatch = journalFeed[index];
            int sortRound = dispatch.Round ?? SmgpDispatch.SeasonEndRound;
            if (dispatch.Round is { } journalRound
                && resultCoveredRounds.Contains((currentSeasonOrdinal, journalRound))
                && string.Equals(dispatch.Kind, "race", StringComparison.Ordinal))
            {
                continue; // superseded by the newsroom's own result article for this round
            }
            if (dispatch.Round is null
                && string.Equals(dispatch.Kind, "season", StringComparison.Ordinal)
                && completedOrdinals.Contains(currentSeasonOrdinal))
            {
                continue; // superseded by the newsroom's season review
            }
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
            .Select(candidate =>
            {
                var story = candidate.Story;
                var state = readingState.GetValueOrDefault(story.Key);
                var threadKey = threadKeyByStory.GetValueOrDefault(story.Key, "");
                return state is null && threadKey.Length == 0
                    ? story
                    : story with
                    {
                        IsRead = state?.IsRead ?? false,
                        IsBookmarked = state?.Bookmarked ?? false,
                        ThreadKey = threadKey,
                    };
            })
            .ToList();
    }

    /// <summary>The fine editorial category folded into the wire's coarse filter buckets —
    /// existing filter chips stay stable; <see cref="NewsStoryViewModel.CategoryDetail"/>
    /// carries the precise label for badges.</summary>
    internal static NewsStoryCategory CoarseCategory(NewsroomCategory category) => category switch
    {
        NewsroomCategory.ChampionshipAnalysis or NewsroomCategory.SeasonReview => NewsStoryCategory.Championship,
        NewsroomCategory.Rivalries => NewsStoryCategory.Rivalry,
        NewsroomCategory.DriverTransfers or NewsroomCategory.ContractNews
            or NewsroomCategory.TeamPerformance or NewsroomCategory.TeamPolitics => NewsStoryCategory.TeamMovement,
        NewsroomCategory.InjuriesAndReplacements => NewsStoryCategory.Injury,
        NewsroomCategory.RecordsAndMilestones or NewsroomCategory.CareerRetrospective => NewsStoryCategory.Records,
        NewsroomCategory.RaceReport or NewsroomCategory.QualifyingReport
            or NewsroomCategory.PostRaceAnalysis or NewsroomCategory.WeekendPreview => NewsStoryCategory.RaceReport,
        _ => NewsStoryCategory.Paddock,
    };

    internal static string DetailLabel(NewsroomCategory category)
    {
        var name = category.ToString();
        var chars = new List<char>(name.Length + 6);
        foreach (var c in name)
        {
            if (char.IsUpper(c) && chars.Count > 0)
            {
                chars.Add(' ');
                chars.Add(char.ToLowerInvariant(c));
            }
            else
            {
                chars.Add(c);
            }
        }
        return new string([.. chars]);
    }

    internal static string StatusLabel(EditorialStatus status) =>
        status.ToString().ToUpperInvariant();

    internal static string ProvenanceLabel(ContentProvenance provenance) => provenance switch
    {
        ContentProvenance.VerifiedHistorical => "HISTORICAL RECORD",
        ContentProvenance.CareerUniverse => "CAREER UNIVERSE",
        ContentProvenance.EditorialAnalysis => "ANALYSIS",
        ContentProvenance.SmgpFiction => "SMGP UNIVERSE",
        _ => "SYSTEM",
    };

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
