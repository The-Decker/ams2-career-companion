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
        Assert.Equal(2, map[1].Count);                          // g2m1 + mp44 overrides, NOT the custom-AI copy
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

    // The newer selector shape (1995/1996-1997/2010/2016): one bat serves multiple seasons, and each
    // round's menu section only PRINTS its changes + a Y/N prompt, then `goto INSTALL_<ID>` where the
    // real COPY block lives. The confirm section also has `goto main_menu` (cancel) and the INSTALL
    // block DEL/COPYs the class custom-AI file (incl. PERF_MODE-conditional variants), neither must
    // be treated as a livery-override swap.
    private static readonly string TwoHopBat = string.Join("\n", new[]
    {
        "@ECHO OFF",
        ":main_menu",
        "if \"%choice%\"==\"4\" goto 1996",
        "if \"%choice%\"==\"5\" goto 1997",
        ":1996",
        "if \"%choice%\"==\"1\" goto 1996_AUSTRALIA",
        "if \"%choice%\"==\"5\" goto 1996_SANMARINO",
        "if \"%choice%\"==\"x\" goto main_menu",
        ":1997",
        "if \"%choice%\"==\"1\" goto 1997_AUSTRALIA",
        "if \"%choice%\"==\"x\" goto main_menu",
        ":1996_AUSTRALIA",
        "\tECHO 1996 AUSTRALIAN GRAND PRIX",
        "\tif /I \"%confirm%\"==\"Y\" (",
        "\t\tcall :SAVE_STATE",
        "\t\tgoto INSTALL_1996_AUSTRALIA",
        "\t)",
        "\tif /I \"%confirm%\"==\"N\" (",
        "\t\tgoto main_menu",
        "\t)",
        ":1996_SANMARINO",
        "\tECHO 1996 SAN MARINO GRAND PRIX",
        "\tgoto INSTALL_1996_SANMARINO",
        ":1997_AUSTRALIA",
        "\tgoto INSTALL_1997_AUSTRALIA",
        ":INSTALL_1996_AUSTRALIA",
        "\tDEL .\\Vehicles\\Textures\\CustomLiveries\\Overrides\\formula_v10_g1\\formula_v10_g1.xml",
        "\tCOPY .\\Vehicles\\Textures\\CustomLiveries\\Overrides\\formula_v10_g1\\formula_v10_g1_1996_01AUS.xml .\\Vehicles\\Textures\\CustomLiveries\\Overrides\\formula_v10_g1\\formula_v10_g1.xml",
        "\tCOPY .\\Vehicles\\Textures\\CustomLiveries\\Overrides\\mclaren_mp4_12\\mclaren_mp4_12_1996_01AUS.xml .\\Vehicles\\Textures\\CustomLiveries\\Overrides\\mclaren_mp4_12\\mclaren_mp4_12.xml",
        "\tDEL .\\UserData\\CustomAIDrivers\\F-V10_Gen1.xml",
        "\tif \"%PERF_MODE%\"==\"EQUAL PERFORMANCE\" ( COPY .\\UserData\\CustomAIDrivers\\F-V10_Gen1_1996_Equal.xml .\\UserData\\CustomAIDrivers\\F-V10_Gen1.xml )",
        "\tgoto main_menu",
        ":INSTALL_1996_SANMARINO",
        // The non-monotonic case: the main file advances to 05SMR while the McLaren KEEPS its 02BRA
        // livery, a plain round-number binder cannot express "car A at R5, car B still at R2".
        "\tCOPY .\\Vehicles\\Textures\\CustomLiveries\\Overrides\\formula_v10_g1\\formula_v10_g1_1996_05SMR.xml .\\Vehicles\\Textures\\CustomLiveries\\Overrides\\formula_v10_g1\\formula_v10_g1.xml",
        "\tCOPY .\\Vehicles\\Textures\\CustomLiveries\\Overrides\\mclaren_mp4_12\\mclaren_mp4_12_1996_02BRA.xml .\\Vehicles\\Textures\\CustomLiveries\\Overrides\\mclaren_mp4_12\\mclaren_mp4_12.xml",
        "\tgoto main_menu",
        ":INSTALL_1997_AUSTRALIA",
        "\tCOPY .\\Vehicles\\Textures\\CustomLiveries\\Overrides\\formula_v10_g1\\formula_v10_g1_1997_01AUS.xml .\\Vehicles\\Textures\\CustomLiveries\\Overrides\\formula_v10_g1\\formula_v10_g1.xml",
        "\tgoto main_menu",
    });

    [Fact]
    public void Parse_FollowsConfirmSection_GotoInstall_ForOverrideSwaps()
    {
        var map = BatScenarioReader.Parse(TwoHopBat, ":1996");

        Assert.Equal(new[] { 1, 5 }, map.Keys.OrderBy(k => k));
        // Round 1: the two livery overrides from the followed INSTALL_ block, NOT the class custom-AI
        // copy, and NOT the PERF_MODE-conditional custom-AI copy.
        Assert.Equal(2, map[1].Count);
        Assert.All(map[1], s => Assert.Contains("Overrides", s.SourceRelativePath));
        Assert.DoesNotContain(map[1], s => s.SourceRelativePath.Contains("CustomAIDrivers"));
        Assert.All(map[1], s => Assert.EndsWith(
            Path.GetFileNameWithoutExtension(s.TargetRelativePath) + ".xml",
            s.TargetRelativePath)); // active <model>.xml, suffix stripped
    }

    [Fact]
    public void Parse_TwoHop_PreservesNonMonotonicPerModelVariant()
    {
        var map = BatScenarioReader.Parse(TwoHopBat, ":1996");

        // R5 San Marino: the main formula file is on its 05SMR variant, but the McLaren is STILL on its
        // 02BRA livery, the exact per-model, non-monotonic state the .bat encodes and a round-number
        // change-point binder loses.
        Assert.Contains(map[5], s => s.SourceRelativePath.EndsWith(@"formula_v10_g1_1996_05SMR.xml"));
        Assert.Contains(map[5], s => s.SourceRelativePath.EndsWith(@"mclaren_mp4_12_1996_02BRA.xml"));
    }

    [Fact]
    public void Parse_SelectsSeasonMenu_ByLabel_NotTheOtherYear()
    {
        var y1997 = BatScenarioReader.Parse(TwoHopBat, ":1997");

        Assert.Equal(new[] { 1 }, y1997.Keys.OrderBy(k => k)); // only 1997's single round menu entry
        Assert.Contains(y1997[1], s => s.SourceRelativePath.EndsWith(@"formula_v10_g1_1997_01AUS.xml"));
        Assert.DoesNotContain(y1997[1], s => s.SourceRelativePath.Contains("1996"));
    }

    [Fact]
    public void Parse_Real1996_1997Bat_MapsBothSeasons_WithNonMonotonicSanMarino()
    {
        // Integration check against the actual community selector when installed; a no-op elsewhere.
        const string batPath =
            @"Y:\SteamLibrary\steamapps\common\Automobilista 2\[F1_1996-1997]Scenarios_FV10G1.bat";
        if (!File.Exists(batPath))
            return;
        string text = File.ReadAllText(batPath);

        var y1996 = BatScenarioReader.Parse(text, ":1996");
        Assert.Equal(Enumerable.Range(1, 16), y1996.Keys.OrderBy(k => k));
        Assert.All(y1996.Values, swaps => Assert.All(swaps, s =>
        {
            Assert.Contains("Overrides", s.SourceRelativePath);
            Assert.DoesNotContain("CustomAIDrivers", s.SourceRelativePath);
        }));
        // The real non-monotonic R5: formula file on 05SMR, McLaren held back on 02BRA.
        Assert.Contains(y1996[5], s => s.SourceRelativePath.EndsWith(@"formula_v10_g1_1996_05SMR.xml"));
        Assert.Contains(y1996[5], s => s.SourceRelativePath.EndsWith(@"mclaren_mp4_12_1996_02BRA.xml"));

        var y1997 = BatScenarioReader.Parse(text, ":1997");
        Assert.Equal(Enumerable.Range(1, 17), y1997.Keys.OrderBy(k => k)); // 1997 ran 17 rounds
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
