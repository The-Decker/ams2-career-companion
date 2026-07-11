using Companion.Ams2.Skins;

namespace Companion.Tests.Ams2;

/// <summary>
/// Comment awareness of the line-level block-group parser (the 1985-pack shape): packs may keep
/// alternate LIVERY_OVERRIDE blocks inside one giant <!-- --> comment with manual swap
/// instructions. AMS2 never loads those, so they must not be counted as active cars or offered as
/// displacement targets — a graft written into a comment is a silent in-game no-op. Stray "-->"
/// orphans (the 1988 shape, no opening "&lt;!--") must NOT count as comments.
/// </summary>
public sealed class BubbleCarGraftCommentAwarenessTests
{
    /// <summary>Miniature of the real F1_1985 formula_retro_g3.xml: two active blocks, then the
    /// alternates section — instructions AND example blocks all inside ONE comment whose dash runs
    /// make the file malformed for strict XML.</summary>
    private const string Retro1985Shape = """
        <?xml version="1.0" encoding="utf-8" ?>
        <USER_OVERRIDES>

        	<LIVERY_OVERRIDE LIVERY="51" NAME="Tyrrell Racing Organisation #3" BASELIVERY="Default">
        		<PREVIEWIMAGE PATH="F1_1985\preview3.dds" />
        		<TEXTURE NAME="BODY" PATH="F1_1985\body3.dds" />
        	</LIVERY_OVERRIDE>

        	<LIVERY_OVERRIDE LIVERY="53" NAME="Skoal Bandit Formula 1 Team #9" BASELIVERY="Default">
        		<PREVIEWIMAGE PATH="F1_1985\preview9.dds" />
        		<TEXTURE NAME="BODY" PATH="F1_1985\body9.dds" />
        	</LIVERY_OVERRIDE>

        <!-- ---------------ALTERNATE LIVERY OPTIONS ------------------
        To replace one of the above liveries with one of the below options:
        3. Replace the "##" with the LIVERY number of the one you are replacing (51 to 60).
        ----------------------------------------------------------------

        	<LIVERY_OVERRIDE LIVERY="##" NAME="Skoal Bandit Formula 1 Team #10" BASELIVERY="Default">
        		<PREVIEWIMAGE PATH="F1_1985\preview10.dds" />
        		<TEXTURE NAME="BODY" PATH="F1_1985\body10.dds" />
        	</LIVERY_OVERRIDE>

        	<LIVERY_OVERRIDE LIVERY="##" NAME="Tyrrell Racing Organisation #3" BASELIVERY="Default">
        		<PREVIEWIMAGE PATH="F1_1985\preview3-alt.dds" />
        		<TEXTURE NAME="BODY" PATH="F1_1985\body3-alt.dds" />
        	</LIVERY_OVERRIDE>

        -->

        </USER_OVERRIDES>
        """;

    private const string DonorSource = """
        <USER_OVERRIDES>
        	<LIVERY_OVERRIDE LIVERY="55" NAME="Haas Lola #33 A. Jones" BASELIVERY="Default">
        		<PREVIEWIMAGE PATH="F1_1985\preview33.dds" />
        		<TEXTURE NAME="BODY" PATH="F1_1985\body33.dds" />
        	</LIVERY_OVERRIDE>
        </USER_OVERRIDES>
        """;

    [Fact]
    public void ActiveNames_ExcludeBlocksInsideTheAlternatesComment()
    {
        Assert.Equal(
            ["Tyrrell Racing Organisation #3", "Skoal Bandit Formula 1 Team #9"],
            BubbleCarGraft.ActiveNames(Retro1985Shape));
    }

    [Fact]
    public void Graft_RefusesACommentedDisplacementTarget()
    {
        // Skoal #10 only exists inside the comment — AMS2 never loads it, so displacing it would
        // write the player's car where the game cannot see it.
        Assert.Null(BubbleCarGraft.Graft(
            Retro1985Shape, DonorSource, "Haas Lola #33 A. Jones", "Skoal Bandit Formula 1 Team #10"));
    }

    [Fact]
    public void Graft_DisplacingAnActiveCar_KeepsTheAlternatesCommentVerbatim()
    {
        string? grafted = BubbleCarGraft.Graft(
            Retro1985Shape, DonorSource, "Haas Lola #33 A. Jones", "Skoal Bandit Formula 1 Team #9");

        Assert.NotNull(grafted);
        Assert.Contains("LIVERY=\"53\" NAME=\"Haas Lola #33 A. Jones\"", grafted);
        // The whole alternates comment — instructions AND example blocks — survives.
        Assert.Contains("ALTERNATE LIVERY OPTIONS", grafted);
        Assert.Contains("preview10.dds", grafted);
        Assert.Equal(
            ["Tyrrell Racing Organisation #3", "Haas Lola #33 A. Jones"],
            BubbleCarGraft.ActiveNames(grafted!));
    }

    [Fact]
    public void Graft_NeverLiftsADonorFromInsideAComment()
    {
        // The donor name only exists as a commented alternate in the source — there is no active
        // block to lift, so the graft must refuse rather than copy comment text.
        Assert.Null(BubbleCarGraft.Graft(
            DonorSource, Retro1985Shape, "Skoal Bandit Formula 1 Team #10", "Haas Lola #33 A. Jones"));
    }
}
