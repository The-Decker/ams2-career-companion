using Companion.Ams2.Skins;

namespace Companion.Tests.Ams2;

/// <summary>
/// The bubble-car graft lifts a car's three malformed block-groups (LIVERY/HELMET/OUTFIT) from a
/// variant that has it and drops them into a displaced peer's slot in the active model file, renumbering
/// the slot — never re-serializing (community override files are malformed with stray comment markers).
/// </summary>
public class BubbleCarGraftTests
{
    // Two-slot active model file (57 = Ligier #25, 58 = Ligier #26), malformed like the real files.
    private const string Active =
        "<?xml version=\"1.0\"?>\n<USER_OVERRIDES>\n" +
        "\t<LIVERY_OVERRIDE LIVERY=\"57\" NAME=\"1988 Ligier #25 - R. Arnoux\" BASELIVERY=\"Default\">\n" +
        "        <TEXTURE NAME=\"BODY\" PATH=\"F1_Season_1988\\ligier25.dds\" />\n" +
        "\t</LIVERY_OVERRIDE> -->\n" +
        "\t<HELMET_OVERRIDE LIVERY=\"57\" BASEHELMET=\"DEFAULT\">\n" +
        "\t</HELMET_OVERRIDE>\n" +
        "\t<OUTFIT_OVERRIDE LIVERY=\"57\" BASEOUTFIT=\"DEFAULT\">\n" +
        "\t</OUTFIT_OVERRIDE>\n" +
        "\t<LIVERY_OVERRIDE LIVERY=\"58\" NAME=\"1988 Ligier #26 - S. Johansson\" BASELIVERY=\"Default\">\n" +
        "        <TEXTURE NAME=\"BODY\" PATH=\"F1_Season_1988\\ligier26.dds\" />\n" +
        "\t</LIVERY_OVERRIDE> -->\n" +
        "\t<HELMET_OVERRIDE LIVERY=\"58\" BASEHELMET=\"DEFAULT\">\n" +
        "\t</HELMET_OVERRIDE>\n" +
        "\t<OUTFIT_OVERRIDE LIVERY=\"58\" BASEOUTFIT=\"DEFAULT\">\n" +
        "\t</OUTFIT_OVERRIDE>\n</USER_OVERRIDES>\n";

    // A variant that carries Eurobrun #33 (here at slot 52) with its own textures.
    private const string Source =
        "<USER_OVERRIDES>\n" +
        "\t<LIVERY_OVERRIDE LIVERY=\"52\" NAME=\"1988 Eurobrun #33 - S. Modena\" BASELIVERY=\"Default\">\n" +
        "        <TEXTURE NAME=\"BODY\" PATH=\"F1_Season_1988\\EuroBrun33.dds\" />\n" +
        "\t</LIVERY_OVERRIDE> -->\n" +
        "\t<HELMET_OVERRIDE LIVERY=\"52\" BASEHELMET=\"DEFAULT\">\n" +
        "\t</HELMET_OVERRIDE>\n" +
        "\t<OUTFIT_OVERRIDE LIVERY=\"52\" BASEOUTFIT=\"DEFAULT\">\n" +
        "\t</OUTFIT_OVERRIDE>\n</USER_OVERRIDES>\n";

    [Fact]
    public void Graft_SwapsTheDisplacedCarForTheBubbleCar_AtTheDisplacedSlot()
    {
        string? result = BubbleCarGraft.Graft(Active, Source,
            playerName: "1988 Eurobrun #33 - S. Modena",
            displaceName: "1988 Ligier #26 - S. Johansson");

        Assert.NotNull(result);

        // The bubble car now occupies the displaced slot (58) with ITS OWN texture; the displaced car is
        // gone; the untouched peer (Ligier #25 at 57) is preserved verbatim.
        var names = BubbleCarGraft.ActiveNames(result!);
        Assert.Equal(["1988 Ligier #25 - R. Arnoux", "1988 Eurobrun #33 - S. Modena"], names);
        Assert.Contains("LIVERY=\"58\" NAME=\"1988 Eurobrun #33 - S. Modena\"", result);
        Assert.Contains("EuroBrun33.dds", result);       // the real skin texture rode along
        Assert.DoesNotContain("Johansson", result);       // displaced car removed
        Assert.DoesNotContain("ligier26.dds", result);    // ...including its texture

        // All three grafted blocks (LIVERY/HELMET/OUTFIT) were renumbered onto slot 58; the donor's
        // slot 52 no longer appears anywhere.
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(result!, "LIVERY=\"58\"").Count);
        Assert.DoesNotContain("LIVERY=\"52\"", result);
    }

    [Fact]
    public void Graft_ReturnsNull_WhenEitherCarIsAbsent()
    {
        Assert.Null(BubbleCarGraft.Graft(Active, Source, "1988 Nope #99 - X. Y", "1988 Ligier #26 - S. Johansson"));
        Assert.Null(BubbleCarGraft.Graft(Active, Source, "1988 Eurobrun #33 - S. Modena", "1988 Nope #99 - X. Y"));
    }
}
