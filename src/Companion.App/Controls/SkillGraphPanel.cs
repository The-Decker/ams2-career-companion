using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Companion.Core.Character;
using Companion.ViewModels.Hub;
using Companion.ViewModels.Wizard;

namespace Companion.App.Controls;

/// <summary>
/// Measures real item presenters and arranges a skill family as a left-to-right wiring diagram.
/// Tiers form columns; authored order forms rows. Prerequisite paths are drawn in
/// <see cref="OnRender"/>, naturally behind the child controls and outside the hit-test tree.
/// </summary>
public sealed class SkillGraphPanel : Panel
{
    public static readonly DependencyProperty NodeWidthProperty = DependencyProperty.Register(
        nameof(NodeWidth), typeof(double), typeof(SkillGraphPanel),
        new FrameworkPropertyMetadata(154d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty NodeMinHeightProperty = DependencyProperty.Register(
        nameof(NodeMinHeight), typeof(double), typeof(SkillGraphPanel),
        new FrameworkPropertyMetadata(70d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty TierGapProperty = DependencyProperty.Register(
        nameof(TierGap), typeof(double), typeof(SkillGraphPanel),
        new FrameworkPropertyMetadata(34d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty RowGapProperty = DependencyProperty.Register(
        nameof(RowGap), typeof(double), typeof(SkillGraphPanel),
        new FrameworkPropertyMetadata(18d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty GraphPaddingProperty = DependencyProperty.Register(
        nameof(GraphPadding), typeof(Thickness), typeof(SkillGraphPanel),
        new FrameworkPropertyMetadata(new Thickness(18), FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ConnectorBrushProperty = DependencyProperty.Register(
        nameof(ConnectorBrush), typeof(Brush), typeof(SkillGraphPanel),
        new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ActiveConnectorBrushProperty = DependencyProperty.Register(
        nameof(ActiveConnectorBrush), typeof(Brush), typeof(SkillGraphPanel),
        new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty OwnedConnectorBrushProperty = DependencyProperty.Register(
        nameof(OwnedConnectorBrush), typeof(Brush), typeof(SkillGraphPanel),
        new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PendingConnectorBrushProperty = DependencyProperty.Register(
        nameof(PendingConnectorBrush), typeof(Brush), typeof(SkillGraphPanel),
        new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ConnectorThicknessProperty = DependencyProperty.Register(
        nameof(ConnectorThickness), typeof(double), typeof(SkillGraphPanel),
        new FrameworkPropertyMetadata(2d, FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly Dictionary<UIElement, NodeLayout> _layouts = [];
    private Size _naturalSize;

    public SkillGraphPanel()
    {
        ClipToBounds = false;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
    }

    public double NodeWidth
    {
        get => (double)GetValue(NodeWidthProperty);
        set => SetValue(NodeWidthProperty, value);
    }

    public double NodeMinHeight
    {
        get => (double)GetValue(NodeMinHeightProperty);
        set => SetValue(NodeMinHeightProperty, value);
    }

    public double TierGap
    {
        get => (double)GetValue(TierGapProperty);
        set => SetValue(TierGapProperty, value);
    }

    public double RowGap
    {
        get => (double)GetValue(RowGapProperty);
        set => SetValue(RowGapProperty, value);
    }

    public Thickness GraphPadding
    {
        get => (Thickness)GetValue(GraphPaddingProperty);
        set => SetValue(GraphPaddingProperty, value);
    }

    public Brush ConnectorBrush
    {
        get => (Brush)GetValue(ConnectorBrushProperty);
        set => SetValue(ConnectorBrushProperty, value);
    }

    public Brush ActiveConnectorBrush
    {
        get => (Brush)GetValue(ActiveConnectorBrushProperty);
        set => SetValue(ActiveConnectorBrushProperty, value);
    }

    public Brush OwnedConnectorBrush
    {
        get => (Brush)GetValue(OwnedConnectorBrushProperty);
        set => SetValue(OwnedConnectorBrushProperty, value);
    }

    public Brush PendingConnectorBrush
    {
        get => (Brush)GetValue(PendingConnectorBrushProperty);
        set => SetValue(PendingConnectorBrushProperty, value);
    }

    public double ConnectorThickness
    {
        get => (double)GetValue(ConnectorThicknessProperty);
        set => SetValue(ConnectorThicknessProperty, value);
    }

    /// <summary>RenderHarness diagnostic: number of resolved prerequisite paths in the last render.</summary>
    internal int RenderedConnectorCount { get; private set; }

    /// <summary>RenderHarness diagnostic: arranged node rectangles keyed by stable id.</summary>
    internal IReadOnlyDictionary<string, Rect> ArrangedNodeBounds => _layouts.Values
        .Where(layout => layout.Node.Id.Length > 0)
        .ToDictionary(layout => layout.Node.Id, layout => layout.Bounds, StringComparer.Ordinal);

    protected override Size MeasureOverride(Size availableSize)
    {
        _layouts.Clear();
        var nodes = new List<(UIElement Child, NodeInfo Node, Size Size)>();
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(NodeWidth, double.PositiveInfinity));
            var measured = new Size(NodeWidth, Math.Max(NodeMinHeight, child.DesiredSize.Height));
            nodes.Add((child, NodeInfo.From(child), measured));
        }

        if (nodes.Count == 0)
        {
            _naturalSize = new Size(0, 0);
            return _naturalSize;
        }

        var columns = nodes
            .GroupBy(item => item.Node.Tier)
            .OrderBy(group => group.Key)
            .Select(group => group
                .OrderBy(item => item.Node.Order)
                .ThenBy(item => item.Node.Id, StringComparer.Ordinal)
                .ToArray())
            .ToArray();

        double contentWidth = columns.Sum(column => column.Max(item => item.Size.Width)) +
                              Math.Max(0, columns.Length - 1) * TierGap;
        double contentHeight = columns.Max(column =>
            column.Sum(item => item.Size.Height) + Math.Max(0, column.Length - 1) * RowGap);
        _naturalSize = new Size(
            contentWidth + GraphPadding.Left + GraphPadding.Right,
            contentHeight + GraphPadding.Top + GraphPadding.Bottom);
        return _naturalSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _layouts.Clear();
        var nodes = InternalChildren.Cast<UIElement>()
            .Select(child => new
            {
                Child = child,
                Node = NodeInfo.From(child),
                Size = new Size(NodeWidth, Math.Max(NodeMinHeight, child.DesiredSize.Height)),
            })
            .ToArray();
        if (nodes.Length == 0)
            return finalSize;

        var columns = nodes
            .GroupBy(item => item.Node.Tier)
            .OrderBy(group => group.Key)
            .Select(group => group
                .OrderBy(item => item.Node.Order)
                .ThenBy(item => item.Node.Id, StringComparer.Ordinal)
                .ToArray())
            .ToArray();

        double naturalContentWidth = columns.Sum(column => column.Max(item => item.Size.Width)) +
                                     Math.Max(0, columns.Length - 1) * TierGap;
        double contentWidth = Math.Max(naturalContentWidth, finalSize.Width - GraphPadding.Left - GraphPadding.Right);
        double contentHeight = Math.Max(
            _naturalSize.Height - GraphPadding.Top - GraphPadding.Bottom,
            finalSize.Height - GraphPadding.Top - GraphPadding.Bottom);
        double x = GraphPadding.Left + Math.Max(0, (contentWidth - naturalContentWidth) / 2);

        foreach (var column in columns)
        {
            double columnWidth = column.Max(item => item.Size.Width);
            double columnHeight = column.Sum(item => item.Size.Height) + Math.Max(0, column.Length - 1) * RowGap;
            double y = GraphPadding.Top + Math.Max(0, (contentHeight - columnHeight) / 2);
            foreach (var item in column)
            {
                var bounds = new Rect(x + (columnWidth - item.Size.Width) / 2, y, item.Size.Width, item.Size.Height);
                item.Child.Arrange(bounds);
                _layouts[item.Child] = new NodeLayout(item.Node, bounds);
                y += item.Size.Height + RowGap;
            }
            x += columnWidth + TierGap;
        }

        InvalidateVisual();
        return finalSize;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        RenderedConnectorCount = 0;
        if (_layouts.Count == 0)
            return;

        var byId = _layouts.Values
            .Where(layout => layout.Node.Id.Length > 0)
            .ToDictionary(layout => layout.Node.Id, StringComparer.Ordinal);

        foreach (NodeLayout target in _layouts.Values)
        {
            foreach (string requiredId in target.Node.RequiresIds)
            {
                if (!byId.TryGetValue(requiredId, out NodeLayout? source))
                    continue;
                DrawConnector(drawingContext, source.Bounds, target.Bounds, BrushFor(target.Node.State));
                RenderedConnectorCount++;
            }
        }
    }

    private void DrawConnector(DrawingContext context, Rect source, Rect target, Brush brush)
    {
        var start = new Point(source.Right, source.Top + source.Height / 2);
        var end = new Point(target.Left, target.Top + target.Height / 2);
        double bend = start.X + Math.Max(10, (end.X - start.X) / 2);
        var geometry = new StreamGeometry();
        using (StreamGeometryContext sink = geometry.Open())
        {
            sink.BeginFigure(start, isFilled: false, isClosed: false);
            sink.LineTo(new Point(bend, start.Y), isStroked: true, isSmoothJoin: true);
            sink.LineTo(new Point(bend, end.Y), isStroked: true, isSmoothJoin: true);
            sink.LineTo(end, isStroked: true, isSmoothJoin: true);
        }
        geometry.Freeze();
        var pen = new Pen(brush, ConnectorThickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        context.DrawGeometry(null, pen, geometry);
        context.DrawEllipse(brush, null, start, ConnectorThickness + 1.5, ConnectorThickness + 1.5);
        context.DrawEllipse(brush, null, end, ConnectorThickness + 1.5, ConnectorThickness + 1.5);
    }

    private Brush BrushFor(SkillNodeState? state) => state switch
    {
        SkillNodeState.Owned => OwnedConnectorBrush,
        SkillNodeState.Pending => PendingConnectorBrush,
        SkillNodeState.Unlockable or SkillNodeState.Mastery => ActiveConnectorBrush,
        _ => ConnectorBrush,
    };

    private sealed record NodeLayout(NodeInfo Node, Rect Bounds);

    private sealed record NodeInfo(
        string Id,
        int Tier,
        int Order,
        IReadOnlyList<string> RequiresIds,
        SkillNodeState? State)
    {
        public static NodeInfo From(UIElement child)
        {
            object? item = (child as FrameworkElement)?.DataContext;
            return item switch
            {
                SkillNodeViewModel node => new(
                    node.Id, node.Tier, node.Order, node.RequiresIds, node.State),
                MasteryPreviewSkill node => new(
                    node.Id, node.Tier, node.Order, node.RequiresIds, null),
                MasteryPreviewAttributeNode node => new(
                    node.Id, node.Tier, node.Order, node.RequiresIds, null),
                _ => new("", 0, 0, Array.Empty<string>(), null),
            };
        }
    }
}

/// <summary>A graph node with a separate double-click command for the v2 quick-queue seam.</summary>
public sealed class SkillGraphNodeButton : Button
{
    public static readonly DependencyProperty DoubleClickCommandProperty = DependencyProperty.Register(
        nameof(DoubleClickCommand), typeof(ICommand), typeof(SkillGraphNodeButton));

    public static readonly DependencyProperty DoubleClickCommandParameterProperty = DependencyProperty.Register(
        nameof(DoubleClickCommandParameter), typeof(object), typeof(SkillGraphNodeButton));

    public ICommand? DoubleClickCommand
    {
        get => (ICommand?)GetValue(DoubleClickCommandProperty);
        set => SetValue(DoubleClickCommandProperty, value);
    }

    public object? DoubleClickCommandParameter
    {
        get => GetValue(DoubleClickCommandParameterProperty);
        set => SetValue(DoubleClickCommandParameterProperty, value);
    }

    protected override void OnMouseDoubleClick(MouseButtonEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.ChangedButton != MouseButton.Left || DoubleClickCommand is null)
            return;
        if (DoubleClickCommand.CanExecute(DoubleClickCommandParameter))
            DoubleClickCommand.Execute(DoubleClickCommandParameter);
        e.Handled = true;
    }
}
