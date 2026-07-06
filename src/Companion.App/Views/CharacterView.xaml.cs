using System.Windows.Controls;

namespace Companion.App.Views;

/// <summary>The new-career wizard's character-creation step (Increment 4a). DataContext is a
/// <c>CharacterViewModel</c>; the view is a pure binding surface (three tiers: archetype preset,
/// stat sliders + perk shelf, raw numbers) with no code-behind logic. Also reusable as the driver
/// dossier's editor.</summary>
public partial class CharacterView : UserControl
{
    public CharacterView() => InitializeComponent();
}
