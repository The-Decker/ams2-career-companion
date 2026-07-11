using Companion.Ams2.Skins;

namespace Companion.Tests.Ams2;

/// <summary>
/// The active-set rewriter (the 1985-pack shape): a fixed budget of active LIVERY_OVERRIDE slots
/// plus alternates kept INSIDE one giant comment with manual copy-paste instructions. The
/// rewriter does the pack's documented procedure automatically: copy a needed alternate out of
/// the comment into the slot of an active car the round does not field — comment preserved,
/// minimal line edits, backup-first.
/// </summary>
public sealed class ActiveSetRewriterTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-active-set-").FullName;
    private static readonly DateTimeOffset Now = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private const string Retro1985Shape = """
        <?xml version="1.0" encoding="utf-8" ?>
        <USER_OVERRIDES>

        	<LIVERY_OVERRIDE LIVERY="51" NAME="Tyrrell Racing Organisation #3" BASELIVERY="Default">
        		<TEXTURE NAME="BODY" PATH="F1_1985\body3.dds" />
        	</LIVERY_OVERRIDE>

        	<LIVERY_OVERRIDE LIVERY="53" NAME="Skoal Bandit Formula 1 Team #9" BASELIVERY="Default">
        		<TEXTURE NAME="BODY" PATH="F1_1985\body9.dds" />
        	</LIVERY_OVERRIDE>

        	<LIVERY_OVERRIDE LIVERY="54" NAME="Osella Squadra Corse #24" BASELIVERY="Default">
        		<TEXTURE NAME="BODY" PATH="F1_1985\body24.dds" />
        	</LIVERY_OVERRIDE>

        <!-- ---------------ALTERNATE LIVERY OPTIONS ------------------
        To replace one of the above liveries with one of the below options:
        3. Replace the "##" with the LIVERY number of the one you are replacing (51 to 60).
        ----------------------------------------------------------------

        	<LIVERY_OVERRIDE LIVERY="##" NAME="Skoal Bandit Formula 1 Team #10" BASELIVERY="Default">
        		<TEXTURE NAME="BODY" PATH="F1_1985\body10.dds" />
        	</LIVERY_OVERRIDE>

        	<LIVERY_OVERRIDE LIVERY="##" NAME="Haas Lola #33 A. Jones" BASELIVERY="Default">
        		<TEXTURE NAME="BODY" PATH="F1_1985\body33.dds" />
        	</LIVERY_OVERRIDE>

        -->

        </USER_OVERRIDES>
        """;

    private string Write(string content)
    {
        string dir = Path.Combine(_root, "formula_retro_g3");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "formula_retro_g3.xml");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void AvailableNames_SplitsActiveFromCommentedAlternates()
    {
        var (active, alternates) = ActiveSetRewriter.AvailableNames(Retro1985Shape);

        Assert.Equal(
            ["Tyrrell Racing Organisation #3", "Skoal Bandit Formula 1 Team #9", "Osella Squadra Corse #24"],
            active);
        Assert.Equal(
            ["Skoal Bandit Formula 1 Team #10", "Haas Lola #33 A. Jones"],
            alternates);
    }

    [Fact]
    public void Apply_LiftsAnAlternateIntoADisplacedSlot_PreservingTheComment()
    {
        string path = Write(Retro1985Shape);
        // The round fields Tyrrell #3, Skoal #9 and Skoal #10 — but NOT Osella #24.
        string[] desired =
            ["Tyrrell Racing Organisation #3", "Skoal Bandit Formula 1 Team #9", "Skoal Bandit Formula 1 Team #10"];

        var result = ActiveSetRewriter.Apply(path, desired, maxSlot: 60, Now);

        Assert.True(result.Changed);
        Assert.Equal(1, result.Activated);
        Assert.Equal(["Osella Squadra Corse #24"], result.Displaced);
        Assert.Empty(result.NotFound);
        Assert.NotNull(result.BackupPath);
        Assert.Contains("Osella", File.ReadAllText(result.BackupPath!)); // pre-edit snapshot

        string edited = File.ReadAllText(path);
        var (active, alternates) = ActiveSetRewriter.AvailableNames(edited);
        // Skoal #10 took Osella's slot 54; the alternates comment still holds BOTH alternates.
        Assert.Equal(
            ["Tyrrell Racing Organisation #3", "Skoal Bandit Formula 1 Team #9", "Skoal Bandit Formula 1 Team #10"],
            active);
        Assert.Contains("LIVERY=\"54\" NAME=\"Skoal Bandit Formula 1 Team #10\"", edited);
        Assert.Equal(["Skoal Bandit Formula 1 Team #10", "Haas Lola #33 A. Jones"], alternates);
        Assert.Contains("ALTERNATE LIVERY OPTIONS", edited);
        Assert.DoesNotContain("NAME=\"Osella Squadra Corse #24\" BASELIVERY",
            edited.Split("<!--")[0]); // the displaced car is out of the ACTIVE section
    }

    [Fact]
    public void Apply_IsANoOp_WhenTheDesiredSetIsAlreadyActive()
    {
        string path = Write(Retro1985Shape);
        string before = File.ReadAllText(path);

        var result = ActiveSetRewriter.Apply(
            path, ["Tyrrell Racing Organisation #3", "Osella Squadra Corse #24"], maxSlot: 60, Now);

        Assert.False(result.Changed);
        Assert.Null(result.BackupPath);
        Assert.Equal(before, File.ReadAllText(path)); // untouched, no backup
    }

    [Fact]
    public void Apply_AppendsOnFreeSlots_WhenNothingIsDisplaceable_RespectingTheCap()
    {
        string path = Write(Retro1985Shape);
        // Every active car is fielded AND both alternates are wanted: nothing to displace, so
        // they append on free slots (52 is the gap, then 55).
        string[] desired =
        [
            "Tyrrell Racing Organisation #3", "Skoal Bandit Formula 1 Team #9",
            "Osella Squadra Corse #24", "Skoal Bandit Formula 1 Team #10", "Haas Lola #33 A. Jones",
        ];

        var result = ActiveSetRewriter.Apply(path, desired, maxSlot: 60, Now);

        Assert.True(result.Changed);
        Assert.Equal(2, result.Activated);
        Assert.Empty(result.Displaced);
        string edited = File.ReadAllText(path);
        Assert.Contains("LIVERY=\"52\" NAME=\"Skoal Bandit Formula 1 Team #10\"", edited);
        Assert.Contains("LIVERY=\"55\" NAME=\"Haas Lola #33 A. Jones\"", edited);
        Assert.Equal(5, ActiveSetRewriter.AvailableNames(edited).Active.Count);

        // With the cap AT the highest used slot nothing can append: the misses are reported,
        // never forced past the class's livery limit.
        string capped = Write(Retro1985Shape);
        var refused = ActiveSetRewriter.Apply(capped, desired, maxSlot: 51, Now);
        Assert.False(refused.Changed);
        Assert.Equal(2, refused.NotFound.Count);
    }

    [Fact]
    public void Apply_ReportsNamesTheFileDoesNotCarry()
    {
        string path = Write(Retro1985Shape);

        var result = ActiveSetRewriter.Apply(
            path, ["Tyrrell Racing Organisation #3", "Brabham BMW #7 N. Piquet"], maxSlot: 60, Now);

        Assert.False(result.Changed);
        Assert.Equal(["Brabham BMW #7 N. Piquet"], result.NotFound);
    }
}
