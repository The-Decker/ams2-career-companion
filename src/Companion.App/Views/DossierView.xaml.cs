using System.Windows.Controls;

namespace Companion.App.Views;

/// <summary>The hub's Driver dossier lens (character depth 3): the player's character — name, stats,
/// perks, and level/XP — as the career unfolds. DataContext is a <c>DossierViewModel</c>; the view is
/// a pure read-only binding surface with no code-behind logic.</summary>
public partial class DossierView : UserControl
{
    public DossierView() => InitializeComponent();
}
