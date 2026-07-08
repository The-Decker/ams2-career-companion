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
    public BriefingChecklistItem(string section, string label, string value)
    {
        Section = section;
        Label = label;
        Value = value;
    }

    /// <summary>The Race-Day section this row is grouped under ("Event", "Practice", "Qualifying",
    /// "Race", "Rules"); empty = ungrouped. Immutable, so it never triggers a regroup.</summary>
    public string Section { get; }

    public string Label { get; }

    public string Value { get; }

    /// <summary>The stable per-round tick key: (section, label). Distinct sessions reuse labels like
    /// "Weather slot 1" / "Duration", so keying ticks on the label alone would cross-tick them —
    /// the section prefix keeps each row independent.</summary>
    public string Key => Section.Length > 0 ? $"{Section}{Label}" : Label;

    [ObservableProperty]
    private bool isChecked;

    public void Toggle() => IsChecked = !IsChecked;
}
