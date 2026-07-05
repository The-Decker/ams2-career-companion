using System.Text.Json;
using Companion.Core.Json;

namespace Companion.Core.Career;

/// <summary>
/// Era-shifted driver aging curves (data/rules/career-aging-curves.json): peak-age plateaus
/// and drift rates per era, plus the retirement hazard parameters. Pure data — Core does no
/// file I/O; callers read the file and hand the JSON to <see cref="Parse"/>.
/// </summary>
public sealed class AgingCurveSet
{
    public required IReadOnlyList<AgingCurve> Eras { get; init; }

    public static AgingCurveSet Parse(string json)
    {
        var dto = JsonSerializer.Deserialize<AgingCurveSetDto>(json, CoreJson.Options)
                  ?? throw new JsonException("Aging-curve file is empty.");
        if (dto.Eras is not { Count: > 0 })
            throw new JsonException("Aging-curve file declares no eras.");

        foreach (var era in dto.Eras)
        {
            if (era.FromYear > era.ToYear)
                throw new JsonException($"Aging era '{era.Key}' has fromYear > toYear.");
            if (era.PeakAgeStart > era.PeakAgeEnd)
                throw new JsonException($"Aging era '{era.Key}' has peakAgeStart > peakAgeEnd.");
            if (era.DeclinePerYear <= 0.0)
                throw new JsonException($"Aging era '{era.Key}' needs declinePerYear > 0.");
            if (era.DeclineAccelPerYear < 0.0 || era.RisePerYear < 0.0 || era.NoiseAmplitude < 0.0)
                throw new JsonException($"Aging era '{era.Key}' has a negative rate.");
        }

        return new AgingCurveSet { Eras = dto.Eras };
    }

    public AgingCurve ForYear(int year) =>
        TryForYear(year)
        ?? throw new KeyNotFoundException($"No aging era covers {year}.");

    public AgingCurve? TryForYear(int year) =>
        Eras.FirstOrDefault(e => e.FromYear <= year && year <= e.ToYear);

    private sealed record AgingCurveSetDto
    {
        public required IReadOnlyList<AgingCurve> Eras { get; init; }
    }
}

/// <summary>One era's aging curve. Ages rise gently to the peak plateau, hold, then decline
/// with accelerating losses.</summary>
public sealed record AgingCurve
{
    public required string Key { get; init; }

    public required int FromYear { get; init; }

    public required int ToYear { get; init; }

    /// <summary>First age of the peak plateau (contract: ~28 in the 60s, later in modern eras).</summary>
    public required int PeakAgeStart { get; init; }

    /// <summary>Last age of the peak plateau.</summary>
    public required int PeakAgeEnd { get; init; }

    /// <summary>Annual rating gain before the peak.</summary>
    public double RisePerYear { get; init; }

    /// <summary>Annual rating loss in the first year past the peak.</summary>
    public required double DeclinePerYear { get; init; }

    /// <summary>Extra annual loss per additional year past the peak (accelerating decline).</summary>
    public double DeclineAccelPerYear { get; init; }

    /// <summary>Amplitude of the deterministic per-driver noise (stream `aging`), applied
    /// on top of the curve: ±noiseAmplitude uniformly.</summary>
    public double NoiseAmplitude { get; init; }

    public required RetirementHazard Retirement { get; init; }

    /// <summary>The curve's deterministic annual rating delta at a given age (noise excluded).
    /// Strictly decreasing past the peak when declineAccelPerYear &gt; 0 — tested invariant.</summary>
    public double AnnualDelta(int age) =>
        age < PeakAgeStart
            ? RisePerYear
            : age <= PeakAgeEnd
                ? 0.0
                : -(DeclinePerYear + DeclineAccelPerYear * (age - PeakAgeEnd - 1));
}

/// <summary>Age+performance retirement hazard for non-canon drivers (stream `retirement`).</summary>
public sealed record RetirementHazard
{
    /// <summary>Age at which the hazard starts accruing.</summary>
    public required int BaseAge { get; init; }

    /// <summary>Hazard added per year past <see cref="BaseAge"/>.</summary>
    public required double PerYearOverBase { get; init; }

    /// <summary>Cap on the age component alone.</summary>
    public double MaxAgeHazard { get; init; } = 0.9;

    /// <summary>Effective raceSkill below which decline adds hazard.</summary>
    public double SkillFloor { get; init; }

    /// <summary>Hazard per full rating point below the floor.</summary>
    public double SkillHazardPerPoint { get; init; }

    /// <summary>Cap on the combined probability.</summary>
    public double Cap { get; init; } = 0.95;

    public double Probability(int age, double raceSkill)
    {
        double ageHazard = Math.Clamp(PerYearOverBase * (age - BaseAge), 0.0, MaxAgeHazard);
        double skillHazard = raceSkill < SkillFloor
            ? SkillHazardPerPoint * (SkillFloor - raceSkill)
            : 0.0;
        return Math.Clamp(ageHazard + skillHazard, 0.0, Cap);
    }
}
