using System.Text.Json;
using System.Text.Json.Serialization;
using Companion.Core.Career;
using Companion.Core.Json;
using Companion.Core.Numerics;

namespace Companion.Core.Dynasty;

/// <summary>
/// The Grand Prix Dynasty economy balance tables (<c>data\rules\dynasty\economy.json</c>), a
/// REQUIRED fold input for economy careers, never hard-coded era numbers in the engine. Pure
/// <see cref="Parse"/> with throwing validation (the <c>RacingDnaCatalog</c> discipline):
/// <see cref="CurrentSchemaVersion"/> is pinned into each career's
/// <see cref="DynastyEconomyState.Version"/> at creation and the fold refuses a mismatched file,
/// so an old career can never silently drift onto later balance numbers. All money is exact
/// <see cref="Rational"/>; base tables are authored in 1960s-era units and every charge/credit
/// scales by the season year's era index, so income and costs grow together and the game's shape
/// stays era-stable. The numbers are stylized game systems, not claims of historical fact.
/// </summary>
public sealed record DynastyEconomyRules
{
    public const int CurrentSchemaVersion = 1;

    public required int SchemaVersion { get; init; }

    public required IReadOnlyList<DynastyEraScale> EraScaling { get; init; }

    /// <summary>Opening balance by the starting team's budget tier (1–5), base units.</summary>
    public required IReadOnlyDictionary<int, Rational> StartingFundsByTier { get; init; }

    /// <summary>Per-round purse by classified finishing position (index 0 = P1), base units.</summary>
    public required IReadOnlyList<Rational> RacePrizeByPosition { get; init; }

    /// <summary>Purse for a classified finish beyond the table, base units.</summary>
    public required Rational RacePrizeClassifiedDefault { get; init; }

    /// <summary>End-of-season constructors' money by final position (index 0 = C1), base units.</summary>
    public required IReadOnlyList<Rational> SeasonPrizeByConstructorPosition { get; init; }

    /// <summary>Season money for a constructors' position beyond the table, base units.</summary>
    public required Rational SeasonPrizeDefault { get; init; }

    /// <summary>Starting money per round the player's car actually starts, base units.</summary>
    public required Rational AppearanceMoney { get; init; }

    public required Rational EntryFee { get; init; }

    public required Rational LogisticsPerRound { get; init; }

    public required Rational UpkeepPerRound { get; init; }

    public required DynastyRepairTable Repairs { get; init; }

    public required DynastyDevelopmentRules Development { get; init; }

    /// <summary>Engineering staff tiers in ascending tier order (tier 1 = first entry).</summary>
    public required IReadOnlyList<DynastyStaffTier> Staff { get; init; }

    public required DynastySecondSeatRules SecondSeat { get; init; }

    public required DynastySponsorRules Sponsors { get; init; }

    public required DynastyBankruptcyRules Bankruptcy { get; init; }

    private readonly Dictionary<string, DynastySponsorDeal> _sponsorsById = new(StringComparer.Ordinal);

    // ---------- resolution helpers (all pure; every money result is base × era index) ----------

    /// <summary>The era index for a season year; years outside the authored bands clamp to the
    /// nearest band so an unexpected pack year never crashes the fold.</summary>
    public Rational IndexForYear(int year)
    {
        var bands = EraScaling;
        if (year < bands[0].FromYear)
            return bands[0].Index;
        foreach (var band in bands)
        {
            if (year >= band.FromYear && year <= band.ToYear)
                return band.Index;
        }
        return bands[^1].Index;
    }

    public Rational StartingFunds(int teamTier, int year) =>
        StartingFundsByTier[Math.Clamp(teamTier, 1, 5)] * IndexForYear(year);

    /// <summary>The round purse for a classified finish (1-based), or zero for null (DNF/DNS).</summary>
    public Rational RacePrize(int? classifiedPosition, int year)
    {
        if (classifiedPosition is not { } position || position < 1)
            return Rational.Zero;
        var basePrize = position <= RacePrizeByPosition.Count
            ? RacePrizeByPosition[position - 1]
            : RacePrizeClassifiedDefault;
        return basePrize * IndexForYear(year);
    }

