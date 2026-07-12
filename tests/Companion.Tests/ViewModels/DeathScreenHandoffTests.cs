using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.ViewModels.Confirm;
using Companion.ViewModels.ResultEntry;
using Companion.ViewModels.Services;
using Companion.ViewModels.Shell;

namespace Companion.Tests.ViewModels;

/// <summary>
/// Character death &amp; injury (Slice 3 review fix): when a fatal accident ENDS the career, the shell must
/// hand off to the death screen from the DB-FREE mortality status — for a Hardcore death the session's DB
/// is already disposed and the file deleted, so touching Summary/Briefing would crash. ApplyDraft must
/// route to <see cref="HomeViewModel.CareerOver"/> WITHOUT querying the session's DB again.
/// </summary>
public sealed class DeathScreenHandoffTests
{
    [Fact]
    public void HardcoreDeath_RoutesToCareerOver_WithoutTouchingTheDisposedDb()
    {
        var session = new DeathSession
        {
            Death = Status(deceased: true, deleted: true),
        };
        using var home = new HomeViewModel(session);

        // Applying the fatal round must NOT throw — the death handoff never queries Summary (which the
        // fake makes throw once "disposed", mirroring the real deleted-DB session).
        ApplyARound(home);

        Assert.NotNull(home.CareerOver);
        Assert.True(home.CareerOver!.CareerFileDeleted);
        Assert.Equal(0, session.SummaryReadsAfterApply); // Summary was never queried post-death
    }

    [Fact]
    public void NormalDeath_RoutesToCareerOver()
    {
        var session = new DeathSession
        {
            Death = Status(deceased: true, deleted: false),
        };
        using var home = new HomeViewModel(session);

        ApplyARound(home);

        Assert.NotNull(home.CareerOver);
        Assert.True(home.CareerOver!.Deceased);
        Assert.False(home.CareerOver.CareerFileDeleted);
        Assert.Equal(0, session.SummaryReadsAfterApply);
    }

    private static PlayerMortalityStatus Status(bool deceased, bool deleted) => new()
    {
        Mode = deleted ? Companion.Core.Career.MortalityMode.Hardcore : Companion.Core.Career.MortalityMode.Normal,
        Deceased = deceased,
        SeasonEndingInjury = false,
        RaceSuspensionRemaining = 0,
        CareerFileDeleted = deleted,
    };

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

    /// <summary>A session that folds a fatal round: Apply "disposes" (further Summary reads throw, like
    /// a deleted-DB session), and PlayerMortality() reports the death from a DB-free status.</summary>
    private sealed class DeathSession : ICareerSession
    {
        private readonly FakeCareerSession _inner = new()
        {
            Grid =
            [
                Seat("driver.hulme", "2", TestPackBuilder.StockLivery2, isPlayer: true),
                Seat("driver.brabham", "1", TestPackBuilder.StockLivery1, isPlayer: false),
            ],
        };
        private bool _applied;

        public required PlayerMortalityStatus Death { get; init; }

        public int SummaryReadsAfterApply { get; private set; }

        public CareerSummary Summary
        {
            get
            {
                if (_applied)
                {
                    SummaryReadsAfterApply++;
                    throw new InvalidOperationException("The career DB is disposed (the file was deleted).");
                }
                return _inner.Summary;
            }
        }

        public void Apply(ResultDraft draft) => _applied = true;

        public PlayerMortalityStatus PlayerMortality() => Death;

        // The rest is delegated to the exhaustive fake (all used BEFORE apply, so the DB is "live").
        public SeasonPack Pack => _inner.Pack;
        public BriefingModel? CurrentBriefing() => _inner.CurrentBriefing();
        public StageOutcome StageCurrentGrid() => _inner.StageCurrentGrid();
        public IReadOnlyList<GridSeat> CurrentGrid() => _inner.CurrentGrid();
        public ConfirmModel Preview(ResultDraft draft) => _inner.Preview(draft);
        public StandingsSnapshot? CurrentStandings() => _inner.CurrentStandings();
        public IReadOnlyList<StandingsSnapshot> AllSnapshots() => _inner.AllSnapshots();
        public int? CurrentSliderRecommendation() => _inner.CurrentSliderRecommendation();
        public SeasonReviewModel? SeasonReview() => _inner.SeasonReview();
        public void AcceptOffer(string teamId) => _inner.AcceptOffer(teamId);
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
