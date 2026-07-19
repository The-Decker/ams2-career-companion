using Companion.Core.Career;
using Companion.Core.Numerics;

namespace Companion.Core.Dynasty;

/// <summary>Everything the decision-application fold consumes: the carried state, the pinned
/// rules, the season year (era scaling) and the round's journaled decisions in seq order.</summary>
public sealed record DynastyDecisionFoldContext
{
    public required DynastyEconomyState State { get; init; }
    public required DynastyEconomyRules Rules { get; init; }
    public required int Year { get; init; }
    public required int Round { get; init; }
    public required IReadOnlyList<DynastyEconomyDecision> Decisions { get; init; }
}

/// <summary>Everything the round settlement consumes. The Data layer distills the round's raw
/// result into plain values so this fold stays pure (no envelope/DB types in Core).</summary>
public sealed record DynastyRoundSettleContext
{
    public required DynastyEconomyState State { get; init; }
    public required DynastyEconomyRules Rules { get; init; }
    public required int Year { get; init; }
    public required int Round { get; init; }

    /// <summary>The pinned season's total round count, the exact accrual denominator.</summary>
    public required int RoundsInSeason { get; init; }

    /// <summary>The player's team budget tier this round (second-driver salary band).</summary>
    public required int PlayerTeamTier { get; init; }

    /// <summary>False on an injury sit-out (DNS) or a round the player was absent from, no
    /// prize, no appearance money, no player repair; the team's fixed costs still run.</summary>
    public required bool PlayerStarted { get; init; }

    /// <summary>The player's classified position per race session of the round (null = not
    /// classified in that race). Empty when the player did not start.</summary>
    public required IReadOnlyList<int?> PlayerSessionFinishes { get; init; }

    /// <summary>The second car's best classified position per race session (null = none).</summary>
    public required IReadOnlyList<int?> TeammateSessionFinishes { get; init; }

    /// <summary>The round's grid fielded a second car for the player's team.</summary>
    public required bool HasSecondCar { get; init; }

    /// <summary>The player's DNF cause in the round's primary race, or null when classified.</summary>
    public DnfCause? PlayerDnf { get; init; }

    /// <summary>The captured accident severity of the player's own accident DNF, or null.</summary>
    public AccidentSeverity? PlayerAccidentSeverity { get; init; }
}

/// <summary>Everything the season settlement consumes.</summary>
public sealed record DynastySeasonSettleContext
{
    public required DynastyEconomyState State { get; init; }
    public required DynastyEconomyRules Rules { get; init; }
    public required int Year { get; init; }

    /// <summary>The player team's final constructors' position, or null when the season scored
    /// no constructors table (the drivers' position then stands in for the prize).</summary>
    public int? ConstructorsPosition { get; init; }

    /// <summary>The player's final drivers' championship position, or null when unclassified.</summary>
    public int? DriversPosition { get; init; }
}

public sealed record DynastyEconomyFoldResult
{
    public required DynastyEconomyState State { get; init; }
    public required IReadOnlyList<JournalEvent> Events { get; init; }

    /// <summary>The round settlement crossed into bankruptcy (terminal; for the caller's
    /// transition detection, the screen-model capture, exactly like the death flow).</summary>
    public bool WentBankrupt { get; init; }
}

