using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Companion.Core.Career;
using Companion.ViewModels.Services;

namespace Companion.ViewModels.Hub;

/// <summary>
/// The History / Scrapbook lens (career-hub-design.md §4, decision 18 "total recall"): a
/// read-only projection of the whole career into per-season scrapbook cards, a lineage timeline,
/// a records book (career bests/streaks/milestones), and every race's archived news article. All
/// four surfaces are pure reads over <see cref="ICareerSession.CareerTimeline"/> and
/// <see cref="ICareerSession.ReadFeed"/>, no sim, no persistence, so the History tab renders
/// (and re-renders after every Apply) with zero new state cost. Refreshed in place off the new
/// session state exactly like the Standings/News lenses.
/// </summary>
public sealed partial class HistoryViewModel : InspectorHostViewModel
{
    private readonly ICareerSession _session;

    public HistoryViewModel(ICareerSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        EraSkin = session.EraThemeOverrides()?.ForYear(session.Pack.Season.Year)
            ?? EraThemes.ForYear(session.Pack.Season.Year);
        Refresh();
    }

    /// <summary>The era skin the scrapbook chrome renders with (era-theming-assets-brief.md
    /// Slice 0): the period medium/accent/font/texture for the career's season year, resolved
    /// through the <c>era-themes.json</c> override when one covers the decade.</summary>
    public IEraSkin EraSkin { get; }

    /// <summary>The era medium flattened to a top-level bindable so a WPF DataTrigger can key
    /// on it without a nested path.</summary>
    public EraMedium EraMedium => EraSkin.Medium;

    // ---------- clickable numbers → the "Why?" inspector (decisions 4 + 5) ----------

    /// <summary>Open the inspector for a season's player numbers (final finish / rep / OPI on a
    /// scrapbook card): walks the player's whole-season journal for the card's
    /// <see cref="SeasonCardViewModel.SeasonYear"/>. Uses the season-scoped seam
    /// (<see cref="ICareerSession.JournalForSeason(string,int,int?)"/>) so ANY completed season on a
    /// card, not just the current one, opens the inspector for that season's numbers; a year with no
    /// matching season returns an empty chain and simply does not open a panel. Mouse (click the
    /// number) and keyboard (a bound accelerator) both invoke this, decision 8's parity.</summary>
    [RelayCommand]
    private void OpenSeasonInspector(int seasonYear) =>
        ShowInspector(_session.JournalForSeason("player", seasonYear));

    /// <summary>One scrapbook card per season (oldest first, the lineage order). Also the
    /// lineage timeline's row source: the view renders the same collection as a vertical
    /// timeline down the left and full cards on the right.</summary>
    public ObservableCollection<SeasonCardViewModel> Seasons { get; } = [];

    /// <summary>The career records book, bests, counts, streaks. Never null.</summary>
    [ObservableProperty]
    private RecordsBookViewModel _records = RecordsBookViewModel.Empty;

    /// <summary>Every archived race/season dispatch of the career, newest first, the same feed
    /// the News tab shows, preserved forever in the scrapbook (decision 18). Each expands into
    /// its full period article on click.</summary>
    public ObservableCollection<NewsItemViewModel> ArchivedArticles { get; } = [];

    /// <summary>True before the first season has any applied round, the tab shows a friendly
    /// empty state instead of a blank scrapbook.</summary>
    public bool IsEmpty => Seasons.Count == 0;

    /// <summary>True once at least one archived dispatch exists (drives the articles section's
    /// empty state independently of the season cards).</summary>
    public bool HasArticles => ArchivedArticles.Count > 0;

    /// <summary>The upcoming race of the CURRENT season, a spoiler-free Race Preview (circuit map +
    /// track detail) shown prominently at the top of the History tab. Null when the current season is
    /// finished or no history is shipped for its year.</summary>
    [ObservableProperty]
    private HistoricalRoundViewModel? _nextRacePreview;

    /// <summary>The current season's year, for the "Next race: 1988" header.</summary>
    [ObservableProperty]
    private int _nextRaceYear;

    public bool HasNextRacePreview => NextRacePreview is not null;

    partial void OnNextRacePreviewChanged(HistoricalRoundViewModel? value) =>
        OnPropertyChanged(nameof(HasNextRacePreview));

    /// <summary>The SMGP-universe "What Really Happened" almanac (the fictional-world counterpart to the
    /// per-card real-F1 panel), the SEGA world's legend of every circuit, unlocked per race. Null for
    /// every non-SMGP career; then the panel is hidden. Shown once per History tab (not per card), since
    /// the replica mode is one continuous fictional world with a fixed set of circuits.</summary>
    [ObservableProperty]
    private SmgpWorldHistoryViewModel? _smgpWorld;

