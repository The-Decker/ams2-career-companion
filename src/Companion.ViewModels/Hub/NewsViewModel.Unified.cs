using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Companion.Core.Newsroom;
using Companion.ViewModels.Services;

namespace Companion.ViewModels.Hub;

/// <summary>A developing-story card for the threads rail.</summary>
public sealed record ThreadCardViewModel(
    string Key,
    string Title,
    string TypeLabel,
    string StateLabel,
    IReadOnlyList<StoryThreadEntry> Entries)
{
    public bool IsActive => StateLabel is not ("RESOLVED" or "HISTORIC" or "DORMANT");
    public string LatestSummary => Entries.Count > 0 ? Entries[^1].Summary : "";
}

/// <summary>A rumour-desk card. The claim is ALWAYS labeled speculation; a resolution links
/// the settling story rather than rewriting the original.</summary>
public sealed record RumorCardViewModel(
    string RumorKey,
    string Claim,
    string StatusLabel,
    string ResolutionNote,
    string ResolvedByKey)
{
    public bool HasResolution => ResolvedByKey.Length > 0;
    public bool IsOpen => StatusLabel == "RUMOUR - OPEN";
}

/// <summary>Presentation-only unified wire and article-reader state.</summary>
public sealed partial class NewsViewModel
{
    private readonly ObservableCollection<NewsStoryViewModel> _stories = [];
    private readonly ObservableCollection<NewsStoryViewModel> _filteredStories = [];
    private readonly ObservableCollection<NewsStoryViewModel> _secondaryStories = [];
    private readonly ObservableCollection<NewsCategoryFilterViewModel> _availableCategories = [];
    private readonly ObservableCollection<NewsStoryViewModel> _bookmarkedStories = [];
    private readonly ObservableCollection<ThreadCardViewModel> _threads = [];
    private readonly ObservableCollection<RumorCardViewModel> _rumors = [];
    private readonly HashSet<string> _bookmarkOverlay = new(StringComparer.Ordinal);
    private readonly HashSet<string> _readThisSession = new(StringComparer.Ordinal);
    private NewsCategoryFilterViewModel? _selectedCategory;
    private string _searchText = "";
    private NewsStoryViewModel? _leadStory;
    private NewsStoryViewModel? _selectedArticle;
    private bool _isReaderOpen;
    private bool _isLegacyLimited;

    /// <summary>The unified career-wide wire, newest first.</summary>
    public ObservableCollection<NewsStoryViewModel> Stories => _stories;

    public ObservableCollection<NewsStoryViewModel> FilteredStories => _filteredStories;

    public ObservableCollection<NewsStoryViewModel> SecondaryStories => _secondaryStories;

    public ObservableCollection<NewsCategoryFilterViewModel> AvailableCategories => _availableCategories;