    /// <summary>The season constructors' money for a final position (1-based), the table default
    /// beyond the table, or zero for null (no constructors classification).</summary>
    public Rational SeasonPrize(int? constructorPosition, int year)
    {
        if (constructorPosition is not { } position || position < 1)
            return Rational.Zero;
        var basePrize = position <= SeasonPrizeByConstructorPosition.Count
            ? SeasonPrizeByConstructorPosition[position - 1]
            : SeasonPrizeDefault;
        return basePrize * IndexForYear(year);
    }

    /// <summary>The player-car repair bill for this round's outcome: an accident DNF bills by its
    /// captured severity, a mechanical DNF the rebuild rate, a driver-error DNF the error rate; a
    /// classified finish bills nothing.</summary>
    public Rational PlayerRepair(AccidentSeverity? severity, DnfCause? dnf, int year)
    {
        Rational baseCost;
        if (severity is { } s)
        {
            baseCost = s switch
            {
                AccidentSeverity.Light => Repairs.AccidentLight,
                AccidentSeverity.Medium => Repairs.AccidentMedium,
                AccidentSeverity.Heavy => Repairs.AccidentHeavy,
                _ => Repairs.AccidentMedium,
            };
        }
        else if (dnf is { } cause)
        {
            baseCost = cause == DnfCause.Mechanical ? Repairs.Mechanical : Repairs.DriverError;
        }
        else
        {
            return Rational.Zero;
        }
        return baseCost * IndexForYear(year);
    }

    public Rational SecondCarRepair(int year) => Repairs.SecondCarDnf * IndexForYear(year);

    /// <summary>The cost of the NEXT development increment from <paramref name="currentLevel"/>:
    /// base × growth^level, staff-discounted, era-scaled. Exact rational throughout.</summary>
    public Rational DevelopmentCost(int currentLevel, int staffTier, int year)
    {
        var cost = Development.BaseCost;
        for (int i = 0; i < currentLevel; i++)
            cost *= Development.Growth;
        int tier = Math.Clamp(staffTier, 0, Staff.Count);
        var discount = Rational.One - Development.StaffDiscountPerTier * tier;
        return cost * discount * IndexForYear(year);
    }

    /// <summary>The development level that survives the season boundary:
    /// floor(level × carryover fraction).</summary>
    public int DevelopmentCarryoverLevel(int level)
    {
        if (level <= 0)
            return 0;
        var carried = Development.Carryover * level;
        return (int)(carried.Numerator / carried.Denominator);
    }

    /// <summary>This staff tier's season upkeep in base units (tier 0 = none = zero).</summary>
    public Rational StaffUpkeepPerSeason(int staffTier) =>
        staffTier <= 0 ? Rational.Zero : Staff[Math.Min(staffTier, Staff.Count) - 1].UpkeepPerSeason;

    /// <summary>The retained second driver's season salary by the team's budget tier, base units.</summary>
    public Rational SecondSeatSalaryPerSeason(int teamTier) =>
        SecondSeat.RetainedSalaryPerSeasonByTier[Math.Clamp(teamTier, 1, 5)];

    /// <summary>An exact per-round accrual: <paramref name="seasonAmount"/> ÷ rounds, the parts
    /// sum back to the season amount with no drift.</summary>
    public static Rational PerRound(Rational seasonAmount, int roundsInSeason) =>
        roundsInSeason <= 0 ? seasonAmount : seasonAmount / roundsInSeason;

    public DynastySponsorDeal? SponsorById(string sponsorId) =>
        _sponsorsById.TryGetValue(sponsorId, out var deal) ? deal : null;

    /// <summary>Slot count for a sponsor tier slot ("title"/"major"/"minor").</summary>
    public int SlotsFor(string tierSlot) =>
        Sponsors.Slots.TryGetValue(tierSlot, out int slots) ? slots : 0;

