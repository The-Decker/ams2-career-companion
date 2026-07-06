using System.Windows.Controls;

namespace Companion.App.Views;

/// <summary>The History / Scrapbook tab: a read-only scrapbook of the whole career — a records
/// book, per-season cards down a lineage timeline, and every dispatch archived forever. Pure
/// bindings to <see cref="Companion.ViewModels.Hub.HistoryViewModel"/>; the archived-article
/// expand/collapse rides the same command-bound toggle the News ticker uses, so there is no
/// code-behind beyond the generated <c>InitializeComponent</c>.</summary>
public partial class HistoryView : UserControl
{
    public HistoryView()
    {
        InitializeComponent();
    }
}
