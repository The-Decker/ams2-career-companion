using System.Windows;
using System.Windows.Media;

namespace Companion.RenderHarness.Tests;

/// <summary>
/// The Season's Grid card labels are drawn in the bundled "Aeromove" display face (Mike's pick,
/// replacing the earlier Victory Striker that read too skinny). WPF silently falls back to Segoe UI
/// when a <c>/Fonts/#Family</c> reference does not resolve — a dropped Resource, a renamed file, or
/// a family-name drift would go UNNOTICED by a plain render test. This guard proves the font is
/// embedded in the app assembly AND that the theme references it by its true family name, so the
/// swap can never silently regress to the fallback.
///
/// (We assert against the EMBEDDED resource via its full pack URI, which is what the real app's
/// startup base-URI expands <c>/Fonts/#AeromoveDemo</c> to. Building a glyph typeface from a
/// theme-loaded FontFamily's RELATIVE source needs the app's startup base URI, which the off-screen
/// harness does not establish — the known-good Race Sport face fails that path here too — so it
/// cannot distinguish a real break from the harness limitation. The embedded-resource load can.)
/// </summary>
public sealed class SeasonGridFontTests
{
    private const string Family = "AeromoveDemo"; // the name the theme references after '#'

    [Fact]
    public void AeromoveFont_IsEmbedded_AndTheThemeReferenceMatchesItsFamilyName()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            GlyphTypeface glyph;
            try
            {
                glyph = new GlyphTypeface(new Uri(
                    "pack://application:,,,/AMS2CareerCompanion;component/Fonts/AeromoveDemoRegular.ttf"));
            }
            catch (Exception ex)
            {
                Assert.Fail(
                    "AeromoveDemoRegular.ttf did not load as an embedded resource — check the " +
                    "<Resource Include=\"Fonts\\AeromoveDemoRegular.ttf\" /> in Companion.App.csproj. " + ex.Message);
                return;
            }

            // The embedded face's family name must be exactly what the theme references after '#',
            // else /Fonts/#AeromoveDemo binds nothing and the cards fall back to Segoe UI.
            Assert.Contains(Family, glyph.Win32FamilyNames.Values);

            var themeFamily = (FontFamily)Application.Current.Resources["AeromoveFont"];
            Assert.Equal("/Fonts/#" + Family, themeFamily.Source);
        });
    }
}
