using System.Globalization;
using System.IO;
using System.Security.Cryptography;
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
            string bannerRoot = Path.Combine(
                AppContext.BaseDirectory,
                "data",
                "ams2",
                "smgp",
                "banners");
            string[] expectedFiles = TeamIds
                .Select(teamId => $"{teamId}.png")
                .Order(StringComparer.Ordinal)
                .ToArray();
            string[] actualFiles = Directory
                .EnumerateFiles(bannerRoot, "*.png", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .OfType<string>()
                .Order(StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(expectedFiles, actualFiles);

            var converter = new KeyedAssetImageConverter();
            var uniqueHashes = new HashSet<string>(StringComparer.Ordinal);

            foreach (string teamId in TeamIds)
            {
                string bannerPath = Path.Combine(bannerRoot, $"{teamId}.png");
                var file = new FileInfo(bannerPath);
                Assert.True(file.Length > 0, $"{file.Name} is empty.");
                string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(bannerPath)));
                Assert.True(uniqueHashes.Add(hash), $"{file.Name} duplicates another SMGP headquarters banner.");

                var image = Assert.IsAssignableFrom<BitmapSource>(converter.Convert(
                    teamId,
                    typeof(ImageSource),
                    "smgp/banners",
                    CultureInfo.InvariantCulture));

                Assert.True(image.IsFrozen);
                Assert.Equal(1040, image.PixelWidth);
                Assert.Equal(200, image.PixelHeight);

                var bgra = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
                int stride = bgra.PixelWidth * 4;
                var pixels = new byte[stride * bgra.PixelHeight];
                bgra.CopyPixels(pixels, stride, 0);
                for (int alphaIndex = 3; alphaIndex < pixels.Length; alphaIndex += 4)
                    Assert.Equal(byte.MaxValue, pixels[alphaIndex]);
            }

            Assert.Equal(TeamIds.Length, uniqueHashes.Count);
        });
    }
}
