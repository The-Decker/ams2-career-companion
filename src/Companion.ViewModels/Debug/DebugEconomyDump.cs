using System.Text;
using Companion.ViewModels.Services;

namespace Companion.ViewModels.Debug;

/// <summary>
/// TIER-1 inspect: renders the live career's <see cref="DynastyEconomyDashboard"/> as plain text
/// for the debug menu's inspector. READ-ONLY — the dashboard is already display-formatted at the
/// session boundary, so this never touches the fold.
///
/// THE MONEY SEAM (build brief §2 — "stub hooks only"): force-money / force-sponsor levers are
/// deliberately NOT here. When the tycoon-economy workstream wants debug levers, they belong in
/// the menu as commands over <see cref="ICareerSession.DeclareEconomyDecision"/> — the validated,
/// journaled INPUT seam the fold re-derives — so a debug-driven economy still resimulates
/// byte-identical. Never poke balance / sponsor / bankruptcy state directly.
/// </summary>
public static class DebugEconomyDump
{
    /// <summary>Renders the whole dashboard: balance + deficit state, development, staff, the
    /// second seat, active sponsors, the sponsor board with eligibility, the season statement,
    /// and the pending decision plan. Money strings arrive display-formatted and pass through
    /// verbatim (exact <c>Rational</c> never leaves the fold — the session already formatted).</summary>
    public static string Format(DynastyEconomyDashboard dashboard)
    {
        ArgumentNullException.ThrowIfNull(dashboard);
        var sb = new StringBuilder();
        sb.AppendLine("=== DYNASTY ECONOMY ===");
        sb.AppendLine($"Balance: {dashboard.Balance}" +
                      (dashboard.Bankrupt ? "  BANKRUPT" : dashboard.InDeficit ? "  (in deficit)" : ""));
        sb.AppendLine($"Deficit rounds: {dashboard.DeficitRounds} (grace {dashboard.GraceRounds})" +
                      $"   hard floor: {dashboard.HardFloor}");
        sb.AppendLine($"Development: level {dashboard.DevelopmentLevel}/{dashboard.DevelopmentMaxLevel}" +
                      (dashboard.DevelopmentAtCap
                          ? " (at cap)"
                          : $"   next: {dashboard.NextDevelopmentCost}"));
        sb.AppendLine($"Staff tier: {dashboard.StaffTier}");
        foreach (var option in dashboard.StaffOptions)
            sb.AppendLine($"  tier {option.Tier}: {option.UpkeepPerSeason}/season" +
                          (option.IsCurrent ? "  (current)" : ""));
        sb.AppendLine($"Second seat: {dashboard.SecondSeat}   retained {dashboard.SecondSeatSalaryPerSeason}/season" +
                      $"   pay-driver backing {dashboard.PayDriverBackingPerSeason}/season");

        sb.AppendLine().AppendLine($"Active sponsors ({dashboard.ActiveSponsors.Count}):");
        foreach (var sponsor in dashboard.ActiveSponsors)
            sb.AppendLine($"  [{sponsor.TierSlot}] {sponsor.Name} — {sponsor.PerRace}/race," +
                          $" {sponsor.PerSeason}/season, {sponsor.SeasonsRemaining} season(s) left");

        sb.AppendLine().AppendLine($"Sponsor board ({dashboard.SponsorBoard.Count}):");
        foreach (var offer in dashboard.SponsorBoard)
            sb.AppendLine($"  [{offer.TierSlot}] {offer.Name} — sign {offer.SigningBonus}," +
                          $" {offer.PerRace}/race, {offer.PerSeason}/season x{offer.ContractSeasons}" +
                          (offer.Eligible ? "  ELIGIBLE" : $"  ({offer.IneligibleReason})"));

        sb.AppendLine().AppendLine($"Statement (newest first, {dashboard.Statement.Count} lines):");
        foreach (var line in dashboard.Statement.Take(12))
            sb.AppendLine($"  {line.Label}: {line.Net}  →  {line.BalanceAfter}" +
                          (line.IsDeficit ? "  (deficit)" : ""));
        if (dashboard.Statement.Count > 12)
            sb.AppendLine($"  … {dashboard.Statement.Count - 12} more");

        sb.AppendLine().AppendLine(dashboard.HasPendingDecisions
            ? $"Pending decisions for round {dashboard.NextRound}:"
            : $"No pending decisions for round {dashboard.NextRound}.");
        foreach (var decision in dashboard.PendingDecisions)
            sb.AppendLine($"  #{decision.Seq} {decision.Description}" +
                          (decision.Amount.Length > 0 ? $"  ({decision.Amount})" : ""));
        return sb.ToString().TrimEnd();
    }
}