    public bool HasSmgpWorld => SmgpWorld is not null;

    partial void OnSmgpWorldChanged(SmgpWorldHistoryViewModel? value) =>
        OnPropertyChanged(nameof(HasSmgpWorld));

    /// <summary>Re-project the whole scrapbook off current session state (on open and after
    /// every Apply). Idempotent: rebuilds every collection from scratch.</summary>
    public void Refresh()
    {
        var timeline = _session.CareerTimeline();

        Seasons.Clear();
        // Newest season first reads best as a scrapbook (this season at the top), while the
        // records book aggregates the whole lineage regardless of order. Each card also carries the
        // REAL historical results of its year (f1db-derived, read-only) so the player can see "what
        // really happened" next to their own diverged season, null when none is shipped for the year.
        // A REPLICA-mode pack (SMGP) is a fictional world: revealing the real season's races round
        // by round would be nonsense there, so its cards carry no historical documents at all.
        bool fictionalSeason = string.Equals(
            _session.Pack.Manifest.CareerStyle, Companion.Core.Smgp.SmgpRules.CareerStyle, StringComparison.Ordinal);
        foreach (var card in timeline.Seasons.Reverse())
            Seasons.Add(new SeasonCardViewModel(
                card, fictionalSeason ? null : _session.HistoricalSeason(card.SeasonYear)));

        Records = new RecordsBookViewModel(timeline.Records);

        ArchivedArticles.Clear();
        foreach (var dispatch in _session.ReadFeed())
            ArchivedArticles.Add(new NewsItemViewModel(dispatch));

        // The current season (newest card) surfaces its next unraced round as a prominent preview.
        var current = Seasons.FirstOrDefault();
        if (current?.RealSeason is { IsSeasonComplete: false } real)
        {
            NextRacePreview = real.Rounds.FirstOrDefault(r => !r.IsRevealed);
            NextRaceYear = real.Year;
        }
        else
        {
            NextRacePreview = null;
            NextRaceYear = 0;
        }

        // The SMGP-universe almanac (fictional replica mode only), the SEGA world's "what really
        // happened" per circuit. Null everywhere else, so the panel is a no-op for normal careers.
        var world = _session.SmgpWorldHistory();
        SmgpWorld = world is not null ? new SmgpWorldHistoryViewModel(world) : null;

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasArticles));
    }
}

/// <summary>The SMGP-universe "What Really Happened" almanac for the History tab: the SEGA world's own
/// legend of every circuit on the calendar, each unlocked once the player has raced it. Display-only
/// projection of <see cref="SmgpWorldHistory"/>.</summary>
public sealed class SmgpWorldHistoryViewModel
{
    public SmgpWorldHistoryViewModel(SmgpWorldHistory world)
    {
        ArgumentNullException.ThrowIfNull(world);
        Races = world.Races.Select(r => new SmgpWorldRaceViewModel(r)).ToList();
        RevealedCount = world.RevealedCount;
        Total = world.Races.Count;
    }

    /// <summary>Every circuit, in the current season's round order, sealed until raced, then the legend.</summary>
    public IReadOnlyList<SmgpWorldRaceViewModel> Races { get; }

    public int RevealedCount { get; }

    public int Total { get; }

    /// <summary>"6 of 16 circuits unlocked", the almanac's progress line.</summary>
    public string ProgressText => $"{RevealedCount} of {Total} circuits unlocked";
}

/// <summary>One circuit's almanac entry. Before the player has raced it (<see cref="IsRevealed"/> =
/// false) it is SEALED, the header shows the venue and a "sealed" tag, and expanding shows only a
/// spoiler-free teaser. Once raced, expanding shows the SEGA world's full legend (title, circuit,
/// champion of record, the story paragraphs, and lore notes).</summary>
public sealed partial class SmgpWorldRaceViewModel : ObservableObject
{
    /// <summary>Whether this entry's detail is expanded (default off, the header always shows).</summary>
    [ObservableProperty]
    private bool _isExpanded;

    [RelayCommand]
    private void Toggle() => IsExpanded = !IsExpanded;

    public SmgpWorldRaceViewModel(SmgpWorldRace race)
    {
        ArgumentNullException.ThrowIfNull(race);
        IsRevealed = race.IsRevealed;
        RoundLabel = $"R{race.Round}";
        VenueName = race.VenueName;
        Title = race.Title;
        Circuit = race.Circuit;
        Champion = race.Champion;
        Body = race.Body;
        Notes = race.Notes;
    }

