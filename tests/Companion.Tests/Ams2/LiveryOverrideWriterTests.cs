using System.Text.RegularExpressions;
using Companion.Ams2.Skins;

namespace Companion.Tests.Ams2;

/// <summary>The in-app livery activator (<see cref="LiveryOverrideWriter"/>): turns a community
/// skin pack's "##" placeholder livery ON by assigning it a real slot in the vehicle's
/// USER_OVERRIDES XML — a minimal, in-place textual edit that preserves the rest of the file
/// byte-for-byte (community files are frequently malformed; never re-serialize). This is the fix
/// for Mike's 1985 Skoal #10 (installed but not switched on in-game).</summary>
public sealed class LiveryOverrideWriterTests
{
    // The exact Skoal shape: #9 slotted (active), #10 a "##" placeholder.
    private const string SkoalXml =
        "<?xml version=\"1.0\" encoding=\"utf-8\" ?>\n" +
        "<USER_OVERRIDES>\n" +
        "  <LIVERY_OVERRIDE LIVERY=\"53\" NAME=\"Skoal Bandit Formula 1 Team #9\" BASELIVERY=\"Default\">\n" +
        "    <TEXTURE NAME=\"BODY\" PATH=\"F1_1985\\body9.dds\" />\n" +
        "  </LIVERY_OVERRIDE>\n" +
        "  <LIVERY_OVERRIDE LIVERY=\"##\" NAME=\"Skoal Bandit Formula 1 Team #10\" BASELIVERY=\"Default\">\n" +
        "    <TEXTURE NAME=\"BODY\" PATH=\"F1_1985\\body10.dds\" />\n" +
        "  </LIVERY_OVERRIDE>\n" +
        "</USER_OVERRIDES>\n";

    [Fact]
    public void NextFreeSlot_IsSmallestUnusedFrom51_FillingGaps()
    {
        Assert.Equal(51, LiveryOverrideWriter.NextFreeSlot("<x/>")); // none used
        Assert.Equal(51, LiveryOverrideWriter.NextFreeSlot(SkoalXml)); // 53 used → 51 is free
        const string dense = "<a LIVERY=\"51\"/><a LIVERY=\"52\"/><a LIVERY=\"54\"/>";
        Assert.Equal(53, LiveryOverrideWriter.NextFreeSlot(dense)); // 53 is the gap
    }

    [Fact]
    public void Activate_AssignsTheSlot_ToThePlaceholder()
    {
        string? edited = LiveryOverrideWriter.Activate(SkoalXml, "Skoal Bandit Formula 1 Team #10", 61);

        Assert.NotNull(edited);
        Assert.Contains("LIVERY=\"61\" NAME=\"Skoal Bandit Formula 1 Team #10\"", edited);
        Assert.DoesNotContain("LIVERY=\"##\"", edited); // the only placeholder is now slotted
        Assert.Contains("LIVERY=\"53\" NAME=\"Skoal Bandit Formula 1 Team #9\"", edited); // #9 untouched
    }

    [Fact]
    public void Activate_ChangesOnlyTheOneAttribute_RestIsByteIdentical()
    {
        string? edited = LiveryOverrideWriter.Activate(SkoalXml, "Skoal Bandit Formula 1 Team #10", 61);

        // The whole file is identical except the single LIVERY value "##" → "61".
        Assert.Equal(SkoalXml.Replace("LIVERY=\"##\"", "LIVERY=\"61\""), edited);
    }

    [Fact]
    public void Activate_ReturnsNull_WhenNameIsAbsentOrAlreadyActive()
    {
        Assert.Null(LiveryOverrideWriter.Activate(SkoalXml, "Nonexistent Team #1", 61));
        // #9 is already active (numeric slot) — nothing to activate.
        Assert.Null(LiveryOverrideWriter.Activate(SkoalXml, "Skoal Bandit Formula 1 Team #9", 61));
    }

    [Fact]
    public void Activate_PicksTheFirstPlaceholder_WhenTwoShareAName()
    {
        const string twoVariants =
            "<USER_OVERRIDES>\n" +
            "  <LIVERY_OVERRIDE LIVERY=\"##\" NAME=\"Skoal #10\"><TEXTURE PATH=\"a.dds\"/></LIVERY_OVERRIDE>\n" +
            "  <LIVERY_OVERRIDE LIVERY=\"##\" NAME=\"Skoal #10\"><TEXTURE PATH=\"b.dds\"/></LIVERY_OVERRIDE>\n" +
            "</USER_OVERRIDES>\n";

        string? edited = LiveryOverrideWriter.Activate(twoVariants, "Skoal #10", 55);

        Assert.NotNull(edited);
        // Exactly one placeholder was consumed (the first); the second stays "##".
        Assert.Single(Regex.Matches(edited!, "LIVERY=\"##\""));
        Assert.Contains("LIVERY=\"55\" NAME=\"Skoal #10\"><TEXTURE PATH=\"a.dds", edited);
    }

