using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.Core.Smgp;
using Companion.ViewModels.Confirm;
using Companion.ViewModels.ResultEntry;
using Companion.ViewModels.Services;
using Companion.ViewModels.Shell;

namespace Companion.Tests.ViewModels;

/// <summary>
/// The SMGP promotion / demotion screen (3c-3): the HomeViewModel shows it as its own full-immersion
/// step AFTER the confirm when the round moved seats — a two-wins offer to accept/decline, or a
/// forced relegation to acknowledge — and does NOT advance the round until it is answered. Plus the
/// session projection (<see cref="ICareerSession.CurrentSmgpPromotion"/>) built from a real pending
/// offer, and that answering it on the screen resolves the 3c-2 fold.
/// </summary>
public sealed class SmgpPromotionScreenTests : IDisposable
{
    private const string SeatA = "Stock Livery #1"; // team.a  LEVEL A
    private const string SeatC = "Stock Livery #3"; // team.c  LEVEL C (the player starts here)
    private const long Seed = 20260711;

    private readonly string _root = Directory.CreateTempSubdirectory("companion-promo-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    // ---------- the HomeViewModel step (fake session, drives the real loop) ----------

    [Fact]
    public void PromotionOffer_ShowsItsOwnStep_ThenAcceptResolvesTheOffer_AndAdvances()
    {
        var session = FakeWithGrid();
        session.Promotion = Offer("AN OFFER FROM MADONNA");
        using var home = new HomeViewModel(session);

        ApplyARound(home);

        // The round did NOT advance to the briefing — the promotion screen owns the step.
        Assert.True(home.IsPromotionStep);
        var screen = Assert.IsType<PromotionViewModel>(home.CurrentContent);
        Assert.Equal("AN OFFER FROM MADONNA", screen.Model.Headline);
        Assert.True(screen.CanDecline);

        screen.AcceptCommand.Execute(null);

        Assert.Equal(new[] { true }, session.ResolvedOffers); // accepted through the 3c-2 seam
        Assert.False(home.IsPromotionStep);                   // advanced off the screen
        Assert.True(home.IsBriefingState);
    }

    [Fact]
    public void PromotionOffer_DeclineResolvesFalse_AndAdvances()
    {
        var session = FakeWithGrid();
        session.Promotion = Offer("AN OFFER FROM BULLETS");
        using var home = new HomeViewModel(session);

        ApplyARound(home);
        var screen = Assert.IsType<PromotionViewModel>(home.CurrentContent);
        screen.DeclineCommand.Execute(null);

        Assert.Equal(new[] { false }, session.ResolvedOffers);
        Assert.True(home.IsBriefingState);
    }

    [Fact]
    public void ForcedDemotion_IsAcknowledgeOnly_AndAdvances_WithoutResolvingAnOffer()
    {
        var session = FakeWithGrid();
        // A demotion (not a pending offer) — already applied inline by the fold; the screen only
        // acknowledges it, so it must NOT call the offer-resolution seam.
        session.Demotion = new SmgpPromotionModel
        {
            Kind = SmgpPromotionKind.Demotion,
            Headline = "RELEGATED TO ZEROFORCE",
            TeamName = "Zeroforce",
            TeamPhotoKey = "zeroforce",
            PlayerImageKey = "player.zeroforce",
        };
        using var home = new HomeViewModel(session);

        ApplyARound(home);

        var screen = Assert.IsType<PromotionViewModel>(home.CurrentContent);
        Assert.False(screen.CanDecline); // a demotion cannot be declined
        Assert.Equal("Onwards", screen.Model.AcceptLabel);

        screen.AcceptCommand.Execute(null);

        Assert.Empty(session.ResolvedOffers); // acknowledge only — no fold decision
        Assert.True(home.IsBriefingState);
    }

    [Fact]
    public void NoSeatChange_AdvancesStraightToTheBriefing_AsBefore()
    {
        var session = FakeWithGrid(); // no promotion, no demotion
        using var home = new HomeViewModel(session);

        ApplyARound(home);

        Assert.False(home.IsPromotionStep);
        Assert.True(home.IsBriefingState); // the shipped loop, unchanged
    }

    // ---------- the session projection (real career, real pending offer) ----------

    [Fact]
    public void CurrentSmgpPromotion_ProjectsThePendingOffersNewTeam_AndResolveMovesTheSeat()
    {
        string packDirectory = Path.Combine(_root, "packs", "promo");
        TestPackBuilder.Write(LadderPack(), packDirectory);
        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs", "promo"),
            library: FourSeatLibrary());
        string careerPath = Path.Combine(_root, "careers", "promo.ams2career");

        using var session = CareerSessionService.CreateCareer(
            new CareerCreationRequest
            {
                PackDirectory = packDirectory,
                CareerFilePath = careerPath,
                CareerName = "promo",
                MasterSeed = Seed,
                PlayerLiveryName = SeatC,
                SmgpMode = true,
            },
            environment);

        // Beat driver.a twice → a two-wins offer is pending (deferred by 3c-2).
        WinAgainst(session, "driver.a");
        WinAgainst(session, "driver.a");

        var promotion = session.CurrentSmgpPromotion();
        Assert.NotNull(promotion);
        Assert.Equal(SmgpPromotionKind.PromotionOffer, promotion!.Kind);
        Assert.True(promotion.CanDecline);
        Assert.Equal("Alpha", promotion.TeamName);                 // the offered (LEVEL A) team
        Assert.Contains("ALPHA", promotion.Headline);
        Assert.Equal("player.a", promotion.PlayerImageKey);        // team-coloured player image
        Assert.Equal("driver.a", promotion.CarKey);                // the offered car preview
        Assert.Equal("driver.a", promotion.RivalName);             // whom you beat twice

        // Accepting on the screen resolves the 3c-2 fold: the seat moves and the offer clears.
        session.ResolveSmgpOffer(accept: true);
        Assert.Null(session.CurrentSmgpPromotion());
        Assert.Equal("team.a", session.CurrentSmgpTeamId());
    }