/// <summary>
/// The Grand Prix Dynasty economy fold (docs/dev/dynasty-tycoon-economy.md §3), three pure,
/// RNG-free entry points mirroring <see cref="AccidentFold"/>: decisions apply at the top of the
/// round fold (a development buy is felt in the same round's expectation), the round settlement
/// runs at the end of the round fold, and the season settlement runs in the season-end pipeline
/// after final standings. Every money value is exact <see cref="Rational"/>; the CALLER gates all
/// three on <c>player.Economy is { Bankrupt: false }</c> + rules present, so every non-economy
/// career emits zero rows and stays byte-identical. Journaled decisions are applied
/// UNCONDITIONALLY (acceptance validated them before journaling); an impossible decision means a
/// tampered journal and throws, the house style for corrupted inputs.
/// </summary>
public static class DynastyEconomyFold
{
    /// <summary>Applies the round's journaled decisions in seq order, emitting one
    /// <c>economy.applied</c> row per decision.</summary>
    public static DynastyEconomyFoldResult ApplyDecisions(DynastyDecisionFoldContext ctx)
    {
        GuardSchema(ctx.State, ctx.Rules);
        var state = ctx.State;
        var events = new List<JournalEvent>(ctx.Decisions.Count);

        foreach (var decision in ctx.Decisions)
        {
            var before = state.Balance;
            Rational amount;
            switch (decision.Kind)
            {
                case DynastyEconomyDecisionKind.SignSponsor:
                {
                    string sponsorId = RequireSponsorId(decision);
                    var deal = ctx.Rules.SponsorById(sponsorId)
                        ?? throw new InvalidOperationException(
                            $"Journaled economy decision signs unknown sponsor '{sponsorId}'.");
                    if (state.SponsorContract(sponsorId) is not null)
                        throw new InvalidOperationException(
                            $"Journaled economy decision signs already-active sponsor '{sponsorId}'.");
                    amount = deal.SigningBonus * ctx.Rules.IndexForYear(ctx.Year);
                    state = state
                        .WithSponsor(sponsorId, new DynastySponsorContract
                        {
                            SeasonsRemaining = deal.ContractSeasons,
                        })
                        with { Balance = before + amount };
                    break;
                }
                case DynastyEconomyDecisionKind.DropSponsor:
                {
                    string sponsorId = RequireSponsorId(decision);
                    if (state.SponsorContract(sponsorId) is null)
                        throw new InvalidOperationException(
                            $"Journaled economy decision drops absent sponsor '{sponsorId}'.");
                    amount = Rational.Zero;
                    state = state.WithoutSponsor(sponsorId);
                    break;
                }
                case DynastyEconomyDecisionKind.BuyDevelopment:
                {
                    if (state.DevelopmentLevel >= ctx.Rules.Development.MaxLevel)
                        throw new InvalidOperationException(
                            "Journaled economy decision buys development past the level cap.");
                    var cost = ctx.Rules.DevelopmentCost(state.DevelopmentLevel, state.StaffTier, ctx.Year);
                    amount = -cost;
                    state = state with
                    {
                        Balance = before - cost,
                        DevelopmentLevel = state.DevelopmentLevel + 1,
                    };
                    break;
                }
                case DynastyEconomyDecisionKind.SetStaff:
                {
                    int tier = decision.StaffTier
                        ?? throw new InvalidOperationException(
                            "Journaled economy staff decision carries no tier.");
                    if (tier < 0 || tier > ctx.Rules.Staff.Count)
                        throw new InvalidOperationException(
                            $"Journaled economy staff decision targets invalid tier {tier}.");
                    amount = Rational.Zero;
                    state = state with { StaffTier = tier };
                    break;
                }
                case DynastyEconomyDecisionKind.SetSecondSeat:
                {
                    var deal = decision.SecondSeat
                        ?? throw new InvalidOperationException(
                            "Journaled economy second-seat decision carries no deal.");
                    amount = Rational.Zero;
                    state = state with { SecondSeat = deal };
                    break;
                }
                default:
                    throw new InvalidOperationException(
                        $"Journaled economy decision has unknown kind '{decision.Kind}'.");
            }

            events.Add(new JournalEvent
            {
                Phase = JournalPhases.EconomyApplied,
                Entity = "player",
                DeltaJson = CareerJson.Serialize(new
                {
                    round = ctx.Round,
                    kind = decision.Kind,
                    sponsorId = decision.SponsorId,
                    staffTier = decision.StaffTier,
                    secondSeat = decision.SecondSeat,
                    amount = amount.ToString(),
                    balanceFrom = before.ToString(),
                    balanceTo = state.Balance.ToString(),
                    developmentLevel = state.DevelopmentLevel,
                }),
                Cause = CauseOf(decision.Kind),
            });
        }

        return new DynastyEconomyFoldResult { State = state, Events = events };
    }

