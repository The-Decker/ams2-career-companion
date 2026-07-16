using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Companion.App.Audio;
using Companion.ViewModels.Hub;

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
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) =>
        SoundAssist.SetSuppressWhen(OpenNewsButton, ResolveHub() is null);

    private void OnOpenNews(object sender, RoutedEventArgs e)
    {
        if (ResolveHub() is not { } hub)
            return;

        var news = hub.Tabs.FirstOrDefault(tab => tab.Key == HubViewModel.NewsTabKey);
        if (news is not null && hub.SelectTabCommand.CanExecute(news))
            hub.SelectTabCommand.Execute(news);
        e.Handled = true;
    }

    private void OnOpenNewsArticle(object sender, RoutedEventArgs e)
    {
        string storyKey = (sender as FrameworkElement)?.Tag as string ?? "";
        if (storyKey.Length == 0 || ResolveHub() is not { } hub)
            return;

        var newsTab = hub.Tabs.FirstOrDefault(tab => tab.Key == HubViewModel.NewsTabKey);
        if (newsTab is null || !hub.News.OpenStoryCommand.CanExecute(storyKey))
            return;

        hub.SelectTabCommand.Execute(newsTab);
        hub.News.OpenStoryCommand.Execute(storyKey);
        e.Handled = true;
    }

    /// <summary>Reveals an exact read-side event after a News article navigates back to History.</summary>
    public bool RevealEvent(string eventKey)
    {
        if (string.IsNullOrWhiteSpace(eventKey) || DataContext is not HistoryViewModel history)
            return false;

        var item = history.Events.FirstOrDefault(candidate =>
            string.Equals(candidate.Key, eventKey, StringComparison.Ordinal));
        if (item is null)
            return false;

        HistoryEventList.UpdateLayout();
        if (HistoryEventList.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement container)
            return false;

        container.BringIntoView();
        HistoryScroll.Focus();
        return true;
    }

    private HubViewModel? ResolveHub()
    {
        DependencyObject? current = this;
        while (current is not null)
        {
            if (current is HubView { DataContext: HubViewModel hub })
                return hub;
            current = VisualTreeHelper.GetParent(current);
        }

        return Window.GetWindow(this)?.Tag as HubViewModel;
    }
}
