using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Companion.ViewModels.Services;

namespace Companion.ViewModels.Hub;

/// <summary>
/// The History / Scrapbook lens (career-hub-design.md §4, decision 18 "total recall"): a
/// read-only projection of the whole career into per-season scrapbook cards, a lineage timeline,
/// a records book (career bests/streaks/milestones), and every race's archived news article. All
/// four surfaces are pure reads over <see cref="ICareerSession.CareerTimeline"/> and
/// <see cref="ICareerSession.ReadFeed"/> — no sim, no persistence — so the History tab renders
/// (and re-renders after every Apply) with zero new state cost. Refreshed in place off the new
/// session state exactly like the Standings/News lenses.
/// </summary>
public sealed partial class HistoryViewModel : ObservableObject
{
    private readonly ICareerSession _session;

    public HistoryViewModel(ICareerSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        Refresh();
    }

    /// <summary>One scrapbook card per season (oldest first — the lineage order). Also the
    /// lineage timeline's row source: the view renders the same collection as a vertical
    /// timeline down the left and full cards on the right.</summary>
    public ObservableCollection<SeasonCardViewModel> Seasons { get; } = [];

    /// <summary>The career records book — bests, counts, streaks. Never null.</summary>
    [ObservableProperty]
    private RecordsBookViewModel _records = RecordsBookViewModel.Empty;

    /// <summary>Every archived race/season dispatch of the career, newest first — the same feed
    /// the News tab shows, preserved forever in the scrapbook (decision 18). Each expands into
    /// its full period article on click.</summary>
    public ObservableCollection<NewsItemViewModel> ArchivedArticles { get; } = [];

    /// <summary>True before the first season has any applied round — the tab shows a friendly
    /// empty state instead of a blank scrapbook.</summary>
    public bool IsEmpty => Seasons.Count == 0;

    /// <summary>True once at least one archived dispatch exists (drives the articles section's
    /// empty state independently of the season cards).</summary>
    public bool HasArticles => ArchivedArticles.Count > 0;

    /// <summary>Re-project the whole scrapbook off current session state (on open and after
    /// every Apply). Idempotent: rebuilds every collection from scratch.</summary>
    public void Refresh()
    {
        var timeline = _session.CareerTimeline();

        Seasons.Clear();
        // Newest season first reads best as a scrapbook (this season at the top), while the
        // records book aggregates the whole lineage regardless of order.
        foreach (var card in timeline.Seasons.Reverse())
            Seasons.Add(new SeasonCardViewModel(card));

        Records = new RecordsBookViewModel(timeline.Records);

        ArchivedArticles.Clear();
        foreach (var dispatch in _session.ReadFeed())
            ArchivedArticles.Add(new NewsItemViewModel(dispatch));

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasArticles));
    }
}

/// <summary>One season's scrapbook card + its timeline node: the year headline, the player's
/// final finish, the champion crown, the folded rep/OPI line, and the season's headlines.</summary>
public sealed class SeasonCardViewModel
{
    private readonly CareerSeasonCard _card;

    public SeasonCardViewModel(CareerSeasonCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        _card = card;
    }

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
                    : $"In progress — {_card.RoundsApplied} of {_card.RoundCount} rounds";
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

    /// <summary>True when the player IS the champion — the card shows the crowning line.</summary>
    public bool PlayerIsChampion => _card.PlayerIsChampion;

    /// <summary>The folded rep/OPI summary line for a completed season; empty otherwise.</summary>
    public string FormText => _card is { FinalReputation: { } rep, FinalOpi: { } opi }
        ? $"Reputation {rep:0.#} · OPI {opi:+0.00;-0.00;0.00}"
        : "";

    public bool HasForm => _card is { FinalReputation: not null, FinalOpi: not null };

    /// <summary>The season's archived headlines (story order) — the card's scrapbook clippings.</summary>
    public IReadOnlyList<string> Headlines => _card.Headlines;

    public bool HasHeadlines => _card.Headlines.Count > 0;
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
