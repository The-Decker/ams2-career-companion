using System.Text.Json;
using Companion.Core.Character;
using Companion.Core.Json;

namespace Companion.Core.Career;

/// <summary>
/// Team-archetype offer weights and salary bands (data/rules/career-team-archetypes.json).
/// Salary figures are in Budget Units (BU): a top tier-5 works team spends ~100 BU/season
/// (RESEARCH.md §6), so driver salaries are single-digit BU. Pure data, no file I/O.
/// </summary>
public sealed class TeamArchetypeCatalog
{
    public required IReadOnlyDictionary<string, TeamArchetype> Archetypes { get; init; }

    public required IReadOnlyDictionary<int, SalaryBand> SalaryBandsByTier { get; init; }

    /// <summary>Minimum player reputation a team of the tier considers at all (tier-gating).</summary>
    public required IReadOnlyDictionary<int, double> RepFloorByTier { get; init; }

    /// <summary>Archetype used when a team has no explicit assignment.</summary>
    public required IReadOnlyDictionary<int, string> DefaultArchetypeByTier { get; init; }

    /// <summary>Top N offers that become letters.</summary>
    public int MaxOffers { get; init; } = 3;

    public static TeamArchetypeCatalog Parse(string json)
    {
        var dto = JsonSerializer.Deserialize<CatalogDto>(json, CoreJson.Options)
                  ?? throw new JsonException("Team-archetype file is empty.");

        if (dto.Archetypes is not { Count: > 0 })
            throw new JsonException("Team-archetype file declares no archetypes.");
        if (dto.MaxOffers < 1)
            throw new JsonException("maxOffers must be at least 1.");

        foreach (var (tier, name) in dto.DefaultArchetypeByTier)
        {
            if (!dto.Archetypes.ContainsKey(name))
                throw new JsonException($"defaultArchetypeByTier[{tier}] names unknown archetype '{name}'.");
        }
        foreach (var (tier, band) in dto.SalaryBandsByTier)
        {
            if (band.MinBu < 0 || band.MaxBu < band.MinBu)
                throw new JsonException($"salaryBandsByTier[{tier}] is not a valid band.");
        }

        return new TeamArchetypeCatalog
        {
            Archetypes = dto.Archetypes,
            SalaryBandsByTier = dto.SalaryBandsByTier,
            RepFloorByTier = dto.RepFloorByTier,
            DefaultArchetypeByTier = dto.DefaultArchetypeByTier,
            MaxOffers = dto.MaxOffers,
        };
    }

    /// <summary>The archetype for a team: explicit assignment when present, else the tier default.</summary>
    public TeamArchetype ForTeam(int tier, string? archetypeName)
    {
        string name = archetypeName
            ?? (DefaultArchetypeByTier.TryGetValue(Math.Clamp(tier, 1, 5), out var byTier)
                ? byTier
                : throw new KeyNotFoundException($"No default archetype for tier {tier}."));
        return Archetypes.TryGetValue(name, out var archetype)
            ? archetype
            : throw new KeyNotFoundException($"Unknown team archetype '{name}'.");
    }

    /// <summary>The contract offer-scoring formula:
    /// <c>w1·rep + w2·OPI + w3·experience − w4·salaryAsk − w5·ageRisk</c>. A character's business
    /// perks scale the experience / salary-ask / age-risk terms; a null modifier scores exactly the
    /// shipped formula.</summary>
    public static double OfferScore(
        TeamArchetype archetype,
        double reputation,
        double opi,
        double experienceSeasons,
        double salaryAskBu,
        double ageRiskYears,
        PlayerPerkModifiers? mods = null)
    {
        if (mods is not null)
        {
            experienceSeasons *= mods.OfferExperienceMult;
            salaryAskBu *= mods.SalaryAskMult;
            ageRiskYears *= mods.AgeRiskMult;
        }

        var w = archetype.Weights;
        return w.Rep * reputation
               + w.Opi * opi
               + w.Experience * experienceSeasons
               - w.Salary * salaryAskBu
               - w.AgeRisk * ageRiskYears;
    }

    public double RepFloor(int tier) =>
        RepFloorByTier.TryGetValue(Math.Clamp(tier, 1, 5), out double floor) ? floor : 0.0;

    /// <summary>Offered salary: the tier's band interpolated by reputation, rounded to 0.1 BU. A
    /// character's income perk scales the figure; a null modifier keeps the shipped band.</summary>
    public double SalaryOffer(int tier, double reputation, PlayerPerkModifiers? mods = null)
    {
        if (!SalaryBandsByTier.TryGetValue(Math.Clamp(tier, 1, 5), out var band))
            throw new KeyNotFoundException($"No salary band for tier {tier}.");
        double t = Math.Clamp(reputation, 0.0, 100.0) / 100.0;
        double offer = band.MinBu + (band.MaxBu - band.MinBu) * t;
        if (mods is not null)
            offer *= mods.SalaryOfferMult;
        return Math.Round(offer, 1);
    }

    private sealed record CatalogDto
    {
        public required IReadOnlyDictionary<string, TeamArchetype> Archetypes { get; init; }
        public required IReadOnlyDictionary<int, SalaryBand> SalaryBandsByTier { get; init; }
        public required IReadOnlyDictionary<int, double> RepFloorByTier { get; init; }
        public required IReadOnlyDictionary<int, string> DefaultArchetypeByTier { get; init; }
        public int MaxOffers { get; init; } = 3;
    }
}

/// <summary>One archetype: offer weights for scoring the player, plus how much the team
/// values pay-driver money when filling its own seats.</summary>
public sealed record TeamArchetype
{
    public required OfferWeights Weights { get; init; }

    /// <summary>Weight on candidate pay budget in the AI seat market (minnows live off it).</summary>
    public double PayDriverWeight { get; init; }
}

/// <summary>The w1..w5 of the contract offer formula, in order.</summary>
public sealed record OfferWeights
{
    public required double Rep { get; init; }
    public required double Opi { get; init; }
    public required double Experience { get; init; }
    public required double Salary { get; init; }
    public required double AgeRisk { get; init; }
}

public sealed record SalaryBand
{
    public required double MinBu { get; init; }
    public required double MaxBu { get; init; }
}
