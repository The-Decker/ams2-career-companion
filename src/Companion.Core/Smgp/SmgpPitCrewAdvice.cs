using System.Text.Json;
using Companion.Core.Json;

namespace Companion.Core.Smgp;

/// <summary>Venue-keyed SMGP pit-wall advice. Display-only and deterministically selected.</summary>
public sealed class SmgpPitCrewAdvice
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _venues;
    private readonly IReadOnlyList<string> _fallback;

    private SmgpPitCrewAdvice(
        IReadOnlyDictionary<string, IReadOnlyList<string>> venues,
        IReadOnlyList<string> fallback)
    {
        _venues = venues;
        _fallback = fallback;
    }

    public const string Default = "KEEP IT CLEAN. BRING THE CAR HOME.";

    public static SmgpPitCrewAdvice Empty { get; } = new(
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase), []);

    public string Line(string venue, uint seed)
    {
        var lines = _venues.GetValueOrDefault(venue);
        if (lines is not { Count: > 0 })
            lines = _fallback;
        return lines.Count == 0 ? Default : lines[(int)(seed % (uint)lines.Count)];
    }

    public static SmgpPitCrewAdvice Load(string rulesDirectory)
    {
        string path = Path.Combine(rulesDirectory, "smgp", "pit-crew-advice.json");
        return File.Exists(path) ? Parse(File.ReadAllText(path)) : Empty;
    }

    public static SmgpPitCrewAdvice Parse(string json)
    {
        var dto = JsonSerializer.Deserialize<AdviceDto>(json, CoreJson.Options)
            ?? throw new JsonException("pit-crew-advice.json parsed to null.");
        ValidatePool("fallback", dto.Fallback);
        var venues = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (venue, lines) in dto.Venues)
        {
            if (string.IsNullOrWhiteSpace(venue))
                throw new JsonException("pit-crew-advice.json contains an empty venue key.");
            ValidatePool($"venue '{venue}'", lines);
            venues[venue] = lines;
        }
        return new SmgpPitCrewAdvice(venues, dto.Fallback);
    }

    private static void ValidatePool(string name, IReadOnlyList<string> lines)
    {
        if (lines.Count == 0 || lines.Any(string.IsNullOrWhiteSpace))
            throw new JsonException($"pit-crew-advice {name} must contain non-empty lines.");
    }

    private sealed record AdviceDto
    {
        public IReadOnlyList<string> Fallback { get; init; } = [];
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Venues { get; init; } =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
    }
}
