using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Companion.Core.Character;
using Companion.ViewModels.Services;
using Companion.ViewModels.Wizard;

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

    /// <summary>One-shot level-up signal. Slice 0 publishes the bind contract; detection lands in Slice 4.</summary>
    [ObservableProperty]
    private bool _levelUpPending;

    /// <summary>How many levels were gained in the unacknowledged level-up moment.</summary>
    [ObservableProperty]
    private int _levelsGained;

    /// <summary>The bindable skill-tree lanes. Empty until the rules-backed projection lands.</summary>
    [ObservableProperty]
    private IReadOnlyList<SkillBranchViewModel> _skillTree = [];

    /// <summary>The in-career spend currency. Skill Points are numerically the existing Available CP.</summary>
    public int SkillPointsAvailable => _session.AvailableCharacterCp();

    /// <summary>Milestone respec tokens currently available.</summary>
    public int RespecTokens => _session.RespecTokensAvailable();

    /// <summary>The five talent stats which directly shape driving ratings.</summary>
    public IReadOnlyList<DossierStat> TalentStatsView =>
        Dossier?.Stats.Where(stat => stat.Talent).ToList() ?? [];

    /// <summary>The marketability and durability meta stats.</summary>
    public IReadOnlyList<DossierStat> MetaStatsView =>
        Dossier?.Stats.Where(stat => !stat.Talent).ToList() ?? [];

    /// <summary>The driver's current availability, ready for display.</summary>
    public string AvailabilityLabel => Dossier?.AvailabilityLabel ?? "Fit";

    /// <summary>True when this career has a character to show — the hub adds the Driver tab only then.</summary>
    public bool HasCharacter => Dossier is not null;

    /// <summary>"Team · year" — who the driver races for this season; null when unknown.</summary>
    public string? TeamLine =>
        _session.PlayerTeamName() is { Length: > 0 } team ? $"{team}  ·  {_session.Summary.SeasonYear}" : null;

    /// <summary>The team-coloured PLAYER portrait (<c>player.&lt;team&gt;</c>), keyed off the player's
    /// current team so it follows a mid-season move. Null when the team is unknown.</summary>
    [ObservableProperty]
    private string? _playerImageKey;

    /// <summary>The car the player currently drives — its preview image key
    /// (<c>cars/&lt;driverId&gt;.png</c>). Null when the player's seat has no car-preview driver id.</summary>
    [ObservableProperty]
    private string? _playerCarKey;

    /// <summary>The player's arcade car-spec card (machine/engine/power + ENG-TM-SUS-TIRE-BRA bars),
    /// or null when the car has no authored spec (the card then collapses).</summary>
    [ObservableProperty]
    private CarSpecCardViewModel? _playerCarSpec;

    /// <summary>The SMGP evolving-narrative TIMELINE for the player (Task 2/3.3) — the milestone beats
    /// (arrived, first win, promotions, titles, rivalries…) surfaced on the Driver tab as the story
    /// progression. Empty for a non-SMGP career.</summary>
    [ObservableProperty]
    private IReadOnlyList<Companion.Core.Smgp.SmgpCareerBeat> _timeline = [];

    /// <summary>The one-line live narrative intro (the header above the timeline). Empty off-SMGP.</summary>
    [ObservableProperty]
    private string _narrativeIntro = "";

    /// <summary>True when there is an SMGP career story to show (the Driver tab renders the timeline).</summary>
    public bool HasSmgpNarrative => Timeline.Count > 0 || NarrativeIntro.Length > 0;

    /// <summary>Dismisses the one-shot level-up moment.</summary>
    [RelayCommand]
    public void AcknowledgeLevelUp()
    {
        LevelUpPending = false;
        LevelsGained = 0;
    }

    /// <summary>Slice-0 command stub; unlock behavior lands after the tree projection.</summary>
    [RelayCommand]
    private void UnlockNode(SkillNodeViewModel? node)
    {
    }

    /// <summary>Slice-0 command stub; respec behavior lands in Slice 5.</summary>
    [RelayCommand]
    private void RespecNode(SkillNodeViewModel? node)
    {
    }

    public void Refresh()
    {
        Dossier = _session.CharacterDossier();
        SkillTree = [];

        // The player's current seat gives the team (portrait + spec) and the car (preview image).
        var playerSeat = _session.CurrentGrid().FirstOrDefault(s => s.IsPlayer);
        PlayerImageKey = playerSeat?.TeamId is { Length: > 0 } teamId
            ? GridSeatChoice.PlayerImageKey(teamId)
            : null;
        PlayerCarKey = playerSeat?.DriverId;
        PlayerCarSpec = _session.PlayerCarSpec();

        // The evolving SMGP story lives on the player's Paddock card (Task 2) — surface it here too.
        var playerCard = _session.SmgpPaddock()?.Drivers.FirstOrDefault(d => d.IsPlayer);
        Timeline = playerCard?.Timeline ?? [];
        NarrativeIntro = playerCard?.NarrativeIntro ?? "";

        OnPropertyChanged(nameof(HasCharacter));
        OnPropertyChanged(nameof(TeamLine));
        OnPropertyChanged(nameof(HasSmgpNarrative));
        OnPropertyChanged(nameof(SkillPointsAvailable));
        OnPropertyChanged(nameof(RespecTokens));
        OnPropertyChanged(nameof(TalentStatsView));
        OnPropertyChanged(nameof(MetaStatsView));
        OnPropertyChanged(nameof(AvailabilityLabel));
    }
}
