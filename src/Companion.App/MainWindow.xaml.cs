using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Companion.App.Views;
using Companion.ViewModels.Hub;
using Companion.ViewModels.Settings;
using Companion.ViewModels.Shell;

namespace Companion.App;

/// <summary>
/// The shell window: a ContentControl over ShellViewModel.Current (DataContext), with every
/// screen mapped by the DataTemplates in App.xaml. The only logic here is bridging the
/// window-level Esc key onto the viewmodel's non-destructive back navigation, plus applying/
/// persisting the remembered window placement (ux-round contract section 4) through the
/// settings seam — everything else lives in Companion.ViewModels.Shell.ShellViewModel.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// App-owned player state shown in the persistent shell header. This is intentionally
    /// separate from the shell DataContext so adding the player cannot disturb navigation.
    /// Assigning null hides the compact player completely.
    /// </summary>
    public object? MusicPlayerDataContext
    {
        get => MusicPlayer.DataContext;
        set => MusicPlayer.DataContext = value;
    }

    public MainWindow()
    {
        InitializeComponent();
        // The App assigns DataContext before Show(), so this runs pre-show — early enough
        // to switch the startup location to the remembered placement.
        DataContextChanged += OnDataContextChanged;
        Closing += OnClosing;
    }

    /// <summary>Window-level key routing (the reliable top of the tunnel, so it fires whatever
    /// child has focus): number keys 1–9 select hub tabs, and Esc = one non-destructive step
    /// back (ux-round contract). Both yield to the result-entry grammar — a focused editable box
    /// keeps its own digits/Esc — and to any modifier chord. Unhandled keys fall through.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled || DataContext is not ShellViewModel shell)
            return;

        // A fatal result replaces the whole Hub with the DB-free death screen. In Hardcore the
        // career database is already disposed and deleted, so no global Esc/tab accelerator may
        // reach hidden Hub/Home commands behind that terminal surface.
        if (shell.Current is HubViewModel { Home.CareerOver: not null }
            or HubViewModel { Home.Briefing.SmgpCareerOver: true })
            return;

        var hubView = FindVisualDescendant<HubView>(this);

        // MainWindow is the top of PreviewKeyDown's tunnel. Give the modal Team HQ first refusal
        // so Esc closes the overlay instead of navigating the disabled Hub content underneath it.
        if (e.Key == Key.Escape && hubView?.TryCloseTycoonDashboard() == true)
        {
            e.Handled = true;
            return;
        }

        // While the dashboard is open, bare digits belong to the focused modal surface; never let
        // the global 1–9 accelerators change a disabled tab behind its scrim.
        if (hubView?.IsTycoonDashboardOpen == true)
            return;

        // Tab accelerators: only when a hub is open, no modifier, and focus is not in an
        // editable box (the result-entry InputBox owns bare digits).
        if (shell.Current is HubViewModel hub &&
            Keyboard.Modifiers == ModifierKeys.None &&
            Keyboard.FocusedElement is not TextBox { IsReadOnly: false })
        {
            int tab = e.Key switch
            {
                >= Key.D1 and <= Key.D9 => e.Key - Key.D1 + 1,
                >= Key.NumPad1 and <= Key.NumPad9 => e.Key - Key.NumPad1 + 1,
                _ => 0,
            };
            if (tab > 0 && hub.SelectTabByNumber(tab))
            {
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Escape && shell.TryEscapeBack())
            e.Handled = true;
    }

    private static T? FindVisualDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                return match;
            if (FindVisualDescendant<T>(child) is { } nested)
                return nested;
        }
        return null;
    }

    // ---------- window placement remembered (ux-round contract section 4) ----------

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsLoaded || e.NewValue is not ShellViewModel shell)
            return;
        if (shell.Settings.Current.WindowPlacement is not { } saved || !saved.IsUsable())
            return;

        // Clamp to today's virtual screen (tested in the viewmodels assembly) so a window
        // last closed on an unplugged monitor still comes back reachable.
        var placement = saved.ClampTo(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = placement.Left;
        Top = placement.Top;
        Width = placement.Width;
        Height = placement.Height;
        if (placement.IsMaximized)
            WindowState = WindowState.Maximized;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is not ShellViewModel shell)
            return;

        // When maximized (or minimized to close), RestoreBounds is the normal-state rect —
        // that is what should come back next launch, plus the IsMaximized flag.
        bool isMaximized = WindowState == WindowState.Maximized;
        Rect bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var placement = new WindowPlacementSettings
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height,
            IsMaximized = isMaximized,
        };
        if (!placement.IsUsable())
            return;

        shell.Settings.Update(s => s with { WindowPlacement = placement });
    }
}
