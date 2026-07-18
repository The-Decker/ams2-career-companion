using Companion.Core.Character;
using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.Data;
using Companion.ViewModels.Hub;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>
/// The Driver dossier's LEVEL-UP acknowledgment persists as a user preference: the banner state
/// derives from a <c>character:levelup:&lt;level&gt;</c> marker in the career's reading-state store
/// (schema v6), so an unacknowledged level-up survives closing the app instead of silently vanishing
/// on the next open. Driven through <see cref="DossierViewModel"/> over a LOCAL fake session (the
/// shared <c>FakeCareerSession</c> is intentionally not extended) implementing just the dossier +
/// reading-state seams, every other member is the interface's additive default.
/// </summary>
public sealed class LevelUpAcknowledgmentTests
{
    private const string AckKeyPrefix = "character:levelup:";

    [Fact]
    public void FreshCareer_FirstRefresh_SeedsTheMarkerAtTheCurrentLevel_WithNoBanner()
    {
        var session = new LevelUpFakeSession { Dossier = DossierAtLevel(5) };

        var vm = new DossierViewModel(session); // the constructor runs the first Refresh

        // History already lived is seeded silently, a marker at the current level, no banner.
        Assert.False(vm.LevelUpPending);
        Assert.Equal(0, vm.LevelsGained);
        var marker = Assert.Single(session.Reading.Keys, k => k.StartsWith(AckKeyPrefix, StringComparison.Ordinal));
        Assert.Equal(AckKeyPrefix + "5", marker);
        Assert.True(session.Reading[marker].IsRead);
    }

    [Fact]
    public void StoredMarkerBehindTheDossier_ANewViewModelRaisesTheBanner_WithTheLevelsGained()
    {
        // The player last acknowledged level 5; the career has since reached level 8 (app restarted).
        var session = new LevelUpFakeSession { Dossier = DossierAtLevel(8) };
        session.Reading[AckKeyPrefix + "5"] = new NewsReadingState { ReadUtc = "2026-07-01T00:00:00Z" };

        var vm = new DossierViewModel(session);

        Assert.True(vm.LevelUpPending);
        Assert.Equal(3, vm.LevelsGained);
        // Deriving the banner never writes a new marker, only an acknowledgment does.
        Assert.DoesNotContain(AckKeyPrefix + "8", session.Reading.Keys);
    }

    [Fact]
    public void AcknowledgeLevelUp_PersistsTheCurrentLevelMarker_AndClearsTheBanner()
    {
        var session = new LevelUpFakeSession { Dossier = DossierAtLevel(8) };
        session.Reading[AckKeyPrefix + "5"] = new NewsReadingState { ReadUtc = "2026-07-01T00:00:00Z" };

        var vm = new DossierViewModel(session);
        Assert.True(vm.LevelUpPending);

        vm.AcknowledgeLevelUp();

        Assert.False(vm.LevelUpPending);
        Assert.Equal(0, vm.LevelsGained);
        Assert.Contains(AckKeyPrefix + "8", session.Reading.Keys);
        Assert.True(session.Reading[AckKeyPrefix + "8"].IsRead);

        // The persisted marker holds across an app restart: a NEW VM shows no banner.
        var reopened = new DossierViewModel(session);
        Assert.False(reopened.LevelUpPending);
        Assert.Equal(0, reopened.LevelsGained);
    }

    private static CharacterDossier DossierAtLevel(int level) => new()
    {
        Name = "Test Driver",
        Level = level,
        Xp = 1_000,
        XpIntoLevel = 10,
        XpForNextLevel = 100,
        CpUnspent = 2,
        Stats = [new DossierStat("pace", "Pace", 0.55, Talent: true)],
        Perks = [],
    };

    /// <summary>A LOCAL fake implementing only the members the level-up acknowledgment exercises
    /// (dossier + reading state + MarkStoryRead) plus the interface's abstract minimum; everything
    /// else is the additive default. The shared SessionTestSupport fake stays untouched.</summary>
    private sealed class LevelUpFakeSession : ICareerSession
    {
        public CharacterDossier? Dossier { get; set; }

        /// <summary>The persisted reading-state store this fake stands in for (schema v6).</summary>
        public Dictionary<string, NewsReadingState> Reading { get; } = new(StringComparer.Ordinal);

        public CharacterDossier? CharacterDossier() => Dossier;

        public IReadOnlyDictionary<string, NewsReadingState> ReadingState() => Reading;

        public void MarkStoryRead(string storyKey) =>
            Reading[storyKey] = new NewsReadingState { ReadUtc = "2026-07-16T00:00:00Z" };

        // ---- the interface's abstract minimum (never meaningfully exercised here) ----

        public CareerSummary Summary { get; } = new()
        {
            CareerName = "Level-Up Fake",
            SeasonYear = 1967,
            SeriesName = "Test Championship",
            CurrentRound = 1,
            RoundCount = 2,
            PlayerDriverId = "driver.hulme",
            PlayerLiveryName = TestPackBuilder.StockLivery2,
        };

        public SeasonPack Pack { get; } = TestPackBuilder.TwoRoundPack();

        public BriefingModel? CurrentBriefing() => null;

        public StageOutcome StageCurrentGrid() => new() { Success = false, Messages = [] };

        public IReadOnlyList<GridSeat> CurrentGrid() => [];

        public ConfirmModel Preview(ResultDraft draft) =>
            new() { RoundPoints = [], Movements = [], Headline = "" };

        public void Apply(ResultDraft draft) { }

        public StandingsSnapshot? CurrentStandings() => null;

        public IReadOnlyList<StandingsSnapshot> AllSnapshots() => [];

        public int? CurrentSliderRecommendation() => null;

        public SeasonReviewModel? SeasonReview() => null;

        public void AcceptOffer(string teamId) { }
    }
}
