using System.Windows.Controls;

namespace Companion.App.Views;

/// <summary>
/// The Career Hub shell: a left tab rail around the re-homed loop. Pure bindings to
/// <c>HubViewModel</c> — tab selection is command-bound and the number-key tab accelerators
/// live at the window level (MainWindow), the reliable top of the key tunnel, so they fire
/// whatever child currently holds focus.
/// </summary>
public partial class HubView : UserControl
{
    public HubView() => InitializeComponent();
}
