using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Companion.Core.Career;
using Companion.ViewModels.Services;
using Companion.ViewModels.Settings;
using Companion.ViewModels.Shell;
using Companion.ViewModels.Standings;

namespace Companion.ViewModels.Hub;

/// <summary>
/// The immersive Career Hub shell (career-hub-design.md §2): a persistent left tab rail around
/// the shipped career loop. Increment 1 composes the existing <see cref="HomeViewModel"/>
/// VERBATIM as the always-present <b>Race</b> tab (so the loop, its keyboard grammar and its
/// tests are untouched), and adds always-present <b>Standings</b> and <b>News</b> lens tabs.
/// The Race tab auto-selects on open and after every Apply — the anti-burial rule: the player
/// is never stranded on a management tab. Owns the career session's lifetime through the Home.
/// </summary>
public sealed partial class HubViewModel : ObservableObject, IDisposable
{
    public const string RaceTabKey = "race";
    public const string StandingsTabKey = "standings";
    public const string DriverTabKey = "driver";
    public const string HistoryTabKey = "history";
    public const string NewsTabKey = "news";

    private readonly ICareerSession _session;
    private readonly ISettingsService? _settings;

    public HubViewModel(
        ICareerSession session,
        IFileWatcher? stagedFileWatcher = null,
        TimeProvider? clock = null,
        ISettingsService? settings = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _settings = settings;
        Era = EraThemes.ForYear(session.Pack.Season.Year);

        Home = new HomeViewModel(session, stagedFileWatcher, clock, settings);
        Home.NextSeasonStarted += OnHomeNextSeasonStarted;
        Home.PropertyChanged += OnHomePropertyChanged;

        if (_settings is not null)
            _settings.Changed += OnSettingsChanged;

        News = new NewsViewModel(session, settings?.Current.NewsDetail ?? NewsDetailLevel.Articles);
        History = new HistoryViewModel(session);
        Dossier = new DossierViewModel(session);

        Tabs =
        [
            new HubTabViewModel(RaceTabKey, "Race", "", Home),
            new HubTabViewModel(StandingsTabKey, "Standings", "", NewStandings()),
            new HubTabViewModel(HistoryTabKey, "History", "", History),
            new HubTabViewModel(NewsTabKey, "News", "", News),
        ];

        // The Driver dossier tab is present only when the career carries a character (depth 3).
        if (Dossier.HasCharacter)
            Tabs.Insert(2, new HubTabViewModel(DriverTabKey, "Driver", "", Dossier));

        SelectTab(Tabs[0]); // auto-select Race on open
    }

    /// <summary>The shipped career loop, unchanged — the Race tab's content.</summary>
    public HomeViewModel Home { get; }

    /// <summary>The News feed (also the source for the future right-dock ticker).</summary>
    public NewsViewModel News { get; }

    /// <summary>The History / Scrapbook lens (per-season cards, lineage timeline, records
    /// book, archived articles) — refreshed in place after every Apply like the other lenses.</summary>
    public HistoryViewModel History { get; }

    /// <summary>The Driver dossier lens — the player's character, stats, perks and level/XP as the
    /// career unfolds. Present as a tab only when the career has a character (depth 3).</summary>
    public DossierViewModel Dossier { get; }

    /// <summary>The period skin resolved from the pack's decade (telegram/fax/email) — drives
    /// the hub's era badge now, and the full resource-dictionary swap in a later slice.</summary>
    public EraTheme Era { get; }

    /// <summary>The immersion master switch (career-hub-design.md decision 7): when off, the hub
    /// hides its era-medium badge and falls back to neutral chrome. Reads live from settings;
    /// defaults on when no settings service is wired.</summary>
    public bool EraThemingEnabled => _settings?.Current.EraThemingEnabled ?? true;

    public ObservableCollection<HubTabViewModel> Tabs { get; }

    [ObservableProperty]
    private HubTabViewModel? _selectedTab;

