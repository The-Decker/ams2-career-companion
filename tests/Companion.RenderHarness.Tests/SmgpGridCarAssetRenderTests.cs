using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Companion.App.Converters;

namespace Companion.RenderHarness.Tests;

/// <summary>Guards the complete portrait-format overhead-car roster consumed by StartingGridView.</summary>
public sealed class SmgpGridCarAssetRenderTests
{
    private static readonly string[] DriverIds =
    [
        "driver.alain_asselin", "driver.alef_delvaux", "driver.alex_picos", "driver.ayrton_senna",
        "driver.bernie_miller", "driver.bruno_salgado", "driver.christopher_tegner", "driver.eddie_bellini",
        "driver.eric_sambena", "driver.esteban_pacheco", "driver.ethan_tornio", "driver.felipe_elssler",
        "driver.george_turner", "driver.gilberto_ceara", "driver.gilles_gould", "driver.giorgio_alberti",
        "driver.ivanazzio_germi", "driver.jean_herbin", "driver.jean_rampal", "driver.julianno_nono",
        "driver.keke_alfven", "driver.kevin_yepes", "driver.luca_dufay", "driver.marcel_moreau",
        "driver.michael_blume", "driver.mika_larssen", "driver.miyagi_hamano", "driver.nigel_jones",
        "driver.park_arai", "driver.paul_klinger", "driver.paul_white", "driver.ryan_cotman",
        "driver.tristan_chardin", "driver.willian_dehehe",
    ];

    [Fact]
    public void SmgpGridCars_CoverTheCompleteRoster_WithCanonicalTransparentSprites()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            Assert.Equal(34, DriverIds.Length);
            var converter = new KeyedAssetImageConverter();

            foreach (string driverId in DriverIds)
            {
                var image = Assert.IsAssignableFrom<BitmapSource>(converter.Convert(
                    driverId, typeof(ImageSource), "smgp/grid-cars|cars", CultureInfo.InvariantCulture));
                Assert.True(image.IsFrozen);
                Assert.Equal(384, image.PixelWidth);
                Assert.Equal(512, image.PixelHeight);

                var bgra = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
                int stride = bgra.PixelWidth * 4;
                byte[] pixels = new byte[stride * bgra.PixelHeight];
                bgra.CopyPixels(pixels, stride, 0);
                Assert.Equal(0, AlphaAt(pixels, stride, 0, 0));
                Assert.Equal(0, AlphaAt(pixels, stride, bgra.PixelWidth - 1, 0));
                Assert.Equal(0, AlphaAt(pixels, stride, 0, bgra.PixelHeight - 1));
                Assert.Equal(0, AlphaAt(pixels, stride, bgra.PixelWidth - 1, bgra.PixelHeight - 1));
                Assert.True(pixels.Where((_, index) => index % 4 == 3).Any(alpha => alpha > 0),
                    $"Expected visible car pixels for {driverId}.");
            }
        });
    }

    [Theory]
    [InlineData("driver.paul_white", "driver.jean_herbin")]       // Blanche
    [InlineData("driver.willian_dehehe", "driver.esteban_pacheco")] // Losel
    [InlineData("driver.julianno_nono", "driver.bernie_miller")]    // Minarae
    [InlineData("driver.tristan_chardin", "driver.ryan_cotman")]    // Rigel
    public void CorrectedFixedLiveryPairs_ShareTheirCanonicalTeamCar(
        string correctedDriverId, string canonicalTeamMateId)
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var converter = new KeyedAssetImageConverter();
            var corrected = Assert.IsAssignableFrom<BitmapSource>(converter.Convert(
                correctedDriverId, typeof(ImageSource), "smgp/grid-cars|cars", CultureInfo.InvariantCulture));
            var canonical = Assert.IsAssignableFrom<BitmapSource>(converter.Convert(
                canonicalTeamMateId, typeof(ImageSource), "smgp/grid-cars|cars", CultureInfo.InvariantCulture));

            Assert.Equal(canonical.PixelWidth, corrected.PixelWidth);
            Assert.Equal(canonical.PixelHeight, corrected.PixelHeight);
            Assert.Equal(BgraPixels(canonical), BgraPixels(corrected));
        });
    }

    private static byte[] BgraPixels(BitmapSource source)
    {
        var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int stride = bgra.PixelWidth * 4;
        byte[] pixels = new byte[stride * bgra.PixelHeight];
        bgra.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    private static byte AlphaAt(byte[] pixels, int stride, int x, int y) =>
        pixels[y * stride + x * 4 + 3];
}
