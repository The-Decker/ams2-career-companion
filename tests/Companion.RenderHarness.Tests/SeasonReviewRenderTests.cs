using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Companion.App.Views;

namespace Companion.RenderHarness.Tests;

/// <summary>Render coverage for the SMGP300 season-review states that must remain actionable:
/// offer selection, a large development bank before signing, and a terminal archive.</summary>
public sealed class SeasonReviewRenderTests
{
    private sealed class ReviewHost(bool hasNextSeason, bool hasOffers, bool hasCharacter, int availableCp)
    {
        public string Title => "Season 10 review";
        public string PlayerSummaryText => "P2 in the championship - 4 wins - 72 points";
        public bool HasOffers => hasOffers;
        public IReadOnlyList<OfferHost> Offers => hasOffers
            ?
            [
                new("#D2A63C", "MADONNA MOTOR SPORT", "FAX", "Factory seat for the next campaign."),
                new("#6BA8D9", "BULLETS RACING", "TELEX", "Lead our return to the front."),
            ]
            : [];
        public ICommand AcceptOfferCommand { get; } = new StubCommand();
        public string? OfferError => null;
        public bool HasCharacter => hasCharacter;
        public int AvailableCp => availableCp;
        public bool HasCp => availableCp > 0;
        public IReadOnlyList<object> DevelopmentStats => [];
        public IReadOnlyList<object> DevelopmentPerks => [];
        public bool HasPurchasablePerks => false;
        public ICommand RaiseStatCommand { get; } = new StubCommand();
        public ICommand BuyPerkCommand { get; } = new StubCommand();
        public bool HasNextSeason => hasNextSeason;
        public string EraTransitionText => hasNextSeason
            ? "The next summer is waiting."
            : "This season is complete.";
        public bool HasBridgeNote => false;
        public string BridgeNote => "";
        public string SignButtonText => "SIGN AND CONTINUE";
        public ICommand SignAndContinueCommand { get; } = new StubCommand();
        public bool OfferAccepted => false;
        public string? TransitionError => null;
        public bool CanRestoreAiFile => false;
        public string? RestoreBanner => null;
        public bool RestoreSucceeded => false;
        public bool HasHeadlines => false;
        public IReadOnlyList<object> Headlines => [];
        public object? FinalStandings => null;
    }

    private sealed record OfferHost(
        string AccentHex,
        string Letterhead,
        string MediumLabel,
        string BodyText)
    {
        public bool IsAccepted => false;
        public string Dateline => "MONZA - OCTOBER 1999";
        public string DocumentFontStack => "Segoe UI";
        public string TierText => "WORKS";
        public string SalaryText => "TIER A";
    }

    private sealed class StubCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) { }
    }

    [Fact]
    public void SeasonReview_RendersOfferLettersWhenAnotherSeasonIsAvailable()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var view = new SeasonReviewView
            {
                DataContext = new ReviewHost(
                    hasNextSeason: true,
                    hasOffers: true,
                    hasCharacter: true,
                    availableCp: 12),
            };
            Arrange(view);

            Assert.Equal(Visibility.Visible,
                Assert.IsType<Border>(view.FindName("OfferLettersPanel")).Visibility);
            Assert.Equal(2,
                Assert.IsType<ItemsControl>(view.FindName("OfferLetters")).Items.Count);
            Assert.Equal(Visibility.Visible,
                Assert.IsType<Button>(view.FindName("SignAndContinueButton")).Visibility);
        });
    }

    [Fact]
    public void SeasonReview_FinalSeasonHoldKeepsTheFullDevelopmentBankVisible()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var view = new SeasonReviewView
            {
                DataContext = new ReviewHost(
                    hasNextSeason: true,
                    hasOffers: false,
                    hasCharacter: true,
                    availableCp: 499),
            };
            Arrange(view);

            Assert.Equal(Visibility.Visible,
                Assert.IsType<Border>(view.FindName("DevelopmentPanel")).Visibility);
            Assert.Contains(Descendants<TextBlock>(view),
                block => InlineText(block).Contains("499 points to spend", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void SeasonReview_TerminalCampaignHidesDeadEndActionsAndKeepsTheArchiveMessage()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var view = new SeasonReviewView
            {
                DataContext = new ReviewHost(
                    hasNextSeason: false,
                    hasOffers: true,
                    hasCharacter: true,
                    availableCp: 499),
            };
            Arrange(view);

            Assert.Equal(Visibility.Collapsed,
                Assert.IsType<Border>(view.FindName("OfferLettersPanel")).Visibility);
            Assert.Equal(Visibility.Collapsed,
                Assert.IsType<Border>(view.FindName("DevelopmentPanel")).Visibility);
            Assert.Equal(Visibility.Collapsed,
                Assert.IsType<Button>(view.FindName("SignAndContinueButton")).Visibility);
            Assert.Equal("This season is complete.",
                Assert.IsType<TextBlock>(view.FindName("TransitionText")).Text);
        });
    }

    private static void Arrange(FrameworkElement view)
    {
        view.Measure(new Size(1200, 1400));
        view.Arrange(new Rect(0, 0, 1200, 1400));
        view.UpdateLayout();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                yield return match;
            foreach (T descendant in Descendants<T>(child))
                yield return descendant;
        }
    }

    private static string InlineText(TextBlock block) =>
        block.Text + string.Concat(block.Inlines.OfType<Run>().Select(run => run.Text));
}