    /// <summary>The round settlement: prize + sponsor + backing income against fees, accruals and
    /// repairs, one <c>economy.round</c> statement row, the deficit-grace update, and the
    /// bankruptcy transition when the money runs out.</summary>
    public static DynastyEconomyFoldResult SettleRound(DynastyRoundSettleContext ctx)
    {
        GuardSchema(ctx.State, ctx.Rules);
        var rules = ctx.Rules;
        var state = ctx.State;
        var index = rules.IndexForYear(ctx.Year);
        bool retainedSecond = state.SecondSeat == SecondSeatDeal.Retained && ctx.HasSecondCar;

        // ---- income ----
        var racePrize = Rational.Zero;
        foreach (int? finish in ctx.PlayerSessionFinishes)
            racePrize += rules.RacePrize(finish, ctx.Year);

        var secondCarPrize = Rational.Zero;
        if (retainedSecond)
        {
            foreach (int? finish in ctx.TeammateSessionFinishes)
                secondCarPrize += rules.RacePrize(finish, ctx.Year);
        }

        var appearance = ctx.PlayerStarted ? rules.AppearanceMoney * index : Rational.Zero;

        var sponsorPerRace = Rational.Zero;
        var sponsorBonus = Rational.Zero;
        int? primaryFinish = ctx.PlayerSessionFinishes.Count > 0 ? ctx.PlayerSessionFinishes[0] : null;
        foreach (var sponsorId in state.Sponsors.Keys)
        {
            var deal = rules.SponsorById(sponsorId)
                ?? throw new InvalidOperationException(
                    $"Active sponsor contract '{sponsorId}' is missing from the rules board.");
            sponsorPerRace += deal.PerRace * index;
            if (primaryFinish is { } finish && finish <= 3)
            {
                sponsorBonus += deal.PodiumBonus * index;
                if (finish == 1)
                    sponsorBonus += deal.WinBonus * index;
            }
        }

        var backing = state.SecondSeat == SecondSeatDeal.PayDriver && ctx.HasSecondCar
            ? DynastyEconomyRules.PerRound(rules.SecondSeat.PayDriverBackingPerSeason, ctx.RoundsInSeason) * index
            : Rational.Zero;

        var incomeTotal = racePrize + secondCarPrize + appearance + sponsorPerRace + sponsorBonus + backing;

        // ---- costs ----
        var entryFee = rules.EntryFee * index;
        var logistics = rules.LogisticsPerRound * index;
        var upkeep = rules.UpkeepPerRound * index;
        var staff = DynastyEconomyRules.PerRound(
            rules.StaffUpkeepPerSeason(state.StaffTier), ctx.RoundsInSeason) * index;
        var secondSalary = retainedSecond
            ? DynastyEconomyRules.PerRound(
                rules.SecondSeatSalaryPerSeason(ctx.PlayerTeamTier), ctx.RoundsInSeason) * index
            : Rational.Zero;
        var repairs = ctx.PlayerStarted
            ? rules.PlayerRepair(ctx.PlayerAccidentSeverity, ctx.PlayerDnf, ctx.Year)
            : Rational.Zero;
        var secondRepairs = retainedSecond
            && ctx.TeammateSessionFinishes.Count > 0
            && ctx.TeammateSessionFinishes[0] is null
            ? rules.SecondCarRepair(ctx.Year)
            : Rational.Zero;

        var costTotal = entryFee + logistics + upkeep + staff + secondSalary + repairs + secondRepairs;

        // ---- the settlement ----
        var net = incomeTotal - costTotal;
        var balanceTo = state.Balance + net;
        int deficitRounds = balanceTo < Rational.Zero ? state.DeficitRounds + 1 : 0;
        bool bankrupt = deficitRounds > rules.Bankruptcy.GraceRounds
            || balanceTo <= rules.HardFloor(ctx.Year);

        var events = new List<JournalEvent>
        {
            new()
            {
                Phase = JournalPhases.EconomyRound,
                Entity = "player",
                DeltaJson = CareerJson.Serialize(new
                {
                    round = ctx.Round,
                    playerStarted = ctx.PlayerStarted,
                    income = new
                    {
                        racePrize = racePrize.ToString(),
                        secondCarPrize = secondCarPrize.ToString(),
                        appearance = appearance.ToString(),
                        sponsorPerRace = sponsorPerRace.ToString(),
                        sponsorBonus = sponsorBonus.ToString(),
                        backing = backing.ToString(),
                        total = incomeTotal.ToString(),
                    },
                    costs = new
                    {
                        entryFee = entryFee.ToString(),
                        logistics = logistics.ToString(),
                        upkeep = upkeep.ToString(),
                        staff = staff.ToString(),
                        secondSalary = secondSalary.ToString(),
                        repairs = repairs.ToString(),
                        secondRepairs = secondRepairs.ToString(),
                        total = costTotal.ToString(),
                    },
                    net = net.ToString(),
                    balanceFrom = state.Balance.ToString(),
                    balanceTo = balanceTo.ToString(),
                    deficitRounds,
                }),
                Cause = balanceTo < Rational.Zero ? "deficit" : "surplus",
            },
        };

        var settled = state with
        {
            Balance = balanceTo,
            DeficitRounds = deficitRounds,
            Bankrupt = state.Bankrupt || bankrupt,
        };

        if (bankrupt)
        {
            events.Add(new JournalEvent
            {
                Phase = JournalPhases.EconomyBankruptcy,
                Entity = "player",
                DeltaJson = CareerJson.Serialize(new
                {
                    round = ctx.Round,
                    balance = balanceTo.ToString(),
                    deficitRounds,
                    graceRounds = rules.Bankruptcy.GraceRounds,
                    hardFloor = rules.HardFloor(ctx.Year).ToString(),
                }),
                Cause = "bankruptcy",
            });
        }

        return new DynastyEconomyFoldResult
        {
            State = settled,
            Events = events,
            WentBankrupt = bankrupt,
        };
    }

