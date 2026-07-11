using System.Windows.Controls;

namespace Companion.App.Views;

/// <summary>The SMGP rival screen — its own step after race setup. DataContext is a
/// RivalScreenViewModel; its content binds to the shared BriefingViewModel.</summary>
public partial class RivalScreenView : UserControl
{
    public RivalScreenView() => InitializeComponent();
}
