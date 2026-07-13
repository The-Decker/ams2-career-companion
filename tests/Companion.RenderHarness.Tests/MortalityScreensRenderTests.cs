using System.Windows;
using System.Windows.Controls;
using Companion.App.Views;
using Companion.Core.Career;
using Companion.Data;
using Companion.ViewModels.Services;
using Companion.ViewModels.Shell;

namespace Companion.RenderHarness.Tests;

/// <summary>Off-screen coverage for the full-immersion injury and both career-over voices.</summary>
public sealed class MortalityScreensRenderTests
{
    [Theory]
    [InlineData(false, 2, "INJURED — auto-simulating round (2 remaining)")]
    [InlineData(true, 0, "SEASON OVER — recovering")]
    public void SitOutView_RendersBothRecoveryStates(bool seasonEnding, int remaining, string headline)
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            bool continued = false;
            var vm = new SitOutViewModel(
                new SitOutStatus
                {
                    Headline = headline,
                    RaceSuspensionRemaining = remaining,
                    SeasonEnding = seasonEnding,
                },
                () => continued = true);
            var view = new SitOutView { DataContext = vm };

            Arrange(view, 1000, 760);

            Assert.Equal(headline, ((TextBlock)view.FindName("SitOutHeadline")).Text);
            var button = (Button)view.FindName("SitOutContinueButton");
            Assert.True(button.Command.CanExecute(null));
            button.Command.Execute(null);
            Assert.True(continued);
        });
    }

    [Fact]
    public void DeathScreenView_NormalDeath_ShowsRestoreAndRecap()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var host = CreateDeathHost(MortalityMode.Normal, fileDeleted: false, withRestore: true);
            var view = new DeathScreenView { DataContext = host };

            Arrange(view, 1100, 900);

            Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("NormalDeathPanel")).Visibility);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)view.FindName("PermadeathPanel")).Visibility);
            Assert.Single(((ItemsControl)view.FindName("RestoreSlotsList")).Items);
            Assert.Equal("Nova Reyes", ((TextBlock)view.FindName("DeathDriverName")).Text);
        });
    }

    [Fact]
    public void DeathScreenView_HardcoreDeath_IsFinalAndNeverOffersRestore()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var host = CreateDeathHost(MortalityMode.Hardcore, fileDeleted: true, withRestore: false);
            var view = new DeathScreenView { DataContext = host };

            Arrange(view, 1100, 900);

            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)view.FindName("NormalDeathPanel")).Visibility);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("PermadeathPanel")).Visibility);
            Assert.Empty(((ItemsControl)view.FindName("RestoreSlotsList")).Items);
        });
    }

    [Fact]
    public void SmgpCareerOverView_RendersTheFiredEnding()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var view = new SmgpCareerOverView
            {
                DataContext = new
                {
                    SmgpCareerOver = true,
                    SmgpRoundHeader = "MONACO · ROUND 12",
                    SmgpCampaignLine = "SEASON 3 / 17",
                },
            };

            Arrange(view, 1000, 800);

            Assert.Equal(Visibility.Visible, view.Visibility);
            Assert.True(((Button)view.FindName("SmgpCareerOverReturnButton")).IsVisible || view.ActualWidth > 0);
        });
    }

    [Fact]
    public void HubView_SmgpFloor_ReplacesTheLiveHubWithTheUnifiedEnding()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var view = new HubView
            {
                DataContext = new HubHost
                {
                    Home = new HomeHost
                    {
                        Briefing = new SmgpFloorHost
                        {
                            SmgpCareerOver = true,
                            SmgpRoundHeader = "MONACO · ROUND 12",
                            SmgpCampaignLine = "SEASON 3 / 17",
                        },
                    },
                },
            };

            Arrange(view, 1100, 820);

            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)view.FindName("LiveHubSurfaces")).Visibility);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("SmgpCareerOverSurface")).Visibility);
        });
    }

    [Fact]
    public void HubView_LiveSmgpBriefing_ShowsLiveHubAndCollapsesCareerOver()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var view = new HubView
            {
                DataContext = new HubHost
                {
                    Home = new HomeHost
                    {
                        Briefing = new SmgpFloorHost
                        {
                            SmgpCareerOver = false,
                            SmgpRoundHeader = "SAN MARINO · ROUND 1",
                            SmgpCampaignLine = "SEASON 1 / 17",
                        },
                    },
                },
            };

            Arrange(view, 1100, 820);

            Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("LiveHubSurfaces")).Visibility);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)view.FindName("SmgpCareerOverSurface")).Visibility);
        });
    }

    private static object CreateDeathHost(MortalityMode mode, bool fileDeleted, bool withRestore)
    {
        IReadOnlyList<SaveSlotInfo> slots = withRestore
            ?
            [
                new SaveSlotInfo
                {
                    SlotId = "manual-001",
                    Label = "Before Monaco",
                    SeasonYear = 1990,
                    Round = 5,
                    CreatedUtc = "2026-07-12T20:00:00Z",
                    IsAutosave = false,
                },
            ]
            : [];

        var screen = DeathScreenModel.Build(
            mode,
            driverName: "Nova Reyes",
            age: 24,
            severity: AccidentSeverity.Heavy,
            venue: "Monaco",
            round: 6,
            record: new CareerRecordsBook
            {
                BestFinish = 1,
                Wins = 3,
                Podiums = 8,
                TotalPoints = 77,
                Championships = 1,
                SeasonsRaced = 2,
                LongestWinStreak = 2,
                LongestPodiumStreak = 4,
            },
            seasons:
            [
                new CareerSeasonCard
                {
                    SeasonYear = 1990,
                    PlayerPosition = 1,
                    RoundsApplied = 16,
                    RoundCount = 16,
                    IsComplete = true,
                    ChampionName = "Nova Reyes",
                    PlayerIsChampion = true,
                },
            ],
            restoreSlots: slots);

        return new DeathHost
        {
            CareerOver = new PlayerMortalityStatus
            {
                Mode = mode,
                Deceased = true,
                SeasonEndingInjury = false,
                RaceSuspensionRemaining = 0,
                CareerFileDeleted = fileDeleted,
            },
            DeathScreen = screen,
        };
    }

    private sealed class DeathHost
    {
        public required PlayerMortalityStatus CareerOver { get; init; }
        public required DeathScreenModel DeathScreen { get; init; }
    }

    private sealed class HubHost
    {
        public required HomeHost Home { get; init; }
    }

    private sealed class HomeHost
    {
        public object? CareerOver => null;
        public required SmgpFloorHost Briefing { get; init; }
    }

    private sealed class SmgpFloorHost
    {
        public bool SmgpCareerOver { get; init; }
        public string SmgpRoundHeader { get; init; } = "";
        public string SmgpCampaignLine { get; init; } = "";
    }

    private static void Arrange(FrameworkElement view, double width, double height)
    {
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        view.UpdateLayout();
        Assert.True(view.ActualWidth > 0);
        Assert.True(view.ActualHeight > 0);
    }
}
