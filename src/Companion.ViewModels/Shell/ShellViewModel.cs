using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
            e.Session.Pack.Manifest.CareerStyle);
        _currentCareerPath = e.CareerFilePath;
        AttachHome(e.Session);
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
            // locked, not a database). The shell reports and stays up — never crashes.
            StatusError = $"Could not open the career: {ex.Message}";
            return;
        }

        Start.RecordCareer(path, session.Summary.CareerName, session.Summary.SeasonYear,
            session.Pack.Manifest.CareerStyle);
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
    /// still points at the finished one — dispose it and reopen the career file, which lands
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

        // The reopen failed (StatusError explains) — never leave a disposed screen showing.
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

    // ---------- Esc = back (ux-round contract: everywhere non-destructive) ----------

    /// <summary>Shell-level Esc: one non-destructive step back. Settings → previous screen;
    /// wizard → previous step (or Start from the first step, where nothing is lost yet);
    /// Home delegates to its content (standings/confirm know their way back). Returns false
    /// when Esc means nothing here (e.g. mid result entry — the grammar owns the keyboard).</summary>
    public bool TryEscapeBack()
    {
        switch (Current)
        {
            case SettingsViewModel:
                CloseSettings();
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
                ? "⚠ AMS2 install not found via Steam — staging will be unavailable until it is."
                : $"AMS2 install detected: {install.InstallDirectory}";
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            return $"⚠ AMS2 install detection failed: {ex.Message}";
        }
    }

    public void Dispose() => CloseHome();
}
