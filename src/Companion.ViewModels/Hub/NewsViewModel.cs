using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Companion.ViewModels.Services;

namespace Companion.ViewModels.Hub;

/// <summary>
/// The News tab / right-dock feed: a read-only projection of <see cref="ICareerSession.ReadFeed"/>
/// into an era-styled ticker, newest first. Increment 1 re-renders the existing journal headline
/// rows (empty until the real projection lands in <c>CareerSessionService</c>); the generative
/// article grammar is a later slice. Refreshed after every round applies.
/// </summary>
public sealed partial class NewsViewModel : ObservableObject
{
    private readonly ICareerSession _session;

    public NewsViewModel(ICareerSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        Refresh();
    }

    public ObservableCollection<NewsItemViewModel> Items { get; } = [];

    /// <summary>True when there is no news yet — the tab shows a friendly empty state
    /// ("no dispatches yet — run a race") instead of a blank panel.</summary>
    public bool IsEmpty => Items.Count == 0;

    /// <summary>Re-pull the feed (called on open and after every Apply).</summary>
    public void Refresh()
    {
        Items.Clear();
        foreach (var dispatch in _session.ReadFeed())
            Items.Add(new NewsItemViewModel(dispatch));
        OnPropertyChanged(nameof(IsEmpty));
    }
}

/// <summary>One dispatch in the feed: a period headline that expands (on click) into the full
/// article, plus the Why? chip's plain sentence. Expansion is view state, never journaled.</summary>
public sealed partial class NewsItemViewModel : ObservableObject
{
    private readonly NewsDispatch _dispatch;

    public NewsItemViewModel(NewsDispatch dispatch)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        _dispatch = dispatch;
    }

    public string Headline => _dispatch.Headline;

    public string Kind => _dispatch.Kind;

    public string WhyText => _dispatch.WhyText;

    public bool HasWhy => !string.IsNullOrEmpty(_dispatch.WhyText);

    /// <summary>The expanded article body; falls back to the headline when the generative
    /// grammar has not produced a body yet.</summary>
    public string Body => string.IsNullOrEmpty(_dispatch.Body) ? _dispatch.Headline : _dispatch.Body;

    /// <summary>Dateline: "1967 · Round 3" (or just the year for season-level items).</summary>
    public string Meta => _dispatch.Round is { } round
        ? $"{_dispatch.SeasonYear} · Round {round}"
        : _dispatch.SeasonYear.ToString();

    /// <summary>Ticker item clicked → the full immersive article is shown inline (decision 17).</summary>
    [ObservableProperty]
    private bool _isExpanded;

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;
}
