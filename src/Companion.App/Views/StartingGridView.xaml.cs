using System.Windows;
using System.Windows.Controls;

namespace Companion.App.Views;

/// <summary>The cinematic starting grid — a staggered two-row card grid paged horizontally. The
/// carousel arrows scroll the grid by roughly two cards; everything else is data-bound. DataContext
/// is a StartingGridViewModel.</summary>
public partial class StartingGridView : UserControl
{
    private const double PageStep = 548; // ~two 264-wide cards + margins

    public StartingGridView() => InitializeComponent();

    private void OnScrollLeft(object sender, RoutedEventArgs e) =>
        GridScroll.ScrollToHorizontalOffset(GridScroll.HorizontalOffset - PageStep);

    private void OnScrollRight(object sender, RoutedEventArgs e) =>
        GridScroll.ScrollToHorizontalOffset(GridScroll.HorizontalOffset + PageStep);
}
