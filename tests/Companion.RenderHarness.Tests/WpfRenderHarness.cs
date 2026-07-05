using System.Windows;
using System.Windows.Threading;

namespace Companion.RenderHarness.Tests;

/// <summary>
/// A minimal off-screen WPF host: one hidden STA <see cref="Application"/> with the app theme
/// merged in (so every <c>StaticResource</c> the view uses resolves), and a helper that runs a
/// test body on a dedicated STA thread with a live Dispatcher. Real WPF controls only work on an
/// STA thread with a Dispatcher; xunit runs on the thread pool (MTA), so every render test hops
/// onto its own STA thread through <see cref="RunSta"/>.
///
/// If STA/WPF is unavailable in this environment the whole harness self-skips via
/// <see cref="IsSupported"/> — the caller turns an unsupported result into <c>Assert.Skip</c>-style
/// early return rather than a false failure.
/// </summary>
internal static class WpfRenderHarness
{
    private static readonly object Gate = new();
    private static bool _themeLoaded;

    /// <summary>WPF renders here only on Windows. (The project targets net10.0-windows, so this
    /// is effectively always true when it builds, but we guard anyway for a portable skip.)</summary>
    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>Runs <paramref name="body"/> on a fresh STA thread carrying a WPF Dispatcher and a
    /// live <see cref="Application"/> whose resources include the app theme. Rethrows any assertion
    /// / exception from the body on the calling thread so xunit sees the real failure.</summary>
    public static void RunSta(Action body)
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationWithTheme();
                body();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
            finally
            {
                // Drain any queued dispatcher work (e.g. the view's deferred FocusInput) so
                // nothing leaks into the next test's thread.
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        if (captured is not null)
            throw new HarnessBodyException(captured);
    }

    /// <summary>Pump the Dispatcher down to <see cref="DispatcherPriority.Input"/> so deferred
    /// <c>BeginInvoke(DispatcherPriority.Input, …)</c> work (the view's FocusInput) actually runs
    /// before the test asserts on focus.</summary>
    public static void Pump(DispatcherPriority priority = DispatcherPriority.Input)
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(priority, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    /// <summary>One Application per AppDomain; only the first STA thread constructs it. The theme
    /// dictionary is merged into <see cref="Application.Resources"/> exactly as App.xaml does, so
    /// a standalone ResultEntryView finds AccentBrush, Panel, DnfReason, etc.</summary>
    private static void EnsureApplicationWithTheme()
    {
        lock (Gate)
        {
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            if (_themeLoaded)
                return;

            // The App project's assembly name is AMS2CareerCompanion (see its csproj), so the
            // theme's compiled resource lives under that authority, not "Companion.App".
            var theme = (ResourceDictionary)Application.LoadComponent(
                new Uri("/AMS2CareerCompanion;component/Themes/Theme.xaml", UriKind.Relative));
            app.Resources.MergedDictionaries.Add(theme);
            // App.xaml.cs seeds a numeric AppFontSize at startup (14 × scale); Theme.xaml declares
            // the key but the DynamicResource consumers want a live value. Mirror the default.
            app.Resources["AppFontSize"] = 14.0;
            _themeLoaded = true;
        }
    }
}

/// <summary>Wraps an exception thrown inside an STA body so the stack trace survives the thread
/// hop with its original message intact.</summary>
internal sealed class HarnessBodyException(Exception inner)
    : Exception(inner.Message, inner);
