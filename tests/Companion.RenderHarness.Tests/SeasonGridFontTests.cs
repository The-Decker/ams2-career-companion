using System.Windows;
using System.Windows.Media;

namespace Companion.RenderHarness.Tests;

/// <summary>
/// The app bundles several faces (Mike's picks): "Retro Floral" is the app-wide regular/body font
/// (every window's base FontFamily, via the BodyFont resource), "Microsport" draws the Season's
/// Grid card names, and "Open Sans" / "Roboto" (Apache 2.0) are the clean sans faces for readable
/// screens (the character screen uses Open Sans). WPF silently falls back to Segoe UI when a
/// <c>/Fonts/#Family</c> reference does not resolve
/// — a dropped Resource, a renamed file, or a family-name drift would go UNNOTICED by a plain render
/// test. This guard proves each face is embedded in the app assembly AND that the theme references it
/// by its true family name, so neither can silently regress to the fallback.
///
/// (We assert against the EMBEDDED resource via its full pack URI, which is what the real app's
/// startup base-URI expands <c>/Fonts/#Family</c> to. Building a glyph typeface from a theme-loaded
/// FontFamily's RELATIVE source needs the app's startup base URI, which the off-screen harness does
/// not establish — even the shipping Race Sport face fails that path here — so it cannot distinguish
/// a real break from the harness limitation. The embedded-resource load can.)
/// </summary>
public sealed class SeasonGridFontTests
{
    [Theory]
    [InlineData("BodyFont", "Retro Floral.ttf", "Retro Floral")]        // app-wide body text
    [InlineData("MicrosportFont", "Microsport Bold.ttf", "Microsport")] // Season's Grid card names
    [InlineData("OpenSansFont", "OpenSans-Regular.ttf", "Open Sans")]   // character screen
    [InlineData("RobotoFont", "Roboto-Regular.ttf", "Roboto")]          // bundled, available
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
                    $"{fileName} did not load as an embedded resource — check the " +
                    $"<Resource Include=\"Fonts\\{fileName}\" /> in Companion.App.csproj. " + ex.Message);
                return;
            }

            // The embedded face's family name must be exactly what the theme references after '#',
            // else /Fonts/#<family> binds nothing and the text falls back to Segoe UI.
            Assert.Contains(family, glyph.Win32FamilyNames.Values);

            var themeFamily = (FontFamily)Application.Current.Resources[resourceKey];
            Assert.Equal("/Fonts/#" + family, themeFamily.Source);
        });
    }
}
