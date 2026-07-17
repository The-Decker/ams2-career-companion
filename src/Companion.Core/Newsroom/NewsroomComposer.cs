using Companion.Core.Determinism;

namespace Companion.Core.Newsroom;

/// <summary>One rendered article body section, in canonical order.</summary>
public sealed record NewsroomSection(string Name, string Text);

/// <summary>
/// A fully rendered, display-only article. NEVER stored: <see cref="Key"/> (the event's dedupe
/// key) is the stable identity that reading state and navigation reference; the text re-renders
/// deterministically from the master seed on every read (docs/dev/newsroom-history-overhaul.md D3).
/// </summary>
public sealed record NewsroomArticle
{
    public required string Key { get; init; }
    public required NewsEventKind EventKind { get; init; }
    public required NewsroomCategory Category { get; init; }
    public required EditorialStatus Status { get; init; }
    public required ContentProvenance Provenance { get; init; }
    public required int SeasonOrdinal { get; init; }
    public required int SeasonYear { get; init; }
    public required int Round { get; init; }
    public string VenueName { get; init; } = "";
    public string SubjectId { get; init; } = "";
    public string SubjectName { get; init; } = "";
    public string TeamName { get; init; } = "";
    public string DeskId { get; init; } = "";
    public string DeskName { get; init; } = "";
    public string DeskMonogram { get; init; } = "";
    public required string Headline { get; init; }
    public string Deck { get; init; } = "";
    public string Summary { get; init; } = "";
    public IReadOnlyList<NewsroomSection> Sections { get; init; } = [];
    public required int ImportanceScore { get; init; }
    public required EditorialTier Tier { get; init; }
    public string TemplateId { get; init; } = "";
    /// <summary>Rough reading time from body word count (~220 wpm, floor 15s).</summary>
    public int ReadingSeconds { get; init; }

    public string Body => string.Join("\n\n", Sections.Select(s => s.Text));
}

/// <summary>Facts the composer needs beyond the event itself — supplied by the session.</summary>
public sealed record NewsroomIdentity
{
    public string PlayerName { get; init; } = "";
    public string PlayerTeamName { get; init; } = "";
    /// <summary>Era override for fictional universes ("smgp"); null resolves by season year.</summary>
    public string? PreferredEra { get; init; }
}

/// <summary>
/// Deterministic article renderer: template + desk by rendezvous hash (stable under corpus
/// growth), pool fragments from the display-only <c>"newsroom"</c> stream (same key = same
/// words on every open), all grammar via <see cref="NewsroomGrammar"/>. Pure — no I/O.
/// </summary>
public static class NewsroomComposer
{
    public const string StreamName = "newsroom";

    public static NewsroomArticle? Compose(
        NewsEvent e,
        NewsroomCorpus corpus,
        NewsDesks desks,
        NewsroomIdentity identity,
        ulong masterSeed)
    {
        var eraKey = identity.PreferredEra is { Length: > 0 } preferred
            ? preferred
            : corpus.ResolveEra(e.SeasonYear);
        var category = NewsroomCategories.CategoryFor(e.Kind);
        var desk = desks.Assign(category, eraKey, e.DedupeKey, masterSeed);
        var template = corpus.Select(e, eraKey, desk?.Id ?? "", masterSeed);
        if (template is null)
        {
            return null;
        }

        var tokens = BuildTokens(e, identity);
        var stream = new StreamFactory(masterSeed)
            .CreateStream(StreamName, e.SeasonOrdinal, e.Round, e.DedupeKey);
        IReadOnlyList<string>? Pools(string name) => corpus.Pool(name, eraKey);

        var headline = NewsroomGrammar.Expand(template.Headline, tokens, Pools, stream);
        var deck = template.Deck.Length > 0 ? NewsroomGrammar.Expand(template.Deck, tokens, Pools, stream) : "";
        var summary = template.Summary.Length > 0 ? NewsroomGrammar.Expand(template.Summary, tokens, Pools, stream) : "";

        var sections = new List<NewsroomSection>();
        foreach (var name in NewsroomCorpus.SectionOrder)
        {
            if (template.Sections.TryGetValue(name, out var sectionTemplate))
            {
                var text = NewsroomGrammar.Expand(sectionTemplate, tokens, Pools, stream);
                if (text.Length > 0)
                {
                    sections.Add(new NewsroomSection(name, text));
                }
            }
        }

        var score = EditorialImportance.Score(e);
        var words = sections.Sum(s => s.Text.Count(c => c == ' ') + 1)
            + summary.Count(c => c == ' ') + 1;

        return new NewsroomArticle
        {
            Key = e.DedupeKey,
            EventKind = e.Kind,
            Category = category,
            Status = NewsroomCategories.StatusFor(e.Kind),
            Provenance = NewsroomCategories.ProvenanceFor(e.Kind),
            SeasonOrdinal = e.SeasonOrdinal,
            SeasonYear = e.SeasonYear,
            Round = e.Round,
            VenueName = e.VenueName,
            SubjectId = e.SubjectId,
            SubjectName = tokens["subject"],
            TeamName = e.SubjectTeamName,
            DeskId = desk?.Id ?? "",
            DeskName = desk?.Name ?? "",
            DeskMonogram = desk?.Monogram ?? "",
            Headline = headline,
            Deck = deck,
            Summary = summary,
            Sections = sections,
            ImportanceScore = score,
            Tier = EditorialImportance.Tier(score),
            TemplateId = template.Id,
            ReadingSeconds = Math.Max(15, words * 60 / 220),
        };
    }

