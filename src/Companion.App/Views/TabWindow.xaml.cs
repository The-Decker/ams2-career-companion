using System.Collections.Generic;
using System.Windows;

namespace Companion.App.Views;

/// <summary>
/// The generic tear-off companion window hosting one hub lens (Standings / History / Driver /
/// Skins) on top of AMS2 on a second monitor. Its DataContext is the tab view-model; the XAML
/// hosts <c>Content</c> through the app DataTemplates, so a lens whose view-model is rebuilt after
/// a round still updates live (the window binds the stable tab, not the swapped view-model).
/// Remembers its last position per tab key for the rest of the app run.
/// </summary>
public partial class TabWindow : Window
{
    private static readonly Dictionary<string, Point> LastPositions = [];
    private string? _key;

    public TabWindow() => InitializeComponent();

    /// <summary>Restore this pop-out's last position for the given tab key (so re-opening a torn-off
    /// lens lands where the user left it), and record it again on close.</summary>
    public void RememberBy(string key)
    {
        _key = key;
        if (LastPositions.TryGetValue(key, out var position))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = position.X;
            Top = position.Y;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        Closing += (_, _) =>
        {
            if (_key is { } k)
                LastPositions[k] = new Point(Left, Top);
        };
    }
}
