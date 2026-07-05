using System.Windows;

namespace Companion.App.Views;

/// <summary>
/// The tear-off News companion window (career-hub-design.md §2.1, "own windows"): a small
/// always-on-top tool window hosting a second <c>NewsView</c> bound to the SAME NewsViewModel as
/// the hub tab, so it mirrors the feed live while you race in AMS2 on another monitor. Read-only,
/// so there is no state or parity cost. Remembers its position for the rest of the app run.
/// </summary>
public partial class NewsWindow : Window
{
    private static Point? _lastPosition;

    public NewsWindow()
    {
        InitializeComponent();

        if (_lastPosition is { } position)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = position.X;
            Top = position.Y;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        Closing += (_, _) => _lastPosition = new Point(Left, Top);
    }
}
