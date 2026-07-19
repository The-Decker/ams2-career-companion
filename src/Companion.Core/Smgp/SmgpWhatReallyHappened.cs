using System.Text.Json.Serialization;
using Companion.Core.Json;

namespace Companion.Core.Smgp;

/// <summary>
/// The SMGP-universe "What Really Happened" almanac: the SEGA world's OWN account of each circuit on
/// the Super Monaco GP calendar, keyed by the round's VENUE NAME (e.g. "San Marino", "Monaco") so the
/// season 2+ calendar variety, a shuffled venue order, still resolves each place's legend. Loaded
/// from <c>data/rules/smgp/what-really-happened.json</c>. DISPLAY-ONLY, never a fold input (exactly
/// like the news corpora and <see cref="SmgpRivalQuotes"/>): the History tab reveals a venue's entry
/// once the player has finished that race. An absent file (or an un-authored venue) resolves to null,
/// so a non-SMGP install or an un-updated data folder is simply unaffected.
/// </summary>
public sealed class SmgpWhatReallyHappened
{
    private readonly IReadOnlyDictionary<string, SmgpRaceLore> _byVenue;

    private SmgpWhatReallyHappened(IReadOnlyDictionary<string, SmgpRaceLore> byVenue) => _byVenue = byVenue;

    /// <summary>An empty almanac (no file shipped): every lookup returns null and
    /// <see cref="Venues"/> is empty, so the History panel simply hides.</summary>
    public static SmgpWhatReallyHappened Empty { get; } =
        new(new Dictionary<string, SmgpRaceLore>(StringComparer.Ordinal));

    /// <summary>This venue's SMGP-world legend, or null when none is authored for it.</summary>
    public SmgpRaceLore? ForVenue(string venueName) => _byVenue.GetValueOrDefault(venueName);

    /// <summary>The venue names the almanac has an authored entry for (drift-guard source).</summary>
    public IReadOnlyCollection<string> Venues => _byVenue.Keys.ToArray();

    /// <summary>Loads <c>data/rules/smgp/what-really-happened.json</c> from the rules directory, or
    /// <see cref="Empty"/> when the file is absent (back-compat: the History panel just stays hidden).</summary>
    public static SmgpWhatReallyHappened Load(string rulesDirectory)
    {
        string path = Path.Combine(rulesDirectory, "smgp", "what-really-happened.json");
        return File.Exists(path) ? Parse(File.ReadAllText(path)) : Empty;
    }

    public static SmgpWhatReallyHappened Parse(string json)
    {
        var dto = System.Text.Json.JsonSerializer.Deserialize<AlmanacDto>(json, CoreJson.Options)
            ?? new AlmanacDto();
        var byVenue = new Dictionary<string, SmgpRaceLore>(StringComparer.Ordinal);
        foreach (var (venue, lore) in dto.Races)
            if (lore is not null)
                byVenue[venue] = lore;
        return new SmgpWhatReallyHappened(byVenue);
    }

    private sealed record AlmanacDto
    {
        [JsonPropertyName("races")]
        public IReadOnlyDictionary<string, SmgpRaceLore?> Races { get; init; } =
            new Dictionary<string, SmgpRaceLore?>(StringComparer.Ordinal);
    }
}

/// <summary>One circuit's SMGP-world legend, display-only reference content the History tab reveals
/// once the player has raced the venue. Fully fictional (the SEGA universe, never real F1).</summary>
public sealed record SmgpRaceLore
{
    /// <summary>A bold arcade headline for this circuit's legend ("MONACO: WHERE THE CROWN IS WON").</summary>
    public string Title { get; init; } = "";

    /// <summary>One line naming this world's circuit character/nickname.</summary>
    public string Circuit { get; init; } = "";

    /// <summary>"Who the world remembers ruling here", a roster" driver + team ("A. Senna · Madonna").</summary>
    public string Champion { get; init; } = "";

    /// <summary>2-3 short paragraphs of the circuit's SMGP-world legend.</summary>
    public IReadOnlyList<string> Body { get; init; } = [];

    /// <summary>2-3 punchy one-line lore bullets (a record, a rivalry note, a quirk of the place).</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
}
