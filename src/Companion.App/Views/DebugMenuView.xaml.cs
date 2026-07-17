using System.Windows.Controls;

namespace Companion.App.Views;

/// <summary>Thin shell over DebugMenuViewModel (dynasty-passport-roadmap Piece 2) — pure bindings,
/// no view-side logic. Dev-only; only rendered when the runtime developer-mode gate is unlocked.</summary>
public partial class DebugMenuView : UserControl
{
    public DebugMenuView()
    {
        InitializeComponent();
    }
}