    /// <summary>The complete token vocabulary. Missing facts become EMPTY strings so
    /// <c>[[?token: …]]</c> optional segments drop cleanly; templates must reach every
    /// non-guaranteed fact through an optional segment (the content tests enforce it).</summary>
    public static IReadOnlyDictionary<string, string> BuildTokens(NewsEvent e, NewsroomIdentity identity)
    {
        var f = e.Facts;
        var isPlayer = e.SubjectId == "player";
        var subject = isPlayer
            ? identity.PlayerName.Length > 0 ? identity.PlayerName : "the driver"
            : e.SubjectName.Length > 0 ? e.SubjectName : "the driver";
        var team = e.SubjectTeamName.Length > 0
            ? e.SubjectTeamName
            : isPlayer ? identity.PlayerTeamName : "";

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["player"] = identity.PlayerName.Length > 0 ? identity.PlayerName : "the driver",
            ["subject"] = subject,
            ["team"] = team.Length > 0 ? team : "the team",
            ["venue"] = e.VenueName,
            ["year"] = Invariant(e.SeasonYear),
            ["season"] = Invariant(e.SeasonOrdinal),
            ["round"] = e.Round is > 0 and < CareerNewsEvents.SeasonEndRound ? Invariant(e.Round) : "",
            ["position"] = f.PlayerFinish is { } p ? Invariant(p) : "",
            ["expected"] = f.ExpectedFinish is { } x ? Invariant(x) : "",
            ["quali"] = f.QualifyingPosition is { } q ? Invariant(q) : "",
            ["champPosition"] = f.ChampionshipPosition is { } cp ? Invariant(cp) : "",
            ["gap"] = f.PointsGapToLeader is { } g and > 0 ? g.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : "",
            ["streak"] = f.StreakLength > 1 ? Invariant(f.StreakLength) : "",
            ["drought"] = f.DroughtLength > 0 ? Invariant(f.DroughtLength) : "",
            ["milestone"] = f.MilestoneValue > 0 ? Invariant(f.MilestoneValue) : "",
            ["counter"] = f.MilestoneCounter,
            ["winner"] = f.WinnerName,
            ["winnerTeam"] = f.WinnerTeamName,
            ["rival"] = f.RivalName,
            ["reason"] = f.RetirementReason,
            ["missRaces"] = f.MissRaces > 0 ? Invariant(f.MissRaces) : "",
            ["wet"] = f.IsWet ? "wet" : "",
            ["finale"] = f.IsFinalRound ? "finale" : "",
            // Dynasty economy tokens (economy §8) — empty for every non-economy story.
            ["sponsor"] = f.SponsorName,
            ["amount"] = f.MoneyAmount,
        };
    }

    private static string Invariant(int n) =>
        n.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>The fixed kind → category/status/provenance mappings (no strings scattered).</summary>
