using System.Text.Json;
using Companion.Core.Career;
using Companion.Core.Dynasty;
using Companion.Core.Numerics;
using Companion.Data;

namespace Companion.ViewModels.Services;

/// <summary>
/// The Dynasty owner-economy session surface (docs/dev/dynasty-tycoon-economy.md §9): the
/// dashboard projection (a pure display read, no fold input is ever derived from it) and the
/// validated decision write path. The session is the affordability/availability AUTHORITY: it
/// dry-runs the pending plan through the REAL fold (<see cref="DynastyEconomyFold.ApplyDecisions"/>)
/// plus policy checks, then journals the accepted decision via
/// <see cref="ReplayService.DeclareEconomyDecision"/> for the next unfolded round. The fold later
/// applies journaled decisions unconditionally, validation lives here, exactly once.
/// </summary>
public sealed partial class CareerSessionService
{
    /// <inheritdoc />
    public DynastyEconomyDashboard? EconomyDashboard()
    {
        if (_careerFileDeleted || _environment.RulesDirectory is null)
            return null;
        var player = CurrentPlayerState();
        if (player?.Economy is not { } economy)
            return null;
        // A career carrying economy state but no tables on this install (a stale-data install) has
        // no dashboard to project, the ledger figures cannot be resolved. The career is not lost;
        // it simply cannot render its economy until the data folder is restored.
        if (_environment.Rules.DynastyEconomy is not { } rules)
            return null;
        int year = _seasonYear;

        var pending = PendingEconomyDecisions();
        var effective = EffectiveEconomyState(economy, pending.Select(p => p.Decision).ToList(), rules, year);

        // ---- development ----
        bool atCap = effective.DevelopmentLevel >= rules.Development.MaxLevel;
        string nextCost = atCap
            ? ""
            : FormatMoney(rules.DevelopmentCost(effective.DevelopmentLevel, effective.StaffTier, year).ToString());

        // ---- sponsors ----
        int playerTier = PlayerTeamTierNow(player);
        var activeSponsors = economy.Sponsors
            .Select(pair =>
            {
                var deal = rules.SponsorById(pair.Key);
                return new DynastySponsorContractModel
                {
                    Id = pair.Key,
                    Name = deal?.Name ?? pair.Key,
                    TierSlot = deal?.TierSlot ?? "",
                    SeasonsRemaining = pair.Value.SeasonsRemaining,
                    PerRace = deal is null ? "" : FormatMoney((deal.PerRace * rules.IndexForYear(year)).ToString()),
                    PerSeason = deal is null ? "" : FormatMoney((deal.PerSeason * rules.IndexForYear(year)).ToString()),
                };
            })
            .ToList();
        var board = rules.Sponsors.Board
            .Where(deal => deal.FromYear <= year && year <= deal.ToYear)
            .Select(deal =>
            {
                string reason = SponsorIneligibleReason(deal, player, effective, rules, year);
                return new DynastySponsorOfferModel
                {
                    Id = deal.Id,
                    Name = deal.Name,
                    TierSlot = deal.TierSlot,
                    SigningBonus = FormatMoney((deal.SigningBonus * rules.IndexForYear(year)).ToString()),
                    PerRace = FormatMoney((deal.PerRace * rules.IndexForYear(year)).ToString()),
                    PerSeason = FormatMoney((deal.PerSeason * rules.IndexForYear(year)).ToString()),
                    ContractSeasons = deal.ContractSeasons,
                    Eligible = reason.Length == 0,
                    IneligibleReason = reason,
                };
            })
            .ToList();

        return new DynastyEconomyDashboard
        {
            Balance = FormatMoney(economy.Balance.ToString()),
            InDeficit = economy.Balance < Rational.Zero,
            DeficitRounds = economy.DeficitRounds,
            GraceRounds = rules.Bankruptcy.GraceRounds,
            HardFloor = FormatMoney(rules.HardFloor(year).ToString()),
            Bankrupt = economy.Bankrupt,
            DevelopmentLevel = economy.DevelopmentLevel,
            DevelopmentMaxLevel = rules.Development.MaxLevel,
            NextDevelopmentCost = nextCost,
            DevelopmentAtCap = atCap,
            StaffTier = economy.StaffTier,
            StaffOptions = Enumerable.Range(0, rules.Staff.Count + 1)
                .Select(tier => new DynastyStaffOptionModel
                {
                    Tier = tier,
                    UpkeepPerSeason = tier == 0
                        ? ""
                        : FormatMoney((rules.StaffUpkeepPerSeason(tier) * rules.IndexForYear(year)).ToString()),
                    IsCurrent = tier == economy.StaffTier,
                })
                .ToList(),
            SecondSeat = economy.SecondSeat,
            SecondSeatSalaryPerSeason = FormatMoney(
                (rules.SecondSeatSalaryPerSeason(playerTier) * rules.IndexForYear(year)).ToString()),
            PayDriverBackingPerSeason = FormatMoney(
                (rules.SecondSeat.PayDriverBackingPerSeason * rules.IndexForYear(year)).ToString()),
            ActiveSponsors = activeSponsors,
            SponsorBoard = board,
            Statement = EconomyStatement(),
            PendingDecisions = BuildPendingModels(pending, economy, rules, year),
            NextRound = CurrentRoundNumber,
        };
    }

