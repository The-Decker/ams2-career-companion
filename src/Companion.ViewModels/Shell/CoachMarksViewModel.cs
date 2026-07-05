using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Companion.ViewModels.Settings;

namespace Companion.ViewModels.Shell;

/// <summary>
/// First-run coach marks (ux-round contract section 4): three dismissable callouts — the
/// briefing setup checklist, result entry's "type OR drag", and the standings rules chip.
/// Each shows until its "Got it" is clicked, then the dismissal persists in settings and the
/// callout never returns (Reset-to-defaults un-dismisses, by design — it resets everything).
/// Owned by <see cref="HomeViewModel"/> so all three career screens bind to one instance;
/// WPF-free so show/dismiss/persist is unit-tested without a view.
/// </summary>
public sealed partial class CoachMarksViewModel : ObservableObject
{
    // ids as persisted in settings.json (camelCase like the rest of the file)
    public const string BriefingChecklistId = "briefingChecklist";
    public const string ResultEntryTypeOrDragId = "resultEntryTypeOrDrag";
    public const string StandingsRulesChipId = "standingsRulesChip";

    private readonly ISettingsService? _settings;

    public CoachMarksViewModel(ISettingsService? settings = null)
    {
        _settings = settings;
        var dismissed = settings?.Current.DismissedCoachMarks ?? [];
        _showBriefingChecklist = !IsDismissed(dismissed, BriefingChecklistId);
        _showResultEntryTypeOrDrag = !IsDismissed(dismissed, ResultEntryTypeOrDragId);
        _showStandingsRulesChip = !IsDismissed(dismissed, StandingsRulesChipId);
    }

    [ObservableProperty]
    private bool _showBriefingChecklist;

    [ObservableProperty]
    private bool _showResultEntryTypeOrDrag;

    [ObservableProperty]
    private bool _showStandingsRulesChip;

    [RelayCommand]
    private void DismissBriefingChecklist()
    {
        ShowBriefingChecklist = false;
        Persist(BriefingChecklistId);
    }

    [RelayCommand]
    private void DismissResultEntryTypeOrDrag()
    {
        ShowResultEntryTypeOrDrag = false;
        Persist(ResultEntryTypeOrDragId);
    }

    [RelayCommand]
    private void DismissStandingsRulesChip()
    {
        ShowStandingsRulesChip = false;
        Persist(StandingsRulesChipId);
    }

    private void Persist(string id) => _settings?.Update(s => s with
    {
        // Normalized() dedupes, so a double dismissal never duplicates the id.
        DismissedCoachMarks = [.. s.DismissedCoachMarks, id],
    });

    private static bool IsDismissed(IReadOnlyList<string> dismissed, string id) =>
        dismissed.Contains(id, StringComparer.OrdinalIgnoreCase);
}
