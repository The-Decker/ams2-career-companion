using System.Windows.Controls;

namespace Companion.App.Views;

/// <summary>The Paddock lens (SMGP driver/team preview) — a DRIVERS/TEAMS master-detail. All state
/// lives on the <c>PaddockViewModel</c>; this is the shell.</summary>
public partial class PaddockView : UserControl
{
    public PaddockView() => InitializeComponent();
}
