using System.Windows.Controls;

namespace Companion.App.Views;

/// <summary>The starting-grid look shown after qualifying: the grid pole-first as driver + car cards,
/// two wide and scrollable. DataContext is a StartingGridViewModel.</summary>
public partial class StartingGridView : UserControl
{
    public StartingGridView() => InitializeComponent();
}
