using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Companion.ViewModels.Services;
using Companion.ViewModels.Start;
using Companion.ViewModels.Wizard;

namespace Companion.ViewModels.Shell;

/// <summary>
/// The app-level conductor behind MainWindow's ContentControl: Start → (wizard | continue) →
/// Home. WPF-free so navigation is unit-testable; the view maps viewmodel types to screens
/// with DataTemplates. Owns at most one open career at a time and disposes the previous
/// session (and its staged-file watcher) when navigating away.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject, IDisposable
{
    private readonly CareerEnvironment _environment;
    private readonly ICareerFactory _factory;
    private readonly Func<IFileWatcher?> _watcherFactory;

    private HomeViewModel? _home;

    public ShellViewModel(
        CareerEnvironment environment,
        ICareerFactory factory,
        IRecentCareersStore recentCareers,
        Func<IFileWatcher?>? stagedFileWatcherFactory = null)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(recentCareers);

        _environment = environment;
        _factory = factory;
        _watcherFactory = stagedFileWatcherFactory ?? (static () => null);

        Start = new StartViewModel(recentCareers);
        Start.ContinueRequested += (_, path) => OpenCareer(path);
        Start.NewCareerRequested += (_, _) => BeginWizard();

        _current = Start;
        InstallStatus = ComposeInstallStatus(environment);
    }

    public StartViewModel Start { get; }

    /// <summary>The wizard in progress; null outside the new-career flow.</summary>
    public NewCareerWizardViewModel? Wizard { get; private set; }

    /// <summary>The screen currently shown (Start, wizard, or Home).</summary>
    [ObservableProperty]
    private ObservableObject _current;

    /// <summary>Shell-level failure banner (e.g. a career file that would not open).</summary>
    [ObservableProperty]
    private string? _statusError;

    /// <summary>One-line AMS2 install detection status for the start screen footer.</summary>
    public string InstallStatus { get; }

    // ---------- navigation ----------

    private void BeginWizard()
    {
        StatusError = null;
        var wizard = new NewCareerWizardViewModel(_environment, _factory);
        wizard.CareerCreated += OnCareerCreated;
        Wizard = wizard;
        OnPropertyChanged(nameof(Wizard));
        Current = wizard;
    }

    private void OnCareerCreated(object? sender, CareerCreatedEventArgs e)
    {
        Start.RecordCareer(e.CareerFilePath, e.Session.Summary.CareerName);
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

        Start.RecordCareer(path, session.Summary.CareerName);
        AttachHome(session);
    }

    private void AttachHome(ICareerSession session)
    {
        CloseHome();
        Wizard = null;
        OnPropertyChanged(nameof(Wizard));
        _home = new HomeViewModel(session, _watcherFactory());
        Current = _home;
    }

    /// <summary>Back to the start screen (also the wizard's Cancel). Closes the open career.</summary>
    [RelayCommand]
    private void GoToStart()
    {
        CloseHome();
        Wizard = null;
        OnPropertyChanged(nameof(Wizard));
        StatusError = null;
        Start.Refresh();
        Current = Start;
    }

    private void CloseHome()
    {
        _home?.Dispose();
        _home = null;
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