    public Rational HardFloor(int year) => Bankruptcy.HardFloor * IndexForYear(year);

    // ---------- parse + validation ----------

    public static DynastyEconomyRules Parse(string json)
    {
        var rules = JsonSerializer.Deserialize<DynastyEconomyRules>(json, ParseOptions)
            ?? throw new JsonException("Dynasty economy rules file is empty.");
        rules.Validate();
        foreach (var deal in rules.Sponsors.Board)
            rules._sponsorsById[deal.Id] = deal;
        return rules;
    }

    /// <summary>Loads <c>data\rules\dynasty\economy.json</c>, or null when the file is absent.
    /// The economy is an OPTIONAL-MODE fold input: required for an economy career (the creation
    /// seed and every economy fold/decision path refuse when this is null, with a clear message),
    /// but ABSENT for every other career, so a legacy/SMGP/Passport career on a stale-data install
    /// that ships no dynasty subfolder is completely unaffected (the dormancy contract). The eager
    /// <c>CareerRulesData.Load</c> must therefore never make this a hard requirement.</summary>
    public static DynastyEconomyRules? Load(string rulesDirectory)
    {
        string path = Path.Combine(rulesDirectory, "dynasty", "economy.json");
        return File.Exists(path) ? Parse(File.ReadAllText(path)) : null;
    }

    private static readonly JsonSerializerOptions ParseOptions = new(CoreJson.Options)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
            throw new JsonException(
                $"Dynasty economy schema version {SchemaVersion} is not supported by this build.");

        if (EraScaling.Count == 0)
            throw new JsonException("Dynasty economy needs at least one era-scaling band.");
        for (int i = 0; i < EraScaling.Count; i++)
        {
            var band = EraScaling[i];
            if (band.ToYear < band.FromYear)
                throw new JsonException($"Era band {band.FromYear}-{band.ToYear} is inverted.");
            if (band.Index <= Rational.Zero)
                throw new JsonException($"Era band {band.FromYear}-{band.ToYear} needs a positive index.");
            if (i > 0 && band.FromYear != EraScaling[i - 1].ToYear + 1)
                throw new JsonException(
                    $"Era bands must be contiguous; {EraScaling[i - 1].ToYear} is not adjacent to {band.FromYear}.");
        }

        for (int tier = 1; tier <= 5; tier++)
        {
            if (!StartingFundsByTier.TryGetValue(tier, out var funds))
                throw new JsonException($"Starting funds are missing for tier {tier}.");
            if (funds <= Rational.Zero)
                throw new JsonException($"Starting funds for tier {tier} must be positive.");
        }

        if (RacePrizeByPosition.Count == 0)
            throw new JsonException("The race prize table needs at least one position.");
        if (RacePrizeByPosition.Any(p => p < Rational.Zero) || RacePrizeClassifiedDefault < Rational.Zero)
            throw new JsonException("Race prizes cannot be negative.");
        if (SeasonPrizeByConstructorPosition.Count == 0)
            throw new JsonException("The season prize table needs at least one position.");
        if (SeasonPrizeByConstructorPosition.Any(p => p < Rational.Zero) || SeasonPrizeDefault < Rational.Zero)
            throw new JsonException("Season prizes cannot be negative.");
        if (AppearanceMoney < Rational.Zero || EntryFee < Rational.Zero
            || LogisticsPerRound < Rational.Zero || UpkeepPerRound < Rational.Zero)
            throw new JsonException("Round fees cannot be negative.");

        foreach (var (name, value) in new[]
        {
            ("accidentLight", Repairs.AccidentLight),
            ("accidentMedium", Repairs.AccidentMedium),
            ("accidentHeavy", Repairs.AccidentHeavy),
            ("mechanical", Repairs.Mechanical),
            ("driverError", Repairs.DriverError),
            ("secondCarDnf", Repairs.SecondCarDnf),
        })
        {
            if (value < Rational.Zero)
                throw new JsonException($"Repair cost '{name}' cannot be negative.");
        }