    /// <summary>True once the player has raced this venue, the legend is unlocked.</summary>
    public bool IsRevealed { get; }

    public string RoundLabel { get; }
    public string VenueName { get; }
    public string Title { get; }
    public string Circuit { get; }
    public string Champion { get; }
    public IReadOnlyList<string> Body { get; }
    public IReadOnlyList<string> Notes { get; }

    public bool HasChampion => Champion.Length > 0;
    public bool HasCircuit => Circuit.Length > 0;
    public bool HasTitle => Title.Length > 0;
    public bool HasNotes => Notes.Count > 0;
}

/// <summary>One season's scrapbook card + its timeline node: the year headline, the player's
/// final finish, the champion crown, the folded rep/OPI line, and the season's headlines.</summary>
public sealed class SeasonCardViewModel
{
    private readonly CareerSeasonCard _card;

    public SeasonCardViewModel(CareerSeasonCard card, HistoricalSeason? realSeason = null)
    {
        ArgumentNullException.ThrowIfNull(card);
        _card = card;
        RealSeason = realSeason is not null
            ? new HistoricalSeasonViewModel(realSeason, card.RoundsApplied, card.IsComplete)
            : null;
    }

    /// <summary>The REAL historical results of this card's year ("what really happened"), or null
    /// when none is shipped. Rendered as a clearly-separated panel below the player's own numbers.</summary>
    public HistoricalSeasonViewModel? RealSeason { get; }

    public bool HasRealSeason => RealSeason is not null;

    public int SeasonYear => _card.SeasonYear;

    public string YearText => _card.SeasonYear.ToString(CultureInfo.InvariantCulture);

    public bool IsComplete => _card.IsComplete;

    /// <summary>The card's one-line status: the player's final finish for a completed season,
    /// or the in-progress round count while the season is still running.</summary>
    public string ResultText
    {
        get
        {
            if (!_card.IsComplete)
                return _card.RoundsApplied == 0
                    ? "Season not started"
                    : $"In progress, {_card.RoundsApplied} of {_card.RoundCount} rounds";
            return _card.PlayerPosition is { } p
                ? $"Finished P{p}"
                : "Unclassified";
        }
    }

    /// <summary>The drivers' champion line ("Champion: <name>"), or empty before any round.</summary>
    public string ChampionText => _card.ChampionName is { Length: > 0 } name
        ? $"Champion: {name}"
        : "";

    public bool HasChampion => _card.ChampionName is { Length: > 0 };

    /// <summary>True when the player IS the champion, the card shows the crowning line.</summary>
    public bool PlayerIsChampion => _card.PlayerIsChampion;

    /// <summary>The folded rep/OPI summary line for a completed season; empty otherwise.</summary>
    public string FormText => _card is { FinalReputation: { } rep, FinalOpi: { } opi }
        ? $"Reputation {rep:0.#} · OPI {opi:+0.00;-0.00;0.00}"
        : "";

    public bool HasForm => _card is { FinalReputation: not null, FinalOpi: not null };

    /// <summary>The season's archived headlines (story order), the card's scrapbook clippings.</summary>
    public IReadOnlyList<string> Headlines => _card.Headlines;

    public bool HasHeadlines => _card.Headlines.Count > 0;

    /// <summary>Only seasons with at least one applied round can open a truthful inspector.</summary>
    public bool CanInspect => _card.RoundsApplied > 0;
}

/// <summary>The career records book row-set: labelled bests/counts/streaks for the view's grid.
/// A record is only shown when it carries a value (before any race is applied the book is
/// empty).</summary>
public sealed class RecordsBookViewModel
{
    public static readonly RecordsBookViewModel Empty = new(CareerRecordsBook.Empty);

    public RecordsBookViewModel(CareerRecordsBook records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var rows = new List<RecordRow>();

        if (records.BestFinish is { } best)
            rows.Add(new RecordRow("Best finish", $"P{best}"));
        rows.Add(new RecordRow("Race wins", records.Wins.ToString(CultureInfo.InvariantCulture)));
        rows.Add(new RecordRow("Podiums", records.Podiums.ToString(CultureInfo.InvariantCulture)));
        rows.Add(new RecordRow("Championships", records.Championships.ToString(CultureInfo.InvariantCulture)));
        rows.Add(new RecordRow("Total points",
            records.TotalPoints.ToString("0.##", CultureInfo.InvariantCulture)));
        rows.Add(new RecordRow("Seasons raced", records.SeasonsRaced.ToString(CultureInfo.InvariantCulture)));
        if (records.LongestWinStreak > 1)
            rows.Add(new RecordRow("Longest win streak",
                $"{records.LongestWinStreak} races"));
        if (records.LongestPodiumStreak > 1)
            rows.Add(new RecordRow("Longest podium streak",
                $"{records.LongestPodiumStreak} races"));

        Rows = rows;
    }

