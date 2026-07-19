using CommunityToolkit.Mvvm.ComponentModel;
using Companion.ViewModels.Briefing;

namespace Companion.ViewModels.Shell;

/// <summary>
/// The SMGP rival screen, its own step in the Upcoming Race flow, AFTER race setup (briefing) and
/// BEFORE qualifying (Mike: "the rival moves to its own expanded screen after race setup"). It is a
/// thin wrapper over the SHARED <see cref="BriefingViewModel"/>: the rival pick / dossier / "name him"
/// state lives there (and is consumed at Apply via <c>BuildSmgpRival()</c>), so pulling the UI onto its
/// own screen changes nothing about the fold, the naming persists across qualifying → grid → race.
/// Only shown for an SMGP career with an active rival briefing.
/// </summary>
public sealed class RivalScreenViewModel : ObservableObject
{
    public RivalScreenViewModel(BriefingViewModel briefing) => Briefing = briefing;

    /// <summary>The shared briefing view-model, the rival screen binds its SMGP rival members
    /// directly (the view sets its content's DataContext to this).</summary>
    public BriefingViewModel Briefing { get; }
}