    public NewsCategoryFilterViewModel? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            value ??= _availableCategories.FirstOrDefault(category => category.IsAll);
            if (SetProperty(ref _selectedCategory, value))
                ApplyFilters();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            value ??= "";
            if (SetProperty(ref _searchText, value))
                ApplyFilters();
        }
    }

    public NewsStoryViewModel? LeadStory => _leadStory;

    public NewsStoryViewModel? SelectedArticle => _selectedArticle;

    public bool IsReaderOpen => _isReaderOpen;

    public bool IsFilteredEmpty => _stories.Count > 0 && _filteredStories.Count == 0;

    /// <summary>The projection is synchronous; this explicit state is part of the GUI contract.</summary>
    public bool IsLoading => false;

    /// <summary>At least one dispatch lacks the exact archived season/round coordinate needed for
    /// optional History deep-linking. The story remains readable without migration.</summary>
    public bool IsLegacyLimited => _isLegacyLimited;

    public bool HasActiveFilter =>
        !string.IsNullOrWhiteSpace(_searchText) || _selectedCategory is { IsAll: false };

    public bool HasLeadStory => _leadStory is not null;

    public bool HasSecondaryStories => _secondaryStories.Count > 0;

    /// <summary>Bookmarked stories, newest first (schema v6 user preference).</summary>
    public ObservableCollection<NewsStoryViewModel> BookmarkedStories => _bookmarkedStories;

    /// <summary>The developing-story threads rail, active threads first.</summary>
    public ObservableCollection<ThreadCardViewModel> Threads => _threads;

    /// <summary>The rumour desk: fact-backed whispers, honestly resolved.</summary>
    public ObservableCollection<RumorCardViewModel> Rumors => _rumors;

    public bool HasBookmarks => _bookmarkedStories.Count > 0;

    public bool HasThreads => _threads.Count > 0;

    public bool HasRumors => _rumors.Count > 0;

    public int UnreadCount => _stories.Count(story =>
        story.IsUnread && !_readThisSession.Contains(story.Key));

    public bool HasUnread => UnreadCount > 0;

    private void RefreshUnifiedProjection(IReadOnlyList<NewsDispatch> journalFeed)
    {
        string selectedCategoryKey = _selectedCategory?.Key ?? "all";
        string? selectedArticleKey = _isReaderOpen ? _selectedArticle?.Key : null;
        var timeline = _session.CareerTimeline();
        var paddock = _session.SmgpPaddock();

        var threads = _session.StoryThreads();
        var threadKeyByStory = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var thread in threads)
        {
            foreach (var entry in thread.Entries)
            {
                threadKeyByStory.TryAdd(entry.StoryKey, thread.Key);
            }
        }

        Replace(_stories, NewsStoryProjection.Build(
            _session.SmgpDispatches(),
            journalFeed,
            timeline,
            paddock,
            showBody: !HeadlinesOnly,
            newsroomArticles: _session.NewsroomFeed(),
            readingState: _session.ReadingState(),
            threadKeyByStory: threadKeyByStory,
            smgpMode: paddock is not null));

        _bookmarkOverlay.Clear();
        foreach (var story in _stories.Where(story => story.IsBookmarked))
        {
            _bookmarkOverlay.Add(story.Key);
        }
        RebuildBookmarks();

        Replace(_threads, threads
            .OrderByDescending(thread => thread.SeasonOrdinal)
            .ThenByDescending(thread => thread.LastRound)
            .Select(thread => new ThreadCardViewModel(
                thread.Key,
                thread.Title,
                ThreadTypeLabel(thread.Type),
                thread.State.ToString().ToUpperInvariant(),
                thread.Entries)));

        Replace(_rumors, _session.RumorBoard()
            .OrderByDescending(rumor => rumor.SeasonOrdinal)
            .Select(rumor => new RumorCardViewModel(
                rumor.RumorKey,
                rumor.Claim,
                rumor.Resolution switch
                {
                    RumorResolutionKind.Confirmed => "CONFIRMED",
                    RumorResolutionKind.Denied => "DENIED",
                    _ => "RUMOUR - OPEN",
                },
                rumor.ResolutionNote,
                rumor.ResolvedByKey)));
        Replace(_availableCategories, NewsStoryProjection.BuildCategories(_stories));
        _selectedCategory = _availableCategories.FirstOrDefault(category =>
                                string.Equals(category.Key, selectedCategoryKey, StringComparison.Ordinal))
                            ?? _availableCategories.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedCategory));

        _isLegacyLimited = _stories.Any(story =>
            (story.SeasonOrdinal > 0 && story.SeasonYear == 0)
            || (story.Round is not null && !story.HasHistoryLink));

        if (selectedArticleKey is not null)
        {
            _selectedArticle = _stories.FirstOrDefault(story =>
                string.Equals(story.Key, selectedArticleKey, StringComparison.Ordinal));
            _isReaderOpen = _selectedArticle is not null;
        }
        else
        {
            _selectedArticle = null;
            _isReaderOpen = false;
        }

        ApplyFilters();
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(IsLegacyLimited));
        OnPropertyChanged(nameof(SelectedArticle));
        OnPropertyChanged(nameof(IsReaderOpen));
        OnPropertyChanged(nameof(HasThreads));
        OnPropertyChanged(nameof(HasRumors));
        OnPropertyChanged(nameof(UnreadCount));
        OnPropertyChanged(nameof(HasUnread));
        OpenArticleCommand.NotifyCanExecuteChanged();
        OpenStoryCommand.NotifyCanExecuteChanged();
        CloseArticleCommand.NotifyCanExecuteChanged();
    }

    private static string ThreadTypeLabel(StoryThreadType type) => type switch
    {
        StoryThreadType.TitleFight => "Title fight",
        StoryThreadType.ReliabilityCrisis => "Reliability",
        StoryThreadType.WinDrought => "The drought",
        StoryThreadType.HotStreak => "Hot streak",
        StoryThreadType.InjuryRecovery => "Recovery",
        StoryThreadType.Rivalry => "Rivalry",
        StoryThreadType.DriverMarket => "Silly season",
        _ => type.ToString(),
    };

    private void RebuildBookmarks()
    {
        _bookmarkedStories.Clear();
        foreach (var story in _stories)
        {
            if (_bookmarkOverlay.Contains(story.Key))
            {
                _bookmarkedStories.Add(story);
            }
        }
        OnPropertyChanged(nameof(HasBookmarks));
    }

    /// <summary>Toggles a story's bookmark: persisted immediately (schema v6), reflected in
    /// the rail without a full feed re-render.</summary>
    [RelayCommand]
    private void ToggleBookmark(NewsStoryViewModel? story)
    {
        if (story is null)
        {
            return;
        }

        bool nowBookmarked = !_bookmarkOverlay.Contains(story.Key);
        _session.SetStoryBookmark(story.Key, nowBookmarked);
        if (nowBookmarked)
        {
            _bookmarkOverlay.Add(story.Key);
        }
        else
        {
            _bookmarkOverlay.Remove(story.Key);
        }
        RebuildBookmarks();
    }

    private bool CanOpenArticle(NewsStoryViewModel? story) =>
        story is { CanRead: true }
        && (!_isReaderOpen
            || !string.Equals(_selectedArticle?.Key, story.Key, StringComparison.Ordinal));

    [RelayCommand(CanExecute = nameof(CanOpenArticle))]
    private void OpenArticle(NewsStoryViewModel? story)
    {
        if (!CanOpenArticle(story))
            return;

        _selectedArticle = story;
        _isReaderOpen = true;

        // Opening IS reading: persist once (the first-read timestamp is kept server-side).
        if (story is not null && _readThisSession.Add(story.Key))
        {
            _session.MarkStoryRead(story.Key);
            OnPropertyChanged(nameof(UnreadCount));
            OnPropertyChanged(nameof(HasUnread));
        }

        OnPropertyChanged(nameof(SelectedArticle));
        OnPropertyChanged(nameof(IsReaderOpen));
        OpenArticleCommand.NotifyCanExecuteChanged();
        OpenStoryCommand.NotifyCanExecuteChanged();
        CloseArticleCommand.NotifyCanExecuteChanged();
    }

    private bool CanOpenStory(string? storyKey)
    {
        if (string.IsNullOrWhiteSpace(storyKey))
            return false;

        var story = _stories.FirstOrDefault(candidate =>
            string.Equals(candidate.Key, storyKey, StringComparison.Ordinal));
        return CanOpenArticle(story);
    }

    [RelayCommand(CanExecute = nameof(CanOpenStory))]
    private void OpenStory(string? storyKey)
    {
        var story = _stories.FirstOrDefault(candidate =>
            string.Equals(candidate.Key, storyKey, StringComparison.Ordinal));
        OpenArticle(story);
    }

    private bool CanCloseArticle() => _isReaderOpen;

    [RelayCommand(CanExecute = nameof(CanCloseArticle))]
    private void CloseArticle()
    {
        if (!_isReaderOpen)
            return;

        _selectedArticle = null;
        _isReaderOpen = false;
        OnPropertyChanged(nameof(SelectedArticle));
        OnPropertyChanged(nameof(IsReaderOpen));
        OpenArticleCommand.NotifyCanExecuteChanged();
        OpenStoryCommand.NotifyCanExecuteChanged();
        CloseArticleCommand.NotifyCanExecuteChanged();
    }

    private bool CanClearFilters() => HasActiveFilter;

    [RelayCommand(CanExecute = nameof(CanClearFilters))]
    private void ClearFilters()
    {
        if (!HasActiveFilter)
            return;

        _searchText = "";
        _selectedCategory = _availableCategories.FirstOrDefault(category => category.IsAll);
        OnPropertyChanged(nameof(SearchText));
        OnPropertyChanged(nameof(SelectedCategory));
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        string search = _searchText.Trim();
        NewsStoryCategory? category = _selectedCategory?.Category;
        _filteredStories.Clear();
        foreach (var story in _stories)
        {
            if ((category is null || story.Category == category) && story.MatchesSearch(search))
                _filteredStories.Add(story);
        }

        _leadStory = _filteredStories.FirstOrDefault(story => story.Importance == NewsStoryImportance.Major)
                     ?? _filteredStories.FirstOrDefault();
        _secondaryStories.Clear();
        foreach (var story in _filteredStories)
        {
            if (_secondaryStories.Count < 5 && !string.Equals(story.Key, _leadStory?.Key, StringComparison.Ordinal))
                _secondaryStories.Add(story);
        }

        OnPropertyChanged(nameof(LeadStory));
        OnPropertyChanged(nameof(IsFilteredEmpty));
        OnPropertyChanged(nameof(HasActiveFilter));
        OnPropertyChanged(nameof(HasLeadStory));
        OnPropertyChanged(nameof(HasSecondaryStories));
        ClearFiltersCommand.NotifyCanExecuteChanged();
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
            target.Add(item);
    }
}
