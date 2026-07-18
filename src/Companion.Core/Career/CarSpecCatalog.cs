using System.Text.Json;
using Companion.Core.Json;

namespace Companion.Core.Career;

/// <summary>The five arcade car-spec bars (each 0..<c>CarSpecCatalog.BarMax</c>), the classic Super
/// Monaco GP car-select readout: ENGine, Transmission, SUSpension, TIRE, BRAke.</summary>
public sealed record CarSpecBars
{
    public int Engine { get; init; }
    public int Transmission { get; init; }
    public int Suspension { get; init; }
    public int Tyre { get; init; }
    public int Brake { get; init; }
}

/// <summary>One car's spec card: the machine name, engine, peak power, and the five bars, the
/// arcade car-select panel Mike wants on the character and rival screens. DISPLAY-ONLY (never a fold
/// input, like the news/quote corpora).</summary>
public sealed record CarSpec
{
    public required string MachineName { get; init; }
    public string Engine { get; init; } = "";
    public int MaxPowerHp { get; init; }
    public required CarSpecBars Bars { get; init; }
}

/// <summary>
/// Per-car spec cards, keyed by TEAM id or VEHICLE id, loaded from <c>data/rules/car-specs.json</c>.
/// A team-id key wins over its car's vehicle-id key, so a pack can give every team its own machine
/// name/bars OR let all teams on a shared car model fall back to that model's row. Absent-tolerant:
/// a missing file, or a car with no entry, returns null so the card simply collapses, which is why
/// the whole pipeline ships now and Mike's real numbers are the only later edit (zero code change).
/// DISPLAY-ONLY; never folded.
/// </summary>
public sealed class CarSpecCatalog
{
    private readonly IReadOnlyDictionary<string, CarSpec> _byKey;

    private CarSpecCatalog(IReadOnlyDictionary<string, CarSpec> byKey, int barMax)
    {
        _byKey = byKey;
        BarMax = barMax;
    }

    /// <summary>The top of every bar's 0..N scale (the arcade drew fixed-length bars); data-driven.</summary>
    public int BarMax { get; }

    /// <summary>An empty catalog (no file shipped): every lookup returns null.</summary>
    public static CarSpecCatalog Empty { get; } =
        new(new Dictionary<string, CarSpec>(StringComparer.Ordinal), 8);

    /// <summary>The spec for a team or its car, team id first (a per-team override), then the vehicle
    /// id (the shared-model default); null when neither is authored, so the card collapses.</summary>
    public CarSpec? For(string? teamId, string? vehicleId)
    {
        if (teamId is not null && _byKey.TryGetValue(teamId, out var byTeam))
            return byTeam;
        if (vehicleId is not null && _byKey.TryGetValue(vehicleId, out var byVehicle))
            return byVehicle;
        return null;
    }

    public static CarSpecCatalog Load(string rulesDirectory)
    {
        string path = Path.Combine(rulesDirectory, "car-specs.json");
        return File.Exists(path) ? Parse(File.ReadAllText(path)) : Empty;
    }

    public static CarSpecCatalog Parse(string json)
    {
        var dto = JsonSerializer.Deserialize<Dto>(json, ParseOptions)
            ?? throw new JsonException("car-specs.json parsed to null.");
        return new CarSpecCatalog(
            dto.Cars ?? new Dictionary<string, CarSpec>(StringComparer.Ordinal),
            dto.BarMax > 0 ? dto.BarMax : 8);
    }

    private static readonly JsonSerializerOptions ParseOptions = new(CoreJson.Options)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private sealed record Dto
    {
        public int BarMax { get; init; } = 8;
        public IReadOnlyDictionary<string, CarSpec>? Cars { get; init; }
    }
}
