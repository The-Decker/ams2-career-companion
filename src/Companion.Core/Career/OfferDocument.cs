using System.Globalization;

namespace Companion.Core.Career;

/// <summary>
/// A contract offer rendered as a PERIOD DOCUMENT (career-hub-design.md, "immersive docs"): the same
/// <c>PlayerOffer</c> facts (team, tier, salary, year) written up in the medium of the era, a 1960s
/// telegram, an 1980s fax memo, or a modern email, so accepting a seat feels like answering a letter
/// from the paddock rather than clicking a table row. Pure + deterministic (no dates read, no RNG):
/// the same inputs always compose the same document, so it adds no un-seeded state.
/// </summary>
public sealed record OfferDocument
{
    /// <summary>The document's masthead / sender line (styled by medium).</summary>
    public required string Letterhead { get; init; }

    /// <summary>The dateline (year + the era's flourish, e.g. "1967 STOP").</summary>
    public required string Dateline { get; init; }

    /// <summary>The offer body, in the era's voice (uppercase wire copy / memo / email).</summary>
    public required string Body { get; init; }

    /// <summary>The resolved era skin (accent, font stack, label) the App renders the document with.</summary>
    public required EraTheme Era { get; init; }

    public static OfferDocument Compose(
        int seasonYear, string teamName, int tier, double salaryBu, string playerName,
        EraThemeCatalog? eraOverrides = null)
    {
        var era = eraOverrides?.ForYear(seasonYear) ?? EraThemes.ForYear(seasonYear);
        string driver = string.IsNullOrWhiteSpace(playerName) ? "Driver" : playerName.Trim();
        string salary = salaryBu.ToString("0.##", CultureInfo.InvariantCulture);
        string seat = SeatPhrase(tier, era.Medium);

        return era.Medium switch
        {
            EraMedium.Telegram => new OfferDocument
            {
                Era = era,
                Letterhead = $"{teamName.ToUpperInvariant()}, RACE DEPT",
                Dateline = $"FILED {seasonYear} {era.DatelineFlourish}".TrimEnd(),
                Body = string.Join(" ", new[]
                {
                    $"TO {driver.ToUpperInvariant()} STOP",
                    $"WE OFFER YOU {seat} FOR THE {seasonYear} CHAMPIONSHIP STOP",
                    $"TERMS {salary} BUDGET UNITS PER SEASON STOP",
                    "REPLY SOONEST STOP",
                }),
            },
            EraMedium.Fax => new OfferDocument
            {
                Era = era,
                Letterhead = $"{teamName}  ·  Contracts",
                Dateline = seasonYear.ToString(CultureInfo.InvariantCulture),
                Body =
                    $"RE: {seasonYear} Driver Contract\n\n" +
                    $"Dear {driver},\n\n" +
                    $"We are pleased to offer you {seat} for the {seasonYear} campaign, " +
                    $"at {salary} BU per season. Kindly confirm by return of fax.\n\n" +
                    $"- {teamName}, Team Principal",
            },
            _ => new OfferDocument
            {
                Era = era,
                Letterhead = $"contracts@{Slug(teamName)}.f1  ·  {teamName}",
                Dateline = seasonYear.ToString(CultureInfo.InvariantCulture),
                Body =
                    $"Subject: {seasonYear} Race Seat\n\n" +
                    $"Hi {driver},\n\n" +
                    $"We'd love to have you in {seat} for {seasonYear}, {salary} BU/season. " +
                    "Let us know and we'll get the paperwork over.\n\n" +
                    $"Cheers,\n{teamName}",
            },
        };
    }

    /// <summary>How the seat is described by tier, in the medium's register (a top seat is a "works
    /// drive"; a backmarker "a drive"). Telegram copy is uppercased by the caller.</summary>
    private static string SeatPhrase(int tier, EraMedium medium) => tier switch
    {
        >= 5 => medium == EraMedium.Telegram ? "OUR NUMBER ONE SEAT" : "our number-one seat",
        4 => medium == EraMedium.Telegram ? "A COMPETITIVE SEAT" : "a competitive seat",
        3 => medium == EraMedium.Telegram ? "A RACE SEAT" : "a race seat",
        2 => medium == EraMedium.Telegram ? "A SEAT ON OUR GRID" : "a seat on our grid",
        _ => medium == EraMedium.Telegram ? "A DRIVE" : "a drive with us",
    };

    private static string Slug(string teamName)
    {
        var chars = teamName.ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c))
            .ToArray();
        return chars.Length == 0 ? "team" : new string(chars);
    }
}