    /// <summary>Pending-decision display lines, amounts computed by replaying the plan ONCE in
    /// journal order, exactly the arithmetic the next fold will run.</summary>
    private IReadOnlyList<DynastyPendingDecisionModel> BuildPendingModels(
        IReadOnlyList<(long Seq, DynastyEconomyDecision Decision)> pending,
        DynastyEconomyState economy,
        DynastyEconomyRules rules,
        int year)
    {
        var models = new List<DynastyPendingDecisionModel>(pending.Count);
        var state = economy;
        foreach (var (seq, decision) in pending)
        {
            string amount = "";
            try
            {
                var after = DynastyEconomyFold.ApplyDecisions(new DynastyDecisionFoldContext
                {
                    State = state,
                    Rules = rules,
                    Year = year,
                    Round = CurrentRoundNumber,
                    Decisions = [decision],
                }).State;
                var delta = after.Balance - state.Balance;
                amount = delta == Rational.Zero ? "" : SignedMoney(delta.ToString());
                state = after;
            }
            catch (InvalidOperationException)
            {
                // A tampered plan is surfaced by Resimulate; the list stays readable.
            }
            models.Add(new DynastyPendingDecisionModel
            {
                Description = DescribePendingDecision(decision, rules),
                Amount = amount,
                Seq = seq,
            });
        }
        return models;
    }

    /// <inheritdoc />
    public void DeclareEconomyDecision(DynastyEconomyDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        if (_careerFileDeleted)
            throw new InvalidOperationException("This career has ended, the save was deleted.");
        var player = CurrentPlayerState();
        if (player?.Economy is not { } economy)
            throw new InvalidOperationException("This career does not run the Dynasty owner economy.");
        if (player.Deceased || player.Smgp?.CareerOver == true)
            throw new InvalidOperationException("The career is over, no further decisions can be made.");
        if (economy.Bankrupt)
            throw new InvalidOperationException("The team is bankrupt, the ledger is closed.");
        if (SeasonComplete)
            throw new InvalidOperationException(
                "The season is complete, decisions reopen with the next season's first round.");
        if (_environment.RulesDirectory is null || _environment.Rules.DynastyEconomy is not { } rules)
            throw new InvalidOperationException(
                "The Dynasty economy tables are unavailable on this install, restore the app's " +
                "data\\rules\\dynasty folder to make decisions.");
        int year = _seasonYear;

        var pending = PendingEconomyDecisions().Select(p => p.Decision).ToList();
        var effective = EffectiveEconomyState(economy, pending, rules, year);

        // ---- policy checks (clean messages BEFORE the structural dry-run backstop) ----
        switch (decision.Kind)
        {
            case DynastyEconomyDecisionKind.SignSponsor:
            {
                var deal = rules.SponsorById(decision.SponsorId ?? "")
                    ?? throw new InvalidOperationException("That sponsor is not on the board.");
                string reason = SponsorIneligibleReason(deal, player, effective, rules, year);
                if (reason.Length > 0)
                    throw new InvalidOperationException(reason);
                break;
            }
            case DynastyEconomyDecisionKind.DropSponsor:
                if (effective.SponsorContract(decision.SponsorId ?? "") is null)
                    throw new InvalidOperationException("That sponsor has no active contract to drop.");
                break;
            case DynastyEconomyDecisionKind.BuyDevelopment:
            {
                if (effective.DevelopmentLevel >= rules.Development.MaxLevel)
                    throw new InvalidOperationException("The development programme is already at its cap.");
                var cost = rules.DevelopmentCost(effective.DevelopmentLevel, effective.StaffTier, year);
                if (effective.Balance - cost < Rational.Zero)
                    throw new InvalidOperationException(
                        $"The team cannot afford this increment ({FormatMoney(cost.ToString())}), " +
                        "development is bought with money in the bank, not on credit.");
                break;
            }
            case DynastyEconomyDecisionKind.SetStaff:
                if (decision.StaffTier is not { } tier || tier < 0 || tier > rules.Staff.Count)
                    throw new InvalidOperationException("That staff tier does not exist.");
                if (tier == effective.StaffTier)
                    throw new InvalidOperationException("The team already runs that staff tier.");
                break;
            case DynastyEconomyDecisionKind.SetSecondSeat:
                if (decision.SecondSeat is not { } deal2)
                    throw new InvalidOperationException("The second-seat decision names no deal.");
                if (deal2 == effective.SecondSeat)
                    throw new InvalidOperationException("The second seat already runs that deal.");
                break;
            default:
                throw new InvalidOperationException($"Unknown economy decision kind '{decision.Kind}'.");
        }

        // Structural backstop: the REAL fold must accept the whole pending plan plus this
        // decision, whatever it refuses now it would refuse at fold time, so nothing invalid
        // can ever reach the journal.
        _ = DynastyEconomyFold.ApplyDecisions(new DynastyDecisionFoldContext
        {
            State = economy,
            Rules = rules,
            Year = year,
            Round = CurrentRoundNumber,
            Decisions = [.. pending, decision],
        });

        ReplayService.DeclareEconomyDecision(_database, _seasonId, CurrentRoundNumber, decision, NowUtc());
    }

