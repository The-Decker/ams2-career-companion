using Companion.Core.Career;
using Companion.ViewModels.Hub;
using Companion.ViewModels.Review;
using Companion.ViewModels.Services;
using Companion.ViewModels.Shell;

namespace Companion.Tests.ViewModels;

/// <summary>The era-skin bind contract (docs/dev/era-theming-assets-brief.md, Slice 0): every
/// era-flavored surface exposes the shared <see cref="IEraSkin"/> as a FIRST-CLASS property plus
/// the medium flattened to a top-level bindable for WPF DataTrigger keying, on the Hub, the
/// career gallery cards, the News and History lenses, and the offer letters. Resolution runs
/// through the session's <c>era-themes.json</c> override catalog when one covers the year's
/// medium, with the built-in table as the fallback.</summary>
public sealed class EraSkinBindContractTests
{
    [Fact]
    public void Hub_exposes_the_era_skin_first_class_and_flattened()
    {
        using var hub = new HubViewModel(new FakeCareerSession());

        Assert.NotNull(hub.EraSkin);
        Assert.Same(hub.Era, hub.EraSkin); // one skin, two typings, no divergence possible
        Assert.Equal(hub.Era.Medium, hub.EraMedium);
        Assert.Equal(EraMedium.Telegram, hub.EraMedium); // the fake pack is a 1967 season
        Assert.Equal("ochre-wire", hub.EraSkin.PaperTextureKey);
    }

    [Fact]
    public void Hub_resolves_the_skin_through_the_session_override_catalog()
    {
        var session = new FakeCareerSession
        {
            EraOverrides = EraThemeCatalog.Parse(
                """{ "1960": { "medium": "telegram", "accent": "#111111" } }"""),
        };
        using var hub = new HubViewModel(session);

        Assert.Equal("#111111", hub.EraSkin.AccentHex); // the override field
        Assert.Equal("ochre-wire", hub.EraSkin.PaperTextureKey); // inherited from the telegram base
        Assert.Equal(EraMedium.Telegram, hub.EraMedium);
    }

    [Fact]
    public void News_exposes_the_era_skin_for_the_careers_year()
    {
        var news = new NewsViewModel(new FakeCareerSession());

        Assert.NotNull(news.EraSkin);
        Assert.Equal(EraMedium.Telegram, news.EraMedium);
        Assert.Equal(news.EraSkin.Medium, news.EraMedium);
    }

    [Fact]
    public void News_resolves_the_skin_through_the_session_override_catalog()
    {
        var session = new FakeCareerSession
        {
            EraOverrides = EraThemeCatalog.Parse(
                """{ "1960": { "medium": "telegram", "paperTexture": "custom-wire" } }"""),
        };
        var news = new NewsViewModel(session);

        Assert.Equal("custom-wire", news.EraSkin.PaperTextureKey);
    }

    [Fact]
    public void History_exposes_the_era_skin_for_the_careers_year()
    {
        var history = new HistoryViewModel(new FakeCareerSession());

        Assert.NotNull(history.EraSkin);
        Assert.Equal(EraMedium.Telegram, history.EraMedium);
        Assert.Equal(history.EraSkin.Medium, history.EraMedium);
    }

    [Fact]
    public void History_resolves_the_skin_through_the_session_override_catalog()
    {
        var session = new FakeCareerSession
        {
            EraOverrides = EraThemeCatalog.Parse(
                """{ "1960": { "medium": "telegram", "accent": "#222222" } }"""),
        };
        var history = new HistoryViewModel(session);

        Assert.Equal("#222222", history.EraSkin.AccentHex);
    }

    [Fact]
    public void An_offer_letter_exposes_its_documents_skin_first_class()
    {
        var offer = new SeasonOfferModel
        {
            TeamId = "team.x",
            TeamName = "Xenon",
            Tier = 3,
            SalaryBu = 12.5,
            Score = 0.5,
            Accepted = false,
        };
        var document = OfferDocument.Compose(1988, offer.TeamName, offer.Tier, offer.SalaryBu, "Pat");

        var letter = new OfferLetterViewModel(offer, document);

        Assert.Same(document.Era, letter.EraSkin);
        Assert.Equal(EraMedium.Fax, letter.EraMedium); // 1988 is a fax year
        Assert.Equal("thermal-grain", letter.EraSkin.PaperTextureKey);
    }

    [Fact]
    public void Offer_compose_uses_the_override_catalog_when_it_covers_the_years_medium()
    {
        var overrides = EraThemeCatalog.Parse(
            """{ "1980": { "medium": "fax", "accent": "#333333" } }""");

        var document = OfferDocument.Compose(1988, "Xenon", 3, 10, "Pat", overrides);

        Assert.Equal("#333333", document.Era.AccentHex);
        Assert.Equal(EraMedium.Fax, document.Era.Medium);
    }

