using System.Windows;
using Companion.App.Views;
using Companion.ViewModels.Services;
using Companion.ViewModels.Shell;

namespace Companion.RenderHarness.Tests;

/// <summary>Off-screen render of the SMGP promotion / demotion screen (3c-3) over a real
/// <see cref="PromotionViewModel"/>, exercises the full-immersion layout (headline, big team photo
/// placeholder, player image + car, the ~5-paragraph history and quotes, and the accept/decline
/// buttons) end to end, catching a broken binding or missing resource. Self-skips off Windows.</summary>
public sealed class PromotionRenderTests
{
    private static PromotionViewModel Promotion(SmgpPromotionKind kind) =>
        new(
            new SmgpPromotionModel
            {
                Kind = kind,
                Headline = kind == SmgpPromotionKind.PromotionOffer
                    ? "AN OFFER FROM MADONNA"
                    : "RELEGATED TO ZEROFORCE",
                TeamName = kind == SmgpPromotionKind.PromotionOffer ? "Madonna" : "Zeroforce",
                TeamPhotoKey = kind == SmgpPromotionKind.PromotionOffer ? "madonna" : "zeroforce",
                PlayerImageKey = kind == SmgpPromotionKind.PromotionOffer ? "player.madonna" : "player.zeroforce",
                CarKey = "driver.senna",
                Motto = "THE CROWN NEVER SLIPS",
                History =
                [
                    "Madonna have owned the front of the SMGP grid since the series began.",
                    "Their scarlet cars are the benchmark every rookie measures themselves against.",
                    "The garage speaks in whispers and wins in silence.",
                    "To wear the crown is to carry its weight, no season is a victory lap.",
                    "You are being handed the fastest car on the grid. Do not waste it.",
                ],
                Quotes =
                [
                    "\"We do not hope to win. We arrive to win.\", the principal",
                    "\"Beat him? You will BE him.\", the garage",
                ],
                RivalName = "Ayrton Senna",
            },
            onAccept: () => { },
            onDecline: () => { });

    [Theory]
    [InlineData(SmgpPromotionKind.PromotionOffer)]
    [InlineData(SmgpPromotionKind.Demotion)]
    public void PromotionView_RendersOverItsViewModel(SmgpPromotionKind kind)
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var view = new PromotionView { DataContext = Promotion(kind) };
            view.Measure(new Size(900, 900));
            view.Arrange(new Rect(0, 0, 900, 900));
            view.UpdateLayout();

            Assert.True(view.ActualWidth > 0);
            Assert.True(view.ActualHeight > 0);
        });
    }
}
