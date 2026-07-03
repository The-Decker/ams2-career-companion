using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.ViewModels.Briefing;
using Companion.ViewModels.ResultEntry;
using Companion.ViewModels.Review;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>
/// SeasonReviewViewModel (m5-fix-integration "App wiring"): offers render + accept-one goes
/// through the seam and re-flags rows, the NAMeS restore button appears exactly when the
/// session supports it and surfaces the outcome banner, and the era-transition placeholder is
/// present. Plus the two small wiring pieces that ride along: the briefing's difficulty
/// recommendation line and the result screen's slider prompt clamping.
/// </summary>
public class SeasonReviewViewModelTests
{
    private static SeasonReviewModel Review(string? acceptedTeamId = null) => new()
    {
        SeasonYear = 1967,
        PlayerPosition = 3,
        FinalReputation = 47.5,
        FinalOpi = 0.8,
        Headlines = ["Headline one", "Headline two"],
        Offers =
        [
            new SeasonOfferModel
            {
                TeamId = "team.lotus", TeamName = "Lotus", Tier = 5,
                SalaryBu = 7.5, Score = 2.1, Accepted = acceptedTeamId == "team.lotus",
            },
            new SeasonOfferModel
            {
                TeamId = "team.cooper", TeamName = "Cooper", Tier = 3,
                SalaryBu = 4.0, Score = 1.4, Accepted = acceptedTeamId == "team.cooper",
            },
        ],
        AcceptedTeamId = acceptedTeamId,
    };

