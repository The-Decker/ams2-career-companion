using Companion.Ams2.CustomAi;
using Companion.Ams2.Preflight;

namespace Companion.Tests.Ams2;

/// <summary>
/// The lenient livery-override scan (fix round): community skin-pack XMLs are routinely not
/// well-formed ('--' runs inside comments, raw '&amp;' in texture paths, mismatched
/// OUTFIT/HELMET tags, multiple roots, all observed on this machine's install). Each file
/// degrades strict → lenient (<see cref="LenientXml.Clean"/>) → regex scrape of the
/// LIVERY_OVERRIDE NAME attributes; a file is unreadable ONLY when even the regex finds
/// nothing. The structured result feeds the ONE-line UI summary.
/// </summary>
public sealed class LiveryOverrideScannerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-livery-scan-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Write(string vehicleFolder, string fileName, string content)
    {
        string directory = Path.Combine(_root, vehicleFolder);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private LiveryScanResult Scan() => LiveryOverrideScanner.ScanDetailed([_root]);

    // ---------- the four real-world file shapes ----------

    private const string WellFormed = """
        <?xml version="1.0" encoding="utf-8" ?>
        <USER_OVERRIDES>
          <LIVERY_OVERRIDE LIVERY="livery_51" NAME="Team Alpha #1">
            <TEXTURE PATH="alpha.dds" />
          </LIVERY_OVERRIDE>
          <LIVERY_OVERRIDE LIVERY="livery_52" NAME="Team Alpha #2" />
        </USER_OVERRIDES>
        """;

    /// <summary>'--' run inside the header comment, illegal XML, strict parse dies. This is
    /// the #1 shape on this machine (48 of 84 strict failures).</summary>
    private const string DashComment = """
        <?xml version="1.0" encoding="utf-8" ?>
        <USER_OVERRIDES>
          <!-- ----------------------------------------
               1988 season table drawn with dashes
               ---------------------------------------- -->
          <LIVERY_OVERRIDE LIVERY="livery_51" NAME="McLaren #12 A. Senna" />
        </USER_OVERRIDES>
        """;

    /// <summary>Raw ampersand inside an attribute value, entity error under strict parsing
    /// (the "'\' is an unexpected token" shape: 33 of 84 on this machine).</summary>
    private const string RawAmpersand = """
        <USER_OVERRIDES>
          <LIVERY_OVERRIDE LIVERY="livery_51" NAME="Bang &amp; Olufsen #7">
            <TEXTURE PATH="skins&misc\bo7.dds" />
          </LIVERY_OVERRIDE>
        </USER_OVERRIDES>
        """;

    /// <summary>Mismatched OUTFIT/HELMET tags, broken beyond any XML parse; only the regex
    /// scrape can save the NAME attributes (formula_classic_g3m4_06_CAN.xml shape).</summary>
    private const string MismatchedTags = """
        <USER_OVERRIDES>
          <LIVERY_OVERRIDE LIVERY="livery_51" NAME="Lotus #99 Works">
            <OUTFIT_OVERRIDE DRIVER="0">
              <TEXTURE PATH="suit.dds" />
            </HELMET_OVERRIDE>
          </LIVERY_OVERRIDE>
          <LIVERY_OVERRIDE LIVERY="livery_52" NAME="Lotus #100 Works" />
        </USER_OVERRIDES>
        """;

    [Fact]
    public void WellFormedFile_ParsesStrictly_NothingRecoveredOrUnreadable()
    {
        Write("formula_vintage_g1m1", "pack.xml", WellFormed);

        var result = Scan();

        Assert.Equal(["Team Alpha #1", "Team Alpha #2"], result.Liveries.Select(l => l.Name));
        Assert.Equal(1, result.FilesScanned);
        Assert.Equal(0, result.FilesRecoveredLeniently);
        Assert.Empty(result.UnreadableFiles);
        Assert.All(result.Liveries, l => Assert.Equal("formula_vintage_g1m1", l.VehicleFolder));
    }

    [Fact]
    public void DashRunComment_RecoversLeniently_WithNoWarning()
    {
        Write("mclaren_mp45b", "mclaren.xml", DashComment);

        var result = Scan();

        var livery = Assert.Single(result.Liveries);
        Assert.Equal("McLaren #12 A. Senna", livery.Name);
        Assert.Equal(1, result.FilesRecoveredLeniently);
        Assert.Empty(result.UnreadableFiles);
    }

    [Fact]
    public void RawAmpersand_RecoversLeniently_PreservingTheEscapedName()
    {
        Write("brabham_bt26", "bo.xml", RawAmpersand);

        var result = Scan();

        var livery = Assert.Single(result.Liveries);
        Assert.Equal("Bang & Olufsen #7", livery.Name); // the authored entity decodes normally
        Assert.Equal(1, result.FilesRecoveredLeniently);
        Assert.Empty(result.UnreadableFiles);
    }

    [Fact]
    public void MismatchedTags_FallBackToTheRegexScrape_AllNamesFound()
    {
        Write("formula_classic_g3m4", "canada.xml", MismatchedTags);

        var result = Scan();

        Assert.Equal(["Lotus #99 Works", "Lotus #100 Works"], result.Liveries.Select(l => l.Name));
        Assert.Equal(1, result.FilesRecoveredLeniently);
        Assert.Empty(result.UnreadableFiles);
    }

    [Fact]
    public void MultipleRootElements_FallBackToTheRegexScrape()
    {
        Write("milano_gt36", "milano.xml", """
            <USER_OVERRIDES>
              <LIVERY_OVERRIDE LIVERY="livery_51" NAME="Milano #1" />
            </USER_OVERRIDES>
            <USER_OVERRIDES>
              <LIVERY_OVERRIDE LIVERY="livery_52" NAME="Milano #2" />
            </USER_OVERRIDES>
            """);

        var result = Scan();

        Assert.Equal(["Milano #1", "Milano #2"], result.Liveries.Select(l => l.Name));
        Assert.Equal(1, result.FilesRecoveredLeniently);
        Assert.Empty(result.UnreadableFiles);
    }

    [Fact]
    public void FileWithNoExtractableNames_IsTheOnlyThingThatWarns()
    {
        string hopeless = Write("arc_camaro", "broken.xml", "not xml at all << no names anywhere");

        var result = Scan();

        Assert.Empty(result.Liveries);
        Assert.Equal(1, result.FilesScanned);
        Assert.Equal(0, result.FilesRecoveredLeniently);
        string warning = Assert.Single(result.UnreadableFiles);
        Assert.Contains(hopeless, warning);
        Assert.Contains("LIVERY_OVERRIDE", warning);
    }

    [Fact]
    public void MixedTree_AggregatesCountsAcrossEveryFile()
    {
        Write("car_a", "a.xml", WellFormed);            // 2 liveries, strict
        Write("car_b", "b.xml", DashComment);           // 1 livery, lenient
        Write("car_c", "c.xml", MismatchedTags);        // 2 liveries, regex
        Write("car_d", "d.xml", "<garbage <<");         // unreadable

        var result = Scan();

        Assert.Equal(5, result.Liveries.Count);
        Assert.Equal(4, result.FilesScanned);
        Assert.Equal(2, result.FilesRecoveredLeniently);
        Assert.Single(result.UnreadableFiles);
        Assert.Equal(
            "Livery scan: 5 liveries from 4 files; 2 recovered leniently; 1 unreadable",
            result.Summary);
    }

    [Fact]
    public void OneBrokenFile_NeverHidesAnotherFilesLiveries()
    {
        Write("car_a", "broken.xml", "<<< nothing here");
        Write("car_a", "good.xml", WellFormed);

        var result = Scan();

        Assert.Equal(2, result.Liveries.Count);
        Assert.Single(result.UnreadableFiles);
    }

    [Fact]
    public void Summary_UsesThousandsSeparators_InvariantCulture()
    {
        var result = new LiveryScanResult
        {
            Liveries = Enumerable.Range(0, 1057)
                .Select(i => new InstalledLivery { Name = $"L{i}", VehicleFolder = "v", SourceFile = "f" })
                .ToList(),
            FilesScanned = 806,
            FilesRecoveredLeniently = 84,
            UnreadableFiles = [],
        };

        Assert.Equal(
            "Livery scan: 1,057 liveries from 806 files; 84 recovered leniently; 0 unreadable",
            result.Summary);
    }

    [Fact]
    public void TupleScan_StaysCompatible_WarningsAreTheUnreadableFiles()
    {
        Write("car_a", "good.xml", DashComment);   // recovers, NOT a warning any more
        Write("car_b", "bad.xml", "<<<");

        var (liveries, warnings) = LiveryOverrideScanner.Scan([_root]);

        Assert.Single(liveries);
        string warning = Assert.Single(warnings);
        Assert.Contains("bad.xml", warning);
    }

    [Fact]
    public void MissingRoots_AreSkipped_NotErrors()
    {
        var result = LiveryOverrideScanner.ScanDetailed(
            [Path.Combine(_root, "does-not-exist"), _root]);

        Assert.Equal(0, result.FilesScanned);
        Assert.Empty(result.UnreadableFiles);
    }

    // ---------- the shared lenient helpers ----------

    [Fact]
    public void ExtractAttributeValues_IsCaseInsensitive_SkipsEmpty_ToleratesSpacing()
    {
        const string text = """
            <livery_override name="Lower Case" />
            <LIVERY_OVERRIDE LIVERY="l1" NAME = "Spaced Equals" />
            <LIVERY_OVERRIDE NAME="" />
            <OTHER_ELEMENT NAME="Wrong Element" />
            """;

        Assert.Equal(
            ["Lower Case", "Spaced Equals"],
            LenientXml.ExtractAttributeValues(text, "LIVERY_OVERRIDE", "NAME"));
    }

    [Fact]
    public void Clean_StripsDashRunComments_AndRepairsBareAmpersands()
    {
        const string dirty = "<a><!-- --- table --- --><b name=\"AMG & BMW\" /></a>";

        string cleaned = LenientXml.Clean(dirty);

        Assert.DoesNotContain("table", cleaned);
        Assert.Contains("AMG &amp; BMW", cleaned);
        // Existing references stay untouched.
        Assert.Equal("<x>&amp;&#38;&#x26;</x>", LenientXml.Clean("<x>&amp;&#38;&#x26;</x>"));
    }

    // ---------- slot capture: active (real number) vs "##" placeholder ----------

    [Fact]
    public void CapturesTheLiverySlot_DistinguishingActiveFromPlaceholder_StrictPath()
    {
        // The exact Skoal shape: #9 slotted (active), #10 a "##" placeholder (installed, not on).
        Write("formula_retro_g3", "formula_retro_g3.xml", """
            <?xml version="1.0" encoding="utf-8" ?>
            <USER_OVERRIDES>
              <LIVERY_OVERRIDE LIVERY="53" NAME="Skoal Bandit Formula 1 Team #9"><TEXTURE PATH="a.dds" /></LIVERY_OVERRIDE>
              <LIVERY_OVERRIDE LIVERY="##" NAME="Skoal Bandit Formula 1 Team #10"><TEXTURE PATH="b.dds" /></LIVERY_OVERRIDE>
            </USER_OVERRIDES>
            """);

        var byName = Scan().Liveries.ToDictionary(l => l.Name);

        Assert.Equal("53", byName["Skoal Bandit Formula 1 Team #9"].Slot);
        Assert.True(byName["Skoal Bandit Formula 1 Team #9"].IsActive);
        Assert.False(byName["Skoal Bandit Formula 1 Team #10"].IsActive); // "##" → not active
    }

    [Fact]
    public void Giant1985AlternatesComment_ActiveBlocksFound_CommentedAlternatesInvisible()
    {
        // Miniature of the real F1_1985 formula_retro_g3.xml: active blocks, then ONE giant
        // comment holding the manual swap instructions AND ~20 alternate "##" blocks. The dash
        // runs make strict parsing fail; after the lenient comment-strip only the ACTIVE blocks
        // remain, the alternates must not be reported (AMS2 never loads them).
        Write("formula_retro_g3", "formula_retro_g3.xml", """
            <?xml version="1.0" encoding="utf-8" ?>
            <USER_OVERRIDES>
            	<LIVERY_OVERRIDE LIVERY="51" NAME="Tyrrell Racing Organisation #3" BASELIVERY="Default">
            		<TEXTURE NAME="BODY" PATH="F1_1985\body3.dds" />
            	</LIVERY_OVERRIDE>
            	<LIVERY_OVERRIDE LIVERY="53" NAME="Skoal Bandit Formula 1 Team #9" BASELIVERY="Default">
            		<TEXTURE NAME="BODY" PATH="F1_1985\body9.dds" />
            	</LIVERY_OVERRIDE>
            <!-- ---------------ALTERNATE LIVERY OPTIONS ------------------
            3. Replace the "##" with the LIVERY number of the one you are replacing (51 to 60).
            ----------------------------------------------------------------
            	<LIVERY_OVERRIDE LIVERY="##" NAME="Skoal Bandit Formula 1 Team #10" BASELIVERY="Default">
            		<TEXTURE NAME="BODY" PATH="F1_1985\body10.dds" />
            	</LIVERY_OVERRIDE>
            -->
            </USER_OVERRIDES>
            """);

        var result = Scan();

        Assert.Equal(
            ["Tyrrell Racing Organisation #3", "Skoal Bandit Formula 1 Team #9"],
            result.Liveries.Select(l => l.Name));
        Assert.All(result.Liveries, l => Assert.True(l.IsActive));
        Assert.Equal(1, result.FilesRecoveredLeniently);
        Assert.Empty(result.UnreadableFiles);
    }

    [Fact]
    public void ExtractElementAttributePairs_ScrapesSlotAndName_OrderIndependent()
    {
        // The regex fallback (for markup no parser survives) must still pair slot + name.
        const string text = """
            <LIVERY_OVERRIDE NAME="Name First #1" LIVERY="51" />
            <LIVERY_OVERRIDE LIVERY="##" NAME="Slot First Placeholder" />
            <LIVERY_OVERRIDE NAME="No Slot" />
            <LIVERY_OVERRIDE LIVERY="52" NAME="" />
            """;

        var pairs = LenientXml.ExtractElementAttributePairs(text, "LIVERY_OVERRIDE", "LIVERY", "NAME");

        Assert.Equal(3, pairs.Count); // the empty-NAME one is skipped
        Assert.Equal(("51", "Name First #1"), pairs[0]);
        Assert.Equal(("##", "Slot First Placeholder"), pairs[1]);
        Assert.Equal(("", "No Slot"), pairs[2]); // NAME present, LIVERY absent → "" slot
    }
}
