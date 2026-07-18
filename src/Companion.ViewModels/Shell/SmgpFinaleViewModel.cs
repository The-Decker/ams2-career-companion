using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Companion.ViewModels.Services;

namespace Companion.ViewModels.Shell;

/// <summary>
/// The 17-season SMGP campaign FINALE (Mike's "final final screen"), a full-immersion step shown
/// ONCE, at the fold that completes the campaign, built around the secret <c>special.jpg</c> (or the
/// deeper <c>ultimate.jpg</c> for a flawless run). A thin wrapper over the display-only
/// <see cref="SmgpFinaleModel"/> plus the single Continue command the shell wires to advance into the
/// season review. Mirrors <see cref="PromotionViewModel"/>; unlike the promotion screen it makes NO
/// <c>ICareerSession</c> write, the finale is a pure read, so it never touches the replay gate.
/// </summary>
public sealed partial class SmgpFinaleViewModel : ObservableObject
{
    private readonly Action _onContinue;

    public SmgpFinaleViewModel(SmgpFinaleModel model, Action onContinue)
    {
        Model = model;
        _onContinue = onContinue;
    }

    /// <summary>The screen's display data, bound directly by the view.</summary>
    public SmgpFinaleModel Model { get; }

    /// <summary>Acknowledge the finale and continue to the season review.</summary>
    [RelayCommand]
    private void Continue() => _onContinue();
}