    /// <summary>The records rows to display; always at least the zeroed counts once the book
    /// exists, and the streak rows only when a streak of more than one race was set.</summary>
    public IReadOnlyList<RecordRow> Rows { get; }
}

/// <summary>One labelled record ("Best finish" → "P2").</summary>
public sealed record RecordRow(string Label, string Value);

/// <summary>"What really happened", a season's REAL historical results (f1db-derived, CC BY 4.0),
/// shown alongside the player's own diverged career. Display-only projection of
/// <see cref="HistoricalSeason"/>; collapsed by default (opt-in per season so a multi-season
/// scrapbook stays compact).</summary>
public sealed partial class HistoricalSeasonViewModel : ObservableObject
{
    /// <summary>Whether the "what really happened" panel is expanded for this season (default off).</summary>
    [ObservableProperty]
    private bool _isExpanded;

    [RelayCommand]
    private void Toggle() => IsExpanded = !IsExpanded;

    public HistoricalSeasonViewModel(HistoricalSeason season, int roundsApplied, bool isSeasonComplete)
    {
        ArgumentNullException.ThrowIfNull(season);
        Year = season.Year;
        Source = season.Source ?? "";
        // The season-level spoilers (champions + summary) reveal only once the player has finished the
        // season in their career, before that, each race is a preview, not a result.
        IsSeasonComplete = isSeasonComplete;

        if (season.DriversChampion is { } champ)
        {
            string team = champ.Team is { Length: > 0 } t ? $" · {t}" : "";
            string points = champ.Points is { Length: > 0 } p ? $" · {p} pts" : "";
            DriversChampionText = $"{champ.Driver}{team}{points}";
        }
        if (season.ConstructorsChampion is { } cons)
        {
            string points = cons.Points is { Length: > 0 } p ? $" · {p} pts" : "";
            ConstructorsChampionText = $"{cons.Team}{points}";
        }

        // Each round reveals its real result only after the player has completed that round (round
        // number <= rounds applied). Until then it is a Race Preview (circuit + detail, no spoiler).
        Rounds = season.Rounds
            .Select(r => new HistoricalRoundViewModel(r, isRevealed: r.Round <= roundsApplied))
            .ToList();
        SummaryText = ComposeSummary(season);
    }

    /// <summary>The number of races in the season whose real result the player has unlocked (raced).</summary>
    public int RevealedCount => Rounds.Count(r => r.IsRevealed);

    /// <summary>True once the whole season is done in the player's career, gates the champion +
    /// summary spoilers.</summary>
    public bool IsSeasonComplete { get; }

    /// <summary>A one-line, DATA-GROUNDED season summary (no invented facts): champion + win count +
    /// title margin over the runner-up, then the dominant constructor. Every number is counted from
    /// the baked f1db results, so it is accurate by construction. Empty when there is no champion.</summary>
    private static string ComposeSummary(HistoricalSeason season)
    {
        if (season.DriversChampion is not { } champ)
            return "";

        int raceCount = season.Rounds.Count;
        int champWins = season.Rounds.Count(r => string.Equals(r.Winner, champ.Driver, StringComparison.Ordinal));

        string wins = champWins > 0 ? $" with {champWins} {(champWins == 1 ? "win" : "wins")}" : "";
        string versus = "";
        if (season.RunnerUp is { } runnerUp)
        {
            string margin = TitleMargin(champ.Points, runnerUp.Points);
            versus = margin.Length > 0 ? $", {margin} ahead of {runnerUp.Driver}" : $" ahead of {runnerUp.Driver}";
        }
        string summary = $"{champ.Driver} took the {season.Year} title{wins}{versus}.";

        if (season.ConstructorsChampion is { } cons)
        {
            int teamWins = season.Rounds.Count(r => string.Equals(r.WinnerTeam, cons.Team, StringComparison.Ordinal));
            string teamWinsText = teamWins > 0 && raceCount > 0
                ? $", winning {teamWins} of {raceCount} {(raceCount == 1 ? "race" : "races")}"
                : "";
            summary += $" {cons.Team} led the constructors{teamWinsText}.";
        }
        return summary;
    }

