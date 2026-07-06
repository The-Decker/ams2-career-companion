using Companion.Core.Career;

namespace Companion.Tests.Career;

/// <summary>Contract offers rendered as period documents: the same facts written in the era's medium
/// (telegram / fax / email), pure and deterministic (no dates, no RNG).</summary>
public sealed class OfferDocumentTests
{
    [Fact]
    public void Compose_TelegramEra_IsUppercaseWireCopyWithStop_AddressedToTheDriver()
    {
        var doc = OfferDocument.Compose(1967, "Team Lotus", tier: 5, salaryBu: 7.5, playerName: "Kobra Fleetworks");

        Assert.Equal(EraMedium.Telegram, doc.Era.Medium);
        Assert.Contains("STOP", doc.Body);
        Assert.Contains("KOBRA FLEETWORKS", doc.Body); // addressed, uppercased
        Assert.Contains("1967", doc.Body);
        Assert.Contains("7.5", doc.Body);
        Assert.Contains("NUMBER ONE SEAT", doc.Body); // tier-5 flavour
        Assert.Equal(doc.Body.ToUpperInvariant(), doc.Body); // all-caps wire copy
    }

    [Fact]
    public void Compose_FaxEra_IsAMemo_NotAWire()
    {
        var doc = OfferDocument.Compose(1988, "Williams", tier: 4, salaryBu: 5.0, playerName: "Denny Hulme");

        Assert.Equal(EraMedium.Fax, doc.Era.Medium);
        Assert.Contains("RE: 1988", doc.Body);
        Assert.Contains("Dear Denny Hulme", doc.Body);
        Assert.DoesNotContain("STOP", doc.Body);
    }

    [Fact]
    public void Compose_EmailEra_HasSubjectAndSluggedSender()
    {
        var doc = OfferDocument.Compose(2005, "Team Lotus", tier: 3, salaryBu: 4.0, playerName: "Ana");

        Assert.Equal(EraMedium.Email, doc.Era.Medium);
        Assert.Contains("Subject: 2005", doc.Body);
        Assert.Contains("Hi Ana", doc.Body);
        Assert.Contains("teamlotus.f1", doc.Letterhead); // slugged sender address
    }

    [Fact]
    public void Compose_BlankDriverName_FallsBackToDriver()
    {
        var doc = OfferDocument.Compose(1967, "Lotus", tier: 3, salaryBu: 4.0, playerName: "");
        Assert.Contains("TO DRIVER STOP", doc.Body);
    }

    [Fact]
    public void Compose_IsDeterministic_SameInputsProduceTheSameDocument()
    {
        var a = OfferDocument.Compose(1972, "BRM", 2, 3.5, "Mike");
        var b = OfferDocument.Compose(1972, "BRM", 2, 3.5, "Mike");
        Assert.Equal(a, b); // record equality — no clock, no RNG
    }
}
