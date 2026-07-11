using System.Windows.Controls;

namespace Companion.App.Views;

/// <summary>The Calendar tab: the whole season as a 4-in-a-row board — one compact card per round
/// with its circuit map, the real venue + the actual AMS2 track driven, a badge for real venue /
/// base stand-in / applied mod alternate, and era-capped fun facts. Results stay hidden until you
/// race (see History). Pure bindings to <see cref="Companion.ViewModels.Hub.CalendarViewModel"/>.</summary>
public partial class CalendarView : UserControl
{
    public CalendarView()
    {
        InitializeComponent();
    }
}
