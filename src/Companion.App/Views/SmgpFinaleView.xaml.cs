using System.Windows.Controls;

namespace Companion.App.Views;

/// <summary>The 17-season SMGP campaign FINALE (Mike's "final final screen"), shown once at the fold
/// that completes the campaign, built around the secret special.jpg / ultimate.jpg. DataContext is an
/// SmgpFinaleViewModel; its Continue command advances into the season review.</summary>
public partial class SmgpFinaleView : UserControl
{
    public SmgpFinaleView() => InitializeComponent();
}
