using System.Windows;

namespace Companion.App.Views;

/// <summary>
/// The compact always-on-top setup checklist (ux-round briefing correction): a small
/// Topmost tool window bound to the SAME BriefingViewModel as the briefing screen, so the
/// user can tick settings off while clicking through AMS2's custom-race steppers in
/// windowed/borderless mode. Remembers its position for the rest of the app run (position
/// persistence across restarts belongs to the settings screen, not v1 of this window).
/// </summary>
public partial class BriefingCompactWindow : Window
{
    private static Point? _lastPosition;

    public BriefingCompactWindow()
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
