using System.Windows.Controls;

namespace Companion.App.Views;

/// <summary>The News tab: an era-styled ticker of journal dispatches, each expanding on click
/// into the full period article. Pure bindings to <c>NewsViewModel</c>; no code-behind logic.</summary>
public partial class NewsView : UserControl
{
    public NewsView() => InitializeComponent();
}