    // ---------- helpers ----------

    private IReadOnlyList<(long Seq, DynastyEconomyDecision Decision)> PendingEconomyDecisions()
    {
        if (SeasonComplete)
            return [];
        int nextRound = CurrentRoundNumber;
        var pending = new List<(long, DynastyEconomyDecision)>();
        foreach (var row in JournalStore.ReadSeason(_database, _seasonId))
        {
            if (!string.Equals(row.Phase, JournalPhases.EconomyDecision, StringComparison.Ordinal)
                || row.Round != nextRound)
            {
                continue;
            }
            try
            {
                var decision = JsonSerializer.Deserialize<DynastyEconomyDecision>(
                    row.DeltaJson, Companion.Core.Json.CoreJson.Options);
                if (decision is not null)
                    pending.Add((row.Seq, decision));
            }
            catch (JsonException)
            {
                // A malformed input row is surfaced by Resimulate; the dashboard stays readable.
            }
        }
        return pending;
    }

    /// <summary>The economy state AFTER the pending plan (a dry-run through the real fold); the
    /// folded state verbatim when the plan cannot apply (a tampered row, resim reports it).</summary>
    private DynastyEconomyState EffectiveEconomyState(
        DynastyEconomyState economy,
        IReadOnlyList<DynastyEconomyDecision> pending,
        DynastyEconomyRules rules,
        int year)
    {
        if (pending.Count == 0)
            return economy;
        try
        {
            return DynastyEconomyFold.ApplyDecisions(new DynastyDecisionFoldContext
            {
                State = economy,
                Rules = rules,
                Year = year,
                Round = CurrentRoundNumber,
                Decisions = pending,
            }).State;
        }
        catch (InvalidOperationException)
        {
            return economy;
        }
    }

    private int PlayerTeamTierNow(PlayerCareerState player) =>
        StateStore.ReadTeamStates(_database, _seasonId, StateStore.StageStart)
            .FirstOrDefault(t => string.Equals(t.TeamId, player.CurrentTeamId, StringComparison.Ordinal))
            ?.Tier ?? 3;

    /// <summary>Why this deal cannot be signed right now, "" when it can (economy §5). The
    /// requirement checks read the current season's standings; a results-gated deal is honestly
    /// unavailable until the team has the record.</summary>
    private string SponsorIneligibleReason(
        DynastySponsorDeal deal,
        PlayerCareerState player,
        DynastyEconomyState effective,
        DynastyEconomyRules rules,
        int year)
    {
        if (deal.FromYear > year || year > deal.ToYear)
            return "Outside the sponsor's era window.";
        if (effective.SponsorContract(deal.Id) is not null)
            return "Already under contract.";
        int slotsUsed = effective.Sponsors.Keys
            .Count(id => string.Equals(rules.SponsorById(id)?.TierSlot, deal.TierSlot, StringComparison.Ordinal));
        if (slotsUsed >= rules.SlotsFor(deal.TierSlot))
            return $"No {deal.TierSlot} slot free.";
        if (player.Reputation < deal.MinReputation)
            return $"Needs reputation {deal.MinReputation:0} (currently {player.Reputation:0}).";
        if (deal.BestConstructorPositionRequired is { } required)
        {
            var standings = CurrentStandings();
            int? position = standings?.Constructors
                ?.FirstOrDefault(c => string.Equals(c.ConstructorId, player.CurrentTeamId, StringComparison.Ordinal))
                ?.Position
                ?? standings?.Drivers
                    .FirstOrDefault(d => string.Equals(d.DriverId, _playerDriverId, StringComparison.Ordinal))
                    ?.Position;
            if (position is null || position > required)
                return $"Needs P{required} or better in the standings.";
        }
        return "";
    }