    /// <summary>The season settlement: constructors' prize money + sponsor season/title bonuses,
    /// then the between-season resets, contract decrement/expiry and the development carryover.
    /// Runs in the season-end pipeline AFTER final standings; rollover copies the result into the
    /// next season's start state, exactly like the SMGP season fold.</summary>
    public static DynastyEconomyFoldResult SettleSeason(DynastySeasonSettleContext ctx)
    {
        GuardSchema(ctx.State, ctx.Rules);
        var rules = ctx.Rules;
        var state = ctx.State;
        var index = rules.IndexForYear(ctx.Year);

        int? standing = ctx.ConstructorsPosition ?? ctx.DriversPosition;
        var seasonPrize = rules.SeasonPrize(standing, ctx.Year);
        bool champion = ctx.DriversPosition == 1;

        var sponsorPerSeason = Rational.Zero;
        var titleBonus = Rational.Zero;
        foreach (var sponsorId in state.Sponsors.Keys)
        {
            var deal = rules.SponsorById(sponsorId)
                ?? throw new InvalidOperationException(
                    $"Active sponsor contract '{sponsorId}' is missing from the rules board.");
            sponsorPerSeason += deal.PerSeason * index;
            if (champion)
                titleBonus += deal.TitleBonus * index;
        }

        var net = seasonPrize + sponsorPerSeason + titleBonus;
        var balanceTo = state.Balance + net;

        // Contracts run down over the winter; an expired sponsor leaves the board cleanly.
        var expired = new List<string>();
        var carried = state with { Balance = balanceTo };
        foreach (var (sponsorId, contract) in state.Sponsors)
        {
            int remaining = contract.SeasonsRemaining - 1;
            if (remaining <= 0)
            {
                expired.Add(sponsorId);
                carried = carried.WithoutSponsor(sponsorId);
            }
            else
            {
                carried = carried.WithSponsor(sponsorId, contract with { SeasonsRemaining = remaining });
            }
        }

        int developmentFrom = carried.DevelopmentLevel;
        int developmentTo = rules.DevelopmentCarryoverLevel(developmentFrom);
        carried = carried with { DevelopmentLevel = developmentTo };

        var events = new List<JournalEvent>
        {
            new()
            {
                Phase = JournalPhases.EconomySeason,
                Entity = "player",
                DeltaJson = CareerJson.Serialize(new
                {
                    standingPosition = standing,
                    seasonPrize = seasonPrize.ToString(),
                    sponsorPerSeason = sponsorPerSeason.ToString(),
                    titleBonus = titleBonus.ToString(),
                    net = net.ToString(),
                    balanceFrom = state.Balance.ToString(),
                    balanceTo = balanceTo.ToString(),
                    developmentFrom,
                    developmentTo,
                    expiredSponsors = expired,
                }),
                Cause = "season-settlement",
            },
        };

        return new DynastyEconomyFoldResult { State = carried, Events = events };
    }

    /// <summary>The journal cause string for a decision kind, shared by the INPUT row (the
    /// declaration) and its DERIVED economy.applied row so news templates key off one name.</summary>
    public static string CauseOf(DynastyEconomyDecisionKind kind) => kind switch
    {
        DynastyEconomyDecisionKind.SignSponsor => "sign-sponsor",
        DynastyEconomyDecisionKind.DropSponsor => "drop-sponsor",
        DynastyEconomyDecisionKind.BuyDevelopment => "buy-development",
        DynastyEconomyDecisionKind.SetStaff => "set-staff",
        DynastyEconomyDecisionKind.SetSecondSeat => "set-second-seat",
        _ => throw new InvalidOperationException($"Unknown economy decision kind '{kind}'."),
    };

    private static void GuardSchema(DynastyEconomyState state, DynastyEconomyRules rules)
    {
        if (state.Version != rules.SchemaVersion)
        {
            throw new InvalidOperationException(
                $"This career's economy was created under rules schema v{state.Version}, but the " +
                $"installed economy.json is v{rules.SchemaVersion}, the fold refuses to drift " +
                "balance tables under an existing career.");
        }
    }

    private static string RequireSponsorId(DynastyEconomyDecision decision) =>
        decision.SponsorId
        ?? throw new InvalidOperationException("Journaled economy sponsor decision carries no sponsor id.");
}
