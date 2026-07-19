using Companion.Core.Packs;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>
/// The ORIGINAL-CIRCUIT lookup rule (Mike's bug report: every smgp-1 Calendar expander showed
/// the WRONG circuit, the join keyed the pack year's SAME-NUMBERED round, but the replica's
/// calendar runs the game's order, not 1990's). <see cref="HistoricalCircuitLookup"/> honors a
/// round's authored history pointer and falls back to the old rule; the shipped smgp-1 pack maps
/// every round onto the 1989 event whose venue it models, validated here against the shipped
/// 1989 reference file, round by round.
/// </summary>
public sealed class SmgpHistoryMappingTests
{
    private static readonly string PacksDirectory = Path.Combine(AppContext.BaseDirectory, "packs");

    private static readonly string HistoryDirectory =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "history");

    [Fact]
    public void Lookup_HonorsTheAuthoredPointer_AndDefaultsToTheOldRule()
    {
        var basePack = TestPackBuilder.TwoRoundPack(); // year 1967, rounds 1-2
        var pack = basePack with
        {
            Season = basePack.Season with
            {
                Rounds =
                [
                    basePack.Season.Rounds[0] with { History = new PackRoundHistoryRef { Year = 1989, Round = 16 } },
                    basePack.Season.Rounds[1], // no pointer, the pack-year default
                ],
            },
        };

        var seasons = new Dictionary<int, HistoricalSeason>
        {
            [1967] = Season(1967, Round(1, "A"), Round(2, "B")),
            [1989] = Season(1989, Round(16, "Z")),
        };
        HistoricalSeason? For(int year) => seasons.GetValueOrDefault(year);

        Assert.Equal("Z", HistoricalCircuitLookup.ForRound(pack, 1, For)?.Name); // the pointer wins
        Assert.Equal("B", HistoricalCircuitLookup.ForRound(pack, 2, For)?.Name); // pack year + same round
        Assert.Null(HistoricalCircuitLookup.ForRound(pack, 3, For));             // no such round
    }

    [Fact]
    public void Smgp1_MapsEveryRound_OntoThe1989EventItsVenueModels()
    {
        // The game's calendar order -> the 1989 Grand Prix whose circuit each round models
        // (docs/dev/smgp-design.md: "Courses model the 1989 F1 circuits").
        var expected = new (int Round, string Gp)[]
        {
            (1, "San Marino Grand Prix"), (2, "Brazilian Grand Prix"), (3, "French Grand Prix"),
            (4, "Hungarian Grand Prix"), (5, "German Grand Prix"), (6, "United States Grand Prix"),
            (7, "Canadian Grand Prix"), (8, "British Grand Prix"), (9, "Italian Grand Prix"),
            (10, "Portuguese Grand Prix"), (11, "Spanish Grand Prix"), (12, "Mexican Grand Prix"),
            (13, "Japanese Grand Prix"), (14, "Belgian Grand Prix"), (15, "Australian Grand Prix"),
            (16, "Monaco Grand Prix"),
        };

        var pack = SeasonPackFiles.Read(Path.Combine(PacksDirectory, "smgp-1")).Parse();
        var history = new HistoricalSeasonStore(HistoryDirectory).ForYear(1989);
        Assert.NotNull(history);

        foreach (var (round, gp) in expected)
        {
            var packRound = pack.Season.Rounds.Single(r => r.Round == round);
            Assert.NotNull(packRound.History);
            Assert.Equal(1989, packRound.History!.Year);

            var historyRound = history!.Rounds.Single(r => r.Round == packRound.History.Round);
            Assert.Equal(gp, historyRound.Name);

            // ...and the shared lookup resolves it to a real circuit with a map key.
            var circuit = HistoricalCircuitLookup.ForRound(
                pack, round, year => year == 1989 ? history : null);
            Assert.NotNull(circuit);
            Assert.False(string.IsNullOrEmpty(circuit!.LayoutId));
        }
    }

    private static HistoricalSeason Season(int year, params HistoricalRound[] rounds) => new()
    {
        Year = year,
        Rounds = rounds,
    };

    private static HistoricalRound Round(int number, string circuitName) => new()
    {
        Round = number,
        Name = $"Round {number}",
        Circuit = new HistoricalCircuit { Name = circuitName, LayoutId = $"layout-{number}" },
    };
}