        if (Development.BaseCost <= Rational.Zero)
            throw new JsonException("Development base cost must be positive.");
        if (Development.Growth < Rational.One)
            throw new JsonException("Development growth must be at least 1 (an escalating curve).");
        if (Development.MaxLevel < 1)
            throw new JsonException("Development needs at least one buyable level.");
        if (Development.Carryover < Rational.Zero || Development.Carryover > Rational.One)
            throw new JsonException("Development carryover must be within [0, 1].");
        if (Development.StrengthPerLevel < 0.0)
            throw new JsonException("Development strength per level cannot be negative.");
        if (Development.StaffDiscountPerTier < Rational.Zero)
            throw new JsonException("Staff development discount cannot be negative.");
        if (Rational.One - Development.StaffDiscountPerTier * Staff.Count <= Rational.Zero)
            throw new JsonException("Staff development discount reaches 100% at the top tier.");

        for (int i = 0; i < Staff.Count; i++)
        {
            if (Staff[i].Tier != i + 1)
                throw new JsonException($"Staff tiers must be contiguous from 1; entry {i} declares tier {Staff[i].Tier}.");
            if (Staff[i].UpkeepPerSeason < Rational.Zero)
                throw new JsonException($"Staff tier {Staff[i].Tier} upkeep cannot be negative.");
        }

        for (int tier = 1; tier <= 5; tier++)
        {
            if (!SecondSeat.RetainedSalaryPerSeasonByTier.TryGetValue(tier, out var salary))
                throw new JsonException($"Second-seat salary is missing for tier {tier}.");
            if (salary < Rational.Zero)
                throw new JsonException($"Second-seat salary for tier {tier} cannot be negative.");
        }
        if (SecondSeat.PayDriverBackingPerSeason < Rational.Zero)
            throw new JsonException("Pay-driver backing cannot be negative.");

        foreach (string slot in new[] { DynastySponsorRules.TitleSlot, DynastySponsorRules.MajorSlot, DynastySponsorRules.MinorSlot })
        {
            if (!Sponsors.Slots.TryGetValue(slot, out int count) || count < 1)
                throw new JsonException($"Sponsor slot count for '{slot}' must be at least 1.");
        }
        if (Sponsors.Board.Count == 0)
            throw new JsonException("The Dynasty sponsor board is empty.");
        var seenSponsors = new HashSet<string>(StringComparer.Ordinal);
        foreach (var deal in Sponsors.Board)
        {
            if (string.IsNullOrWhiteSpace(deal.Id))
                throw new JsonException("A sponsor deal is missing its id.");
            if (!seenSponsors.Add(deal.Id))
                throw new JsonException($"Sponsor id '{deal.Id}' is duplicated.");
            if (!Sponsors.Slots.ContainsKey(deal.TierSlot))
                throw new JsonException($"Sponsor '{deal.Id}' declares unknown tier slot '{deal.TierSlot}'.");
            if (deal.ToYear < deal.FromYear)
                throw new JsonException($"Sponsor '{deal.Id}' has an inverted era window.");
            if (deal.ContractSeasons < 1)
                throw new JsonException($"Sponsor '{deal.Id}' needs a contract of at least one season.");
            if (deal.MinReputation is < 0.0 or > 100.0)
                throw new JsonException($"Sponsor '{deal.Id}' reputation floor must be within 0–100.");
            foreach (var (name, value) in new[]
            {
                ("signingBonus", deal.SigningBonus), ("perRace", deal.PerRace),
                ("perSeason", deal.PerSeason), ("podiumBonus", deal.PodiumBonus),
                ("winBonus", deal.WinBonus), ("titleBonus", deal.TitleBonus),
            })
            {
                if (value < Rational.Zero)
                    throw new JsonException($"Sponsor '{deal.Id}' {name} cannot be negative.");
            }
        }

