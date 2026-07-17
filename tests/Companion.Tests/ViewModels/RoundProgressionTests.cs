using Companion.Core.Character;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>
/// <see cref="ICareerSession.RoundProgression"/> — what one applied round did to the player's
/// progression, projected from that round's journaled <c>player.xp</c> row (the fold's own audit
/// record). A character career's applied round reports its XP movement and banked Skill Points; an
/// un-applied round and a career with no character both report null (the additive seam default).
/// </summary>
public sealed class RoundProgressionTests : IDisposable
{
    private const string PlayerId = "driver.hulme";
    private const long Seed = 20260712;

    private readonly string _root = Directory.CreateTempSubdirectory("companion-roundxp-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static CharacterProfile Character() => new()
    {
        Name = "Rising Star",
        Stats = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["pace"] = 0.55, ["oneLap"] = 0.50, ["craft"] = 0.50, ["racecraft"] = 0.50,
            ["adaptability"] = 0.50, ["marketability"] = 0.55, ["durability"] = 0.5,
        },
        PerkIds = [],
        CpUnspent = 0,
    };

    private CareerSessionService Create(string name, CharacterProfile? character)
    {
        string packDirectory = Path.Combine(_root, name, "pack");
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), packDirectory);
        return CareerSessionService.CreateCareer(
            new CareerCreationRequest
            {
                PackDirectory = packDirectory,
                CareerFilePath = Path.Combine(_root, name, name + ".ams2career"),
                CareerName = name,
                MasterSeed = Seed,
                PlayerLiveryName = TestPackBuilder.StockLivery2,
                Character = character,
            },
            ViewModelTestData.Environment(
                documentsDirectory: Path.Combine(_root, name, "docs"),
                library: TestPackBuilder.Library()));
    }

    /// <summary>Applies the current round with the player WINNING — a strong result, so the round's
    /// XP award is positive on the shipped legacy formula (win bonus + a non-negative expectation term).</summary>
    private static void ApplyWinningRound(ICareerSession session)
    {
        var grid = session.CurrentGrid().Select(s => s.DriverId).ToList();
        var classified = new List<string> { PlayerId };
        classified.AddRange(grid.Where(id => id != PlayerId));
        session.Apply(new ResultDraft
        {
            Classified = classified,
            DidNotFinish = new Dictionary<string, string>(),
            Disqualified = [],
        });
    }

    [Fact]
    public void CharacterCareer_AppliedRound_ReportsItsJournaledXpMovement()
    {
        using var session = Create("charxp", Character());

        // Nothing applied yet: no player.xp row exists for round 1 either.
        Assert.Null(session.RoundProgression(1));

        ApplyWinningRound(session);                                // round 1: a win

        var progression = session.RoundProgression(1);
        Assert.NotNull(progression);
        Assert.Equal(1, progression!.Round);
        Assert.True(progression.XpGained > 0,
            $"a winning round must gain XP (got {progression.XpGained})");
        Assert.True(progression.LevelAfter >= progression.LevelBefore,
            $"levels never regress within a round ({progression.LevelBefore} → {progression.LevelAfter})");
        Assert.Equal(
            Math.Max(0, progression.LevelAfter - progression.LevelBefore),
            progression.LevelsGained);
        Assert.True(progression.SkillPointsAvailable >= 0);

        // Projection stability: a second read of the same journal is value-identical.
        Assert.Equal(progression, session.RoundProgression(1));

        // An un-applied round has no journaled XP row — null, not a zeroed summary.
        Assert.Null(session.RoundProgression(2));
        Assert.Null(session.RoundProgression(99));
    }

    [Fact]
    public void CareerWithoutACharacter_ReportsNull_EvenForAnAppliedRound()
    {
        using var session = Create("nochar", character: null);

        ApplyWinningRound(session);                                // round 1 applied, no character

        Assert.Null(session.RoundProgression(1));
        Assert.Null(session.RoundProgression(99));
    }
}
