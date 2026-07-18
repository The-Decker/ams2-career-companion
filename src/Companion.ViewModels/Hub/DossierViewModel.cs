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
    private readonly List<string> _pendingSkillNodeIds = [];

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

    /// <summary>The seven v2 attribute rails, separate from each family's mastery tree.</summary>
    [ObservableProperty]
    private IReadOnlyList<SkillAttributeRailViewModel> _attributeRails = [];

    [ObservableProperty]
    private SkillNodeViewModel? _selectedSkillNode;

    [ObservableProperty]
    private bool _skillNodeDetailOpen;

    /// <summary>The local, unconfirmed acquisition order. Confirm is the only write seam.</summary>
    [ObservableProperty]
    private IReadOnlyList<SkillNodeViewModel> _pendingSkillNodes = [];

    [ObservableProperty]
    private int _pendingSkillPointCost;

    [ObservableProperty]
    private int _skillPointsAfterPlan;

    [ObservableProperty]
    private bool _skillPlanDirty;

    [ObservableProperty]
    private string? _skillActionError;

    /// <summary>The authoritative XP-funded committed-tree reset quote. This is distinct from the
    /// free, local pending-plan reset above and is available only during a v2 season review.</summary>
    [ObservableProperty]
    private SkillResetPreview? _skillResetPreview;

    partial void OnSkillResetPreviewChanged(SkillResetPreview? value)
    {
        OnPropertyChanged(nameof(AvailableResetXp));
        OnPropertyChanged(nameof(SkillResetCost));
        OnPropertyChanged(nameof(AvailableResetXpAfter));
        OnPropertyChanged(nameof(SkillPointsRefunded));
        OnPropertyChanged(nameof(SkillPointsAfterReset));
        OnPropertyChanged(nameof(SkillResetAcquisitionCount));
        OnPropertyChanged(nameof(CanResetCommittedSkillTree));
        OnPropertyChanged(nameof(SkillResetBlockReason));
    }

    [ObservableProperty]
    private bool _skillResetConfirmationOpen;

    // The Driver view is a large binding surface and WPF can evaluate the same binding several times
    // whenever its DataTemplate is reconstructed. Keep every database-backed display value in the
    // refresh snapshot: selecting the tab must never re-enter the career session or scan the journal.
    private int _skillPointsAvailable;
    private int _respecTokens;
    private string? _teamLine;
    private string _countryName = "";
    private string? _countryFlagKey;
    private IReadOnlyList<DossierStat> _talentStatsView = [];
    private IReadOnlyList<DossierStat> _metaStatsView = [];

    /// <summary>The in-career spend currency. Skill Points are numerically the existing Available CP.</summary>
    public int SkillPointsAvailable => _skillPointsAvailable;

    public long LifetimeXp => Dossier?.LifetimeXp ?? 0L;

    public long AvailableResetXp =>
        SkillResetPreview?.AvailableResetXp ?? Dossier?.AvailableResetXp ?? 0L;

    public long SkillResetCost => SkillResetPreview?.Cost ?? 0L;

    public long AvailableResetXpAfter => SkillResetPreview?.AvailableResetXpAfter ?? AvailableResetXp;

    public int SkillPointsRefunded => SkillResetPreview?.SkillPointsRefunded ?? 0;

    public int SkillPointsAfterReset => SkillResetPreview?.SkillPointsAfterReset ?? SkillPointsAvailable;

    public int SkillResetAcquisitionCount => SkillResetPreview?.AcquisitionCount ?? 0;

    public bool CanResetCommittedSkillTree => SkillResetPreview?.CanApply == true;

    public string? SkillResetBlockReason => SkillResetPreview?.BlockReason;

    /// <summary>Milestone respec tokens currently available.</summary>
    public int RespecTokens => _respecTokens;

    /// <summary>The five talent stats which directly shape driving ratings.</summary>
    public IReadOnlyList<DossierStat> TalentStatsView => _talentStatsView;

    /// <summary>The marketability and durability meta stats.</summary>
    public IReadOnlyList<DossierStat> MetaStatsView => _metaStatsView;

    /// <summary>The driver's current availability, ready for display.</summary>
    public string AvailabilityLabel => Dossier?.AvailabilityLabel ?? "Fit";

    /// <summary>True when this career has a character to show, the hub adds the Driver tab only then.</summary>
    public bool HasCharacter => Dossier is not null;

    /// <summary>The player's persisted three-letter country code, or null for a legacy profile.</summary>
    public string? CountryCode => Dossier?.CountryCode;

    /// <summary>Display name resolved from the same immutable catalog used by character creation.</summary>
    public string CountryName => _countryName;

    /// <summary>The shipped country-keyed flag-art key for the player's nationality.</summary>
    public string? CountryFlagKey => _countryFlagKey;

    public bool HasCountry => CountryFlagKey is not null;

    /// <summary>"Team · year", who the driver races for this season; null when unknown.</summary>
    public string? TeamLine => _teamLine;

    /// <summary>The team-coloured PLAYER portrait (<c>player.&lt;team&gt;</c>), keyed off the player's
    /// current team so it follows a mid-season move. Null when the team is unknown.</summary>
    [ObservableProperty]
    private string? _playerImageKey;

    /// <summary>The car the player currently drives, its preview image key
    /// (<c>cars/&lt;driverId&gt;.png</c>). Null when the player's seat has no car-preview driver id.</summary>
    [ObservableProperty]
    private string? _playerCarKey;

    /// <summary>The player's arcade car-spec card (machine/engine/power + ENG-TM-SUS-TIRE-BRA bars),
    /// or null when the car has no authored spec (the card then collapses).</summary>
    [ObservableProperty]
    private CarSpecCardViewModel? _playerCarSpec;

    /// <summary>The SMGP evolving-narrative TIMELINE for the player (Task 2/3.3), the milestone beats
    /// (arrived, first win, promotions, titles, rivalries…) surfaced on the Driver tab as the story
    /// progression. Empty for a non-SMGP career.</summary>
    [ObservableProperty]
    private IReadOnlyList<Companion.Core.Smgp.SmgpCareerBeat> _timeline = [];

    /// <summary>The one-line live narrative intro (the header above the timeline). Empty off-SMGP.</summary>
    [ObservableProperty]
    private string _narrativeIntro = "";

    /// <summary>True when there is an SMGP career story to show (the Driver tab renders the timeline).</summary>
    public bool HasSmgpNarrative => Timeline.Count > 0 || NarrativeIntro.Length > 0;

    /// <summary>Dismisses the one-shot level-up moment and persists the acknowledgment (a user
    /// preference, never a fold input), so an unacknowledged level-up survives closing the app
    /// instead of silently vanishing on the next open.</summary>
    [RelayCommand]
    public void AcknowledgeLevelUp()
    {
        LevelUpPending = false;
        LevelsGained = 0;
        if (Dossier is { Level: > 0 } dossier)
        {
            _session.MarkStoryRead(LevelAckKeyPrefix
                + dossier.Level.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Key prefix for persisted level-up acknowledgments in the career's reading-state
    /// preference store (schema v6, user preference, survives re-simulation, never a fold input).</summary>
    private const string LevelAckKeyPrefix = "character:levelup:";

    /// <summary>The highest level the player has acknowledged (0 = no marker yet).</summary>
    private int LastAcknowledgedLevel()
    {
        int last = 0;
        foreach (var key in _session.ReadingState().Keys)
        {
            if (key.StartsWith(LevelAckKeyPrefix, StringComparison.Ordinal)
                && int.TryParse(
                    key.AsSpan(LevelAckKeyPrefix.Length),
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out int level))
            {
                last = Math.Max(last, level);
            }
        }
        return last;
    }

    /// <summary>The career's immutable mortality setting, readable mid-career (the wizard was the
    /// only surface that showed it). Display-only, the mode cannot change on an active career.</summary>
    public string MortalityLabel => _session.Mortality switch
    {
        Companion.Core.Career.MortalityMode.Normal => "MORTALITY: NORMAL",
        Companion.Core.Career.MortalityMode.Hardcore => "MORTALITY: HARDCORE",
        _ => "MORTALITY: OFF",
    };

    /// <summary>The career's medical record, every journaled accident outcome, oldest first.</summary>
    [ObservableProperty]
    private IReadOnlyList<InjuryHistoryEntry> _injuryHistory = [];

    public bool HasInjuryHistory => InjuryHistory.Count > 0;

    /// <summary>Unlocks one eligible perk/stat node through the existing authoritative spend seam.</summary>
    [RelayCommand]
    private void UnlockNode(SkillNodeViewModel? node)
    {
        if (node is null || !node.CanUnlock)
            return;
        if (node.Kind is CharacterSkillPlanEntry.MasteryKind or CharacterSkillPlanEntry.AttributeKind)
        {
            QueueSkillNode(node);
            return;
        }
        var spend = string.Equals(node.Kind, "stat", StringComparison.Ordinal)
            ? CharacterSpend.Stat(node.Id, node.Cost)
            : CharacterSpend.Perk(node.Id, node.Cost);
        _session.SpendCharacterPoint(spend);
        Refresh();
    }

    [RelayCommand]
    private void OpenSkillNode(SkillNodeViewModel? node)
    {
        if (node is null)
            return;
        SelectedSkillNode = node;
        SkillNodeDetailOpen = true;
        SkillActionError = null;
    }

    [RelayCommand]
    private void CloseSkillNode() => SkillNodeDetailOpen = false;

    [RelayCommand]
    private void QueueSkillNode(SkillNodeViewModel? node)
    {
        node ??= SelectedSkillNode;
        if (node is null || !node.CanUnlock || _pendingSkillNodeIds.Contains(node.Id, StringComparer.Ordinal))
            return;

        var candidate = _pendingSkillNodeIds.Append(node.Id).ToArray();
        try
        {
            var preview = _session.PreviewSkillPlan(candidate);
            _pendingSkillNodeIds.Add(node.Id);
            ApplySkillPlanPreview(preview, selectedId: node.Id);
            SkillActionError = null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or ArgumentException)
        {
            SkillActionError = ex.Message;
        }
    }

    /// <summary>Removing an ordered acquisition truncates it and every dependent suffix.</summary>
    [RelayCommand]
    private void RemovePendingSkillNode(SkillNodeViewModel? node)
    {
        node ??= SelectedSkillNode;
        if (node is null)
            return;
        int index = _pendingSkillNodeIds.FindIndex(id =>
            string.Equals(id, node.Id, StringComparison.Ordinal));
        if (index < 0)
            return;
        _pendingSkillNodeIds.RemoveRange(index, _pendingSkillNodeIds.Count - index);
        if (_pendingSkillNodeIds.Count == 0)
        {
            ResetSkillPlan();
            return;
        }

        try
        {
            ApplySkillPlanPreview(
                _session.PreviewSkillPlan(_pendingSkillNodeIds.ToArray()),
                selectedId: node.Id);
            SkillActionError = null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or ArgumentException)
        {
            SkillActionError = ex.Message;
        }
    }

    [RelayCommand]
    private void ResetSkillPlan()
    {
        _pendingSkillNodeIds.Clear();
        PendingSkillNodes = [];
        PendingSkillPointCost = 0;
        SkillPlanDirty = false;
        SkillActionError = null;
        RefreshSkillTree();
        SkillPointsAfterPlan = SkillPointsAvailable;
    }

    [RelayCommand]
    private void OpenSkillResetConfirmation()
    {
        RefreshSkillResetPreview();
        SkillResetConfirmationOpen = SkillResetPreview is not null;
    }

    [RelayCommand]
    private void CloseSkillResetConfirmation() => SkillResetConfirmationOpen = false;

    [RelayCommand]
    private void ConfirmSkillReset()
    {
        if (!CanResetCommittedSkillTree)
        {
            SkillActionError = SkillResetBlockReason ?? "The committed skill tree cannot be reset right now.";
            return;
        }

        try
        {
            _session.ApplySkillReset();
            _pendingSkillNodeIds.Clear();
            PendingSkillNodes = [];
            PendingSkillPointCost = 0;
            SkillPlanDirty = false;
            SkillNodeDetailOpen = false;
            SkillResetConfirmationOpen = false;
            SkillActionError = null;
            Refresh();
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or ArgumentException)
        {
            // A failed destructive confirmation changes nothing: preserve the local plan and keep
            // the confirmation open so the player can read the authoritative failure.
            SkillActionError = ex.Message;
        }
    }

    [RelayCommand]
    private void ConfirmSkillPlan()
    {
        if (_pendingSkillNodeIds.Count == 0)
            return;
        try
        {
            _session.ApplySkillPlan(_pendingSkillNodeIds.ToArray());
            _pendingSkillNodeIds.Clear();
            PendingSkillNodes = [];
            PendingSkillPointCost = 0;
            SkillPlanDirty = false;
            SkillActionError = null;
            SkillNodeDetailOpen = false;
            Refresh();
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or ArgumentException)
        {
            // Retain the local plan so the player can recover or adjust it after reading the error.
            SkillActionError = ex.Message;
        }
    }

    /// <summary>Refunds one owned tree node through the session's authoritative respec seam
    /// (milestone-token funded for v1 nodes; v2 careers use the committed-tree reset instead).</summary>
    [RelayCommand]
    private void RespecNode(SkillNodeViewModel? node)
    {
        if (node is null || !node.IsOwned)
            return;
        _session.RespecNode(node.Id);
        Refresh();
    }

    public void Refresh()
    {
        int? previousLevel = Dossier?.Level;
        var nextDossier = _session.CharacterDossier();
        Dossier = nextDossier;
        if (previousLevel is { } previous && nextDossier is { } next && next.Level > previous)
        {
            LevelUpPending = true;
            LevelsGained += next.Level - previous;
        }
        else if (previousLevel is null && nextDossier is { Level: > 0 } opened)
        {
            // Session (re)opened: the banner derives from the persisted acknowledgment marker so
            // an unacknowledged level-up survives an app restart. A career with no marker yet is
            // seeded at its current level (silently, no banner for history already lived).
            int acknowledged = LastAcknowledgedLevel();
            if (acknowledged == 0)
            {
                _session.MarkStoryRead(LevelAckKeyPrefix
                    + opened.Level.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            else if (opened.Level > acknowledged)
            {
                LevelUpPending = true;
                LevelsGained = opened.Level - acknowledged;
            }
        }

        RefreshBindingSnapshot(nextDossier);
        RefreshSkillTree();
        RefreshSkillResetPreview();
        if (_pendingSkillNodeIds.Count == 0)
        {
            PendingSkillNodes = [];
            PendingSkillPointCost = 0;
            SkillPointsAfterPlan = SkillPointsAvailable;
            SkillPlanDirty = false;
        }
        else
        {
            try
            {
                ApplySkillPlanPreview(_session.PreviewSkillPlan(_pendingSkillNodeIds.ToArray()));
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or ArgumentException)
            {
                SkillActionError = ex.Message;
            }
        }

        // The player's current seat gives the team (portrait + spec) and the car (preview image).
        var playerSeat = _session.CurrentGrid().FirstOrDefault(s => s.IsPlayer);
        PlayerImageKey = playerSeat?.TeamId is { Length: > 0 } teamId
            ? GridSeatChoice.PlayerImageKey(teamId)
            : null;
        PlayerCarKey = playerSeat?.DriverId;
        PlayerCarSpec = _session.PlayerCarSpec();

        // The evolving SMGP story lives on the player's Paddock card (Task 2), surface it here too.
        var playerCard = _session.SmgpPaddock()?.Drivers.FirstOrDefault(d => d.IsPlayer);
        Timeline = playerCard?.Timeline ?? [];
        NarrativeIntro = playerCard?.NarrativeIntro ?? "";

        // The medical record: every journaled accident outcome, verbatim from the fold's rows.
        InjuryHistory = _session.InjuryHistory();
        OnPropertyChanged(nameof(HasInjuryHistory));

        OnPropertyChanged(nameof(HasCharacter));
        OnPropertyChanged(nameof(CountryCode));
        OnPropertyChanged(nameof(CountryName));
        OnPropertyChanged(nameof(CountryFlagKey));
        OnPropertyChanged(nameof(HasCountry));
        OnPropertyChanged(nameof(TeamLine));
        OnPropertyChanged(nameof(HasSmgpNarrative));
        OnPropertyChanged(nameof(SkillPointsAvailable));
        OnPropertyChanged(nameof(LifetimeXp));
        OnPropertyChanged(nameof(AvailableResetXp));
        OnPropertyChanged(nameof(SkillResetCost));
        OnPropertyChanged(nameof(AvailableResetXpAfter));
        OnPropertyChanged(nameof(SkillPointsRefunded));
        OnPropertyChanged(nameof(SkillPointsAfterReset));
        OnPropertyChanged(nameof(SkillResetAcquisitionCount));
        OnPropertyChanged(nameof(CanResetCommittedSkillTree));
        OnPropertyChanged(nameof(SkillResetBlockReason));
        OnPropertyChanged(nameof(RespecTokens));
        OnPropertyChanged(nameof(TalentStatsView));
        OnPropertyChanged(nameof(MetaStatsView));
        OnPropertyChanged(nameof(AvailabilityLabel));
    }

    /// <summary>
    /// Captures values that are stable until the next applied round/development action. Several of
    /// these session calls read SQLite (and respec tokens scan the journal), so none belongs in a
    /// property getter that WPF may call repeatedly while rebuilding the Driver visual tree.
    /// </summary>
    private void RefreshBindingSnapshot(CharacterDossier? dossier)
    {
        if (dossier is null)
        {
            _skillPointsAvailable = 0;
            _respecTokens = 0;
            _teamLine = null;
            _countryName = "";
            _countryFlagKey = null;
            _talentStatsView = [];
            _metaStatsView = [];
            return;
        }

        _skillPointsAvailable = _session.AvailableCharacterCp();
        _respecTokens = _session.RespecTokensAvailable();
        string? team = _session.PlayerTeamName();
        _teamLine = team is { Length: > 0 }
            ? $"{team}  ·  {_session.Summary.SeasonYear}"
            : null;

        var country = CharacterCountryCatalog.Find(dossier.CountryCode);
        _countryName = country?.Name ?? dossier.CountryCode ?? "";
        _countryFlagKey = country?.FlagKey;
        _talentStatsView = dossier.Stats.Where(stat => stat.Talent).ToArray();
        _metaStatsView = dossier.Stats.Where(stat => !stat.Talent).ToArray();
    }

    private void RefreshSkillTree() => SetSkillTree(_session.SkillTree());

    private void RefreshSkillResetPreview()
    {
        try
        {
            SkillResetPreview = _session.PreviewSkillReset();
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or ArgumentException)
        {
            SkillResetPreview = null;
            SkillActionError = ex.Message;
        }
    }

    private void ApplySkillPlanPreview(SkillPlanPreview preview, string? selectedId = null)
    {
        SetSkillTree(preview.ProjectedTree, selectedId);
        var byId = SkillTree.SelectMany(branch => branch.Nodes)
            .ToDictionary(node => node.Id, StringComparer.Ordinal);
        PendingSkillNodes = _pendingSkillNodeIds
            .Where(byId.ContainsKey)
            .Select(id => byId[id])
            .ToArray();
        PendingSkillPointCost = preview.Input.TotalCost;
        SkillPointsAfterPlan = preview.SkillPointsAfterPlan;
        SkillPlanDirty = _pendingSkillNodeIds.Count > 0;
    }

    private void SetSkillTree(SkillTreeSnapshot? snapshot, string? selectedId = null)
    {
        selectedId ??= SelectedSkillNode?.Id;
        if (snapshot is null)
        {
            SkillTree = [];
            AttributeRails = [];
            SelectedSkillNode = null;
            return;
        }

        var names = snapshot.Branches.SelectMany(branch => branch.Nodes)
            .ToDictionary(node => node.Id, node => node.Name, StringComparer.Ordinal);
        SkillTree = snapshot.Branches.Select(branch =>
        {
            var nodes = branch.Nodes.Select(node => new SkillNodeViewModel
            {
                Id = node.Id,
                Name = node.Name,
                Description = node.Description,
                Kind = node.Kind,
                Cost = node.Cost,
                Tier = node.Tier,
                UnlockLevel = node.UnlockLevel,
                RequiresLabels = node.Requires.Select(id => names.GetValueOrDefault(id, id)).ToList(),
                RequiresIds = node.Requires,
                Order = node.Order,
                IconKey = node.IconKey,
                ExclusiveGroup = node.ExclusiveGroup,
                RailId = node.RailId,
                RailName = node.RailName,
                AttributeStatId = node.AttributeStatId,
                AttributeValueAfter = node.AttributeValueAfter,
                IsMasteryOverride = node.IsMasteryOverride,
                Benefits = node.Benefits,
                Drawbacks = node.Drawbacks,
                Effects = node.Effects,
                State = node.State,
                LockReason = node.LockReason,
            }).ToList();
            return new SkillBranchViewModel
            {
                Id = branch.Id,
                Name = branch.Name,
                IsMeta = branch.IsMeta,
                Nodes = nodes,
                MasteryNodes = nodes.Where(node =>
                    string.Equals(node.Kind, CharacterSkillPlanEntry.MasteryKind, StringComparison.Ordinal)).ToList(),
            };
        }).ToList();
        AttributeRails = SkillTree
            .SelectMany(branch => branch.Nodes)
            .Where(node =>
                string.Equals(node.Kind, CharacterSkillPlanEntry.AttributeKind, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(node.RailId))
            .GroupBy(node => node.RailId!, StringComparer.Ordinal)
            .Select(group =>
            {
                var nodes = group.ToList();
                return new SkillAttributeRailViewModel
                {
                    Id = group.Key,
                    Name = nodes[0].RailName,
                    StatId = nodes[0].AttributeStatId,
                    Nodes = nodes,
                    OwnedCount = nodes.Count(node => node.IsOwned),
                    TotalCount = nodes.Count,
                };
            }).ToList();
        SelectedSkillNode = selectedId is null
            ? null
            : SkillTree.SelectMany(branch => branch.Nodes).FirstOrDefault(node =>
                string.Equals(node.Id, selectedId, StringComparison.Ordinal));
    }
}
