using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Companion.ViewModels.Services;

namespace Companion.ViewModels.Hub;

/// <summary>
/// The reusable "Why?" inspector panel (career-hub-design.md §5, decisions 4 + 5): renders a
/// <see cref="JournalChain"/>, a title, an ordered list of labelled contribution rows, and a
/// plain-language summary sentence, for binding. Pure, WPF-free, unit-testable: it holds no
/// session and does no I/O, it only shapes a value the seam's <see cref="ICareerSession.JournalFor"/>
/// already produced. The ordered <see cref="Rows"/> are the contribution-breakdown format designed
/// now to accept perk/stat rows later (decision 5) with no view-model change, a perk row is just
/// another <see cref="InspectorRowViewModel"/>.
/// </summary>
public sealed partial class InspectorViewModel : ObservableObject
{
    public InspectorViewModel(JournalChain chain)
    {
        ArgumentNullException.ThrowIfNull(chain);
        Chain = chain;
        Title = chain.Title;
        Summary = chain.Summary;
        Rows = chain.Contributions.Select(c => new InspectorRowViewModel(c)).ToArray();
    }

    /// <summary>The chain this inspector renders (kept for provenance / re-projection).</summary>
    public JournalChain Chain { get; }

    /// <summary>The panel header (e.g. "Why P2, You, Round 3").</summary>
    public string Title { get; }

    /// <summary>The ordered contribution rows, oldest journal row first, the walk-back top to
    /// bottom. Empty when the chain explained nothing.</summary>
    public IReadOnlyList<InspectorRowViewModel> Rows { get; }

    /// <summary>The one-line plain-language summary sentence (the Why? chip's prose); empty when
    /// the chain has no summary.</summary>
    public string Summary { get; }

    /// <summary>True when there is at least one contribution row to show, the panel binds its
    /// content visibility to this and shows a friendly empty note otherwise.</summary>
    public bool HasRows => Rows.Count > 0;

    /// <summary>True once a summary sentence exists (drives the summary line's visibility).</summary>
    public bool HasSummary => Summary.Length > 0;
}

/// <summary>One row of the inspector: a labelled contribution with an optional detail sentence and
/// an optional number. A record-like presentation wrapper over <see cref="JournalContribution"/>.</summary>
public sealed class InspectorRowViewModel
{
    private readonly JournalContribution _contribution;

    public InspectorRowViewModel(JournalContribution contribution)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        _contribution = contribution;
    }

    /// <summary>The short label naming the contribution (e.g. "Reputation", "Pace anchor").</summary>
    public string Label => _contribution.Label;

    /// <summary>The longer plain-language detail; empty when the label + value say it all.</summary>
    public string Detail => _contribution.Detail;

    public bool HasDetail => _contribution.Detail.Length > 0;

    /// <summary>The contribution's number as display text ("P8", "−3", "42.5"); empty for a
    /// narrative row (a headline) that carries no number.</summary>
    public string Value => _contribution.Value ?? "";

    /// <summary>True when this row carries a number (drives the value column's visibility).</summary>
    public bool HasValue => _contribution.Value is { Length: > 0 };

    /// <summary>The source journal <c>seq</c>, the provenance anchor behind the row.</summary>
    public long SourceSeq => _contribution.SourceSeq;
}

/// <summary>
/// A tiny host mixin for a surface that opens the shared inspector (Standings, History): it holds
/// the currently open <see cref="InspectorViewModel"/> and a close command, so a view binds one
/// panel and one dismiss target regardless of which number was clicked. Both mouse (click a number)
/// and keyboard (Esc / a bound key) drive it, locked decision 8's mouse+keyboard parity, because
/// <see cref="CloseInspectorCommand"/> is a plain command any input can invoke.
/// </summary>
public abstract partial class InspectorHostViewModel : ObservableObject
{
    /// <summary>The inspector panel currently open over this surface, or null when none is. The
    /// view shows the panel exactly when this is non-null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInspectorOpen))]
    private InspectorViewModel? _selectedInspector;

    /// <summary>True while an inspector is open (drives the panel + overlay visibility).</summary>
    public bool IsInspectorOpen => SelectedInspector is not null;

    /// <summary>Opens the inspector for a chain (no-ops on an empty chain so a number with no
    /// journal behind it simply does not open a blank panel).</summary>
    protected void ShowInspector(JournalChain chain)
    {
        ArgumentNullException.ThrowIfNull(chain);
        if (chain.IsEmpty)
            return;
        SelectedInspector = new InspectorViewModel(chain);
    }

    /// <summary>Dismisses the open inspector. Mouse (a close button) and keyboard (Esc / a bound
    /// accelerator) both bind here, so the panel is closable by either input (decision 8).</summary>
    [RelayCommand]
    private void CloseInspector() => SelectedInspector = null;
}