    [Fact]
    public void Deactivate_PutsAnActiveLiveryBackToPlaceholder()
    {
        string? edited = LiveryOverrideWriter.Deactivate(SkoalXml, "Skoal Bandit Formula 1 Team #9");

        Assert.NotNull(edited);
        Assert.Contains("LIVERY=\"##\" NAME=\"Skoal Bandit Formula 1 Team #9\"", edited);
    }

    [Fact]
    public void Activate_IsCaseSensitiveOnTheName()
    {
        // Livery names bind case-sensitively — a different case is NOT the same livery.
        Assert.Null(LiveryOverrideWriter.Activate(SkoalXml, "skoal bandit formula 1 team #10", 61));
    }

    // ---------- backup-first file I/O ----------

    [Fact]
    public void ActivateInFile_WritesTheEdit_AfterBackingUpTheOriginal()
    {
        string dir = Directory.CreateTempSubdirectory("companion-livery-activate-").FullName;
        try
        {
            string path = Path.Combine(dir, "formula_retro_g3.xml");
            File.WriteAllText(path, SkoalXml);

            var result = LiveryOverrideWriter.ActivateInFile(
                path, "Skoal Bandit Formula 1 Team #10", DateTimeOffset.UnixEpoch);

            Assert.True(result.Success);
            Assert.Equal(51, result.Slot); // next free (53 is the only used slot → 51 is free)
            Assert.NotNull(result.BackupPath);
            Assert.True(File.Exists(result.BackupPath!));

            // The live file now has #10 slotted; the backup still has the "##" placeholder.
            Assert.Contains("LIVERY=\"51\" NAME=\"Skoal Bandit Formula 1 Team #10\"", File.ReadAllText(path));
            Assert.Contains("LIVERY=\"##\"", File.ReadAllText(result.BackupPath!));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ActivateInFile_PicksASlotFreeAcrossSiblingOverrideFiles()
    {
        // AMS2 merges root override XMLs by slot; a slot used in a sibling (_dist) is not free.
        string dir = Directory.CreateTempSubdirectory("companion-livery-siblings-").FullName;
        try
        {
            string main = Path.Combine(dir, "car.xml");
            File.WriteAllText(main,
                "<USER_OVERRIDES><LIVERY_OVERRIDE LIVERY=\"51\" NAME=\"A\"/>" +
                "<LIVERY_OVERRIDE LIVERY=\"##\" NAME=\"Target\"/></USER_OVERRIDES>");
            // Sibling already uses 52 — so the next free across both is 53.
            File.WriteAllText(Path.Combine(dir, "car_dist.xml"),
                "<USER_OVERRIDES><LIVERY_OVERRIDE LIVERY=\"52\" NAME=\"B\"/></USER_OVERRIDES>");

            var result = LiveryOverrideWriter.ActivateInFile(main, "Target", DateTimeOffset.UnixEpoch);

            Assert.True(result.Success);
            Assert.Equal(53, result.Slot); // 51 (main) + 52 (sibling) used → 53
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ActivateInFile_RefusesToExceedTheClassLiveryCap()
    {
        string dir = Directory.CreateTempSubdirectory("companion-livery-cap-").FullName;
        try
        {
            string path = Path.Combine(dir, "car.xml");
            // Slots 51..53 all used; cap of 3 liveries means max slot 53 → the next free (54) is over.
            File.WriteAllText(path,
                "<USER_OVERRIDES>" +
                "<LIVERY_OVERRIDE LIVERY=\"51\" NAME=\"A\"/>" +
                "<LIVERY_OVERRIDE LIVERY=\"52\" NAME=\"B\"/>" +
                "<LIVERY_OVERRIDE LIVERY=\"53\" NAME=\"C\"/>" +
                "<LIVERY_OVERRIDE LIVERY=\"##\" NAME=\"Target\"/></USER_OVERRIDES>");

            var result = LiveryOverrideWriter.ActivateInFile(
                path, "Target", DateTimeOffset.UnixEpoch, slot: null, maxSlot: 53);

            Assert.False(result.Success);
            Assert.Contains("livery limit", result.Message);
            Assert.Contains("LIVERY=\"##\"", File.ReadAllText(path)); // unchanged
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ActivateInFile_Fails_WhenTheNameHasNoPlaceholder()
    {
        string dir = Directory.CreateTempSubdirectory("companion-livery-activate-").FullName;
        try
        {
            string path = Path.Combine(dir, "car.xml");
            File.WriteAllText(path, SkoalXml);

            var result = LiveryOverrideWriter.ActivateInFile(path, "No Such Livery", DateTimeOffset.UnixEpoch);

            Assert.False(result.Success);
            Assert.Null(result.Slot);
            // The file is untouched (no spurious backup/write on failure).
            Assert.Equal(SkoalXml, File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
