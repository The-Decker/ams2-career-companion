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
    private readonly Companion.Core.Smgp.SmgpCanon? _smgpCanon;

    private CarSpecCatalog(
        IReadOnlyDictionary<string, CarSpec> byKey, int barMax, Companion.Core.Smgp.SmgpCanon? smgpCanon = null)
    {
        _byKey = byKey;
        BarMax = barMax;
        _smgpCanon = smgpCanon;
    }

    /// <summary>The top of every bar's 0..N scale (the arcade drew fixed-length bars); data-driven.</summary>
    public int BarMax { get; }

    /// <summary>An empty catalog (no file shipped): every lookup returns null.</summary>
    public static CarSpecCatalog Empty { get; } =
        new(new Dictionary<string, CarSpec>(StringComparer.Ordinal), 8);

    /// <summary>Overlays the SMGP canon lock (mission SMGP-024): a canonical SMGP team's machine
    /// and engine names ALWAYS resolve from the registry, so the real-world or generic vehicle
    /// rows ("MP4/5B", "Honda V10", "Type G3-M1") can never leak onto an SMGP card. The arcade
    /// bars and any un-authored power figure still derive from the car-specs rows; those are
    /// display flavor, not identity. Non-SMGP teams and real-F1 careers are untouched.</summary>
    public CarSpecCatalog WithSmgpCanon(Companion.Core.Smgp.SmgpCanon canon) =>
        new(_byKey, BarMax, canon);

    /// <summary>The spec for a team or its car, team id first (a per-team override), then the vehicle
    /// id (the shared-model default); null when neither is authored, so the card collapses.</summary>
    public CarSpec? For(string? teamId, string? vehicleId)
    {
        CarSpec? legacy = null;
        if (teamId is not null && _byKey.TryGetValue(teamId, out var byTeam))
            legacy = byTeam;
        else if (vehicleId is not null && _byKey.TryGetValue(vehicleId, out var byVehicle))
            legacy = byVehicle;

        if (teamId is not null && _smgpCanon?.ForTeam(teamId) is { } canonTeam)
        {
            return new CarSpec
            {
                MachineName = canonTeam.CarDisplayName,
                Engine = canonTeam.EngineDisplayName,
                MaxPowerHp = canonTeam.MaxPowerHp > 0
                    ? canonTeam.MaxPowerHp
                    : legacy?.MaxPowerHp ?? 0,
                Bars = legacy?.Bars ?? new CarSpecBars(),
            };
        }

        return legacy;
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