    /// <summary>The points gap between champion and runner-up as "N point(s)", or "" when it cannot
    /// be computed (missing/non-numeric points, or a non-positive gap).</summary>
    private static string TitleMargin(string? championPoints, string? runnerUpPoints)
    {
        if (decimal.TryParse(championPoints, System.Globalization.NumberStyles.Any, CultureInfo.InvariantCulture, out var c) &&
            decimal.TryParse(runnerUpPoints, System.Globalization.NumberStyles.Any, CultureInfo.InvariantCulture, out var r) &&
            c > r)
        {
            decimal margin = c - r;
            return margin == 1m
                ? "1 point"
                : $"{margin.ToString("0.##", CultureInfo.InvariantCulture)} points";
        }
        return "";
    }

    public int Year { get; }

    /// <summary>CC BY 4.0 attribution line.</summary>
    public string Source { get; }

    /// <summary>"Denny Hulme · Brabham · 51 pts", empty when unknown.</summary>
    public string DriversChampionText { get; } = "";

    public bool HasDriversChampion => DriversChampionText.Length > 0;

    public string ConstructorsChampionText { get; } = "";

    public bool HasConstructorsChampion => ConstructorsChampionText.Length > 0;

    /// <summary>A one-line, data-grounded summary of the season ("X took the title with 8 wins, 3
    /// points ahead of Y. Z led the constructors, winning 15 of 16 races.").</summary>
    public string SummaryText { get; } = "";

    public bool HasSummary => SummaryText.Length > 0;

    /// <summary>Every real race of the season, in calendar order, each expandable to its full grid.</summary>
    public IReadOnlyList<HistoricalRoundViewModel> Rounds { get; }
}

/// <summary>One real historical race. Before the player has raced this round it is a RACE PREVIEW
/// (circuit map + track detail, no result spoiler); once raced (<see cref="IsRevealed"/>) it becomes a
/// HISTORICAL DOCUMENT (winner, fastest lap, and the full classified grid behind an expander).</summary>
public sealed partial class HistoricalRoundViewModel : ObservableObject
{
    /// <summary>Whether this round's detail is expanded (default off, the summary line always shows).</summary>
    [ObservableProperty]
    private bool _isExpanded;

    [RelayCommand]
    private void Toggle() => IsExpanded = !IsExpanded;

    public HistoricalRoundViewModel(HistoricalRound round, bool isRevealed)
    {
        ArgumentNullException.ThrowIfNull(round);
        IsRevealed = isRevealed;
        RoundLabel = $"R{round.Round}";
        Name = round.Name;
        WinnerText = round.Winner is { Length: > 0 } w
            ? (round.WinnerTeam is { Length: > 0 } team ? $"{w} · {team}" : w)
            : "-";
        FastestLapText = round.FastestLap is { Length: > 0 } fl ? $"Fastest lap: {fl}" : "";
        Results = round.Results
            .Select(x => new HistoricalResultRow(x.Pos, x.Driver, x.Team, x.Status ?? ""))
            .ToList();

        CircuitLayoutId = round.Circuit?.LayoutId ?? "";
        CircuitCaption = CircuitCaptions.Compose(round.Circuit);
        CircuitHistory = round.Circuit?.History ?? "";
    }

    /// <summary>True once the player has raced this round, the real result is unlocked. False = a
    /// spoiler-free Race Preview.</summary>
    public bool IsRevealed { get; }

    public string RoundLabel { get; }
    public string Name { get; }

    /// <summary>The circuit-layout id for the map (empty when unknown).</summary>
    public string CircuitLayoutId { get; }
    public bool HasCircuit => CircuitLayoutId.Length > 0;
    /// <summary>"Imola · 4.96 km · 22 turns · anti-clockwise circuit", the preview detail.</summary>
    public string CircuitCaption { get; }
    /// <summary>A brief, data-grounded circuit history for the preview.</summary>
    public string CircuitHistory { get; } = "";
    public bool HasCircuitHistory => CircuitHistory.Length > 0;

    /// <summary>The winner line, but only once revealed, a preview never leaks it.</summary>
    public string WinnerText { get; }
    public string FastestLapText { get; }
    public bool HasFastestLap => FastestLapText.Length > 0;
    public IReadOnlyList<HistoricalResultRow> Results { get; }
    public bool HasResults => Results.Count > 0;
}

/// <summary>One row of a historical race's full classified result.</summary>
public sealed record HistoricalResultRow(string Pos, string Driver, string Team, string Status)
{
    public bool HasStatus => Status.Length > 0;
}
