using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Companion.ViewModels.Services;

namespace Companion.ViewModels.Shell;

/// <summary>
/// The SMGP promotion / demotion screen (3c-3) — its own full-immersion step in the Upcoming Race
/// loop, shown AFTER the confirm interstitial when the round produced a seat change: a two-wins
/// offer to accept/decline (a climb up the ladder) or a forced relegation to acknowledge. A thin
/// wrapper over the display-only <see cref="SmgpPromotionModel"/> (the new team's photo, story and
/// car) plus the two commands the shell wires to <c>ICareerSession.ResolveSmgpOffer</c> + advance.
/// </summary>
public sealed partial class PromotionViewModel : ObservableObject
{
    private readonly Action _onAccept;
    private readonly Action _onDecline;

    public PromotionViewModel(SmgpPromotionModel model, Action onAccept, Action onDecline)
    {
        Model = model;
        _onAccept = onAccept;
        _onDecline = onDecline;
    }

    /// <summary>The screen's display data — bound directly by the view.</summary>
    public SmgpPromotionModel Model { get; }

    /// <summary>True for a promotion offer (the Decline button shows); false for a forced demotion
    /// (acknowledge only).</summary>
    public bool CanDecline => Model.CanDecline;

    /// <summary>Accept the offer (promotion) or acknowledge the drop (demotion) — then advance.</summary>
    [RelayCommand]
    private void Accept() => _onAccept();

    /// <summary>Decline the offer and keep the current seat — then advance. Never shown for a
    /// demotion (it cannot be declined).</summary>
    [RelayCommand]
    private void Decline() => _onDecline();
}
