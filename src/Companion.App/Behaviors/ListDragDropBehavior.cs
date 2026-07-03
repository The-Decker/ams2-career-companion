using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Companion.Core.Grid;
using Companion.ViewModels.ResultEntry;

namespace Companion.App.Behaviors;

/// <summary>
/// The thin drag-and-drop layer for the result-entry screen (ux-round contract §1). Attached
/// properties turn a list or drop-zone border into a node of the drag graph via a
/// <see cref="RoleProperty"/> ("remaining" | "order" | "dnf" | "dsq"); every drop resolves to
/// exactly one call on the tested <see cref="ResultEntryViewModel"/> mouse primitives —
/// InsertAt / MoveTo / MarkDnf(+Bulk) / MarkDsq / Unmark — so NO result mutation logic lives
/// here. What does live here is pure UI mechanics: the drag threshold, the payload
/// (driver ids + source role), the insertion-indicator adorner on the finishing order, and
/// the <see cref="IsDragOverProperty"/> flag the zone styles highlight on.
/// </summary>
public static class ListDragDropBehavior
{
    public const string RemainingRole = "remaining";
    public const string OrderRole = "order";
    public const string DnfRole = "dnf";
    public const string DsqRole = "dsq";

    private const string IdsFormat = "companion/driver-ids";
    private const string SourceRoleFormat = "companion/source-role";

    // One drag at a time, app-wide.
    private static Point _dragStart;
    private static string? _pressedDriverId;
    private static WeakReference<FrameworkElement>? _pressedSource;

    // ---------- attached properties ----------

    /// <summary>Which node of the result-entry drag graph this element is.</summary>
    public static readonly DependencyProperty RoleProperty =
        DependencyProperty.RegisterAttached(
            "Role", typeof(string), typeof(ListDragDropBehavior), new PropertyMetadata(null));

    public static string? GetRole(DependencyObject element) => (string?)element.GetValue(RoleProperty);

    public static void SetRole(DependencyObject element, string? value) => element.SetValue(RoleProperty, value);

    /// <summary>Rows of this element can be picked up and dragged.</summary>
    public static readonly DependencyProperty IsDragSourceProperty =
        DependencyProperty.RegisterAttached(
            "IsDragSource", typeof(bool), typeof(ListDragDropBehavior),
            new PropertyMetadata(false, OnIsDragSourceChanged));

    public static bool GetIsDragSource(DependencyObject element) => (bool)element.GetValue(IsDragSourceProperty);

    public static void SetIsDragSource(DependencyObject element, bool value) => element.SetValue(IsDragSourceProperty, value);

    /// <summary>This element accepts driver drops (per the role acceptance rules).</summary>
    public static readonly DependencyProperty IsDropTargetProperty =
        DependencyProperty.RegisterAttached(
            "IsDropTarget", typeof(bool), typeof(ListDragDropBehavior),
            new PropertyMetadata(false, OnIsDropTargetChanged));

    public static bool GetIsDropTarget(DependencyObject element) => (bool)element.GetValue(IsDropTargetProperty);

    public static void SetIsDropTarget(DependencyObject element, bool value) => element.SetValue(IsDropTargetProperty, value);

    /// <summary>True while an acceptable drag hovers this drop target — styles highlight on it.</summary>
    public static readonly DependencyProperty IsDragOverProperty =
        DependencyProperty.RegisterAttached(
            "IsDragOver", typeof(bool), typeof(ListDragDropBehavior), new PropertyMetadata(false));

    public static bool GetIsDragOver(DependencyObject element) => (bool)element.GetValue(IsDragOverProperty);

    public static void SetIsDragOver(DependencyObject element, bool value) => element.SetValue(IsDragOverProperty, value);

    /// <summary>Mirrors a ListBox's (Extended) selection into the viewmodel's multi-select
    /// state, so bulk drags are testable VM-side (ToggleSelected/ClearSelection).</summary>
    public static readonly DependencyProperty SyncSelectionProperty =
        DependencyProperty.RegisterAttached(
            "SyncSelection", typeof(bool), typeof(ListDragDropBehavior),
            new PropertyMetadata(false, OnSyncSelectionChanged));

    public static bool GetSyncSelection(DependencyObject element) => (bool)element.GetValue(SyncSelectionProperty);

    public static void SetSyncSelection(DependencyObject element, bool value) => element.SetValue(SyncSelectionProperty, value);

    private static readonly DependencyProperty InsertionAdornerProperty =
        DependencyProperty.RegisterAttached(
            "InsertionAdorner", typeof(InsertionAdorner), typeof(ListDragDropBehavior),
            new PropertyMetadata(null));

    // ---------- wiring ----------

