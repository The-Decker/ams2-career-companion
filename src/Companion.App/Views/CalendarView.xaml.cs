using System.Windows.Controls;

namespace Companion.App.Views;

/// <summary>The Calendar tab: the season's full track schedule up front — real venue, the actual AMS2
/// track driven, and a badge for real venue / base stand-in / applied mod alternate. Pure bindings to
/// <see cref="Companion.ViewModels.Hub.CalendarViewModel"/>; no code-behind beyond InitializeComponent.</summary>
public partial class CalendarView : UserControl
{
    public CalendarView()
    {
        InitializeComponent();
    }
}
