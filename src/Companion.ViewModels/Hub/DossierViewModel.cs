using CommunityToolkit.Mvvm.ComponentModel;
using Companion.Core.Character;
using Companion.ViewModels.Services;

namespace Companion.ViewModels.Hub;

/// <summary>
/// The hub's Driver dossier lens (character depth 3): the player's character as the career unfolds —
/// name, the seven stats, the perks with what they do, and level/XP progression. A thin read-only
/// wrapper that re-projects <see cref="ICareerSession.CharacterDossier"/> after every applied round.
/// </summary>
public sealed partial class DossierViewModel : ObservableObject
{
    private readonly ICareerSession _session;

    public DossierViewModel(ICareerSession session)
    {
        _session = session;
        Refresh();
    }

    [ObservableProperty]
    private CharacterDossier? _dossier;

    /// <summary>True when this career has a character to show — the hub adds the Driver tab only then.</summary>
    public bool HasCharacter => Dossier is not null;

    /// <summary>"Team · year" — who the driver races for this season; null when unknown.</summary>
    public string? TeamLine =>
        _session.PlayerTeamName() is { Length: > 0 } team ? $"{team}  ·  {_session.Summary.SeasonYear}" : null;

    public void Refresh()
    {
        Dossier = _session.CharacterDossier();
        OnPropertyChanged(nameof(HasCharacter));
        OnPropertyChanged(nameof(TeamLine));
    }
}
