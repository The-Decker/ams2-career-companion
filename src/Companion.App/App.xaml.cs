using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Companion.App.Audio;
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
    private AppAudioController? _audio;

    /// <summary>App-layer path registry used by the save UI to reopen a session after a whole-file
    /// restore. Render-only hosts never construct the composition root, so callers treat it as optional.</summary>
    internal TrackingCareerFactory? TrackedCareerFactory { get; private set; }

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
            var factory = new TrackingCareerFactory(new CareerSessionFactory(environment));
            TrackedCareerFactory = factory;
            var recentCareers = RecentCareersStore.CreateDefault();

            ApplyTheme(settings.Current);
            ApplyAppearance(settings.Current);
            settings.Changed += (_, current) => Dispatcher.Invoke(() =>
            {
                ApplyTheme(current);
                ApplyAppearance(current);
            });

            _shell = new ShellViewModel(
                environment,
                factory,
                recentCareers,
                stagedFileWatcherFactory: static () => new FileSystemFileWatcher(),
                settings: settings);

            // Audio is presentation-only and deliberately sits at the composition root. Music is
            // controlled solely by the persistent manual player; navigation only emits explicitly
            // opted-in interaction SFX. A machine without usable Windows media support still gets
            // the complete app, just without the player.
            TryInitializeAudio(settings);

            var window = new MainWindow
            {
                DataContext = _shell,
                MusicPlayerDataContext = _audio?.Player,
            };
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
        // The base body font stays 14; the root UI-scale transform applies the scale (so an inline
        // FontSize scales the same as an inherited one, and there is no double-scaling).
        Resources["AppFontSize"] = 14.0;
        Resources["AppUiScale"] = settings.FontScalePercent / 100.0;
    }

    // The two runtime-swappable theme slots (Codex's theme contract): a BASE palette
    // (Theme.Dark/Light.xaml — the 32 semantic brushes) and an ACCENT (Accents/<base>/Accent.<name>.xaml
    // — the 6 accent brushes). Merged AFTER Theme.xaml so they win, and every view consumes the brushes
    // via DynamicResource, so replacing these recolors every open screen + tear-off window live.
    private ResourceDictionary? _baseThemeDict;
    private ResourceDictionary? _accentDict;

    /// <summary>Loads the base + accent ResourceDicts for the chosen theme/accent and swaps them into the
    /// app's merged dictionaries. Light/Dark selection + the 7 named accents both flow through here.</summary>
    private void ApplyTheme(AppSettings settings)
    {
        string baseName = string.Equals(settings.Theme, AppSettings.ThemeLight, StringComparison.OrdinalIgnoreCase)
            ? "Light" : "Dark";
        string accentName = AppSettings.NormalizeAccentName(settings.AccentName);

        var baseDict = LoadThemeDictionary($"Theme.{baseName}.xaml");
        var accentDict = LoadThemeDictionary($"Accents/{baseName}/Accent.{accentName}.xaml");

        var merged = Resources.MergedDictionaries;
        if (_baseThemeDict is not null)
            merged.Remove(_baseThemeDict);
        if (_accentDict is not null)
            merged.Remove(_accentDict);
        merged.Add(baseDict);      // base first…
        merged.Add(accentDict);    // …then accent, so its 6 brushes override the base defaults
        _baseThemeDict = baseDict;
        _accentDict = accentDict;
    }

    private static ResourceDictionary LoadThemeDictionary(string relativePath) =>
        (ResourceDictionary)LoadComponent(
            new Uri("/AMS2CareerCompanion;component/Themes/" + relativePath, UriKind.Relative));

    protected override void OnExit(ExitEventArgs e)
    {
        DisposeAudio();
        _shell?.Dispose();
        base.OnExit(e);
    }

    private void TryInitializeAudio(ISettingsService settings)
    {
        AppAudioController? audio = null;
        try
        {
            audio = new AppAudioController(settings);
            SoundAssist.Connect((cue, source) => audio.PlayEffect(cue, source));
            Activated += OnApplicationActivated;
            Deactivated += OnApplicationDeactivated;

            // Transfer the completed lifetime to the App only after every setup step succeeds.
            _audio = audio;
            audio = null;
        }
        catch
        {
            // Audio must never turn an otherwise valid launch into the fatal startup dialog.
            Activated -= OnApplicationActivated;
            Deactivated -= OnApplicationDeactivated;
            SoundAssist.Disconnect();
            TryDisposeAudio(audio);
            _audio = null;
        }
    }

    private void DisposeAudio()
    {
        Activated -= OnApplicationActivated;
        Deactivated -= OnApplicationDeactivated;
        SoundAssist.Disconnect();

        AppAudioController? audio = _audio;
        _audio = null;
        TryDisposeAudio(audio);
    }

    private static void TryDisposeAudio(IDisposable? audioLifetime)
    {
        try
        {
            audioLifetime?.Dispose();
        }
        catch
        {
            // Media teardown is decorative too. App/session disposal must always continue.
        }
    }

    private void OnApplicationActivated(object? sender, EventArgs e) =>
        _audio?.SetApplicationActive(true);

    private void OnApplicationDeactivated(object? sender, EventArgs e) =>
        _audio?.SetApplicationActive(false);

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
