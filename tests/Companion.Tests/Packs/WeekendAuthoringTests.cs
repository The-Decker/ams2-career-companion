using Companion.Core.Packs;

namespace Companion.Tests.Packs;

/// <summary>
/// v0.4.0 pack-authoring guard: every bundled pack authors the REAL historical weekend on every
/// round — practice + qualifying + exactly ONE race labelled "Grand Prix". Sprint races only
/// happened in the real seasons historically, and none of the bundled eras (1967–2000) ran them,
/// so no bundled round may declare a second race or a sprint table. Directory-driven like
/// <see cref="ReferencePackTests"/>, so a newly added pack is held to the same bar automatically.
/// </summary>
public class WeekendAuthoringTests
{
    private static string PacksDirectory => Path.Combine(AppContext.BaseDirectory, "packs");

    public static TheoryData<string> BundledPackIds()
    {
        var data = new TheoryData<string>();
        foreach (var dir in Directory.GetDirectories(PacksDirectory).OrderBy(d => d, StringComparer.Ordinal))
            data.Add(Path.GetFileName(dir)!);
        return data;
    }

    [Theory]
    [MemberData(nameof(BundledPackIds))]
    public void EveryRound_AuthorsTheHistoricalWeekend_SingleGrandPrixNoSprint(string packId)
    {
        string dir = Path.Combine(PacksDirectory, packId);
        string Read(string file) => File.ReadAllText(Path.Combine(dir, file));
        var pack = PackLoader.Parse(
            Read("pack.json"), Read("season.json"), Read("teams.json"),
            Read("drivers.json"), Read("entries.json"));

        Assert.All(pack.Season.Rounds, round =>
        {
            Assert.True(round.Weekend is not null,
                $"{packId} round {round.Round} ({round.Name}) has no weekend block.");
            Assert.True(round.Weekend!.Practice is { Present: true },
                $"{packId} round {round.Round} has no practice session.");
            Assert.True(round.Weekend.Qualifying is { Present: true },
                $"{packId} round {round.Round} has no qualifying session.");

            // The historical shape: exactly one race, the Grand Prix, on the round's default
            // (primary/alternate-per-round) table — never a sprint, never a second race.
            var race = Assert.Single(round.Weekend.Races);
            Assert.Equal("Grand Prix", race.Label);
            Assert.Null(race.PointsTable);
        });
    }
}
