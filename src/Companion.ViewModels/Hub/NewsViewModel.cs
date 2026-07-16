using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Companion.ViewModels.Services;
using Companion.ViewModels.Settings;

namespace Companion.ViewModels.Hub;

/// <summary>
/// The News tab / right-dock feed: a read-only projection of <see cref="ICareerSession.ReadFeed"/>
/// into an era-styled ticker, newest first. Increment 1 re-renders the existing journal headline
/// rows (empty until the real projection lands in <c>CareerSessionService</c>); the generative
/// article grammar is a later slice. Refreshed after every round applies.
///
/// <para>The <see cref="AppSettings.NewsDetail"/> immersion setting (career-hub-design.md decision
/// 17) gates how much each dispatch shows: full <see cref="NewsDetailLevel.Articles"/> expand into
/// the period body, while <see cref="NewsDetailLevel.HeadlinesOnly"/>/<see cref="NewsDetailLevel.Minimal"/>
/// show headlines only (no expanded body).</para>
/// </summary>
public sealed partial class NewsViewModel : ObservableObject
{
    private readonly ICareerSession _session;

    public NewsViewModel(ICareerSession session, NewsDetailLevel newsDetail = NewsDetailLevel.Articles)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        NewsDetail = newsDetail;
        Refresh();
    }

    /// <summary>The immersion verbosity these items were projected under.</summary>
    public NewsDetailLevel NewsDetail { get; }

    /// <summary>True when dispatches show only their headline (no expanded article body) —
    /// any level other than <see cref="NewsDetailLevel.Articles"/>.</summary>
    public bool HeadlinesOnly => NewsDetail != NewsDetailLevel.Articles;

    public ObservableCollection<NewsItemViewModel> Items { get; } = [];

    /// <summary>True when there is no news yet — the tab shows a friendly empty state
    /// ("no dispatches yet — run a race") instead of a blank panel.</summary>
    public bool IsEmpty => Stories.Count == 0;

    /// <summary>Re-pull the feed (called on open and after every Apply).</summary>
    public void Refresh()
    {
        var journalFeed = _session.ReadFeed();
        Items.Clear();
        foreach (var dispatch in journalFeed)
            Items.Add(new NewsItemViewModel(dispatch, showBody: !HeadlinesOnly));
        RefreshUnifiedProjection(journalFeed);
        OnPropertyChanged(nameof(IsEmpty));
    }
}

/// <summary>One dispatch in the feed: a period headline that expands (on click) into the full
/// article, plus the Why? chip's plain sentence. Expansion is view state, never journaled.</summary>
public sealed partial class NewsItemViewModel : ObservableObject
{
    private readonly NewsDispatch _dispatch;
    private readonly bool _showBody;

    public NewsItemViewModel(NewsDispatch dispatch, bool showBody = true)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        _dispatch = dispatch;
        _showBody = showBody;
    }

    public string Headline => _dispatch.Headline;

    public string Kind => _dispatch.Kind;

    public string WhyText => _dispatch.WhyText;

    public bool HasWhy => !string.IsNullOrEmpty(_dispatch.WhyText);

    /// <summary>True when this item can expand into a body — off under a headlines-only
    /// immersion level, which collapses the expander to a plain headline.</summary>
    public bool HasBody => _showBody;

    /// <summary>The expanded article body; falls back to the headline when the generative
    /// grammar has not produced a body yet. Empty under a headlines-only immersion level so the
    /// view shows just the headline (career-hub-design.md decision 17).</summary>
    public string Body => _showBody
        ? (string.IsNullOrEmpty(_dispatch.Body) ? _dispatch.Headline : _dispatch.Body)
        : "";

    /// <summary>Dateline: "1967 · Round 3" (or just the year for season-level items).</summary>
    public string Meta => _dispatch.Round is { } round
        ? $"{_dispatch.SeasonYear} · Round {round}"
        : _dispatch.SeasonYear.ToString();

    /// <summary>Ticker item clicked → the full immersive article is shown inline (decision 17).</summary>
    [ObservableProperty]
    private bool _isExpanded;

    [RelayCommand]
    private void ToggleExpanded()
    {
        if (!_showBody)
            return; // headlines-only immersion level: nothing to expand into
        IsExpanded = !IsExpanded;
    }
}
