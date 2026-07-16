using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Companion.App.Audio;
using Companion.ViewModels.Hub;
using Companion.ViewModels.Shell;

namespace Companion.App.Views;

/// <summary>The living News desk: a read-only front page and article reader over the unified
/// dispatch projection. Code-behind is limited to focus restoration, App-owned History/tab
/// navigation, and the always-on-top <see cref="NewsWindow"/> mirror.</summary>
public partial class NewsView : UserControl
{
    // One shared companion window across the app; re-popping just re-focuses it (and replaces it
    // if a different career's feed is now showing).
    private static NewsWindow? _popOut;
    private FrameworkElement? _lastArticleInvoker;
    private NewsViewModel? _observedViewModel;

    public NewsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // The second NewsView living *inside* the pop-out window must not offer its own pop-out.
        if (Window.GetWindow(this) is NewsWindow)
            PopOutButton.Visibility = Visibility.Collapsed;
        Observe(DataContext as NewsViewModel);
        UpdateActionSuppression();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => Observe(null);

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        Observe(e.NewValue as NewsViewModel);

    private void Observe(NewsViewModel? viewModel)
    {
        if (ReferenceEquals(_observedViewModel, viewModel))
            return;
        if (_observedViewModel is not null)
            _observedViewModel.PropertyChanged -= OnNewsPropertyChanged;
        _observedViewModel = viewModel;
        if (_observedViewModel is not null)
            _observedViewModel.PropertyChanged += OnNewsPropertyChanged;
    }

    private void OnNewsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NewsViewModel.IsReaderOpen) or nameof(NewsViewModel.SelectedArticle))
            QueueReaderFocus();
    }

    private void OnStoryOpened(object sender, RoutedEventArgs e)
    {
        _lastArticleInvoker = sender as FrameworkElement;
        QueueReaderFocus();
    }

    private void OnArticleClosed(object sender, RoutedEventArgs e) => QueueReaderFocus();

    private void QueueReaderFocus() => Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
    {
        UpdateActionSuppression();
        if (DataContext is not NewsViewModel news)
            return;

        if (news.IsReaderOpen)
        {
            ArticleReaderScroll.ScrollToTop();
            ArticleCloseButton.Focus();
        }
        else if (_lastArticleInvoker is { IsVisible: true } invoker)
        {
            invoker.Focus();
        }
    });

    private void OnOpenHistory(object sender, RoutedEventArgs e)
    {
        string eventKey = (sender as FrameworkElement)?.Tag as string ?? "";
        if (eventKey.Length == 0 || ResolveHub() is not { } hub)
            return;

        HubView? hubView = FindAncestor<HubView>(this);
        DependencyObject? revealRoot = hubView is not null
            ? hubView
            : Window.GetWindow(this)?.Owner;
        var historyTab = hub.Tabs.FirstOrDefault(tab => tab.Key == HubViewModel.HistoryTabKey);
        if (historyTab is null || !hub.SelectTabCommand.CanExecute(historyTab))
            return;

        hub.SelectTabCommand.Execute(historyTab);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (revealRoot is FrameworkElement element)
                element.UpdateLayout();
            FindDescendant<HistoryView>(revealRoot)?.RevealEvent(eventKey);
        });
        e.Handled = true;
    }

    private void UpdateActionSuppression()
    {
        bool missingHub = ResolveHub() is null;
        SoundAssist.SetSuppressWhen(HistoryLinkButton, missingHub);
        SoundAssist.SetSuppressWhen(PopOutButton,
            _popOut is { IsLoaded: true, IsActive: true } existing &&
            ReferenceEquals(existing.DataContext, DataContext));
    }

    private void OnPopOut(object sender, RoutedEventArgs e)
    {
        if (_popOut is { IsLoaded: true } existing)
        {
            if (ReferenceEquals(existing.DataContext, DataContext))
            {
                existing.Activate();
                UpdateActionSuppression();
                return;
            }
            existing.Close(); // a stale window from a previous career — replace it
        }

        var owner = Window.GetWindow(this);
        var hub = ResolveHub() ?? (owner?.DataContext as ShellViewModel)?.Current as HubViewModel;
        var window = new NewsWindow
        {
            DataContext = DataContext,
            // The feed VM intentionally stays narrow; the Window tag carries the live Hub session
            // and RoundText token so Task-4 world dispatches remain reactive in the tear-off.
            Tag = hub,
            Owner = owner,
        };
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_popOut, window))
                _popOut = null;
            UpdateActionSuppression();
        };
        window.Activated += (_, _) => UpdateActionSuppression();
        window.Deactivated += (_, _) => UpdateActionSuppression();
        _popOut = window;
        window.Show();
        UpdateActionSuppression();
    }

    private HubViewModel? ResolveHub()
    {
        if (FindAncestor<HubView>(this) is { DataContext: HubViewModel hub })
            return hub;

        Window? window = Window.GetWindow(this);
        return window?.Tag as HubViewModel
            ?? window?.Owner?.Tag as HubViewModel
            ?? (window?.Owner?.DataContext as ShellViewModel)?.Current as HubViewModel;
    }

    private static T? FindAncestor<T>(DependencyObject child) where T : DependencyObject
    {
        for (DependencyObject? current = child; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is T match)
                return match;
        }
        return null;
    }

    private static T? FindDescendant<T>(DependencyObject? root) where T : DependencyObject
    {
        if (root is null)
            return null;
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                return match;
            if (FindDescendant<T>(child) is { } descendant)
                return descendant;
        }
        return null;
    }
}

/// <summary>
/// Live bookmark state for the reader's toggle button. The story records are immutable, so the
/// overlay-backed <see cref="NewsViewModel.BookmarkedStories"/> collection is the session truth:
/// membership by key decides filled/outline, and binding the collection's Count re-evaluates the
/// trigger on every toggle. The story's own persisted <c>IsBookmarked</c> flag (seeded from
/// schema-v6 reading state) is the fallback when the collection binding cannot resolve.
/// </summary>
public sealed class NewsBookmarkStateConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var story = values.Length > 2 ? values[2] as NewsStoryViewModel : null;
        if (story is null)
            return false;

        if (values[0] is IEnumerable<NewsStoryViewModel> bookmarked)
            return bookmarked.Any(candidate => string.Equals(candidate.Key, story.Key, StringComparison.Ordinal));

        return values.Length > 3 && values[3] is bool persisted && persisted;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
