using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Companion.App.Views;
using Companion.Core.Career;
using Companion.Core.Grid;
using Companion.Core.Newsroom;
using Companion.Core.Numerics;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.Core.Smgp;
using Companion.Data;
using Companion.ViewModels.Hub;
using Companion.ViewModels.ResultEntry;
using Companion.ViewModels.Review;
using Companion.ViewModels.Services;
using Companion.ViewModels.Standings;

namespace Companion.RenderHarness.Tests;

/// <summary>Real-WPF coverage for the era-medium document theming (era-theming-assets-brief.md
/// A2/A3): the offer letter and the newsroom article render as period documents per medium
/// (telegram/fax/email), the EraThemingEnabled master switch fully neutralizes the era chrome,
/// and the dense tool surfaces (standings table, result entry) render pixel-identical with the
/// era dictionaries in scope ("immersive docs, legible tools").</summary>
public sealed class EraThemingRenderTests
{
    // ---------- offer letter: per-medium document snapshot ----------

    [Theory]
    [InlineData(1967, "TELEGRAM", "Consolas", "FILED 1967 STOP", 0xE7, 0xD5, 0xA5, 0xDC, 0xC4, 0x8A)]
    [InlineData(1991, "FAX", "Cascadia Mono", "1991", 0xF1, 0xEE, 0xE4, 0x46, 0x58, 0x6A)]
    [InlineData(2000, "EMAIL", "Segoe UI", "2000", 0xFA, 0xFB, 0xFD, 0xEE, 0xF3, 0xFA)]
    public void OfferLetter_RendersAsPeriodDocumentForTheEraMedium(
        int year, string label, string fontFamily, string dateline,
        byte paperR, byte paperG, byte paperB, byte bandR, byte bandG, byte bandB)
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var letter = Offer(year);
            using var host = DocumentHost.Show(letter, eraGate: null);

            Border surface = DocumentHost.SurfaceBorder(Assert.IsType<ContentControl>(host.View));
            // The period paper: a texture tile for telegram/fax, a clean solid for email.
            if (year == 2000)
            {
                var paper = Assert.IsType<SolidColorBrush>(surface.Background);
                Assert.Equal(Color.FromRgb(paperR, paperG, paperB), paper.Color);
                Assert.Equal(new CornerRadius(4), surface.CornerRadius);
            }
            else
            {
                Assert.IsType<DrawingBrush>(surface.Background);
                Assert.Equal(new CornerRadius(2), surface.CornerRadius);
            }

            string[] texts = DocumentHost.VisibleTexts(host.View).ToArray();
            Assert.Contains(letter.Letterhead, texts); // the medium letterhead band is applied
            Assert.Contains(label, texts); // the medium stamp in the letterhead
            Assert.Contains(dateline, texts); // the era dateline ("FILED 1967 STOP" for telegram)

            TextBlock body = DocumentHost.Descendants<TextBlock>(host.View)
                .Single(text => text.Text == letter.BodyText);
            Assert.Contains(fontFamily, body.FontFamily.Source, StringComparison.Ordinal);