    private IReadOnlyList<DynastyLedgerLineModel> EconomyStatement()
    {
        var lines = new List<DynastyLedgerLineModel>();
        foreach (var row in JournalStore.ReadSeason(_database, _seasonId))
        {
            try
            {
                switch (row.Phase)
                {
                    case JournalPhases.EconomyApplied:
                    {
                        using var doc = JsonDocument.Parse(row.DeltaJson);
                        var root = doc.RootElement;
                        string kind = root.TryGetProperty("kind", out var kv) ? kv.GetString() ?? "" : "";
                        lines.Add(new DynastyLedgerLineModel
                        {
                            Label = $"Decision, {row.Cause.Replace('-', ' ')}",
                            Round = row.Round,
                            Net = SignedMoney(root.TryGetProperty("amount", out var av) ? av.GetString() ?? "0" : "0"),
                            BalanceAfter = FormatMoney(
                                root.TryGetProperty("balanceTo", out var bv) ? bv.GetString() ?? "" : ""),
                            IsDeficit = false,
                        });
                        _ = kind;
                        break;
                    }
                    case JournalPhases.EconomyRound:
                    {
                        using var doc = JsonDocument.Parse(row.DeltaJson);
                        var root = doc.RootElement;
                        string balanceTo = root.TryGetProperty("balanceTo", out var bv) ? bv.GetString() ?? "" : "";
                        lines.Add(new DynastyLedgerLineModel
                        {
                            Label = $"Round {row.Round} settlement",
                            Round = row.Round,
                            Net = SignedMoney(root.TryGetProperty("net", out var nv) ? nv.GetString() ?? "0" : "0"),
                            BalanceAfter = FormatMoney(balanceTo),
                            IsDeficit = string.Equals(row.Cause, "deficit", StringComparison.Ordinal),
                        });
                        break;
                    }
                    case JournalPhases.EconomySeason:
                    {
                        using var doc = JsonDocument.Parse(row.DeltaJson);
                        var root = doc.RootElement;
                        lines.Add(new DynastyLedgerLineModel
                        {
                            Label = "Season settlement",
                            Round = null,
                            Net = SignedMoney(root.TryGetProperty("net", out var nv) ? nv.GetString() ?? "0" : "0"),
                            BalanceAfter = FormatMoney(
                                root.TryGetProperty("balanceTo", out var bv) ? bv.GetString() ?? "" : ""),
                            IsDeficit = false,
                        });
                        break;
                    }
                    case JournalPhases.EconomyBankruptcy:
                    {
                        using var doc = JsonDocument.Parse(row.DeltaJson);
                        var root = doc.RootElement;
                        lines.Add(new DynastyLedgerLineModel
                        {
                            Label = "BANKRUPTCY",
                            Round = row.Round,
                            Net = "",
                            BalanceAfter = FormatMoney(
                                root.TryGetProperty("balance", out var bv) ? bv.GetString() ?? "" : ""),
                            IsDeficit = true,
                        });
                        break;
                    }
                }
            }
            catch (JsonException)
            {
                // A malformed delta cell never breaks the display feed.
            }
        }
        lines.Reverse(); // newest first
        return lines;
    }

    private static string DescribePendingDecision(DynastyEconomyDecision decision, DynastyEconomyRules rules) =>
        decision.Kind switch
        {
            DynastyEconomyDecisionKind.SignSponsor =>
                $"Sign {rules.SponsorById(decision.SponsorId ?? "")?.Name ?? decision.SponsorId}",
            DynastyEconomyDecisionKind.DropSponsor =>
                $"Drop {rules.SponsorById(decision.SponsorId ?? "")?.Name ?? decision.SponsorId}",
            DynastyEconomyDecisionKind.BuyDevelopment => "Buy a development increment",
            DynastyEconomyDecisionKind.SetStaff => decision.StaffTier is > 0
                ? $"Set engineering staff to tier {decision.StaffTier}"
                : "Release the engineering staff",
            DynastyEconomyDecisionKind.SetSecondSeat => decision.SecondSeat == SecondSeatDeal.PayDriver
                ? "Switch the second seat to a pay-driver deal"
                : "Retain the second driver on salary",
            _ => decision.Kind.ToString(),
        };

    private static string SignedMoney(string rationalText)
    {
        string formatted = FormatMoney(rationalText);
        // A zero movement (a free lever, drop a sponsor, set staff/second-seat) shows blank, not
        // "+0", matching the pending-decision display so the ledger reads consistently.
        if (formatted.Length == 0 || formatted == "0")
            return "";
        return formatted.StartsWith('-') ? formatted : "+" + formatted;
    }
}
