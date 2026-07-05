using CommunityToolkit.Mvvm.ComponentModel;

namespace Companion.ViewModels.Hub;

/// <summary>
/// One entry in the hub's left tab rail: a stable key, a display title, a Segoe MDL2 Assets
/// glyph for the rail icon, and the content view-model shown when it is selected. The content
/// is settable so a lens tab (Standings, News) can be refreshed in place after a round applies
/// without rebuilding the rail. Pure presentation state — no session coupling.
/// </summary>
public sealed partial class HubTabViewModel : ObservableObject
{
    public HubTabViewModel(string key, string title, string glyph, ObservableObject content)
    {
        Key = key;
        Title = title;
        Glyph = glyph;
        _content = content;
    }

    /// <summary>Stable identity (not localized) — number-key + auto-select target.</summary>
    public string Key { get; }

    public string Title { get; }

    /// <summary>Segoe MDL2 Assets glyph shown on the rail.</summary>
    public string Glyph { get; }

    /// <summary>The tab's content view-model (resolved to a view by the App DataTemplates).
    /// Settable so a read-only lens can be re-projected after an Apply.</summary>
    [ObservableProperty]
    private ObservableObject _content;

    /// <summary>True while this is the selected rail entry (drives the accent underline).</summary>
    [ObservableProperty]
    private bool _isSelected;
}
