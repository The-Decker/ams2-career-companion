using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Companion.App.Converters;

namespace Companion.RenderHarness.Tests;

/// <summary>
/// Guards the permanent rival-screen banner contract. RivalScreenView resolves the selected team's
/// exact team id through <c>smgp/banners</c>; a missing key otherwise degrades into an empty hero.
/// </summary>
public sealed class SmgpRivalBannerAssetRenderTests
{
    private static readonly string[] TeamIds =
    [
        "team.azalea", "team.bestowal", "team.blanche", "team.bullets",
        "team.comet", "team.cool", "team.dardan", "team.feet",
        "team.firenze", "team.iris", "team.joke", "team.lares",
        "team.linden", "team.losel", "team.madonna", "team.may",
        "team.millions", "team.minarae", "team.moon", "team.orchis",
        "team.rigel", "team.serga", "team.tyrant", "team.zeroforce",
    ];

    [Fact]
    public void SmgpRivalBanners_CoverEveryTeam_AtTheAuthoredWindowAspect()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            Assert.Equal(24, TeamIds.Length);
            var converter = new KeyedAssetImageConverter();

            foreach (string teamId in TeamIds)
            {
                var image = Assert.IsAssignableFrom<BitmapSource>(converter.Convert(
                    teamId,
                    typeof(ImageSource),
                    "smgp/banners",
                    CultureInfo.InvariantCulture));

                Assert.True(image.IsFrozen);
                Assert.Equal(1040, image.PixelWidth);
                Assert.Equal(200, image.PixelHeight);
            }
        });
    }
}
