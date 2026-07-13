using System.Windows;
using Companion.App.Views;
using Companion.ViewModels.Services;
using Companion.ViewModels.Shell;

namespace Companion.RenderHarness.Tests;

/// <summary>Off-screen render of the 17-season SMGP campaign FINALE (Mike's "final final screen") over a
/// real <see cref="SmgpFinaleViewModel"/> — exercises the full-immersion layout (the flawless ribbon,
/// the triumphant headline, the secret-hero placeholder, the dedication line, the record) end to end,
/// catching a broken binding or missing brush/converter resource. Both the survivor (special.jpg) and
/// the flawless (ultimate.jpg) variants render. Self-skips off Windows.</summary>
public sealed class SmgpFinaleRenderTests
{
    private static SmgpFinaleViewModel Finale(bool flawless) =>
        new(
            new SmgpFinaleModel
            {
                Headline = flawless ? "THE FLAWLESS EMPEROR" : "SEVENTEEN SEASONS CONQUERED",
                Subhead = flawless
                    ? "Champion of every season across the whole SEGA world."
                    : "You went the distance — all seventeen seasons survived.",
                IsFlawless = flawless,
                HeroImageKey = flawless ? "ultimate" : "special",
                Record =
                [
                    "17 SEASONS CONQUERED",
                    flawless ? "17 CHAMPIONSHIPS" : "6 CHAMPIONSHIPS",
                ],
            },
            onContinue: () => { });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SmgpFinaleView_RendersOverItsViewModel(bool flawless)
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var view = new SmgpFinaleView { DataContext = Finale(flawless) };
            view.Measure(new Size(900, 900));
            view.Arrange(new Rect(0, 0, 900, 900));
            view.UpdateLayout();

            Assert.True(view.ActualWidth > 0);
            Assert.True(view.ActualHeight > 0);
        });
    }
}
