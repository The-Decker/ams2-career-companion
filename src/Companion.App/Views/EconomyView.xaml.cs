using System.Windows.Controls;

namespace Companion.App.Views;

/// <summary>
/// The Dynasty owner-economy "Team Ledger" lens. Pure bindings over
/// <c>EconomyViewModel.Dashboard</c>, which is replaced wholesale on every refresh, so the view
/// keeps no transient state of its own (the hub snaps back to the Race tab after each Apply and
/// this lens must read cleanly on re-entry).
/// </summary>
public partial class EconomyView : UserControl
{
    public EconomyView() => InitializeComponent();
}