    /// <summary>Forwarded from Home so the shell reopens the career after sign-and-continue
    /// (M6) exactly as it did when Home was the top-level screen.</summary>
    public event EventHandler? NextSeasonStarted;

    // ---------- rail navigation (mouse + keyboard parity, decision 8) ----------

    [RelayCommand]
    private void SelectTab(HubTabViewModel? tab)
    {
        if (tab is null)
            return;
        foreach (var t in Tabs)
            t.IsSelected = ReferenceEquals(t, tab);
        SelectedTab = tab;
    }

    /// <summary>Number-key accelerator: 1..N selects tab N. Returns false when out of range so
    /// the key falls through. The view binds keys 1–9 to this.</summary>
    public bool SelectTabByNumber(int oneBased)
    {
        if (oneBased < 1 || oneBased > Tabs.Count)
            return false;
        SelectTab(Tabs[oneBased - 1]);
        return true;
    }

    /// <summary>Jump straight to the Race tab (the header's always-visible primary action).</summary>
    [RelayCommand]
    private void GoToRace()
    {
        if (RaceTab is { } race)
            SelectTab(race);
    }

    /// <summary>Header "Race day" button: select the Race tab AND show the briefing (one click
    /// back to the loop from any tab). No-ops the loop step when the season is complete.</summary>
    [RelayCommand]
    private void GoToBriefing()
    {
        GoToRace();
        if (Home.ShowBriefingCommand.CanExecute(null))
            Home.ShowBriefingCommand.Execute(null);
    }

    /// <summary>Header "Enter result" button: select the Race tab AND open result entry.</summary>
    [RelayCommand]
    private void GoToResult()
    {
        GoToRace();
        if (Home.EnterResultCommand.CanExecute(null))
            Home.EnterResultCommand.Execute(null);
    }

    private HubTabViewModel? RaceTab => Tabs.FirstOrDefault(t => t.Key == RaceTabKey);

    private HubTabViewModel? StandingsTab => Tabs.FirstOrDefault(t => t.Key == StandingsTabKey);

    // ---------- keep the lenses fresh + snap back to the loop after Apply ----------

    private void OnHomePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Summary changes exactly once per applied round (and on season completion). Re-project
        // the read-only lenses off the new state and snap back to the Race tab so depth never
        // buries the loop (career-hub-design.md §2.1).
        if (e.PropertyName != nameof(HomeViewModel.Summary))
            return;

        if (StandingsTab is { } standings)
            standings.Content = NewStandings();
        News.Refresh();
        History.Refresh();
        Dossier.Refresh();

        if (RaceTab is { } race)
            SelectTab(race);
    }

    private void OnHomeNextSeasonStarted(object? sender, EventArgs e) =>
        NextSeasonStarted?.Invoke(this, e);

    /// <summary>Live-apply the immersion master switch: toggling era theming shows/hides the
    /// hub's era badge without rebuilding the hub (career-hub-design.md decision 7).</summary>
    private void OnSettingsChanged(object? sender, AppSettings settings) =>
        OnPropertyChanged(nameof(EraThemingEnabled));

    private StandingsViewModel NewStandings() =>
        new(_session.AllSnapshots(), _session.Pack, _settings, _session);

    // ---------- Esc = back (ux-round contract) ----------

    /// <summary>Hub's share of the shell-level Esc: on a lens tab, Esc returns to the Race tab
    /// (back to the loop); on the Race tab it delegates to Home's non-destructive back.</summary>
    public bool TryEscapeBack()
    {
        if (SelectedTab is { Key: RaceTabKey })
            return Home.TryEscapeBack();

        GoToRace();
        return true;
    }

    public void Dispose()
    {
        if (_settings is not null)
            _settings.Changed -= OnSettingsChanged;
        Home.NextSeasonStarted -= OnHomeNextSeasonStarted;
        Home.PropertyChanged -= OnHomePropertyChanged;
        Home.Dispose(); // disposes the session + staged-file watcher
    }
}
