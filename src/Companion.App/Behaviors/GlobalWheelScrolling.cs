using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Companion.App.Behaviors;

/// <summary>
/// App-wide mouse-wheel scrolling: the wheel always scrolls the nearest scrollable region, and
/// when that region can't move any further (or can't scroll at all), the gesture is handed up to
/// the nearest scrollable ancestor so the outer page keeps scrolling, no dead spots over lists,
/// cards, text boxes, or closed combo boxes.
///
/// WPF's default is the friction the user hit: an inner <see cref="ScrollViewer"/> (the one every
/// ListBox/ItemsControl/DataGrid/multiline TextBox builds internally) marks the wheel event
/// <c>Handled</c> even when it has nothing left to scroll, and a closed <see cref="ComboBox"/>
/// eats the wheel to change its selection. Either way the outer page stops responding while the
/// cursor sits over that element. This registers two application-global class handlers on the
/// TUNNELING preview event, so it runs before the eater consumes the gesture, and, only when the
/// element under the cursor can't use the wheel, re-raises the gesture directly on the nearest
/// scrollable ancestor (skipping any intermediate can't-scroll region, so arbitrary nesting still
/// works, at native scroll speed). Normal inner scrolling is untouched: a list still scrolls while
/// it has room. No per-view markup required.
///
/// Registered from a <see cref="ModuleInitializerAttribute"/> so it arms once at assembly load with
/// zero edits to the shell/composition root, a single self-contained hook.
/// </summary>
internal static class GlobalWheelScrolling
{
    [ModuleInitializer]
    internal static void Install()
    {
        // Preview (tunneling) so we run BEFORE the ScrollViewer/ComboBox marks the event handled.
        EventManager.RegisterClassHandler(
            typeof(ScrollViewer),
            UIElement.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(OnScrollViewerPreviewWheel));

        EventManager.RegisterClassHandler(
            typeof(ComboBox),
            UIElement.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(OnComboBoxPreviewWheel));
    }

    private static void OnScrollViewerPreviewWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || e.Delta == 0)
        {
            return;
        }

        var scrollViewer = (ScrollViewer)sender;

        // The tunnel visits every ScrollViewer ancestor of the cursor from the outside in; only the
        // innermost one (the region actually under the pointer) gets to decide, so an inner list
        // keeps its own scrolling and the outer page only steps in when the inner region is spent.
        if (!ReferenceEquals(NearestScrollViewer(e.OriginalSource as DependencyObject), scrollViewer))
        {
            return;
        }

        if (!CanScroll(scrollViewer, e.Delta))
        {
            RedirectToScrollableAncestor(scrollViewer, e);
        }
    }

    private static void OnComboBoxPreviewWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || e.Delta == 0)
        {
            return;
        }

        // An OPEN combo box scrolls its dropdown normally; a CLOSED one would otherwise silently
        // change its selection on a stray wheel tick while the page fails to scroll. Hand the closed
        // case to the page (only when there is actually a scroll region to move, otherwise leave
        // the combo's default behavior alone).
        if (!((ComboBox)sender).IsDropDownOpen)
        {
            RedirectToScrollableAncestor((ComboBox)sender, e);
        }
    }

    /// <summary>Finds the nearest ancestor <see cref="ScrollViewer"/> that can move in the wheel's
    /// direction and re-raises the gesture on it (native speed, skipping any spent region between).
    /// No-op when nothing above can scroll, the original event is left untouched.</summary>
    private static void RedirectToScrollableAncestor(FrameworkElement source, MouseWheelEventArgs e)
    {
        for (var node = VisualParentOf(source); node is not null; node = VisualParentOf(node))
        {
            if (node is ScrollViewer ancestor && CanScroll(ancestor, e.Delta))
            {
                e.Handled = true;
                ancestor.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                {
                    RoutedEvent = UIElement.MouseWheelEvent,
                    Source = ancestor,
                });
                return;
            }
        }
    }

    /// <summary>True when the viewer has room to move vertically in the wheel's direction
    /// (<paramref name="delta"/> &gt; 0 = scroll up / toward offset 0).</summary>
    private static bool CanScroll(ScrollViewer scrollViewer, int delta) =>
        scrollViewer.ScrollableHeight > 0.0 &&
        ((delta < 0 && scrollViewer.VerticalOffset < scrollViewer.ScrollableHeight) ||
         (delta > 0 && scrollViewer.VerticalOffset > 0.0));

    /// <summary>The parent to forward to, visual parent first (covers templated content), logical
    /// parent as a fallback for elements not yet in a visual tree.</summary>
    private static DependencyObject? VisualParentOf(DependencyObject node) =>
        (node is Visual or Visual3D ? VisualTreeHelper.GetParent(node) : null)
        ?? LogicalTreeHelper.GetParent(node);

    /// <summary>Walks up from the cursor's element to the first enclosing <see cref="ScrollViewer"/>,
    /// or null when the pointer is over content outside any scroll region.</summary>
    private static ScrollViewer? NearestScrollViewer(DependencyObject? node)
    {
        for (; node is not null; node = VisualParentOf(node))
        {
            if (node is ScrollViewer scrollViewer)
            {
                return scrollViewer;
            }
        }
        return null;
    }
}
