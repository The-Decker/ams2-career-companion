using System.Windows;
using System.Windows.Media;

namespace Companion.RenderHarness.Tests;

/// <summary>
/// WPF silently falls back to a system font when a <c>/Fonts/#Family</c> reference is wrong.
/// These guards prove that every face in the app's type-system contract is embedded in the app
/// assembly and that Theme.xaml names the face by its real internal family name.
/// </summary>
public sealed class SeasonGridFontTests
{
    [Theory]
    [InlineData("DisplayFont", "Orbitron-Bold.ttf", "Orbitron")]
    [InlineData("BodyFont", "Inter-Regular.ttf", "Inter")]
    [InlineData("PixelFont", "PressStart2P-Regular.ttf", "Press Start 2P")]
    [InlineData("MonoFont", "JetBrainsMono-Regular.ttf", "JetBrains Mono")]
    [InlineData("AltDisplayFont", "ChakraPetch-Bold.ttf", "Chakra Petch")]
    [InlineData("AltBodyFont", "Saira-Regular.ttf", "Saira")]
    [InlineData("AltPixelFont", "Silkscreen-Regular.ttf", "Silkscreen")]
    public void BundledFont_IsEmbedded_AndTheThemeReferenceMatchesItsFamilyName(
        string resourceKey, string fileName, string family)
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            GlyphTypeface glyph;
            try
            {
                glyph = new GlyphTypeface(new Uri(
                    "pack://application:,,,/AMS2CareerCompanion;component/Fonts/" + fileName));
            }
            catch (Exception ex)
            {
                Assert.Fail(
                    $"{fileName} did not load as an embedded resource - check the " +
                    $"<Resource Include=\"Fonts\\{fileName}\" /> in Companion.App.csproj. " + ex.Message);
                return;
            }

            Assert.Contains(family, glyph.Win32FamilyNames.Values);

            var themeFamily = Assert.IsType<FontFamily>(Application.Current.Resources[resourceKey]);
            Assert.Equal("/Fonts/#" + family, themeFamily.Source);
        });
    }
}
