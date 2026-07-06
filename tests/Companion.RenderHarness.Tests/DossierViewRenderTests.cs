using System.Windows;
using Companion.App.Views;
using Companion.Core.Character;

namespace Companion.RenderHarness.Tests;

/// <summary>Off-screen render of the Driver dossier over a real <see cref="CharacterDossier"/> — the
/// view binds to a host's <c>Dossier</c> property, so a lightweight stand-in exercises the view layer
/// (stat bars, perk cards, the level progress bar) without a full session. Self-skips off Windows.</summary>
public sealed class DossierViewRenderTests
{
    /// <summary>The dossier view binds <c>{Binding Dossier}</c> on its DataContext — this stands in
    /// for the DossierViewModel.</summary>
    private sealed class DossierHost
    {
        public required CharacterDossier Dossier { get; init; }
    }

    private static CharacterDossier Dossier() => new()
    {
        Name = "Nova Reyes",
        Level = 3,
        Xp = 250,
        XpIntoLevel = 15,
        XpForNextLevel = 182,
        CpUnspent = 2,
        Stats =
        [
            new DossierStat("pace", "Pace", 0.70, Talent: true),
            new DossierStat("oneLap", "One-lap pace", 0.55, Talent: true),
            new DossierStat("marketability", "Marketability", 0.60, Talent: false),
        ],
        Perks =
        [
            new DossierPerk("rain_man", "Rain Man", "weather", "Untouchable in the wet, ordinary in the dry.", 1),
        ],
    };

    [Fact]
    public void DossierView_RendersOverACharacterDossier()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var view = new DossierView { DataContext = new DossierHost { Dossier = Dossier() } };
            view.Measure(new Size(900, 700));
            view.Arrange(new Rect(0, 0, 900, 700));
            view.UpdateLayout();

            Assert.True(view.ActualWidth > 0);
            Assert.True(view.ActualHeight > 0);
        });
    }
}
