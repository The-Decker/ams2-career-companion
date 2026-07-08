using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Companion.App.Services;
using Companion.ViewModels.Services;
using Companion.ViewModels.Settings;
using Companion.ViewModels.Shell;

namespace Companion.App;

/// <summary>
/// Composition root (app-shell contract): detect the AMS2 install via SteamLocator, load the
/// content library from the exe-adjacent data\ams2 folder, construct the services and the
/// WPF-free ShellViewModel, and show MainWindow over it. The published exe is self-sufficient:
/// data\ams2, data\rules and packs\ are copied beside it at build/publish time.
///
/// Settings live-apply (ux-round contract section 3): the accent color mutates the shared
/// AccentBrush/AccentDimBrush instances in place and the font scale replaces the AppUiScale
/// resource (which every window's root LayoutTransform reads), so every open screen — and every
/// tear-off window — restyles and rescales immediately, no restart, no view reload.
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
            var settings = new SettingsService(JsonSettingsStore.CreateDefault());
            var environment = CareerEnvironment.CreateDefault(ams2DataDirectory);
            // Era-transition pack discovery (M6) searches the same roots as the wizard:
            // the defaults plus the settings screen's live custom pack folders.
            environment.PackSearchRoots = () =>
            [
                .. PackDiscovery.DefaultSearchRoots(environment.DocumentsDirectory),
                .. settings.Current.PackFolders,
            ];
            var factory = new CareerSessionFactory(environment);
            var recentCareers = RecentCareersStore.CreateDefault();

            ApplyAppearance(settings.Current);
            settings.Changed += (_, current) => Dispatcher.Invoke(() => ApplyAppearance(current));

            _shell = new ShellViewModel(
                environment,
                factory,
                recentCareers,
                stagedFileWatcherFactory: static () => new FileSystemFileWatcher(),
                settings: settings);

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

    /// <summary>Pushes the appearance settings into the theme resources: accent + derived
    /// dim accent (a 24% blend toward the window background) and the root UI scale (font scale ÷
    /// 100), which every window's root LayoutTransform multiplies by so ALL text and spacing scale
    /// uniformly — inline sizes, headings and tear-off windows included, not just inherited body
    /// text. Mutating the brush instances / replacing the resources updates every reference live.</summary>
    private void ApplyAppearance(AppSettings settings)
    {
        var accent = ParseColor(settings.AccentColor) ?? ParseColor(AppSettings.DefaultAccentColor)!.Value;
        SetBrushColor("AccentBrush", accent);
        SetBrushColor("AccentDimBrush", Blend(accent, (Color)ColorConverter.ConvertFromString("#1B1B1F"), 0.24));
        // The base body font stays 14; the root UI-scale transform applies the scale (so an inline
        // FontSize scales the same as an inherited one, and there is no double-scaling).
        Resources["AppFontSize"] = 14.0;
        Resources["AppUiScale"] = settings.FontScalePercent / 100.0;
    }

    private void SetBrushColor(string key, Color color)
    {
        if (Resources[key] is SolidColorBrush { IsFrozen: false } brush)
            brush.Color = color;
        else
            Resources[key] = new SolidColorBrush(color);
    }

    private static Color? ParseColor(string hex)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(
                hex.StartsWith('#') ? hex : "#" + hex);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>fraction of <paramref name="accent"/> over <paramref name="background"/>.</summary>
    private static Color Blend(Color accent, Color background, double fraction) => Color.FromRgb(
        (byte)(background.R + (accent.R - background.R) * fraction),
        (byte)(background.G + (accent.G - background.G) * fraction),
        (byte)(background.B + (accent.B - background.B) * fraction));

    protected override void OnExit(ExitEventArgs e)
    {
        _shell?.Dispose();
        base.OnExit(e);
    }

    private bool _reportingCrash;

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Last-resort guard: report instead of tearing the process down mid-career.
        e.Handled = true;

        // Re-entrancy guard: MessageBox pumps messages, so a layout/render exception that
        // fires every frame would re-enter here from inside the box and recurse until the
        // stack overflows. One report at a time; repeats while the box is open are dropped.
        if (_reportingCrash)
            return;
        _reportingCrash = true;
        try
        {
            TryWriteCrashLog(e.Exception);
            MessageBox.Show(
                "Unexpected error:\n\n" + e.Exception.Message,
                "AMS2 Career Companion",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _reportingCrash = false;
        }
    }

    /// <summary>Full exception detail lands in %APPDATA%\AMS2CareerCompanion\last-crash.txt
    /// (the message box only shows the message) — best-effort, never throws.</summary>
    private static void TryWriteCrashLog(Exception exception)
    {
        try
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AMS2CareerCompanion");
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, "last-crash.txt"),
                $"{DateTimeOffset.Now:O}{Environment.NewLine}{exception}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
        }
    }
}
