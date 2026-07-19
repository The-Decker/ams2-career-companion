using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Companion.Core.Dynasty;
using Companion.ViewModels.Services;

namespace Companion.ViewModels.Hub;

/// <summary>
/// The Dynasty owner-economy hub lens ("Team Ledger", economy §9): the dashboard projection plus
/// the five decision commands, following the DossierViewModel decision cycle, the SESSION is the
/// affordability/availability authority; a refused decision surfaces its reason in
/// <see cref="EconomyActionError"/> and changes nothing. Present as a rail tab only when the
/// career runs the economy (<see cref="HasEconomy"/>). The GUI lane binds this against the
/// contract in docs/dev/codex-gui-dynasty-economy-brief.md.
/// </summary>
public sealed partial class EconomyViewModel : ObservableObject
{
    private readonly ICareerSession _session;

    public EconomyViewModel(ICareerSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        Dashboard = session.EconomyDashboard();
    }

    /// <summary>The whole bindable projection; null only for a non-economy career (the tab is
    /// then absent). Replaced wholesale on every <see cref="Refresh"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEconomy))]
    private DynastyEconomyDashboard? _dashboard;

    /// <summary>The player-facing reason the LAST decision was refused ("" after a success).</summary>
    [ObservableProperty]
    private string _economyActionError = "";

    /// <summary>The rail-tab presence gate (the Paddock HasPaddock pattern).</summary>
    public bool HasEconomy => Dashboard is not null;

    /// <summary>Re-projects the dashboard from the session (after every Apply/decision).</summary>
    public void Refresh()
    {
        Dashboard = _session.EconomyDashboard();
    }

    [RelayCommand]
    private void SignSponsor(string? sponsorId)
    {
        if (string.IsNullOrEmpty(sponsorId))
            return;
        Declare(new DynastyEconomyDecision
        {
            Kind = DynastyEconomyDecisionKind.SignSponsor,
            SponsorId = sponsorId,
        });
    }

    [RelayCommand]
    private void DropSponsor(string? sponsorId)
    {
        if (string.IsNullOrEmpty(sponsorId))
            return;
        Declare(new DynastyEconomyDecision
        {
            Kind = DynastyEconomyDecisionKind.DropSponsor,
            SponsorId = sponsorId,
        });
    }

    [RelayCommand]
    private void BuyDevelopment() =>
        Declare(new DynastyEconomyDecision { Kind = DynastyEconomyDecisionKind.BuyDevelopment });

    [RelayCommand]
    private void SetStaff(int tier) =>
        Declare(new DynastyEconomyDecision
        {
            Kind = DynastyEconomyDecisionKind.SetStaff,
            StaffTier = tier,
        });

    [RelayCommand]
    private void SetSecondSeat(SecondSeatDeal deal) =>
        Declare(new DynastyEconomyDecision
        {
            Kind = DynastyEconomyDecisionKind.SetSecondSeat,
            SecondSeat = deal,
        });

    private void Declare(DynastyEconomyDecision decision)
    {
        try
        {
            _session.DeclareEconomyDecision(decision);
            EconomyActionError = "";
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or ArgumentException)
        {
            EconomyActionError = ex.Message;
            return;
        }
        Refresh();
    }
}
