using System.Windows;
using System.Windows.Controls;
using Companion.ViewModels.Hub;
using Companion.ViewModels.Shell;

namespace Companion.App.Views;

/// <summary>The News tab: an era-styled ticker of journal dispatches, each expanding on click
/// into the full period article. The only code-behind is the tear-off: pop the feed into an
/// always-on-top <see cref="NewsWindow"/> bound to the same view-model (decision 1).</summary>
public partial class NewsView : UserControl
{
    // One shared companion window across the app; re-popping just re-focuses it (and replaces it
    // if a different career's feed is now showing).
    private static NewsWindow? _popOut;

    public NewsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // The second NewsView living *inside* the pop-out window must not offer its own pop-out.
        if (Window.GetWindow(this) is NewsWindow)
            PopOutButton.Visibility = Visibility.Collapsed;
    }

    private void OnPopOut(object sender, RoutedEventArgs e)
    {
        if (_popOut is { IsLoaded: true } existing)
        {
            if (ReferenceEquals(existing.DataContext, DataContext))
            {
                existing.Activate();
                return;
            }
            existing.Close(); // a stale window from a previous career — replace it
        }

        var owner = Window.GetWindow(this);
        var hub = (owner?.DataContext as ShellViewModel)?.Current as HubViewModel;
        var window = new NewsWindow
        {
            DataContext = DataContext,
            // The feed VM intentionally stays narrow; the Window tag carries the live Hub session
            // and RoundText token so Task-4 world dispatches remain reactive in the tear-off.
            Tag = hub,
            Owner = owner,
        };
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_popOut, window))
                _popOut = null;
        };
        _popOut = window;
        window.Show();
    }
}
