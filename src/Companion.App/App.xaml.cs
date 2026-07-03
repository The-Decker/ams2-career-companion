using System.IO;
using System.Windows;
using System.Windows.Threading;
using Companion.App.Services;
using Companion.ViewModels.Services;
using Companion.ViewModels.Shell;

namespace Companion.App;

/// <summary>
/// Composition root (app-shell contract): detect the AMS2 install via SteamLocator, load the
/// content library from the exe-adjacent data\ams2 folder, construct the services and the
/// WPF-free ShellViewModel, and show MainWindow over it. The published exe is self-sufficient:
/// data\ams2, data\rules and packs\ are copied beside it at build/publish time.
/// </summary>
public partial class App : Application
{
    private ShellViewModel? _shell;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        try
        {
            string ams2DataDirectory = Path.Combine(AppContext.BaseDirectory, "data", "ams2");
            var environment = CareerEnvironment.CreateDefault(ams2DataDirectory);
            var factory = new CareerSessionFactory(environment);
            var recentCareers = RecentCareersStore.CreateDefault();

            _shell = new ShellViewModel(
                environment,
                factory,
                recentCareers,
                stagedFileWatcherFactory: static () => new FileSystemFileWatcher());

            var window = new MainWindow { DataContext = _shell };
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "AMS2 Career Companion could not start:\n\n" + ex.Message +
                "\n\nThe data\\ams2 content library must sit beside the exe.",
                "AMS2 Career Companion",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shell?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Last-resort guard: report instead of tearing the process down mid-career.
        MessageBox.Show(
            "Unexpected error:\n\n" + e.Exception.Message,
            "AMS2 Career Companion",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
