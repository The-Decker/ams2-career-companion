using Companion.Ams2.Scenarios;

namespace Companion.Tests.Scenarios;

public sealed class ScenarioTests
{
    // A minimal scenario .bat in the community shape: a main menu (season vs what-if), a real-season
    // round menu, per-round sections that DEL+COPY the active override, plus the custom-AI copy the
    // app must ignore, and a "what-if" section that must not be treated as a round.
    private static readonly string Bat = string.Join("\n", new[]
    {
        "@ECHO OFF",
        ":main_menu",
        "if \"%choice%\"==\"1\" goto 1988",
        "if \"%choice%\"==\"2\" goto FICT",
        ":1988",
        "if \"%choice%\"==\"1\" goto 1988_Rio",
        "if \"%choice%\"==\"2\" goto 1988_Imola",
        "if \"%choice%\"==\"x\" goto main_menu",
        ":FICT",
        "if \"%choice%\"==\"1\" goto 1988_Fict",
        ":1988_Rio",
        "\tDEL .\\Vehicles\\Textures\\CustomLiveries\\Overrides\\formula_classic_g2m1\\formula_classic_g2m1.xml",
        "\tCOPY .\\Vehicles\\Textures\\CustomLiveries\\Overrides\\formula_classic_g2m1\\formula_classic_g2m1_01Brazil.xml .\\Vehicles\\Textures\\CustomLiveries\\Overrides\\formula_classic_g2m1\\formula_classic_g2m1.xml",
        "\tCOPY .\\Vehicles\\Textures\\CustomLiveries\\Overrides\\mclaren_mp44\\mclaren_mp44_Tobacco.xml .\\Vehicles\\Textures\\CustomLiveries\\Overrides\\mclaren_mp44\\mclaren_mp44.xml",
        "\tCOPY .\\UserData\\CustomAIDrivers\\F-Classic_Gen2_1988.xml .\\UserData\\CustomAIDrivers\\F-Classic_Gen2.xml",
        "\tgoto 1988",
        ":1988_Imola",
        "\tCOPY .\\Vehicles\\Textures\\CustomLiveries\\Overrides\\formula_classic_g2m1\\formula_classic_g2m1_02SanMarino.xml .\\Vehicles\\Textures\\CustomLiveries\\Overrides\\formula_classic_g2m1\\formula_classic_g2m1.xml",
        "\tgoto 1988",
        ":1988_Fict",
        "\tCOPY .\\Vehicles\\Textures\\CustomLiveries\\Overrides\\formula_classic_g2m3\\formula_classic_g2m3_Piquet.xml .\\Vehicles\\Textures\\CustomLiveries\\Overrides\\formula_classic_g2m3\\formula_classic_g2m3.xml",
        "\tgoto FICT",
    });

    [Fact]
    public void Parse_MapsRealSeasonRounds_ToOverrideSwaps_ExcludingCustomAiAndWhatIf()
    {
        var map = BatScenarioReader.Parse(Bat);

        Assert.Equal(new[] { 1, 2 }, map.Keys.OrderBy(k => k)); // two real rounds; the what-if is not a round
        Assert.Equal(2, map[1].Count);                          // g2m1 + mp44 overrides — NOT the custom-AI copy
        Assert.All(map[1], s => Assert.Contains("Overrides", s.SourceRelativePath));
        Assert.DoesNotContain(map[1], s => s.SourceRelativePath.Contains("CustomAIDrivers"));
        Assert.Contains(map[1], s => s.SourceRelativePath.EndsWith(@"formula_classic_g2m1_01Brazil.xml"));
        Assert.Contains(map[1], s => s.TargetRelativePath.EndsWith(@"formula_classic_g2m1.xml")); // active file
        Assert.StartsWith(@"Vehicles\", map[1][0].SourceRelativePath); // ".\" stripped, root-relative
        Assert.Single(map[2]);
    }

    [Fact]
    public void Apply_BacksUpTheActiveOverride_ThenCopiesTheVariant()
    {
        var root = Directory.CreateTempSubdirectory("scen");
        try
        {
            string modelDir = Path.Combine(root.FullName,
                @"Vehicles\Textures\CustomLiveries\Overrides\formula_classic_g2m1");
            Directory.CreateDirectory(modelDir);
            File.WriteAllText(Path.Combine(modelDir, "formula_classic_g2m1.xml"), "OLD-ACTIVE");
            File.WriteAllText(Path.Combine(modelDir, "formula_classic_g2m1_01Brazil.xml"), "BRAZIL");

            var swaps = new[]
            {
                new ScenarioSwap(
                    @"Vehicles\Textures\CustomLiveries\Overrides\formula_classic_g2m1\formula_classic_g2m1_01Brazil.xml",
                    @"Vehicles\Textures\CustomLiveries\Overrides\formula_classic_g2m1\formula_classic_g2m1.xml"),
            };

            var result = ScenarioApplier.Apply(root.FullName, swaps, DateTimeOffset.UnixEpoch);

            Assert.Equal(1, result.Applied);
            Assert.Equal("BRAZIL", File.ReadAllText(Path.Combine(modelDir, "formula_classic_g2m1.xml")));
            var backup = Assert.Single(result.Backups);
            Assert.Equal("OLD-ACTIVE", File.ReadAllText(backup)); // prior active backed up first
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Parse_RealOvertake1988Bat_MapsAll16Rounds_ToFourModelOverrides()
    {
        // Integration check against the actual community .bat when it's installed on this machine;
        // a no-op on machines without it, so the suite stays portable.
        const string batPath =
            @"Y:\SteamLibrary\steamapps\common\Automobilista 2\[F1-1988]Scenarios_FClassicGen2.bat";
        if (!File.Exists(batPath))
            return;

        var map = BatScenarioReader.Parse(File.ReadAllText(batPath));

        Assert.Equal(Enumerable.Range(1, 16), map.Keys.OrderBy(k => k)); // exactly rounds 1..16
        foreach (var (round, swaps) in map)
        {
            Assert.Equal(4, swaps.Count); // g2m1, g2m2, g2m3, mclaren_mp44
            Assert.All(swaps, s => Assert.Contains("Overrides", s.SourceRelativePath));
            Assert.DoesNotContain(swaps, s => s.SourceRelativePath.Contains("CustomAIDrivers"));
            Assert.All(swaps, s => Assert.EndsWith(
                Path.GetFileName(s.TargetRelativePath),
                Path.GetFileNameWithoutExtension(s.TargetRelativePath) + ".xml")); // active file, no suffix
        }
    }

    [Fact]
    public void Apply_MissingVariant_IsSkipped_NotThrown()
    {
        var root = Directory.CreateTempSubdirectory("scen2");
        try
        {
            var swaps = new[] { new ScenarioSwap(@"gone\src.xml", @"gone\dst.xml") };
            var result = ScenarioApplier.Apply(root.FullName, swaps, DateTimeOffset.UnixEpoch);

            Assert.Equal(0, result.Applied);
            Assert.Equal(1, result.Skipped);
            Assert.Single(result.Errors);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
