using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Companion.Core.Career;
using Companion.ViewModels.Debug;
using Companion.ViewModels.Hub;
using Companion.ViewModels.Services;
using Companion.ViewModels.Settings;
using Companion.ViewModels.Start;
using Companion.ViewModels.Wizard;

namespace Companion.ViewModels.Shell;

/// <summary>
/// The app-level conductor behind MainWindow's ContentControl: Start → (wizard | continue) →
/// Home, plus the Settings overlay screen (gear icon in the shell header; Esc or Done goes
/// back to whatever was open). WPF-free so navigation is unit-testable; the view maps
/// viewmodel types to screens with DataTemplates. Owns at most one open career at a time and
/// disposes the previous session (and its staged-file watcher) when navigating away.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject, IDisposable
{
    private readonly CareerEnvironment _environment;
    private readonly ICareerFactory _factory;
    private readonly Func<IFileWatcher?> _watcherFactory;
    private readonly ISettingsService _settings;

    private HubViewModel? _hub;
    private ObservableObject? _beforeSettings;
    private ObservableObject? _beforeDebug;
    private string? _currentCareerPath;

    public ShellViewModel(
        CareerEnvironment environment,
        ICareerFactory factory,
        IRecentCareersStore recentCareers,
        Func<IFileWatcher?>? stagedFileWatcherFactory = null,
        ISettingsService? settings = null)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(recentCareers);

        _environment = environment;
        _factory = factory;
        _watcherFactory = stagedFileWatcherFactory ?? (static () => null);
        _settings = settings ?? new SettingsService(new InMemorySettingsStore());

        Start = new StartViewModel(recentCareers, _settings);
        Start.ContinueRequested += (_, path) => OpenCareer(path);
        Start.NewCareerRequested += (_, _) => BeginWizard();
        Start.CareerModeRequested += (_, request) => BeginWizard(request.ExperienceMode);

        _current = Start;
        InstallStatus = ComposeInstallStatus(environment);
    }

    public StartViewModel Start { get; }

    /// <summary>The wizard in progress; null outside the new-career flow.</summary>
    public NewCareerWizardViewModel? Wizard { get; private set; }

    /// <summary>The live settings seam (consumers: wizard defaults, home, the App's
    /// accent/font resource application).</summary>
    public ISettingsService Settings => _settings;

    /// <summary>The screen currently shown (Start, wizard, Home, or Settings).</summary>
    [ObservableProperty]
    private ObservableObject _current;

    /// <summary>The era medium of the career currently ON SCREEN, or null outside a career
    /// (gallery, wizard, menus, overlays), the one-way era-skin token the App pushes to its
    /// audio controller (<c>SetEraSkin</c>) whenever it changes (era-theming-assets-brief.md,
    /// Workstream B: era changes how a cue is voiced, never when/whether it fires; the audio
    /// layer is TOLD the skin and never observes career state or outcomes). Raises
    /// PropertyChanged on every navigation through <see cref="Current"/>.</summary>
    public EraMedium? ActiveCareerEraMedium => Current is HubViewModel hub ? hub.EraMedium : null;

    partial void OnCurrentChanged(ObservableObject value) =>
        OnPropertyChanged(nameof(ActiveCareerEraMedium));

    /// <summary>Shell-level failure banner (e.g. a career file that would not open).</summary>
    [ObservableProperty]
    private string? _statusError;

    /// <summary>One-line AMS2 install detection status for the start screen footer.</summary>
    public string InstallStatus { get; }

    // ---------- navigation ----------

    private void BeginWizard(string? experienceMode = null)
    {
        StatusError = null;
        _beforeSettings = null;
        // Mode cards enter an explicit, pack-filtered v2 flow. The retained generic New Career
        // command uses the pre-card compatibility behavior: its selected pack resolves SMGP vs
        // Dynasty after parsing. Direct wizard callers still opt into v2 explicitly.
        var wizard = new NewCareerWizardViewModel(
            _environment,
            _factory,
            settings: _settings,
            experienceMode: experienceMode,
            inferExperienceModeFromPack: experienceMode is null);
        wizard.CareerCreated += OnCareerCreated;
        Wizard = wizard;
        OnPropertyChanged(nameof(Wizard));
        Current = wizard;
    }

    private void OnCareerCreated(object? sender, CareerCreatedEventArgs e)
    {
        Start.RecordCareer(e.CareerFilePath, e.Session.Summary.CareerName, e.Session.Summary.SeasonYear,
            e.Session.Pack.Manifest.CareerStyle, TerminalState(e.Session));
        _currentCareerPath = e.CareerFilePath;
        AttachHome(e.Session);
    }

    /// <summary>The gallery badge state for a career as observed from its opened session:
    /// "deceased" (Normal-mode death, the file stays as a viewable archive), "careerOver" (the
    /// SMGP floor knock-out), "bankrupt" (the Dynasty team folded), or null for a live career.</summary>
    private static string? TerminalState(ICareerSession session)
    {
        var mortality = session.PlayerMortality();
        if (mortality.Deceased || mortality.CareerFileDeleted)
            return "deceased";
        if (session.CurrentSmgpBriefing()?.CareerOver == true)
            return "careerOver";
        return session.BankruptcyScreen() is not null ? "bankrupt" : null;
    }

    private void OpenCareer(string path)
    {
        StatusError = null;
        ICareerSession session;
        try
        {
            session = _factory.Open(path);
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            // Opening an arbitrary *.ams2career file can fail many ways (missing, corrupt,
            // locked, not a database). The shell reports and stays up, never crashes.
            StatusError = $"Could not open the career: {ex.Message}";
            return;
        }

        Start.RecordCareer(path, session.Summary.CareerName, session.Summary.SeasonYear,
            session.Pack.Manifest.CareerStyle, TerminalState(session));
        _currentCareerPath = path;
        AttachHome(session);
    }

    private void AttachHome(ICareerSession session)
    {
        CloseHome();
        Wizard = null;
        _beforeSettings = null;
        OnPropertyChanged(nameof(Wizard));
        _hub = new HubViewModel(session, _watcherFactory(), settings: _settings);
        _hub.NextSeasonStarted += OnNextSeasonStarted;
        Current = _hub;
    }

    /// <summary>Sign-and-continue (M6): the new season is persisted but the open session
    /// still points at the finished one, dispose it and reopen the career file, which lands
    /// in the new season's round 1 briefing (OpenCareer opens the LATEST season).</summary>
    private void OnNextSeasonStarted(object? sender, EventArgs e)
    {
        string? path = _currentCareerPath;
        CloseHome();
        if (path is null)
        {
            GoToStart();
            return;
        }

        OpenCareer(path);
        if (Current is HubViewModel)
            return;

        // The reopen failed (StatusError explains), never leave a disposed screen showing.
        Start.Refresh();
        Current = Start;
    }

    /// <summary>Back to the start screen (also the wizard's Cancel). Closes the open career.</summary>
    [RelayCommand]
    private void GoToStart()
    {
        CloseHome();
        Wizard = null;
        _beforeSettings = null;
        _currentCareerPath = null;
        OnPropertyChanged(nameof(Wizard));
        StatusError = null;
        Start.Refresh();
        Current = Start;
    }

    private void CloseHome()
    {
        if (_hub is not null)
        {
            _hub.NextSeasonStarted -= OnNextSeasonStarted;
            _hub.Dispose();
            _hub = null;
        }
    }

    // ---------- settings screen ----------

    /// <summary>Gear icon in the shell header: opens Settings over the current screen, or
    /// closes it when it is already open (the same button toggles).</summary>
    [RelayCommand]
    private void ToggleSettings()
    {
        if (Current is SettingsViewModel)
        {
            CloseSettings();
            return;
        }

        var settingsScreen = new SettingsViewModel(_settings, _environment.DocumentsDirectory);
        settingsScreen.CloseRequested += (_, _) => CloseSettings();
        _beforeSettings = Current;
        Current = settingsScreen;
    }

    private void CloseSettings()
    {
        if (Current is not SettingsViewModel)
            return;
        Current = _beforeSettings ?? Start;
        _beforeSettings = null;
    }

    // ---------- developer debug menu (dynasty-passport-roadmap Piece 2) ----------

    /// <summary>The app-wide developer debug menu gate (default OFF, not in the normal Settings UI).
    /// Reads live from the settings seam. When false the debug keybind is a no-op and nothing renders;
    /// unlock it with Ctrl+Shift+F12 (persists here) or the <c>AMS2_DEVMODE=1</c> environment
    /// variable at startup.</summary>
    public bool DeveloperMode => _settings.Current.DeveloperMode;

    /// <summary>Directory the debug menu creates throwaway (real) careers under. A subfolder of
    /// Documents so the real factory can write a genuine .ams2career the gallery can reopen.</summary>
    private string DebugCareersDirectory =>
        Path.Combine(_environment.DocumentsDirectory, "AMS2CareerCompanion", "DebugCareers");

    /// <summary>The hidden UNLOCK chord (Ctrl+Shift+F12): flips <see cref="DeveloperMode"/> and
    /// PERSISTS it. Turning it on opens the debug overlay so the unlock is visibly confirmed; turning
    /// it off closes the overlay if it is showing.</summary>
    [RelayCommand]
    private void ToggleDeveloperMode()
    {
        bool next = !_settings.Current.DeveloperMode;
        _settings.Update(s => s with { DeveloperMode = next });
        OnPropertyChanged(nameof(DeveloperMode));

        if (next)
            OpenDebug();
        else if (Current is DebugMenuViewModel)
            CloseDebug();
    }

    /// <summary>The debug OPEN/CLOSE chord (Ctrl+Shift+D): opens the overlay over the current screen,
    /// or closes it when it is already open. A NO-OP while <see cref="DeveloperMode"/> is off, a
    /// shipped Release with the flag off shows nothing and costs nothing.</summary>
    [RelayCommand]
    private void ToggleDebug()
    {
        if (!DeveloperMode)
            return;
        if (Current is DebugMenuViewModel)
        {
            CloseDebug();
            return;
        }
        OpenDebug();
    }

    private void OpenDebug()
    {
        if (Current is DebugMenuViewModel)
            return;

        var debug = new DebugMenuViewModel(
            _environment,
            _factory,
            DebugCareersDirectory,
            currentSession: () => _hub?.Home.Session,
            currentCareerPath: () => _currentCareerPath);
        debug.CloseRequested += (_, _) => CloseDebug();
        debug.RealCareerRequested += OnDebugRealCareerRequested;
        debug.PreviewRequested += OnDebugPreviewRequested;
        debug.ScreenRequested += OnDebugScreenRequested;

        // Stash the pre-debug screen ONLY when opening from a non-debug context. Re-opening the menu
        // from a debug-spawned leaf (a promotion/demotion preview) must not overwrite the real
        // location with that transient leaf, so closing later still returns where the user was.
        _beforeDebug ??= Current;
        Current = debug;
    }

    private void CloseDebug()
    {
        if (Current is not DebugMenuViewModel)
            return;
        Current = _beforeDebug ?? Start;
        _beforeDebug = null;
    }

    /// <summary>Tier-1: a REAL throwaway career the debug menu created, recorded in the gallery and
    /// opened exactly like any career (it reopens, signs, and resimulates like a normal save).</summary>
    private void OnDebugRealCareerRequested(object? sender, DebugCareerOpenedEventArgs e)
    {
        _beforeDebug = null;
        StatusError = null;
        Start.RecordCareer(e.CareerFilePath, e.Session.Summary.CareerName, e.Session.Summary.SeasonYear,
            e.Session.Pack.Manifest.CareerStyle, TerminalState(e.Session));
        _currentCareerPath = e.CareerFilePath;
        AttachHome(e.Session);
    }

    /// <summary>Tier-2: a display-only preview session hosted in a hub. It is pathless (never signs or
    /// reopens) and is NOT recorded in the gallery, nothing about it touches disk.</summary>
    private void OnDebugPreviewRequested(object? sender, ICareerSession session)
    {
        _beforeDebug = null;
        StatusError = null;
        _currentCareerPath = null;
        AttachHome(session);
    }

    /// <summary>Tier-2: a single leaf screen (promotion / demotion), or a reopen of the debug overlay
    /// itself. Set directly as the current screen; the debug overlay underneath stays stashed so
    /// closing it later restores wherever the user was.</summary>
    private void OnDebugScreenRequested(object? sender, ObservableObject screen) =>
        Current = screen;

    // ---------- Esc = back (ux-round contract: everywhere non-destructive) ----------

    /// <summary>Shell-level Esc: one non-destructive step back. Settings → previous screen;
    /// wizard → previous step (or Start from the first step, where nothing is lost yet);
    /// Home delegates to its content (standings/confirm know their way back). Returns false
    /// when Esc means nothing here (e.g. mid result entry, the grammar owns the keyboard).</summary>
    public bool TryEscapeBack()
    {
        switch (Current)
        {
            case SettingsViewModel:
                CloseSettings();
                return true;

            case DebugMenuViewModel:
                CloseDebug();
                return true;

            case NewCareerWizardViewModel wizard:
                if (wizard.CanGoBack)
                {
                    wizard.BackCommand.Execute(null);
                    return true;
                }
                GoToStart();
                return true;

            case HubViewModel hub:
                return hub.TryEscapeBack();

            default:
                return false;
        }
    }

    private static string ComposeInstallStatus(CareerEnvironment environment)
    {
        try
        {
            var install = environment.LocateInstall();
            return install is null
                ? "⚠ AMS2 install not found via Steam, staging will be unavailable until it is."
                : $"AMS2 install detected: {install.InstallDirectory}";
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            return $"⚠ AMS2 install detection failed: {ex.Message}";
        }
    }

    public void Dispose() => CloseHome();
}
