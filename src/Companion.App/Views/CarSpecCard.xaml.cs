using System.Windows.Controls;

namespace Companion.App.Views;

/// <summary>The reusable arcade car-spec card (machine/engine/power + ENG-TM-SUS-TIRE-BRA bars),
/// hosted on the rival dossier and the character screen. DataContext is a CarSpecCardViewModel.</summary>
public partial class CarSpecCard : UserControl
{
    public CarSpecCard() => InitializeComponent();
}
