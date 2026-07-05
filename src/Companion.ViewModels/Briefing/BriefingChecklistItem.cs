using CommunityToolkit.Mvvm.ComponentModel;

namespace Companion.ViewModels.Briefing;

/// <summary>
/// One check-off row of the Race Day setup checklist (ux-round briefing correction: AMS2's
/// custom-race settings are arrow-steppers — nothing can be pasted, so the briefing is a
/// manual checklist, not a copy source). Label + big glanceable value, plus the tick the
/// user flips as they set the line in-game. Tick state is session-scoped UI state — never
/// part of the result draft, never persisted across app restarts.
/// </summary>
public sealed partial class BriefingChecklistItem : ObservableObject
{
    public BriefingChecklistItem(string label, string value)
    {
        Label = label;
        Value = value;
    }

    public string Label { get; }

    public string Value { get; }

    [ObservableProperty]
    private bool isChecked;

    public void Toggle() => IsChecked = !IsChecked;
}