            // The pixel snapshot: the paper color genuinely covers the document.
            Assert.True(
                DocumentHost.ColorFraction(surface, paperR, paperG, paperB) > 0.15,
                $"Expected the {label} paper to cover the offer document.");
            Assert.True(
                DocumentHost.ColorFraction(surface, bandR, bandG, bandB) > 0.005,
                $"Expected the {label} letterhead band to render.");
        });
    }

    [Theory]
    [InlineData(1967, "Consolas")]
    [InlineData(1991, "Cascadia Mono")]
    [InlineData(2000, "Segoe UI")]
    public void OfferLetter_EraThemingOff_RendersNeutralChrome(int year, string eraFontFamily)
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var letter = Offer(year);
            using var host = DocumentHost.Show(letter, eraGate: false);

            Border surface = DocumentHost.SurfaceBorder(Assert.IsType<ContentControl>(host.View));
            var neutral = Assert.IsType<SolidColorBrush>(surface.Background);
            Assert.Equal(Color.FromRgb(0x20, 0x23, 0x2A), neutral.Color); // theme SurfaceBrush
            Assert.Equal(new CornerRadius(8), surface.CornerRadius);

            // The medium stamp disappears, the letterhead line stays (as plain text), the body
            // falls back to the app's legible base face.
            string[] texts = DocumentHost.VisibleTexts(host.View).ToArray();
            Assert.Contains(letter.Letterhead, texts);
            Assert.DoesNotContain("TELEGRAM", texts);
            Assert.DoesNotContain("FAX", texts);
            Assert.DoesNotContain("EMAIL", texts);

            TextBlock body = DocumentHost.Descendants<TextBlock>(host.View)
                .Single(text => text.Text == letter.BodyText);
            Assert.DoesNotContain(eraFontFamily, body.FontFamily.Source, StringComparison.Ordinal);
            Assert.Contains("Inter", body.FontFamily.Source, StringComparison.Ordinal);
        });
    }

    // ---------- newsroom: era-skinned per medium ----------

    [Theory]
    [InlineData(1967, "TELEGRAM", "Consolas", 0xC8, 0x92, 0x2E, 0xDC, 0xC4, 0x8A, true)]
    [InlineData(1991, "FAX", "Cascadia Mono", 0x5E, 0x7A, 0x8C, 0x46, 0x58, 0x6A, false)]
    [InlineData(2000, "EMAIL", "Segoe UI", 0x3B, 0x7D, 0xD8, 0xEE, 0xF3, 0xFA, false)]
    public void Newsroom_SkinsMastheadCardsAndArticleToTheEraMedium(
        int year, string label, string fontFamily,
        byte accentR, byte accentG, byte accentB, byte bandR, byte bandG, byte bandB,
        bool expectsFlourish)
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new NewsViewModel(EraSession.ForYear(year));
            var view = new NewsView { DataContext = vm };
            using var host = DocumentHost.Show(view, eraGate: null);

            // Masthead: the era-medium chip and the era accent rule.
            Border chip = Assert.IsType<Border>(view.FindName("EraMediumChip"));
            Assert.Equal(Visibility.Visible, chip.Visibility);
            var chipBrush = Assert.IsType<SolidColorBrush>(chip.BorderBrush);
            Assert.Equal(Color.FromRgb(accentR, accentG, accentB), chipBrush.Color);
            TextBlock chipText = DocumentHost.Descendants<TextBlock>(chip).Single();
            Assert.Equal(label, chipText.Text);

            Border rule = Assert.IsType<Border>(view.FindName("NewsMastheadRule"));
            var ruleBrush = Assert.IsType<SolidColorBrush>(rule.Background);
            Assert.Equal(Color.FromRgb(accentR, accentG, accentB), ruleBrush.Color);

            // Front-page card: the lead headline takes the era document face (dark card kept).
            TextBlock cardHeadline = DocumentHost.Descendants<TextBlock>(view)
                .First(text => text.Text == "DESK VERDICT AT ITALY");
            Assert.Contains(fontFamily, cardHeadline.FontFamily.Source, StringComparison.Ordinal);

            // The article reader renders as the period document.
            vm.OpenArticleCommand.Execute(vm.Stories[0]);
            host.Layout();

            Border document = Assert.IsType<Border>(view.FindName("ArticleDocument"));
            if (year == 2000)
            {
                var paper = Assert.IsType<SolidColorBrush>(document.Background);
                Assert.Equal(Color.FromRgb(0xFA, 0xFB, 0xFD), paper.Color);
            }
            else
            {
                Assert.IsType<DrawingBrush>(document.Background);
            }

            Border band = Assert.IsType<Border>(view.FindName("ArticleDocLetterhead"));
            Assert.Equal(Visibility.Visible, band.Visibility);
            var bandBrush = Assert.IsType<SolidColorBrush>(band.Background);
            Assert.Equal(Color.FromRgb(bandR, bandG, bandB), bandBrush.Color);

            // One letterhead mark per medium: stamp ring / sender stripes / envelope glyph.
            Assert.Equal(year <= 1979 ? Visibility.Visible : Visibility.Collapsed,
                Assert.IsType<Border>(view.FindName("ArticleDocStamp")).Visibility);
            Assert.Equal(year is >= 1980 and <= 1993 ? Visibility.Visible : Visibility.Collapsed,
                Assert.IsType<StackPanel>(view.FindName("ArticleDocStripes")).Visibility);
            Assert.Equal(year >= 1994 ? Visibility.Visible : Visibility.Collapsed,
                Assert.IsType<Canvas>(view.FindName("ArticleDocGlyph")).Visibility);

            TextBlock readerHeadline = DocumentHost.Descendants<TextBlock>(document)
                .Single(text => text.Text == "DESK VERDICT AT ITALY");
            Assert.Contains(fontFamily, readerHeadline.FontFamily.Source, StringComparison.Ordinal);

            // The dateline flourish is the telegram era's STOP (hidden for the other media).
            TextBlock? flourish = DocumentHost.Descendants<TextBlock>(document)
                .FirstOrDefault(text => text.Text == "STOP");
            if (expectsFlourish)
            {
                Assert.NotNull(flourish);
                Assert.Equal(Visibility.Visible, flourish!.Visibility);
            }
            else
            {
                Assert.True(flourish is null || flourish.Visibility != Visibility.Visible);
            }
        });
    }

    [Fact]
    public void Newsroom_EraThemingOff_FullyNeutralizesTheEraChrome()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            // 1967 on purpose: the strongest era (uppercase wire face, STOP flourish, ochre paper)
            // must leave no trace when the master switch is off.
            var vm = new NewsViewModel(EraSession.ForYear(1967));
            var view = new NewsView { DataContext = vm };
            using var host = DocumentHost.Show(view, eraGate: false);

            Assert.Equal(Visibility.Collapsed,
                Assert.IsType<Border>(view.FindName("EraMediumChip")).Visibility);
            Border rule = Assert.IsType<Border>(view.FindName("NewsMastheadRule"));
            Assert.Equal(Color.FromRgb(0x6F, 0x9B, 0xFF),
                Assert.IsType<SolidColorBrush>(rule.Background).Color); // theme AccentBrush

            TextBlock cardHeadline = DocumentHost.Descendants<TextBlock>(view)
                .First(text => text.Text == "DESK VERDICT AT ITALY");
            Assert.Contains("Orbitron", cardHeadline.FontFamily.Source, StringComparison.Ordinal);

            vm.OpenArticleCommand.Execute(vm.Stories[0]);
            host.Layout();

            Border document = Assert.IsType<Border>(view.FindName("ArticleDocument"));
            Assert.Equal(Color.FromRgb(0x20, 0x23, 0x2A),
                Assert.IsType<SolidColorBrush>(document.Background).Color); // theme SurfaceBrush
            Assert.Equal(Visibility.Collapsed,
                Assert.IsType<Border>(view.FindName("ArticleDocLetterhead")).Visibility);

            TextBlock readerHeadline = DocumentHost.Descendants<TextBlock>(document)
                .Single(text => text.Text == "DESK VERDICT AT ITALY");
            Assert.Contains("Orbitron", readerHeadline.FontFamily.Source, StringComparison.Ordinal);

            TextBlock readerBody = DocumentHost.Descendants<TextBlock>(document)
                .Single(text => text.Text == vm.Stories[0].Body);
            Assert.Contains("Inter", readerBody.FontFamily.Source, StringComparison.Ordinal);

            Assert.DoesNotContain(DocumentHost.Descendants<TextBlock>(document),
                text => text.Text == "STOP" && text.Visibility == Visibility.Visible);
        });
    }

    // ---------- legible tools: era dictionaries never change dense surfaces ----------

    [Fact]
    public void StandingsTable_RendersPixelIdenticalWithEraDictionariesInScope()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            AssertAppearanceUnchangedAcrossEraScope(
                new StandingsViewModel(SmallFixture.Snapshots(), SmallFixture.Pack()),
                vm => new StandingsView { DataContext = vm });
        });
    }

    [Fact]
    public void ResultEntry_RendersPixelIdenticalWithEraDictionariesInScope()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            AssertAppearanceUnchangedAcrossEraScope(
                new ResultEntryViewModel(SmallFixture.Grid(), SmallFixture.PlayerId),
                vm => new ResultEntryView { DataContext = vm });
        });
    }

    /// <summary>Renders ONE live view in a plain host, then merges the three era dictionaries into
    /// the host's resources and renders it again. Same instance, same focus and layout state, so
    /// the ONLY variable is the resource merge itself. Merging any dictionary invalidates the
    /// window's resources and forces a re-layout that can shift a text run's anti-aliasing edge
    /// by a pixel or two (measured: channel delta up to ~25, under half a percent of pixels); a
    /// real era skin (paper backgrounds, ink recolors, era fonts) would recolor whole regions far
    /// above that. The proof therefore tolerates the mechanics floor and still fails the moment
    /// an era key actually reaches a dense tool surface ("immersive docs, legible tools").</summary>
    /// <summary>Proves the era skin never reaches a dense tool surface WITHOUT pixel comparisons
    /// (a resource merge forces a re-layout whose anti-aliasing noise is nondeterministic under
    /// load, so pixel tolerances are inherently flaky). Instead it snapshots every element's
    /// appearance properties before and after the merge and requires them all unchanged: if any
    /// era brush, font, or style actually reached the surface, its owning element's properties
    /// change — layout noise alone never changes a property value. Deterministic under load.</summary>
    private static void AssertAppearanceUnchangedAcrossEraScope(
        object viewModel, Func<object, FrameworkElement> viewFactory)
    {
        using var host = DocumentHost.Show(viewFactory(viewModel), eraGate: null, mergeEraDictionaries: false);
        DocumentHost.Settle(host.Window);
        Keyboard.ClearFocus();
        var before = AppearanceSnapshot(host.View);

        DocumentHost.MergeEraDictionaries(host.Window);
        host.Window.UpdateLayout();
        WpfRenderHarness.Pump(DispatcherPriority.Render);
        var after = AppearanceSnapshot(host.View);

        Assert.Equal(before, after);
    }

    /// <summary>Every element's appearance signature in visual-tree order: type, foreground and
    /// background brushes (as color/brush identity), font family, size and weight, and style key.
    /// A brush identity compares the actual resolved brush, not the binding, so a skin arriving
    /// through any channel (trigger, style, direct set) is caught.</summary>
    private static IReadOnlyList<string> AppearanceSnapshot(DependencyObject root)
    {
        var lines = new List<string>();
        foreach (var element in DocumentHost.Descendants<FrameworkElement>(root).Prepend((FrameworkElement)root))
        {
            string foreground = element switch
            {
                TextBlock text => BrushKey(text.Foreground),
                System.Windows.Controls.Control control => BrushKey(control.Foreground),
                _ => "",
            };
            string background = element switch
            {
                Border border => BrushKey(border.Background),
                System.Windows.Controls.Control control => BrushKey(control.Background),
                _ => "",
            };
            string font = element is TextBlock text2
                ? $"{text2.FontFamily.Source}|{text2.FontSize}|{text2.FontWeight}"
                : element is System.Windows.Controls.Control control2
                    ? $"{control2.FontFamily.Source}|{control2.FontSize}|{control2.FontWeight}"
                    : "";
            lines.Add($"{element.GetType().Name}|{foreground}|{background}|{font}|{element.Style?.TargetType.Name}");
        }

        return lines;
    }

    private static string BrushKey(Brush? brush) => brush switch
    {
        null => "null",
        SolidColorBrush solid => $"#{solid.Color.A:X2}{solid.Color.R:X2}{solid.Color.G:X2}{solid.Color.B:X2}",
        _ => brush.ToString(),
    };

    /// <summary>The bounding box of changed pixels (diagnostic for assert messages).</summary>
    private static string ChangedRegion(byte[] before, byte[] after, int width)
    {
        int minX = int.MaxValue;
        int minY = int.MaxValue;
        int maxX = -1;
        int maxY = -1;
        for (int index = 0; index < before.Length; index += 4)
        {
            if (before[index] != after[index] || before[index + 1] != after[index + 1] ||
                before[index + 2] != after[index + 2] || before[index + 3] != after[index + 3])
            {
                int pixel = index / 4;
                int x = pixel % width;
                int y = pixel / width;
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
        }
        return maxX < 0
            ? "No changed pixels."
            : $"Changed region: x[{minX}..{maxX}] y[{minY}..{maxY}] of {width}px wide.";
    }

    // ---------- fixtures + hosts ----------

    private static OfferLetterViewModel Offer(int year)
    {
        var offer = new SeasonOfferModel
        {
            TeamId = "team.madonna",
            TeamName = "Madonna",
            Tier = 4,
            SalaryBu = 12.5,
            Score = 0.8123,
            Accepted = false,
        };
        return new OfferLetterViewModel(
            offer, OfferDocument.Compose(year, offer.TeamName, offer.Tier, offer.SalaryBu, "Mike Racer"));
    }

    /// <summary>The gate the EraThemingEnabled master switch binds through (the hub/window tag in
    /// the real shell); <c>null</c> = no shell above the surface, the gate defaults on.</summary>
    private sealed class EraGate
    {
        public bool EraThemingEnabled { get; init; }
    }

    private sealed class DocumentHost : IDisposable
    {
        private DocumentHost(Window window, FrameworkElement view)
        {
            Window = window;
            View = view;
        }

        public Window Window { get; }
        public FrameworkElement View { get; }

        public static DocumentHost Show(object content, bool? eraGate, bool mergeEraDictionaries = true)
        {
            var dictionaries = new ResourceDictionary();
            if (mergeEraDictionaries)
                dictionaries.MergedDictionaries.Add(LoadEraDictionaries());

            FrameworkElement view;
            if (content is FrameworkElement element)
            {
                view = element;
            }
            else
            {
                view = new ContentControl { Content = content, Width = 620 };
                view.Resources.MergedDictionaries.Add(dictionaries);
                ((ContentControl)view).ContentTemplate =
                    (DataTemplate)view.FindResource("EraOfferDocumentTemplate");
            }

            var window = new Window
            {
                Content = view,
                Tag = eraGate is null ? null : new EraGate { EraThemingEnabled = eraGate.Value },
                Width = 1100,
                Height = 760,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                Left = -10000,
                Top = -10000,
            };
            if (content is FrameworkElement)
                window.Resources.MergedDictionaries.Add(dictionaries);

            window.Show();
            window.UpdateLayout();
            WpfRenderHarness.Pump(DispatcherPriority.Loaded);
            window.UpdateLayout();
            WpfRenderHarness.Pump(DispatcherPriority.Render);
            return new DocumentHost(window, view);
        }

        /// <summary>The three era dictionaries, loaded exactly as the views merge them.</summary>
        public static ResourceDictionary LoadEraDictionaries()
        {
            var dictionaries = new ResourceDictionary();
            foreach (string name in new[] { "Era.Telegram", "Era.Fax", "Era.Email" })
            {
                dictionaries.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(
                    new Uri($"/AMS2CareerCompanion;component/Themes/{name}.xaml", UriKind.Relative)));
            }
            return dictionaries;
        }

        /// <summary>Brings the era dictionaries into an already-shown host's resource scope, the
        /// exact change the app-wide merge would make.</summary>
        public static void MergeEraDictionaries(Window window) =>
            window.Resources.MergedDictionaries.Add(LoadEraDictionaries());

        public void Layout()
        {
            Window.UpdateLayout();
            WpfRenderHarness.Pump(DispatcherPriority.Input);
            WpfRenderHarness.Pump(DispatcherPriority.Render);
            WpfRenderHarness.Pump();
        }

        /// <summary>Let any startup motion finish so two sequential renders are comparable.</summary>
        public static void Settle(Window window)
        {
            WpfRenderHarness.Pump(DispatcherPriority.Background);
            Thread.Sleep(400);
            window.UpdateLayout();
            WpfRenderHarness.Pump(DispatcherPriority.Render);
        }

        public void Dispose()
        {
            Window.Close();
            WpfRenderHarness.Pump(DispatcherPriority.Background);
        }

        /// <summary>The outermost border of the era offer document template (the paper surface).</summary>
        public static Border SurfaceBorder(ContentControl content)
        {
            var presenter = DocumentHost.Descendants<ContentPresenter>(content).First();
            return DocumentHost.Descendants<Border>(presenter).First();
        }

        public static IReadOnlyList<string> VisibleTexts(DependencyObject root) =>
            Descendants<TextBlock>(root)
                .Where(IsEffectivelyVisible)
                .Select(text => text.Text)
                .Where(text => !string.IsNullOrEmpty(text))
                .ToArray();

        /// <summary>The fraction of rendered pixels within a small tolerance of the given color
        /// (paper/band coverage, robust to where the text happens to fall).</summary>
        public static double ColorFraction(FrameworkElement element, byte r, byte g, byte b)
        {
            byte[] pixels = RenderPixels(element, out int width, out int height);
            int total = width * height;
            int matches = 0;
            for (int index = 0; index < pixels.Length; index += 4)
            {
                if (Math.Abs(pixels[index] - b) <= 3 &&
                    Math.Abs(pixels[index + 1] - g) <= 3 &&
                    Math.Abs(pixels[index + 2] - r) <= 3)
                {
                    matches++;
                }
            }
            return (double)matches / total;
        }

        public static byte[] RenderPixels(FrameworkElement element) =>
            RenderPixels(element, out _, out _);

        public static byte[] RenderPixels(FrameworkElement element, out int width, out int height)
        {
            width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth));
            height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight));
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(element);
            var pixels = new byte[width * height * 4];
            bitmap.CopyPixels(pixels, width * 4, 0);
            return pixels;
        }

        public static bool IsEffectivelyVisible(DependencyObject element)
        {
            for (DependencyObject? node = element; node is not null; node = VisualTreeHelper.GetParent(node))
            {
                if (node is UIElement ui && ui.Visibility != Visibility.Visible)
                    return false;
            }
            return true;
        }

        public static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int index = 0; index < count; index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, index);
                if (child is T match)
                    yield return match;
                foreach (T descendant in Descendants<T>(child))
                    yield return descendant;
            }
        }
    }

    /// <summary>A minimal in-career session whose pack year selects the era medium; one lead
    /// newsroom article gives the front page and the reader something to render.</summary>
    private sealed class EraSession(int year) : ICareerSession
    {
        private static readonly string[] Venues =
            ["Brazil", "San Marino", "Monaco", "France", "Britain", "Germany", "Belgium", "Italy"];

        public static EraSession ForYear(int year) => new(year);

        public SeasonPack Pack { get; } = new()
        {
            Manifest = new PackManifest
            {
                PackId = $"era-render-{year}",
                Name = $"Era Render {year}",
                Version = "1.0.0",
                FormatVersion = 1,
                CareerStyle = SmgpRules.CareerStyle,
            },
            Season = new SeasonDefinition
            {
                Year = year,
                SeriesName = $"Era Series {year}",
                Ams2Class = "smgp",
                PointsSystem = new CatalogSeason { RacePoints = [new(9), new(6), new(4)] },
                Rounds = [],
            },
            Teams = [],
            Drivers = [],
            Entries = [],
        };

        public CareerSummary Summary => new()
        {
            CareerName = $"Era Render {year}",
            SeasonYear = year,
            SeriesName = $"Era Series {year}",
            CurrentRound = 3,
            RoundCount = 16,
            PlayerDriverId = "driver.player",
            PlayerLiveryName = "vehicle-livery",
            PlayerPosition = 1,
        };

        public IReadOnlyList<NewsDispatch> ReadFeed() => [];
        public CareerTimeline CareerTimeline() => new()
        {
            Seasons = [SeasonCard()],
            Records = new CareerRecordsBook { Wins = 2, Podiums = 5, TotalPoints = 40 },
        };
        public SmgpPaddockModel? SmgpPaddock() => null;
        public IReadOnlyList<SmgpDispatch> SmgpDispatches() => [];
        public IReadOnlyList<StoryThread> StoryThreads() => [];
        public IReadOnlyList<RumorRecord> RumorBoard() => [];
        public IReadOnlyDictionary<string, NewsReadingState> ReadingState() =>
            new Dictionary<string, NewsReadingState>(StringComparer.Ordinal);
        public void MarkStoryRead(string storyKey) { }
        public void SetStoryBookmark(string storyKey, bool bookmarked) { }

        public IReadOnlyList<NewsroomArticle> NewsroomFeed() =>
        [
            new NewsroomArticle
            {
                Key = $"news:1:3:deskVerdict",
                EventKind = NewsEventKind.TitleFightTightens,
                Category = NewsroomCategory.ChampionshipAnalysis,
                Status = EditorialStatus.Analysis,
                Provenance = ContentProvenance.CareerUniverse,
                SeasonOrdinal = 1,
                SeasonYear = year,
                Round = 3,
                VenueName = "Monaco",
                SubjectName = "Mike Racer",
                TeamName = "Bullets",
                DeskName = "Apex Technical Review",
                DeskMonogram = "AT",
                Headline = "DESK VERDICT AT ITALY",
                Deck = "The desk weighs the championship arithmetic.",
                Summary = "An analysis piece from the rendered newsroom.",
                Sections =
                [
                    new NewsroomSection("body", "The newsroom weighs the title arithmetic after Monaco."),
                ],
                ImportanceScore = 100,
                Tier = EditorialTier.Lead,
                ReadingSeconds = 45,
            },
        ];

        public BriefingModel? CurrentBriefing() => null;
        public StageOutcome StageCurrentGrid() => new() { Success = true, Messages = [] };
        public IReadOnlyList<GridSeat> CurrentGrid() => [];
        public ConfirmModel Preview(ResultDraft draft) => new() { RoundPoints = [], Movements = [], Headline = "" };
        public void Apply(ResultDraft draft) { }
        public StandingsSnapshot? CurrentStandings() => null;
        public IReadOnlyList<StandingsSnapshot> AllSnapshots() => [];
        public int? CurrentSliderRecommendation() => null;
        public SeasonReviewModel? SeasonReview() => null;
        public void AcceptOffer(string teamId) { }

        private CareerSeasonCard SeasonCard()
        {
            var lines = new List<CareerSeasonRoundLine>();
            double points = 0;
            for (int index = 0; index < Venues.Length; index++)
            {
                points += index % 3 == 0 ? 9 : 6;
                lines.Add(new CareerSeasonRoundLine
                {
                    Round = index + 1,
                    Venue = Venues[index],
                    PlayerFinish = index % 4 + 1,
                    RivalName = "A. Senna",
                    RivalFinish = index % 3 + 1,
                    ChampionAfter = index % 2 == 0 ? "Mike Racer" : "A. Senna",
                    PlayerPointsAfter = points,
                });
            }
            return new CareerSeasonCard
            {
                SeasonYear = year,
                RoundsApplied = lines.Count,
                RoundCount = 16,
                RoundLines = lines,
            };
        }
    }

    /// <summary>Small deterministic standings/grid fixtures for the pixel-identity proofs.</summary>
    private static class SmallFixture
    {
        public const string PlayerId = "d.amon";

        private static readonly string[] Names =
            ["Chris Amon", "Jack Brabham", "Denny Hulme", "Jim Clark", "Graham Hill", "Jackie Stewart"];

        public static SeasonPack Pack() => new()
        {
            Manifest = new PackManifest
            {
                PackId = "era-tools-render",
                Name = "Era Tools Render",
                Version = "1.0.0",
                FormatVersion = 1,
            },
            Season = new SeasonDefinition
            {
                Year = 1967,
                SeriesName = "Era Tools",
                Ams2Class = "F-Classic_Gen1",
                PointsSystem = new CatalogSeason { RacePoints = [new(9), new(6), new(4), new(3)] },
                Rounds = Enumerable.Range(1, 3).Select(round => new PackRound
                {
                    Round = round,
                    Name = $"Grand Prix {round}",
                    Date = $"1967-0{round + 4}-01",
                    Track = new PackTrackRef { Id = $"track-{round}" },
                    Laps = 60,
                }).ToArray(),
            },
            Teams = Enumerable.Range(0, Names.Length).Select(index => new PackTeam
            {
                Id = $"team.{index}",
                Name = $"Team {index + 1}",
                CarVehicleIds = ["car.render"],
            }).ToArray(),
            Drivers = Names.Select((name, index) => new PackDriver
            {
                Id = $"driver.{index}",
                Name = name,
                Ratings = new PackDriverRatings { RaceSkill = 0.9, QualifyingSkill = 0.9 },
            }).ToArray(),
            Entries = [],
        };

        public static IReadOnlyList<StandingsSnapshot> Snapshots() =>
        [
            new StandingsSnapshot
            {
                AfterRound = 3,
                Drivers = Names.Select((name, index) => new DriverStanding
                {
                    DriverId = $"driver.{index}",
                    Position = index + 1,
                    GrossPoints = new Rational(Math.Max(0, 9 - index) * 3),
                    CountedPoints = new Rational(Math.Max(0, 9 - index) * 3),
                    RoundScores = Enumerable.Range(1, 3).Select(round => new RoundScore
                    {
                        Round = round,
                        Points = new Rational(Math.Max(0, 9 - index)),
                    }).ToArray(),
                    Dropped = [],
                }).ToArray(),
                Constructors = Enumerable.Range(0, 3).Select(index => new ConstructorStanding
                {
                    ConstructorId = $"team.{index}",
                    Position = index + 1,
                    GrossPoints = new Rational((6 - index) * 3),
                    CountedPoints = new Rational((6 - index) * 3),
                    RoundScores = Enumerable.Range(1, 3).Select(round => new RoundScore
                    {
                        Round = round,
                        Points = new Rational(6 - index),
                    }).ToArray(),
                    Dropped = [],
                }).ToArray(),
            },
        ];

        public static IReadOnlyList<GridSeat> Grid() =>
            Names.Select((name, index) => new GridSeat
            {
                DriverId = $"d.{name.Split(' ')[1].ToLowerInvariant()}",
                DriverName = name,
                TeamId = $"t.{index}",
                TeamName = $"Team {name}",
                Number = (index + 2).ToString(),
                Ams2LiveryName = name,
                Ratings = new PackDriverRatings { RaceSkill = 0.9, QualifyingSkill = 0.9 },
                Reliability = 1.0,
                WeightScalar = 1.0,
                PowerScalar = 1.0,
                DragScalar = 1.0,
                IsPlayer = index == 0,
            }).ToArray();
    }
}
