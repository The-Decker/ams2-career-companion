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
        // A null next season (this fake leaves it unset) collapses the block to a terminal note —
        // the real session always offers a carryover or a changeover once the season is complete.
        Assert.False(vm.HasNextSeason);
        Assert.Equal("This season is complete.", vm.EraTransitionText);
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
        Assert.Equal("1968 has no pack, your career bridges through it.", vm.BridgeNote);
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

        Assert.Equal("1968–1973 have no packs, your career bridges through them.", vm.BridgeNote);
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

    // ---------- character development block (depth 4) ----------

    private static Companion.Core.Character.CharacterDossier Dossier() => new()
    {
        Name = "Kobra",
        Level = 3,
        Xp = 500,
        XpIntoLevel = 100,
        XpForNextLevel = 300,
        LevelCap = 30,
        CpUnspent = 0,
        Stats =
        [
            new Companion.Core.Character.DossierStat("pace", "Pace", 0.60, Talent: true),
            new Companion.Core.Character.DossierStat("racecraft", "Racecraft", 0.55, Talent: true),
        ],
        Perks = [],
    };

    [Fact]
    public void Development_HiddenWhenTheCareerHasNoCharacter()
    {
        var vm = new SeasonReviewViewModel(new FakeCareerSession { Review = Review() });

        Assert.False(vm.HasCharacter);
        Assert.Empty(vm.DevelopmentStats);
        Assert.Equal(0, vm.AvailableCp);
        Assert.False(vm.HasCp);
    }

    [Fact]
    public void Development_ShowsPointsAndStats_AndRaisingSpendsThroughTheSeam()
    {
        var session = new FakeCareerSession { Review = Review(), Dossier = Dossier(), Cp = 2 };
        var vm = new SeasonReviewViewModel(session);

        Assert.True(vm.HasCharacter);
        Assert.Equal(2, vm.AvailableCp);
        Assert.True(vm.HasCp);
        Assert.Equal(2, vm.DevelopmentStats.Count);
        Assert.Equal("Pace", vm.DevelopmentStats[0].Label);
        Assert.Equal("0.60", vm.DevelopmentStats[0].ValueText);

        // Raise pace: one stat spend goes through the seam, the pool drops, the shown value climbs.
        vm.RaiseStatCommand.Execute("pace");
        Assert.Single(session.Spends);
        Assert.Equal("stat", session.Spends[0].Kind);
        Assert.Equal("pace", session.Spends[0].Target);
        Assert.Equal(1, vm.AvailableCp);
        Assert.True(vm.HasCp);
        Assert.Equal("0.62", vm.DevelopmentStats[0].ValueText);

        // Spend the last point: the pool empties and the raise gate closes.
        vm.RaiseStatCommand.Execute("pace");
        Assert.Equal(0, vm.AvailableCp);
        Assert.False(vm.HasCp);
        Assert.Equal(2, session.Spends.Count);
    }

    [Fact]
    public void Development_OffersAffordablePerks_AndBuyingSpendsThroughTheSeam()
    {
        var session = new FakeCareerSession { Review = Review(), Dossier = Dossier(), Cp = 2 };
        session.Buyable.Add(new PurchasablePerk
        {
            Id = "rain_man", Name = "Rain Man", Category = "weather", Cost = 1,
            Benefits = ["Faster in the wet"], Drawbacks = [],
        });
        session.Buyable.Add(new PurchasablePerk
        {
            Id = "engineers_favorite", Name = "Engineer's Favorite", Category = "crew", Cost = 2,
            Benefits = ["A stronger car"], Drawbacks = ["Costs two points"],
        });
        var vm = new SeasonReviewViewModel(session);

        Assert.True(vm.HasPurchasablePerks);
        Assert.Equal(2, vm.DevelopmentPerks.Count);
        Assert.Equal("1 pt", vm.DevelopmentPerks[0].CostText);
        Assert.Equal("2 pts", vm.DevelopmentPerks[1].CostText);
        Assert.True(vm.DevelopmentPerks[1].HasDrawback);

        // Buy Rain Man: the spend goes through the seam and the pool drops to 1, which no longer
        // affords the 2-point perk, so the offer list empties.
        vm.BuyPerkCommand.Execute("rain_man");
        Assert.Contains(session.Spends, s => s.Kind == "perk" && s.Target == "rain_man");
        Assert.Equal(1, vm.AvailableCp);
        Assert.Empty(vm.DevelopmentPerks);
        Assert.False(vm.HasPurchasablePerks);
    }

    [Fact]
    public void Development_NoPerksOfferedWhenNoneAreListed()
    {
        var session = new FakeCareerSession { Review = Review(), Dossier = Dossier(), Cp = 2 };
        var vm = new SeasonReviewViewModel(session);

        Assert.True(vm.HasCharacter);
        Assert.False(vm.HasPurchasablePerks);
        Assert.Empty(vm.DevelopmentPerks);
    }

    [Fact]
    public void Development_UnaffordableRaiseIsANoOp()
    {
        var session = new FakeCareerSession { Review = Review(), Dossier = Dossier(), Cp = 0 };
        var vm = new SeasonReviewViewModel(session);

        Assert.True(vm.HasCharacter);
        Assert.False(vm.HasCp);

        vm.RaiseStatCommand.Execute("pace");   // no points: the command swallows the throw
        Assert.Empty(session.Spends);
        Assert.Equal(0, vm.AvailableCp);
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