    private static void OnIsDragSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
            return;
        if (e.NewValue is true)
        {
            element.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
            element.PreviewMouseMove += OnPreviewMouseMove;
        }
        else
        {
            element.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
            element.PreviewMouseMove -= OnPreviewMouseMove;
        }
    }

    private static void OnIsDropTargetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
            return;
        if (e.NewValue is true)
        {
            element.AllowDrop = true;
            element.DragOver += OnDragOver;
            element.DragLeave += OnDragLeave;
            element.Drop += OnDrop;
        }
        else
        {
            element.AllowDrop = false;
            element.DragOver -= OnDragOver;
            element.DragLeave -= OnDragLeave;
            element.Drop -= OnDrop;
        }
    }

    private static void OnSyncSelectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox listBox)
            return;
        if (e.NewValue is true)
            listBox.SelectionChanged += OnSelectionChanged;
        else
            listBox.SelectionChanged -= OnSelectionChanged;
    }

    private static void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not FrameworkElement element || ViewModelOf(element) is not { } vm)
            return;

        foreach (object? item in e.RemovedItems)
        {
            if (DriverIdOf(item) is { } id && vm.IsSelected(id))
                vm.ToggleSelected(id);
        }
        foreach (object? item in e.AddedItems)
        {
            if (DriverIdOf(item) is { } id && !vm.IsSelected(id))
                vm.ToggleSelected(id);
        }
    }

    // ---------- drag source ----------

    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pressedDriverId = null;
        if (sender is not FrameworkElement element)
            return;
        if (RowDriverIdAt(element, e.OriginalSource) is not { } id)
            return;

        _pressedDriverId = id;
        _pressedSource = new WeakReference<FrameworkElement>(element);
        _dragStart = e.GetPosition(element);
    }

    private static void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_pressedDriverId is null ||
            sender is not FrameworkElement element ||
            _pressedSource?.TryGetTarget(out var pressed) != true || !ReferenceEquals(pressed, element) ||
            e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var position = e.GetPosition(element);
        if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        if (ViewModelOf(element) is not { } vm || GetRole(element) is not { } role)
            return;

        // A bulk drag: the pressed row is part of a multi-selection in the Remaining list.
        string[] ids =
            role == RemainingRole && vm.SelectedDriverIds.Count > 1 && vm.IsSelected(_pressedDriverId)
                ? vm.Remaining.Select(s => s.DriverId).Where(vm.IsSelected).ToArray()
                : [_pressedDriverId];
        if (ids.Length == 0)
            ids = [_pressedDriverId];

        var data = new DataObject();
        data.SetData(IdsFormat, string.Join('\n', ids));
        data.SetData(SourceRoleFormat, role);

        _pressedDriverId = null;
        DragDrop.DoDragDrop(element, data, DragDropEffects.Move);
    }

    // ---------- drop target ----------

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement element)
            return;
        e.Handled = true;

        string? targetRole = GetRole(element);
        if (ReadPayload(e.Data) is not { } payload || targetRole is null ||
            !Accepts(payload.SourceRole, targetRole))
        {
            e.Effects = DragDropEffects.None;
            SetIsDragOver(element, false);
            RemoveInsertionAdorner(element);
            return;
        }

        e.Effects = DragDropEffects.Move;
        SetIsDragOver(element, true);
        if (targetRole == OrderRole && element is ItemsControl list)
            ShowInsertionAdorner(list, InsertionIndex(list, e.GetPosition(list)));
    }

    private static void OnDragLeave(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement element)
            return;
        SetIsDragOver(element, false);
        RemoveInsertionAdorner(element);
    }

    private static void OnDrop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement element)
            return;
        e.Handled = true;
        SetIsDragOver(element, false);
        RemoveInsertionAdorner(element);

        if (ReadPayload(e.Data) is not { } payload ||
            GetRole(element) is not { } targetRole ||
            !Accepts(payload.SourceRole, targetRole) ||
            ViewModelOf(element) is not { } vm)
        {
            return;
        }

        switch (targetRole)
        {
            case OrderRole when element is ItemsControl list:
                DropOnOrder(vm, payload, InsertionIndex(list, e.GetPosition(list)));
                break;

            case DnfRole:
                if (payload.Ids.Length > 1)
                {
                    vm.MarkDnfBulk(payload.Ids);
                }
                else if (vm.MarkDnf(payload.Ids[0]))
                {
                    // The inline reason picker appears on the freshly dropped row.
                    vm.ReasonPickerDriverId = payload.Ids[0];
                }
                break;

            case DsqRole:
                foreach (string id in payload.Ids)
                    vm.MarkDsq(id);
                break;

            case RemainingRole:
                foreach (string id in payload.Ids)
                    vm.Unmark(id);
                break;
        }
    }

    /// <summary>Insert-before semantics at the indicator line. Reorders route through MoveTo
    /// (== the grammar's penalty reposition); everything else through InsertAt, which also
    /// pulls DNF/DSQ drivers back into the order.</summary>
    private static void DropOnOrder(ResultEntryViewModel vm, DragPayload payload, int insertionIndex)
    {
        foreach (string id in payload.Ids)
        {
            if (payload.SourceRole == OrderRole)
            {
                int current = IndexInOrder(vm, id);
                if (current < 0)
                    continue;
                // Removing the row from above the line shifts the line up by one.
                int finalIndex = current < insertionIndex ? insertionIndex - 1 : insertionIndex;
                vm.MoveTo(id, finalIndex);
            }
            else if (vm.InsertAt(id, insertionIndex))
            {
                insertionIndex++; // keep multi-drops in their dragged order
            }
        }
    }

    /// <summary>Every cross-role move is allowed; dropping back on the source list is not
    /// (except the order list, where it means reorder).</summary>
    private static bool Accepts(string sourceRole, string targetRole) =>
        targetRole == OrderRole || !string.Equals(sourceRole, targetRole, StringComparison.Ordinal);

    private static int IndexInOrder(ResultEntryViewModel vm, string driverId)
    {
        var order = vm.Classified;
        for (int i = 0; i < order.Count; i++)
        {
            if (order[i].DriverId == driverId)
                return i;
        }
        return -1;
    }

    /// <summary>The index the drop would insert BEFORE: the first row whose vertical midpoint
    /// is below the pointer; past the last row (or an empty list) appends.</summary>
    private static int InsertionIndex(ItemsControl list, Point position)
    {
        for (int i = 0; i < list.Items.Count; i++)
        {
            if (list.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement container &&
                container.IsVisible)
            {
                var top = container.TranslatePoint(new Point(0, 0), list);
                if (position.Y < top.Y + container.ActualHeight / 2)
                    return i;
            }
        }
        return list.Items.Count;
    }

    // ---------- payload ----------

    private readonly record struct DragPayload(string[] Ids, string SourceRole);

    private static DragPayload? ReadPayload(IDataObject data)
    {
        if (data.GetDataPresent(IdsFormat) && data.GetDataPresent(SourceRoleFormat) &&
            data.GetData(IdsFormat) is string joined && data.GetData(SourceRoleFormat) is string role)
        {
            string[] ids = joined.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (ids.Length > 0)
                return new DragPayload(ids, role);
        }
        return null;
    }

    // ---------- row / viewmodel resolution ----------

    private static ResultEntryViewModel? ViewModelOf(FrameworkElement element) =>
        element.DataContext as ResultEntryViewModel;

    /// <summary>The driver id of the row under the mouse: walk up from the original source to
    /// the first element whose DataContext is a row item (GridSeat or DnfEntry). Empty list
    /// space has the viewmodel as DataContext and resolves to null.</summary>
    private static string? RowDriverIdAt(FrameworkElement root, object originalSource)
    {
        var node = originalSource as DependencyObject;
        while (node is not null && !ReferenceEquals(node, root))
        {
            if (node is FrameworkElement fe && DriverIdOf(fe.DataContext) is { } id)
                return id;
            node = node is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }
        return null;
    }

    /// <summary>Row items are GridSeat (remaining/order/DSQ) or DnfEntry (DNF zone).</summary>
    private static string? DriverIdOf(object? item) => item switch
    {
        GridSeat seat => seat.DriverId,
        DnfEntry entry => entry.Seat.DriverId,
        _ => null,
    };

    // ---------- insertion indicator adorner ----------

    private static void ShowInsertionAdorner(ItemsControl list, int index)
    {
        if (list.GetValue(InsertionAdornerProperty) is not InsertionAdorner adorner)
        {
            var layer = AdornerLayer.GetAdornerLayer(list);
            if (layer is null)
                return;
            adorner = new InsertionAdorner(list);
            layer.Add(adorner);
            list.SetValue(InsertionAdornerProperty, adorner);
        }
        adorner.Index = index;
        adorner.InvalidateVisual();
    }

    private static void RemoveInsertionAdorner(FrameworkElement element)
    {
        if (element.GetValue(InsertionAdornerProperty) is InsertionAdorner adorner)
        {
            AdornerLayer.GetAdornerLayer(element)?.Remove(adorner);
            element.SetValue(InsertionAdornerProperty, null);
        }
    }

    /// <summary>The horizontal accent line marking where the dragged driver will land.</summary>
    private sealed class InsertionAdorner : Adorner
    {
        private static readonly Pen LinePen = CreatePen();
        private readonly ItemsControl _list;

        public InsertionAdorner(ItemsControl list)
            : base(list)
        {
            _list = list;
            IsHitTestVisible = false;
        }

        public int Index { get; set; }

        private static Pen CreatePen()
        {
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x4F, 0x8C, 0xFF)), 2.5);
            pen.Freeze();
            return pen;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            double y = 2;
            if (_list.Items.Count > 0)
            {
                if (Index < _list.Items.Count &&
                    _list.ItemContainerGenerator.ContainerFromIndex(Index) is FrameworkElement next)
                {
                    y = next.TranslatePoint(new Point(0, 0), _list).Y;
                }
                else if (_list.ItemContainerGenerator.ContainerFromIndex(_list.Items.Count - 1)
                    is FrameworkElement last)
                {
                    y = last.TranslatePoint(new Point(0, 0), _list).Y + last.ActualHeight;
                }
            }

            drawingContext.DrawLine(LinePen, new Point(2, y), new Point(_list.ActualWidth - 2, y));
            drawingContext.DrawEllipse(LinePen.Brush, null, new Point(4, y), 3.5, 3.5);
        }
    }
}
