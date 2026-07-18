using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Companion.ViewModels.Services;

namespace Companion.App.Controls;

/// <summary>The campaign arc strip (codex-gui-smgp300-brief.md §4): one slot per season of the
/// whole campaign road, bound entirely in XAML to <see cref="ICareerSession.CampaignTimeline"/>
/// through the shared <c>CampaignTimeline</c> converter (session bridged from the Hub, or the
/// tagged tear-off Window). The only code-behind is the header: the slot count arrives as the
/// panel's DataContext, and the header counts real seasons only, never the synthetic ordinal-0
/// Formula Junior prologue slot that heads a Dynasty arc without being a season itself.</summary>
public partial class CampaignTimelineStrip : UserControl
{
    public CampaignTimelineStrip()
    {
        InitializeComponent();
        CampaignTimelinePanel.DataContextChanged += (_, args) => UpdateHeader(args.NewValue);
        UpdateHeader(CampaignTimelinePanel.DataContext);
    }

    private void UpdateHeader(object? timeline)
    {
        int seasons = timeline is IEnumerable<CampaignTimelineEntry> entries
            ? entries.Count(entry => !entry.IsPrologue)
            : 0;
        CampaignTimelineHeader.Text =
            string.Concat(seasons.ToString(CultureInfo.InvariantCulture), "-SEASON CAMPAIGN");
    }
}
