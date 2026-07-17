using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using Companion.App.Views;
using Companion.ViewModels.Services;

namespace Companion.RenderHarness.Tests;

/// <summary>Real-WPF coverage for the single post-Apply progression announcement owned by Home.</summary>
public sealed class HomeProgressionRenderTests
{
    private sealed class HomeHost
    {
        public string? ContentError => null;
        public bool IsSeasonReview => false;
        public required RoundProgressionSummary? LastProgression { get; init; }
        public object CurrentContent { get; } = new Border { MinHeight = 120 };
    }

    [Fact]
    public void ProgressionAnnouncement_GroupsMultiLevelGainIntoOneNonBlockingStrip()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var view = Arrange(new HomeHost
            {
                LastProgression = new RoundProgressionSummary
                {
                    Round = 4,
                    XpGained = 38,
                    LevelBefore = 41,
                    LevelAfter = 43,
                    SkillPointsAvailable = 6,
                },
            });

            var strip = Assert.IsType<Border>(view.FindName("ProgressionAnnouncement"));
            var xp = Assert.IsType<TextBlock>(view.FindName("ProgressionXpText"));
            var level = Assert.IsType<TextBlock>(view.FindName("ProgressionLevelText"));

            Assert.Equal(Visibility.Visible, strip.Visibility);
            Assert.True(strip.ActualHeight > 0);
            Assert.Equal("Driver progression update", AutomationProperties.GetName(strip));
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(strip));
            Assert.Contains("+38 XP", InlineText(xp), StringComparison.Ordinal);
            Assert.Contains("LEVEL 41 → 43", InlineText(level), StringComparison.Ordinal);
            Assert.Contains("+2", InlineText(level), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ProgressionAnnouncement_CollapsesForNullAndZeroMovement()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            HomeView absent = Arrange(new HomeHost { LastProgression = null });
            Assert.Equal(
                Visibility.Collapsed,
                Assert.IsType<Border>(absent.FindName("ProgressionAnnouncement")).Visibility);

            HomeView zero = Arrange(new HomeHost
            {
                LastProgression = new RoundProgressionSummary
                {
                    Round = 5,
                    XpGained = 0,
                    LevelBefore = 43,
                    LevelAfter = 43,
                    SkillPointsAvailable = 6,
                },
            });
            Assert.Equal(
                Visibility.Collapsed,
                Assert.IsType<Border>(zero.FindName("ProgressionAnnouncement")).Visibility);
        });
    }

    private static HomeView Arrange(HomeHost host)
    {
        var view = new HomeView { DataContext = host };
        view.Measure(new Size(1100, 700));
        view.Arrange(new Rect(0, 0, 1100, 700));
        view.UpdateLayout();
        return view;
    }

    private static string InlineText(TextBlock block) =>
        block.Text + string.Concat(block.Inlines.OfType<Run>().Select(run => run.Text));
}
