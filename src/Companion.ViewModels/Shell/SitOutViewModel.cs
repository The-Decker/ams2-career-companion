using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Companion.ViewModels.Services;

namespace Companion.ViewModels.Shell;

/// <summary>
/// The INJURED sit-out screen (character death &amp; injury §5): AMS2 cannot spectate a single-player race,
/// so a round the injured player must sit out is AUTO-SIMULATED — the player never enters a manual result
/// for it. A thin wrapper over the display-only <see cref="SitOutStatus"/> ("INJURED — auto-simulating
/// (N remaining)" / "SEASON OVER — recovering") plus the single Continue command the shell wires to fold
/// the auto-simulated round and advance. Mirrors <see cref="SmgpFinaleViewModel"/>.
/// </summary>
public sealed partial class SitOutViewModel : ObservableObject
{
    private readonly Action _onContinue;

    public SitOutViewModel(SitOutStatus status, Action onContinue)
    {
        Status = status;
        _onContinue = onContinue;
    }

    /// <summary>The sit-out banner data — bound directly by the view.</summary>
    public SitOutStatus Status { get; }

    /// <summary>Fold the auto-simulated round and advance to the next round (or the next sit-out).</summary>
    [RelayCommand]
    private void Continue() => _onContinue();
}