public static class NewsroomCategories
{
    public static NewsroomCategory CategoryFor(NewsEventKind kind) => kind switch
    {
        NewsEventKind.RaceWon or NewsEventKind.PodiumFinish or NewsEventKind.PointsFinish
            or NewsEventKind.MidfieldResult or NewsEventKind.SatOutRound => NewsroomCategory.RaceReport,
        NewsEventKind.PolePosition or NewsEventKind.FirstPole
            or NewsEventKind.QualifyingSurprise => NewsroomCategory.QualifyingReport,
        NewsEventKind.Overperformed or NewsEventKind.Underperformed
            or NewsEventKind.DominantDisplay => NewsroomCategory.PostRaceAnalysis,
        NewsEventKind.ChampionshipLeadTaken or NewsEventKind.ChampionshipLeadLost
            or NewsEventKind.LeadChangedHands or NewsEventKind.TitleFightTightens
            or NewsEventKind.FinalRoundShowdown or NewsEventKind.TitleClinchedEarly
            or NewsEventKind.TitleRaceLost or NewsEventKind.ChampionCrowned
            or NewsEventKind.StandingsClimb => NewsroomCategory.ChampionshipAnalysis,
        NewsEventKind.FirstStart or NewsEventKind.FirstPoints or NewsEventKind.FirstTop5
            or NewsEventKind.FirstPodium or NewsEventKind.FirstWin or NewsEventKind.FirstRetirement
            or NewsEventKind.CareerMilestone or NewsEventKind.BestFinishImproved
            or NewsEventKind.WinStreak or NewsEventKind.PodiumStreak or NewsEventKind.PointsStreak
            or NewsEventKind.WinDroughtEnded
            or NewsEventKind.PointsDroughtEnded => NewsroomCategory.RecordsAndMilestones,
        NewsEventKind.RetiredMechanical
            or NewsEventKind.RetirementStreak => NewsroomCategory.MechanicalReliability,
        NewsEventKind.RetiredDriverError => NewsroomCategory.DriverPerformance,
        NewsEventKind.AiWinStreak or NewsEventKind.UpsetWinner => NewsroomCategory.DriverPerformance,
        NewsEventKind.TeamPromoted or NewsEventKind.TeamRelegated => NewsroomCategory.TeamPerformance,
        NewsEventKind.OfferReceived or NewsEventKind.PlayerTeamChanged or NewsEventKind.SeatVacancy
            or NewsEventKind.SeatFilled => NewsroomCategory.DriverTransfers,
        NewsEventKind.RetirementConsidered or NewsEventKind.DriverRetired => NewsroomCategory.VeteranWatch,
        NewsEventKind.PlayerInjured or NewsEventKind.SeasonEndingInjury
            or NewsEventKind.PlayerDied
            or NewsEventKind.ReturnedFromInjury => NewsroomCategory.InjuriesAndReplacements,
        NewsEventKind.LevelMilestone or NewsEventKind.Level300Reached => NewsroomCategory.RecordsAndMilestones,
        NewsEventKind.CareerCompleted => NewsroomCategory.CareerRetrospective,
        NewsEventKind.RivalryDeveloped => NewsroomCategory.Rivalries,
        NewsEventKind.SeasonCompleted => NewsroomCategory.SeasonReview,
        NewsEventKind.SeasonStarted or NewsEventKind.CareerCreated => NewsroomCategory.WeekendPreview,
        NewsEventKind.HistoryDiverged or NewsEventKind.HistoryHeld
            or NewsEventKind.SmgpCanonDiverged
            or NewsEventKind.SmgpCanonHeld => NewsroomCategory.HistoricalRetrospective,
        NewsEventKind.DnqDrama => NewsroomCategory.RaceReport,
        // Dynasty economy (economy §8)
        NewsEventKind.SponsorSigned => NewsroomCategory.ContractNews,
        NewsEventKind.MajorRepairBill => NewsroomCategory.MechanicalReliability,
        NewsEventKind.NearBankruptcy or NewsEventKind.BankruptcyDeclared => NewsroomCategory.OperationalPressure,
        NewsEventKind.FinancialWindfall => NewsroomCategory.ContractNews,
        NewsEventKind.DevelopmentMilestone => NewsroomCategory.TechnicalDevelopments,
        _ => NewsroomCategory.RaceReport,
    };

    public static EditorialStatus StatusFor(NewsEventKind kind) => kind switch
    {
        NewsEventKind.RetirementConsidered or NewsEventKind.SeatVacancy => EditorialStatus.Reported,
        NewsEventKind.TitleFightTightens or NewsEventKind.FinalRoundShowdown
            or NewsEventKind.NearBankruptcy => EditorialStatus.Developing,
        NewsEventKind.Overperformed or NewsEventKind.Underperformed or NewsEventKind.DominantDisplay
            or NewsEventKind.StandingsClimb => EditorialStatus.Analysis,
        NewsEventKind.HistoryDiverged or NewsEventKind.HistoryHeld
            or NewsEventKind.SmgpCanonDiverged or NewsEventKind.SmgpCanonHeld
            or NewsEventKind.SeasonCompleted
            or NewsEventKind.CareerCompleted => EditorialStatus.Retrospective,
        _ => EditorialStatus.Confirmed,
    };

    public static ContentProvenance ProvenanceFor(NewsEventKind kind) => kind switch
    {
        NewsEventKind.HistoryHeld => ContentProvenance.VerifiedHistorical,
        // The SEGA canon is FICTION: its divergence layer must badge as SMGP UNIVERSE, never as
        // verified history and never silently as the career universe (D9's layer separation).
        NewsEventKind.SmgpCanonDiverged or NewsEventKind.SmgpCanonHeld => ContentProvenance.SmgpFiction,
        _ => ContentProvenance.CareerUniverse,
    };
}
