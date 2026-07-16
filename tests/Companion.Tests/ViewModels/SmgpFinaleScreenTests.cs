using Companion.Core.Grid;
using Companion.ViewModels.Confirm;
using Companion.ViewModels.ResultEntry;
using Companion.ViewModels.Services;
using Companion.ViewModels.Shell;

namespace Companion.Tests.ViewModels;

/// <summary>
/// The 17-season SMGP campaign FINALE (Mike's "final final screen"): when the season that just
/// completed is a beaten campaign summit, the HomeViewModel shows the locked special.jpg / ultimate.jpg
/// celebration as its own full-immersion step BEFORE the season review, exactly once; its Continue
/// command advances into the review. A non-summit season goes straight to the review, unchanged.
/// </summary>
public sealed class SmgpFinaleScreenTests
{
    [Fact]
    public void CampaignSummit_ShowsTheFinaleStep_ThenContinueAdvancesToTheReview()
    {
        var session = FakeCompletingSession();
        session.Finale = new SmgpFinaleModel
        {
            Headline = "SEVENTEEN SEASONS CONQUERED",
            Subhead = "You went the distance.",
            IsFlawless = false,
            HeroImageKey = "special",
            Record = ["17 SEASONS CONQUERED", "6 CHAMPIONSHIPS"],
        };
        using var home = new HomeViewModel(session);

        ApplyARound(home);

        // The finale owns the step — the season did NOT jump straight to the review.
        Assert.True(home.IsFinaleStep);
        Assert.False(home.IsSeasonReviewState);
        var screen = Assert.IsType<SmgpFinaleViewModel>(home.CurrentContent);
        Assert.Equal("special", screen.Model.HeroImageKey);
        Assert.False(screen.Model.IsFlawless);

        screen.ContinueCommand.Execute(null);

        // Acknowledged → into the final season review.
        Assert.False(home.IsFinaleStep);
        Assert.True(home.IsSeasonReviewState);
    }

    [Fact]
    public void FlawlessCampaign_RevealsTheUltimateSecret()
    {
        var session = FakeCompletingSession();
        session.Finale = new SmgpFinaleModel
        {
            Headline = "THE FLAWLESS EMPEROR",
            Subhead = "Champion of every season.",
            IsFlawless = true,
            HeroImageKey = "ultimate",
            Record = ["17 SEASONS CONQUERED", "17 CHAMPIONSHIPS"],
        };
        using var home = new HomeViewModel(session);

        ApplyARound(home);

        var screen = Assert.IsType<SmgpFinaleViewModel>(home.CurrentContent);
        Assert.True(screen.Model.IsFlawless);
        Assert.Equal("ultimate", screen.Model.HeroImageKey);
    }

    [Fact]
    public void NonSummitSeason_GoesStraightToTheReview_WithNoFinale()
    {
        var session = FakeCompletingSession(); // Finale stays null (mid-campaign, or a career that ended)
        using var home = new HomeViewModel(session);

        ApplyARound(home);

        Assert.False(home.IsFinaleStep);
        Assert.True(home.IsSeasonReviewState); // the shipped end-of-season flow, unchanged
    }

    // ---------- helpers ----------

    /// <summary>A fake whose Apply completes the season, so the shell reaches the season-complete branch
    /// where the finale (or review) is chosen.</summary>
    private static FakeCareerSession FakeCompletingSession()
    {
        var session = new FakeCareerSession
        {
            CompletesSeasonOnApply = true,
            Grid =
            [
                Seat("driver.hulme", "2", TestPackBuilder.StockLivery2, isPlayer: true),
                Seat("driver.brabham", "1", TestPackBuilder.StockLivery1, isPlayer: false),
            ],
        };
        return session;
    }

    private static void ApplyARound(HomeViewModel home)
    {
        home.EnterResultCommand.Execute(null);
        Assert.IsType<SessionIntroViewModel>(home.CurrentContent).ContinueCommand.Execute(null);
        var entry = Assert.IsType<ResultEntryViewModel>(home.CurrentContent);
        entry.Input = "1";
        entry.SubmitCommand.Execute(null);
        entry.Input = "2";
        entry.SubmitCommand.Execute(null);
        home.ConfirmResultCommand.Execute(null);
        Assert.IsType<ConfirmViewModel>(home.CurrentContent).ApplyCommand.Execute(null);
    }

    private static GridSeat Seat(string driverId, string number, string livery, bool isPlayer) => new()
    {
        DriverId = driverId,
        DriverName = driverId,
        TeamId = "team.brabham",
        TeamName = "Brabham",
        Number = number,
        Ams2LiveryName = livery,
        Ratings = TestPackBuilder.Driver(driverId).Ratings,
        Reliability = 0.9,
        WeightScalar = 1,
        PowerScalar = 1,
        DragScalar = 1,
        IsPlayer = isPlayer,
    };
}
