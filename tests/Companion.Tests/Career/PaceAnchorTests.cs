using Companion.Core.Career;
using Companion.Core.Grid;

namespace Companion.Tests.Career;

public class PaceAnchorTests
{
    [Fact]
    public void FirstSampleSeedsTheAnchorDirectly()
    {
        Assert.Equal(93.0, PaceAnchorMath.Update(0.0, 93.0), 12);
    }

    [Fact]
    public void UpdateIsAlphaPointThreeEwma()
    {
        Assert.Equal(0.7 * 90.0 + 0.3 * 96.0, PaceAnchorMath.Update(90.0, 96.0), 12);
    }

    [Fact]
    public void AnchorConvergesOnARepeatedSample()
    {
        double anchor = 85.0;
        double previousDistance = double.MaxValue;
        for (int i = 0; i < 30; i++)
        {
            anchor = PaceAnchorMath.Update(anchor, 93.0);
            double distance = Math.Abs(anchor - 93.0);
            Assert.True(distance < previousDistance, "Each round must move the anchor closer.");
            previousDistance = distance;
        }
        Assert.True(Math.Abs(anchor - 93.0) < 0.01,
            $"After 30 rounds the anchor ({anchor}) must sit on the sample.");
    }

    [Fact]
    public void ImpliedPaceReadsTheAiAtThePlayersFinishRank()
    {
        var grid = CareerTestData.PlayerGrid();
        // AI skills sorted desc: 0.85, 0.70, 0.55. Player P1 ⇒ yardstick 0.85 at slider 90.
        Assert.Equal(90.0 - 5.0 + 10.0 * 0.85, PaceAnchorMath.ImpliedPlayerPace(grid, 1, 90.0), 12);
        // P4 (beyond the 3 AI) clamps to the slowest AI.
        Assert.Equal(90.0 - 5.0 + 10.0 * 0.55, PaceAnchorMath.ImpliedPlayerPace(grid, 4, 90.0), 12);
    }

    // ---------- difficulty mapping (research compression note) ----------

    [Fact]
    public void CompressionEndpoints_AtNinetyPercentSlider()
    {
        // Research: at 90% slider a 1.0-rated AI runs ~95%, a 0.0-rated AI ~85%.
        Assert.Equal(95.0, DifficultyModel.AiPacePercent(1.0, 90.0), 12);
        Assert.Equal(85.0, DifficultyModel.AiPacePercent(0.0, 90.0), 12);
    }

    [Fact]
    public void RecommendationInvertsTheCompressionLine()
    {
        // A player pacing exactly like a 1.0 AI at slider 90 should be told: slider 90.
        Assert.Equal(90, DifficultyModel.RecommendSlider(95.0, 1.0));
        Assert.Equal(90, DifficultyModel.RecommendSlider(85.0, 0.0));
        // Faster player, same target rating ⇒ higher slider.
        Assert.Equal(100, DifficultyModel.RecommendSlider(105.0, 1.0));
    }

    [Fact]
    public void RecommendationClampsToTheInGameRange()
    {
        Assert.Equal(120, DifficultyModel.RecommendSlider(200.0, 0.0));
        Assert.Equal(70, DifficultyModel.RecommendSlider(10.0, 1.0));
    }

    [Fact]
    public void RecommendationRoundsToWholePercent()
    {
        // 90.6 + 5 − 10·0.5 = 90.6 ⇒ 91.
        Assert.Equal(91, DifficultyModel.RecommendSlider(90.6, 0.5));
        // Midpoint rounds away from zero: 90.5 + 5 − 5 = 90.5 ⇒ 91.
        Assert.Equal(91, DifficultyModel.RecommendSlider(90.5, 0.5));
    }

    [Fact]
    public void MedianAiSkillIgnoresThePlayer()
    {
        var grid = CareerTestData.PlayerGrid();
        // AI skills: 0.85, 0.70, 0.55 ⇒ median 0.70 (the player's 0.70 seat is excluded by flag).
        Assert.Equal(0.70, PaceAnchorMath.MedianAiRaceSkill(grid), 12);
    }

    // ---------- qualifying (one-lap) anchor — Increment 2 ----------

    [Fact]
    public void QualifyingAnchorReadsTheOneLapAxis_NotRaceSkill()
    {
        // Skills chosen so the qualifying order differs from the race order:
        //   qualifyingSkill desc: 0.90, 0.50, 0.30   ·   raceSkill: 0.40, 0.60, 0.85
        var grid = CareerTestData.Grid(
            QSeat("driver.pole", race: 0.40, quali: 0.90),
            QSeat("driver.mid", race: 0.60, quali: 0.50),
            QSeat("driver.back", race: 0.85, quali: 0.30),
            QSeat(CareerTestData.PlayerDriverId, race: 0.70, quali: 0.70, player: true));

        // Player qualified P1 ⇒ yardstick is the fastest QUALIFIER (0.90), not the fastest racer.
        Assert.Equal(DifficultyModel.AiPacePercent(0.90, 95.0),
            PaceAnchorMath.ImpliedPlayerQualiPace(grid, 1, 95.0), 12);
        // P3 ⇒ slowest qualifier (0.30); positions beyond the AI count clamp to it.
        Assert.Equal(DifficultyModel.AiPacePercent(0.30, 95.0),
            PaceAnchorMath.ImpliedPlayerQualiPace(grid, 3, 95.0), 12);
        Assert.Equal(DifficultyModel.AiPacePercent(0.30, 95.0),
            PaceAnchorMath.ImpliedPlayerQualiPace(grid, 9, 95.0), 12);

        // The qualifying median (0.50) is distinct from the race median (0.60) — proof of axis.
        Assert.Equal(0.50, PaceAnchorMath.MedianAiQualifyingSkill(grid), 12);
        Assert.Equal(0.60, PaceAnchorMath.MedianAiRaceSkill(grid), 12);
    }

    private static GridSeat QSeat(string id, double race, double quali, bool player = false) => new()
    {
        DriverId = id,
        DriverName = id,
        TeamId = "team",
        TeamName = "team",
        Number = "0",
        Ams2LiveryName = id,
        Ratings = CareerTestData.Ratings(race, quali),
        Reliability = 0.9,
        PowerScalar = 1.0,
        WeightScalar = 1.0,
        DragScalar = 1.0,
        IsPlayer = player,
    };
}
