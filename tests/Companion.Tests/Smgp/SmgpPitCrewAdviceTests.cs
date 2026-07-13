using System.Text.Json;
using Companion.Core.Smgp;

namespace Companion.Tests.Smgp;

public sealed class SmgpPitCrewAdviceTests
{
    [Fact]
    public void VenuePoolAndFallback_AreStableBySeed()
    {
        var advice = SmgpPitCrewAdvice.Parse("""
        {
          "fallback": ["FALLBACK A", "FALLBACK B"],
          "venues": { "Monaco": ["HAIRPIN A", "HAIRPIN B"] }
        }
        """);

        Assert.Equal("HAIRPIN A", advice.Line("Monaco", 0));
        Assert.Equal("HAIRPIN B", advice.Line("Monaco", 1));
        Assert.Equal("FALLBACK B", advice.Line("Unknown", 1));
        Assert.Equal(advice.Line("Monaco", 9), advice.Line("Monaco", 9));
    }

    [Fact]
    public void Parse_RejectsAnEmptyPool()
    {
        Assert.Throws<JsonException>(() => SmgpPitCrewAdvice.Parse(
            "{\"fallback\":[],\"venues\":{}}"));
    }

    [Fact]
    public void ShippedCorpus_CoversEverySmgpVenueIdentity()
    {
        string rules = Path.Combine(AppContext.BaseDirectory, "Fixtures", "rules");
        var advice = SmgpPitCrewAdvice.Load(rules);
        string packDir = Path.Combine(AppContext.BaseDirectory, "packs", "smgp-1");
        var pack = Companion.Core.Packs.PackLoader.Parse(
            File.ReadAllText(Path.Combine(packDir, "pack.json")),
            File.ReadAllText(Path.Combine(packDir, "season.json")),
            File.ReadAllText(Path.Combine(packDir, "teams.json")),
            File.ReadAllText(Path.Combine(packDir, "drivers.json")),
            File.ReadAllText(Path.Combine(packDir, "entries.json")));

        foreach (var round in pack.Season.Rounds)
            Assert.NotEqual(SmgpPitCrewAdvice.Default, advice.Line(round.Name, 0));
    }
}