    [Fact]
    public void Offer_compose_without_overrides_is_byte_for_byte_the_builtin_skin()
    {
        var document = OfferDocument.Compose(1967, "Xenon", 3, 10, "Pat");

        Assert.Equal(EraThemes.Telegram, document.Era);
    }

    [Theory]
    [InlineData(1967, EraMedium.Telegram)]
    [InlineData(1988, EraMedium.Fax)]
    [InlineData(2019, EraMedium.Email)]
    public void A_gallery_card_exposes_the_era_skin_for_its_year(int year, EraMedium expected)
    {
        var entry = new RecentCareer
        {
            Path = @"C:\careers\x.ams2career",
            CareerName = $"Season {year}",
            LastOpenedUtc = DateTimeOffset.UnixEpoch,
            SeasonYear = year,
        };

        Assert.NotNull(entry.EraSkin);
        Assert.Equal(expected, entry.EraSkin.Medium);
        Assert.Equal(expected, entry.EraMedium);
        Assert.False(string.IsNullOrWhiteSpace(entry.EraSkin.Label));
    }

    [Fact]
    public void A_gallery_card_falls_back_to_parsing_the_year_out_of_its_name()
    {
        var entry = new RecentCareer
        {
            Path = @"C:\careers\x.ams2career",
            CareerName = "My 1988 season",
            LastOpenedUtc = DateTimeOffset.UnixEpoch,
            SeasonYear = 0, // a legacy entry persisted before the stored year existed
        };

        Assert.Equal(EraMedium.Fax, entry.EraMedium);
    }

    [Fact]
    public void A_gallery_card_with_no_known_year_has_a_neutral_skin()
    {
        var entry = new RecentCareer
        {
            Path = @"C:\careers\x.ams2career",
            CareerName = "My Career",
            LastOpenedUtc = DateTimeOffset.UnixEpoch,
        };

        Assert.Null(entry.EraSkin);
        Assert.Null(entry.EraMedium);
    }

    // ---------- Workstream B: the shell's era-skin push token (ActiveCareerEraMedium) ----------

    [Fact]
    public void The_shell_token_is_neutral_until_a_career_is_on_screen()
    {
        using var shell = CreateShell();

        Assert.Null(shell.ActiveCareerEraMedium); // the start gallery is era-neutral
    }

    [Fact]
    public void The_shell_token_follows_the_open_career_and_clears_on_close()
    {
        using var shell = CreateShell();
        var pushes = new List<EraMedium?>();
        shell.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.ActiveCareerEraMedium))
                pushes.Add(shell.ActiveCareerEraMedium);
        };

        string path = Path.Combine(Path.GetTempPath(), $"era-skin-{Guid.NewGuid():N}.ams2career");
        File.WriteAllText(path, "x");
        try
        {
            shell.Start.OpenCareerCommand.Execute(path);

            Assert.IsType<HubViewModel>(shell.Current);
            Assert.Equal(EraMedium.Telegram, shell.ActiveCareerEraMedium); // the fake pack is 1967
            Assert.Contains(EraMedium.Telegram, pushes); // the push fired for the career open

            shell.GoToStartCommand.Execute(null);

            Assert.Null(shell.ActiveCareerEraMedium);
            Assert.Null(pushes[^1]); // ... and again (neutral) for the close
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void The_shell_token_goes_neutral_over_a_menu_overlay_and_returns_with_the_career()
    {
        using var shell = CreateShell();
        string path = Path.Combine(Path.GetTempPath(), $"era-skin-{Guid.NewGuid():N}.ams2career");
        File.WriteAllText(path, "x");
        try
        {
            shell.Start.OpenCareerCommand.Execute(path);
            Assert.Equal(EraMedium.Telegram, shell.ActiveCareerEraMedium);

            shell.ToggleSettingsCommand.Execute(null); // a menu overlay over the career
            Assert.Null(shell.ActiveCareerEraMedium);

            shell.ToggleSettingsCommand.Execute(null); // back to the career
            Assert.Equal(EraMedium.Telegram, shell.ActiveCareerEraMedium);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static ShellViewModel CreateShell()
    {
        var environment = new CareerEnvironment
        {
            ContentLibrary = TestPackBuilder.Library(),
            LocateInstall = static () => null,
            DocumentsDirectory = Path.GetTempPath(),
        };
        return new ShellViewModel(environment, new EraSkinFakeFactory(), new EraSkinFakeStore());
    }

    private sealed class EraSkinFakeFactory : ICareerFactory
    {
        public ICareerSession Create(CareerCreationRequest request) => new FakeCareerSession();

        public ICareerSession Open(string careerFilePath) => new FakeCareerSession();
    }

    private sealed class EraSkinFakeStore : IRecentCareersStore
    {
        public IReadOnlyList<RecentCareer> Load() => [];

        public void Touch(string path, string careerName, int seasonYear = 0, string? careerStyle = null)
        {
        }

        public void Remove(string path)
        {
        }
    }
}
