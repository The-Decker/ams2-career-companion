using System.Text.RegularExpressions;
using Companion.Core.Career;

namespace Companion.Tests.Career;

/// <summary>The era-skin resolver (career-hub-design.md §6): a pure year → medium mapping that
/// makes 1967 resolve differently from 1988. Deterministic, no I/O.</summary>
public sealed class EraThemesTests
{
    [Theory]
    [InlineData(1967, EraMedium.Telegram)]
    [InlineData(1974, EraMedium.Telegram)]
    [InlineData(1979, EraMedium.Telegram)]
    [InlineData(1980, EraMedium.Fax)]
    [InlineData(1988, EraMedium.Fax)]
    [InlineData(1993, EraMedium.Fax)]
    [InlineData(1994, EraMedium.Email)]
    [InlineData(2000, EraMedium.Email)]
    [InlineData(2022, EraMedium.Email)]
    public void ForYear_maps_the_year_to_its_medium(int year, EraMedium expected) =>
        Assert.Equal(expected, EraThemes.ForYear(year).Medium);

    [Fact]
    public void ForYear_is_monotonic_across_a_long_career()
    {
        EraMedium previous = EraMedium.Telegram;
        for (int year = 1960; year <= 2025; year++)
        {
            var medium = EraThemes.ForYear(year).Medium;
            Assert.True((int)medium >= (int)previous, $"era went backwards at {year}");
            previous = medium;
        }
    }

    [Fact]
    public void Every_theme_is_well_formed()
    {
        foreach (var theme in new[] { EraThemes.Telegram, EraThemes.Fax, EraThemes.Email })
        {
            Assert.False(string.IsNullOrWhiteSpace(theme.Label));
            Assert.False(string.IsNullOrWhiteSpace(theme.DocumentFontStack));
            Assert.Matches(new Regex("^#[0-9A-Fa-f]{6}$"), theme.AccentHex);
        }

        // The telegram era carries its signature dateline flourish; later media drop it.
        Assert.Equal("STOP", EraThemes.Telegram.DatelineFlourish);
        Assert.Equal("", EraThemes.Email.DatelineFlourish);
    }
}
