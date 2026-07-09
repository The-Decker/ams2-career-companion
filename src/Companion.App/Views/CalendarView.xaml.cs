using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Companion.ViewModels.Hub;

namespace Companion.App.Views;

/// <summary>The Calendar tab: the season's full track schedule up front — real venue, the actual AMS2
/// track driven, and a badge for real venue / base stand-in / applied mod alternate. Each card expands
/// to the ORIGINAL circuit (map, facts, history) + an optional venue photo; clicking the photo opens a
/// resizable full-size window. Bindings to <see cref="CalendarViewModel"/>; the only code-behind is the
/// photo pop-out (a pure view concern).</summary>
public partial class CalendarView : UserControl
{
    public CalendarView()
    {
        InitializeComponent();
    }

    private void OnVenuePhotoClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Image { Source: { } source } image)
            return;
        string title = image.DataContext is CalendarRoundViewModel round ? round.RealVenue : "Photo";
        new PhotoWindow(source, title) { Owner = Window.GetWindow(this) }.Show();
    }
}
