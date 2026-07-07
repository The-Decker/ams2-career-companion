using Companion.Core.Career;
using Companion.Core.Determinism;

namespace Companion.Core.News;

/// <summary>
/// Renders a round's <see cref="NewsFacts"/> into a period-voiced article body through a
/// <see cref="NewsArticleBank"/>, using a <c>headlines</c> PCG32 stream constructed IDENTICALLY
/// to the shipped headline path (<c>CareerStreams.Headlines</c>, keyed by year + round +
/// "race"). Centralizing the stream construction here keeps the two consumers — the sim's
/// headline pick and the read-side article render — in lockstep on the same deterministic key,
/// so the body is a pure function of <c>(masterSeed, journal row)</c> and re-derives
/// byte-identically on replay.
/// </summary>
public static class NewsArticleComposer
{
    /// <summary>Builds the deterministic article body, or null when the bank has no template
    /// for the facts' <c>phase|cause</c> (the caller keeps the headline as the whole story).
    /// A fresh stream is created per call, so the render is independent of, and consistent
    /// with, every prior render of the same round.
    ///
    /// <paramref name="streamDiscriminator"/> is the third stream key component (default
    /// <c>"race"</c>, matching the shipped race-headline path). A season-summary article passes
    /// <c>"season"</c> so it draws from a distinct sub-stream than that round's race body — the
    /// same year+round can therefore carry both a race article and, at year end, a season article
    /// without one determining the other's wording.</summary>
    public static string? Compose(
        NewsArticleBank bank, NewsFacts facts, ulong masterSeed, string streamDiscriminator = "race")
    {
        ArgumentNullException.ThrowIfNull(bank);
        ArgumentNullException.ThrowIfNull(facts);

        var stream = new StreamFactory(masterSeed)
            .CreateStream(CareerStreams.Headlines, facts.Year, facts.Round, streamDiscriminator);
        return bank.BuildBody(facts, stream);
    }
}
