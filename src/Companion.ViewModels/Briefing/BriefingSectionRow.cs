namespace Companion.ViewModels.Briefing;

/// <summary>
/// One Race-Day checklist section (Event / Practice / Qualifying / Race / Rules) for the sectioned
/// briefing view: a heading plus the rows under it. The <see cref="Items"/> are the SAME
/// <see cref="BriefingChecklistItem"/> instances the viewmodel also holds flat in
/// <c>Settings</c> — ticking one here flips it there too, so the "N of M set" progress and the copy
/// summary (both computed over the flat list) stay in sync. Grouping in the viewmodel (rather than a
/// WPF <c>CollectionViewSource</c> with a <c>PropertyGroupDescription</c>) keeps the ViewModels
/// layer WPF-free and lets the view bind a plain nested <c>ItemsControl</c>. It does NOT by itself
/// make the render tests thread-safe: any <c>ItemsControl</c> still builds a default view through
/// WPF's process-wide static <c>ViewManager</c>, so the harness's determinism comes solely from
/// <c>DisableTestParallelization</c> (see the render harness AssemblyInfo).
/// </summary>
public sealed record BriefingSectionRow(string Title, IReadOnlyList<BriefingChecklistItem> Items);
