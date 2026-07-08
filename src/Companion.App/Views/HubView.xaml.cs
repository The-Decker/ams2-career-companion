using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Companion.ViewModels.Hub;

namespace Companion.App.Views;

/// <summary>
/// The Career Hub shell: a left tab rail around the re-homed loop. Pure bindings to
/// <c>HubViewModel</c> — tab selection is command-bound and the number-key tab accelerators
/// live at the window level (MainWindow), the reliable top of the key tunnel, so they fire
/// whatever child currently holds focus.
///
/// The only code-behind is the rail's tear-off (⧉): pop a read-only lens tab into an always-on-top
/// <see cref="TabWindow"/> bound to the SAME tab view-model, so it mirrors the lens live (even the
/// Standings lens, whose view-model is rebuilt each round, because the window binds the stable tab).
/// </summary>
public partial class HubView : UserControl
{
    // One companion window per torn-off tab (Standings and History can be out at once); re-clicking
    // a tab's ⧉ re-focuses its window instead of spawning a duplicate.
    private readonly Dictionary<HubTabViewModel, TabWindow> _popOuts = [];

    public HubView() => InitializeComponent();

    private void OnPopOutTab(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not HubTabViewModel tab)
            return;

        if (_popOuts.TryGetValue(tab, out var existing))
        {
            if (existing.IsLoaded)
            {
                existing.Activate();
                return;
            }
            _popOuts.Remove(tab); // a window that closed without us hearing — replace it
        }

        var window = new TabWindow
        {
            DataContext = tab,
            Owner = Window.GetWindow(this),
        };
        window.RememberBy(tab.Key);
        window.Closed += (_, _) => _popOuts.Remove(tab);
        _popOuts[tab] = window;
        window.Show();
    }
}
