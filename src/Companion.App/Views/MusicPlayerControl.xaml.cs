using System.Windows.Controls;

namespace Companion.App.Views;

/// <summary>
/// Persistent shell music controls. Playback state and commands are supplied by the app-owned
/// music-player view model; this view deliberately contains no scene or navigation behavior.
/// </summary>
public partial class MusicPlayerControl : UserControl
{
    public MusicPlayerControl()
    {
        InitializeComponent();
    }
}