        if (Bankruptcy.GraceRounds < 1)
            throw new JsonException("Bankruptcy grace must allow at least one deficit round.");
        if (Bankruptcy.HardFloor >= Rational.Zero)
            throw new JsonException("The bankruptcy hard floor must be negative (an overdraft limit).");
    }
}

/// <summary>One era-scaling band: seasons in [FromYear, ToYear] scale every base table by Index.</summary>
public sealed record DynastyEraScale
{
    public required int FromYear { get; init; }
    public required int ToYear { get; init; }
    public required Rational Index { get; init; }
}

public sealed record DynastyRepairTable
{
    public required Rational AccidentLight { get; init; }
    public required Rational AccidentMedium { get; init; }
    public required Rational AccidentHeavy { get; init; }
    public required Rational Mechanical { get; init; }
    public required Rational DriverError { get; init; }
    public required Rational SecondCarDnf { get; init; }
}

public sealed record DynastyDevelopmentRules
{
    public required Rational BaseCost { get; init; }

    /// <summary>Cost multiplier per level already bought (e.g. 27/20 = 1.35× each level).</summary>
    public required Rational Growth { get; init; }

    public required int MaxLevel { get; init; }

    /// <summary>The fraction of the level that survives the season boundary (floor).</summary>
    public required Rational Carryover { get; init; }

    /// <summary>The seat-strength adjustment each level adds in the expectation channel, NOT
    /// money, so a plain double like every other rating input.</summary>
    public required double StrengthPerLevel { get; init; }

    /// <summary>Development discount per engineering staff tier (e.g. 1/10 = 10% per tier).</summary>
    public required Rational StaffDiscountPerTier { get; init; }
}

public sealed record DynastyStaffTier
{
    public required int Tier { get; init; }
    public required Rational UpkeepPerSeason { get; init; }
}

public sealed record DynastySecondSeatRules
{
    public required IReadOnlyDictionary<int, Rational> RetainedSalaryPerSeasonByTier { get; init; }
    public required Rational PayDriverBackingPerSeason { get; init; }
}

public sealed record DynastySponsorRules
{
    public const string TitleSlot = "title";
    public const string MajorSlot = "major";
    public const string MinorSlot = "minor";

    /// <summary>Concurrent contract slots by tier slot name.</summary>
    public required IReadOnlyDictionary<string, int> Slots { get; init; }

    public required IReadOnlyList<DynastySponsorDeal> Board { get; init; }
}

/// <summary>One authored sponsor deal on the Dynasty board. Availability floors gate SIGNING
/// only (an existing contract is honored to its end); all money is base units, era-scaled at
/// charge time like every other table.</summary>
public sealed record DynastySponsorDeal
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    /// <summary>"title" / "major" / "minor", which slot pool the deal occupies.</summary>
    public required string TierSlot { get; init; }

    /// <summary>The signing window: the deal is offered only for seasons in [FromYear, ToYear].</summary>
    public required int FromYear { get; init; }
    public required int ToYear { get; init; }

    /// <summary>Minimum player reputation to sign.</summary>
    public required double MinReputation { get; init; }

    /// <summary>Best (lowest) constructors' position the team must have managed LAST season, or
    /// null for no result requirement. A first season has no last-season result, so deals with a
    /// requirement are unavailable in season one.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? BestConstructorPositionRequired { get; init; }

    public required Rational SigningBonus { get; init; }
    public required Rational PerRace { get; init; }
    public required Rational PerSeason { get; init; }
    public required Rational PodiumBonus { get; init; }
    public required Rational WinBonus { get; init; }
    public required Rational TitleBonus { get; init; }
    public required int ContractSeasons { get; init; }
}

public sealed record DynastyBankruptcyRules
{
    /// <summary>How many CONSECUTIVE rounds may end in deficit before the team folds.</summary>
    public required int GraceRounds { get; init; }

    /// <summary>The overdraft limit (negative, base units): a settlement at or below it is an
    /// immediate bankruptcy regardless of the grace window.</summary>
    public required Rational HardFloor { get; init; }
}
