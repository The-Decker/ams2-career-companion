using System.Windows.Controls;

namespace Companion.App.Views;

/// <summary>The SMGP promotion / demotion screen (3c-3), its own full-immersion step after the
/// confirm. DataContext is a PromotionViewModel; its Model carries the new team's photo, story and
/// car, and its Accept/Decline commands resolve the offer (or acknowledge a drop) and advance.</summary>
public partial class PromotionView : UserControl
{
    public PromotionView() => InitializeComponent();
}