    [Fact]
    public void Review_RendersOffersDigestAndEraPlaceholder()
    {
        var session = new FakeCareerSession { Review = Review() };
        var vm = new SeasonReviewViewModel(session);

        Assert.Equal("1967 season review", vm.Title);
        Assert.Contains("P3", vm.PlayerSummaryText);
        Assert.Contains("47.5", vm.PlayerSummaryText);
        Assert.Equal(2, vm.Offers.Count);
        Assert.True(vm.HasOffers);
        Assert.Equal(new[] { "Headline one", "Headline two" }, vm.Headlines);
        Assert.True(vm.HasHeadlines);
        Assert.False(vm.OfferAccepted);
        // No next pack installed: the block explains what packs are and where they go.
        Assert.False(vm.HasNextSeason);
        Assert.Contains("season pack", vm.EraTransitionText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AMS2CareerCompanion\\Packs", vm.EraTransitionText);
        Assert.Null(vm.BridgeNote);
        Assert.False(vm.SignAndContinueCommand.CanExecute(null));
        Assert.False(vm.CanRestoreAiFile);           // plain session: no restore surface
    }

    // ---------- era transition: sign & continue (M6) ----------

    private static NextSeasonInfo Next1969(params int[] bridged) => new()
    {
        PackDirectory = @"Z:\packs\f1-1969",
        PackId = "f1-1969",
        PackName = "Formula One 1969",
        SeasonYear = 1969,
        BridgedYears = bridged,
    };

    [Fact]
    public void Sign_IsGatedOnAcceptedOffer_AndShowsYearAndBridgeNote()
    {
        var session = new FakeCareerSession { Review = Review(), Next = Next1969(1968) };
        var vm = new SeasonReviewViewModel(session);

        Assert.True(vm.HasNextSeason);
        Assert.Equal("Sign & start 1969", vm.SignButtonText);
        Assert.Equal("1968 has no pack — your career bridges through it.", vm.BridgeNote);
        Assert.True(vm.HasBridgeNote);
        Assert.Contains("1969", vm.EraTransitionText);

        // No accepted offer yet: the sign action is unavailable.
        Assert.False(vm.CanSign);
        Assert.False(vm.SignAndContinueCommand.CanExecute(null));

        vm.AcceptOfferCommand.Execute(vm.Offers[0]);
        Assert.True(vm.CanSign);
        Assert.True(vm.SignAndContinueCommand.CanExecute(null));

        bool signed = false;
        vm.SeasonSigned += (_, _) => signed = true;
        vm.SignAndContinueCommand.Execute(null);

        Assert.Equal(new[] { "team.lotus" }, session.SignedTeams);
        Assert.True(signed);
        Assert.Null(vm.TransitionError);
    }

    [Fact]
    public void Sign_ConsecutiveSeasons_HaveNoBridgeNote()
    {
        var session = new FakeCareerSession
        {
            Review = Review(),
            Next = Next1969() with { SeasonYear = 1968, PackId = "f1-1968", PackName = "Formula One 1968" },
        };
        var vm = new SeasonReviewViewModel(session);

        Assert.True(vm.HasNextSeason);
        Assert.Null(vm.BridgeNote);
        Assert.False(vm.HasBridgeNote);
        Assert.Equal("Sign & start 1968", vm.SignButtonText);
    }

    [Fact]
    public void Sign_MultiYearGap_BridgeNoteSpansTheRange()
    {
        var session = new FakeCareerSession { Review = Review(), Next = Next1969(1968) with
        {
            SeasonYear = 1974,
            BridgedYears = [1968, 1969, 1970, 1971, 1972, 1973],
        } };
        var vm = new SeasonReviewViewModel(session);

        Assert.Equal("1968–1973 have no packs — your career bridges through them.", vm.BridgeNote);
    }

    [Fact]
    public void Sign_FailureSurfacesTheError_AndDoesNotNavigate()
    {
        var session = new FakeCareerSession
        {
            Review = Review(),
            Next = Next1969(1968),
            StartNextSeasonThrows = new InvalidOperationException(
                "The accepted offer names team 'team.lotus', which does not exist in pack 'f1-1969'."),
        };
        var vm = new SeasonReviewViewModel(session);
        vm.AcceptOfferCommand.Execute(vm.Offers[0]);

        bool signed = false;
        vm.SeasonSigned += (_, _) => signed = true;
        vm.SignAndContinueCommand.Execute(null);

        Assert.False(signed);
        Assert.Empty(session.SignedTeams);
        Assert.Contains("does not exist in pack 'f1-1969'", vm.TransitionError);
    }

    [Fact]
    public void AcceptOffer_GoesThroughTheSeam_AndReFlagsExactlyOneRow()
    {
        var session = new FakeCareerSession { Review = Review() };
        var vm = new SeasonReviewViewModel(session);

        vm.AcceptOfferCommand.Execute(vm.Offers[0]);
        Assert.Equal(new[] { "team.lotus" }, session.AcceptedOffers);
        Assert.True(vm.Offers[0].IsAccepted);
        Assert.False(vm.Offers[1].IsAccepted);
        Assert.True(vm.OfferAccepted);
        Assert.Equal("team.lotus", vm.AcceptedTeamId);

        // Accept-one: choosing again replaces the acceptance, never adds a second.
        vm.AcceptOfferCommand.Execute(vm.Offers[1]);
        Assert.Equal(new[] { "team.lotus", "team.cooper" }, session.AcceptedOffers);
        Assert.False(vm.Offers[0].IsAccepted);
        Assert.True(vm.Offers[1].IsAccepted);
        Assert.Equal("team.cooper", vm.AcceptedTeamId);
    }

    [Fact]
    public void Review_ReflectsAPreviouslyAcceptedOffer()
    {
        var session = new FakeCareerSession { Review = Review(acceptedTeamId: "team.cooper") };
        var vm = new SeasonReviewViewModel(session);

        Assert.True(vm.OfferAccepted);
        Assert.Equal("team.cooper", vm.AcceptedTeamId);
        Assert.False(vm.Offers[0].IsAccepted);
        Assert.True(vm.Offers[1].IsAccepted);
    }

    [Fact]
    public void RestoreButton_SurfacesTheOutcomeBanner()
    {
        var session = new RestoringSession
        {
            Review = Review(),
            Outcome = new RestoreOutcome
            {
                Success = true,
                RestoredFromBackupPath = @"C:\backups\F-Vintage_Gen1.20260701T000000Z.xml",
                CurrentFileBackupPath = @"C:\backups\F-Vintage_Gen1.20260702T000000Z.xml",
                Messages = ["Current file re-backed up.", "Restored the original."],
            },
        };
        var vm = new SeasonReviewViewModel(session);

        Assert.True(vm.CanRestoreAiFile);
        vm.RestoreAiFileCommand.Execute(null);

        Assert.True(vm.RestoreSucceeded);
        Assert.Contains("Restored the original", vm.RestoreBanner);

        session.Outcome = new RestoreOutcome
        {
            Success = false,
            Messages = ["No backup exists."],
        };
        vm.RestoreAiFileCommand.Execute(null);
        Assert.False(vm.RestoreSucceeded);
        Assert.Contains("No backup", vm.RestoreBanner);
    }

    // ---------- the rest of the round wiring ----------

    [Fact]
    public void Briefing_ShowsTheDifficultyRecommendationLine()
    {
        var session = new FakeCareerSession();
        session.Briefing = new BriefingModel
        {
            Round = session.Pack.Season.Rounds[0],
            VenueDisplayName = "Kyalami",
            IsPlaceholder = false,
            Settings = [],
            RecommendedSlider = 104,
        };

        var vm = new BriefingViewModel(session);
        Assert.NotNull(vm.DifficultyRecommendation);
        Assert.Contains("104%", vm.DifficultyRecommendation);

        session.Briefing = session.Briefing with { RecommendedSlider = null };
        vm.Refresh();
        Assert.Null(vm.DifficultyRecommendation); // uncalibrated: no recommendation line
    }

    [Fact]
    public void SliderPrompt_ClampsTo70Through120_AndRoundsIntoTheDraft()
    {
        var vm = new ResultEntryViewModel(
            [ResultEntryViewModelTests.Seat("d.player", "Test Player", "1", isPlayer: true)],
            "d.player");

        Assert.Equal(ResultEntryViewModel.NeutralSlider, vm.SliderUsed); // default prefill

        vm.SliderUsed = 300.0;
        Assert.Equal(120.0, vm.SliderUsed);
        vm.SliderUsed = 12.0;
        Assert.Equal(70.0, vm.SliderUsed);

        vm.SliderUsed = 96.4;
        vm.Input = "1";
        vm.SubmitCommand.Execute(null);
        Assert.True(vm.IsComplete);
        Assert.Equal(96.0, vm.BuildDraft().SliderUsed);
        Assert.Equal("96%", vm.SliderUsedText);
    }

    /// <summary>A fake session that also supports the season-end restore surface.</summary>
    private sealed class RestoringSession : ICareerSession, IAiFileRestore
    {
        public SeasonReviewModel? Review { get; init; }

        public RestoreOutcome Outcome { get; set; } = new() { Success = false, Messages = [] };

        public CareerSummary Summary => new()
        {
            CareerName = "Restore Career",
            SeasonYear = 1967,
            SeriesName = "Test Championship",
            CurrentRound = 2,
            RoundCount = 2,
            PlayerDriverId = "driver.hulme",
            PlayerLiveryName = TestPackBuilder.StockLivery2,
            SeasonComplete = true,
        };

        public SeasonPack Pack { get; } = TestPackBuilder.TwoRoundPack();

        public BriefingModel? CurrentBriefing() => null;

        public StageOutcome StageCurrentGrid() => new() { Success = false, Messages = [] };

        public IReadOnlyList<GridSeat> CurrentGrid() => [];

        public ConfirmModel Preview(ResultDraft draft) => throw new NotSupportedException();

        public void Apply(ResultDraft draft) => throw new NotSupportedException();

        public StandingsSnapshot? CurrentStandings() => null;

        public IReadOnlyList<StandingsSnapshot> AllSnapshots() => [];

        public int? CurrentSliderRecommendation() => null;

        public SeasonReviewModel? SeasonReview() => Review;

        public void AcceptOffer(string teamId)
        {
        }

        public RestoreOutcome RestoreOriginalAiFile() => Outcome;
    }
}