    // ---------- helpers ----------

    private static FakeCareerSession FakeWithGrid()
    {
        var session = new FakeCareerSession
        {
            Grid =
            [
                Seat("driver.hulme", "2", TestPackBuilder.StockLivery2, isPlayer: true),
                Seat("driver.brabham", "1", TestPackBuilder.StockLivery1, isPlayer: false),
            ],
        };
        return session;
    }

    private static SmgpPromotionModel Offer(string headline) => new()
    {
        Kind = SmgpPromotionKind.PromotionOffer,
        Headline = headline,
        TeamName = "Madonna",
        TeamPhotoKey = "madonna",
        PlayerImageKey = "player.madonna",
        CarKey = "driver.senna",
        RivalName = "Ayrton Senna",
    };

    /// <summary>Drives one round through the shell to Apply: enter the race, classify the two-car
    /// grid, confirm, apply.</summary>
    private static void ApplyARound(HomeViewModel home)
    {
        home.EnterResultCommand.Execute(null);
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

    /// <summary>Applies one round with the player finishing FIRST and naming <paramref name="rival"/>
    /// — a win in the two-wins ladder.</summary>
    private static void WinAgainst(ICareerSession session, string rival)
    {
        var others = session.CurrentGrid()
            .Select(s => s.DriverId)
            .Where(id => !string.Equals(id, session.Summary.PlayerDriverId, StringComparison.Ordinal))
            .ToList();
        session.Apply(new ResultDraft
        {
            Classified = new List<string> { session.Summary.PlayerDriverId }.Concat(others).ToList(),
            DidNotFinish = new Dictionary<string, string>(),
            Disqualified = [],
            SmgpRival = new Companion.Data.SmgpRivalCall { RivalDriverId = rival },
        });
    }

    private static SeasonPack LadderPack()
    {
        var basePack = TestPackBuilder.TwoRoundPack();
        return basePack with
        {
            Manifest = basePack.Manifest with { CareerStyle = SmgpRules.CareerStyle },
            Season = basePack.Season with
            {
                Rounds =
                [
                    TestPackBuilder.Round(1, "1967-01-02"),
                    TestPackBuilder.Round(2, "1967-03-06"),
                    TestPackBuilder.Round(3, "1967-05-07"),
                ],
            },
            Teams =
            [
                Team("team.a", "Alpha", 5),
                Team("team.b", "Bravo", 4),
                Team("team.c", "Charlie", 3),
                Team("team.d", "Delta", 2),
            ],
            Drivers =
            [
                TestPackBuilder.Driver("driver.a"),
                TestPackBuilder.Driver("driver.b"),
                TestPackBuilder.Driver("driver.c"),
                TestPackBuilder.Driver("driver.d"),
            ],
            Entries =
            [
                Entry("team.a", "driver.a", "1", SeatA),
                Entry("team.b", "driver.b", "2", "Stock Livery #2"),
                Entry("team.c", "driver.c", "3", SeatC),
                Entry("team.d", "driver.d", "4", "Stock Livery #4"),
            ],
        };
    }

    private static PackEntry Entry(string teamId, string driverId, string number, string livery) =>
        TestPackBuilder.Entry(teamId, driverId, number, livery) with { Rounds = "1-3" };

    private static PackTeam Team(string id, string name, int prestige) => new()
    {
        Id = id,
        Name = name,
        CarVehicleIds = [TestPackBuilder.VintageCar],
        Reliability = 0.93,
        Prestige = prestige,
        BudgetTier = prestige,
    };

    private static Companion.Ams2.ContentLibrary.Ams2ContentLibrary FourSeatLibrary()
    {
        var library = TestPackBuilder.Library();
        return new()
        {
            ExtractedFrom = library.ExtractedFrom,
            Classes = library.Classes,
            Vehicles = library.Vehicles,
            Tracks = library.Tracks,
            Liveries = new Dictionary<string, Companion.Ams2.ContentLibrary.Ams2LiveryClassEntry>(StringComparer.Ordinal)
            {
                [TestPackBuilder.VintageClass] = new()
                {
                    Name = TestPackBuilder.VintageClass,
                    StockLib1563 = [SeatA, "Stock Livery #2", SeatC, "Stock Livery #4"],
                },
            },
        };
    }
}
