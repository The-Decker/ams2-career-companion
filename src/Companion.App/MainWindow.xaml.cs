using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
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
    public MainWindow()
    {
        InitializeComponent();
        // The App assigns DataContext before Show(), so this runs pre-show — early enough
        // to switch the startup location to the remembered placement.
        DataContextChanged += OnDataContextChanged;
        Closing += OnClosing;
    }

    /// <summary>Esc = one non-destructive step back, everywhere (ux-round contract). The
    /// viewmodel decides whether Esc means anything right now (it never cancels destructively
    /// and never steals Esc from the result-entry grammar); unhandled Esc falls through.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || e.Handled)
            return;
        if (DataContext is ShellViewModel shell && shell.TryEscapeBack())
            e.Handled = true;
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
