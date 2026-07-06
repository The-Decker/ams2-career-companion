using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Companion.App;

/// <summary>
/// Attached behaviours for tactile "game feel" that a ControlTemplate alone cannot express — a
/// Material-style click ripple that radiates from exactly where the pointer lands. Opt any control
/// in with <c>MotionAssist.Ripple="True"</c> (the base Button style opts every button in). It is
/// pure presentation: a transient <see cref="RippleAdorner"/> is added to the adorner layer, never
/// touching layout, focus, hit-testing or the click's command, and every step fails safe (no adorner
/// layer, or any error, is a silent no-op) so a ripple can never bring down a view.
/// </summary>
public static class MotionAssist
{
    public static readonly DependencyProperty RippleProperty =
        DependencyProperty.RegisterAttached(
            "Ripple", typeof(bool), typeof(MotionAssist),
            new PropertyMetadata(false, OnRippleChanged));

    public static bool GetRipple(DependencyObject d) => (bool)d.GetValue(RippleProperty);
    public static void SetRipple(DependencyObject d, bool value) => d.SetValue(RippleProperty, value);

    private static void OnRippleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
            return;
        // PreviewMouseLeftButtonDown is a bubbling/tunnelling handler we never mark handled, so the
        // control's own Click/command fires exactly as before — the ripple just rides along.
        if ((bool)e.NewValue)
            element.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        else
            element.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
    }

    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.ActualWidth <= 0 || element.ActualHeight <= 0)
            return;
        try
        {
            var layer = AdornerLayer.GetAdornerLayer(element);
            if (layer is null)
                return;
            layer.Add(new RippleAdorner(element, e.GetPosition(element)));
        }
        catch
        {
            // presentation-only — a failed ripple must never interrupt the click
        }
    }

    // ---------- entrance transition (screens fade + slide in on navigation) ----------

    /// <summary>Set on a <see cref="ContentControl"/> (the shell's screen host): every time its
    /// Content changes, the new screen fades up from slightly below — so moving between Start,
    /// wizard, hub and settings feels like arriving somewhere, not a hard cut. Fails safe.</summary>
    public static readonly DependencyProperty EntranceProperty =
        DependencyProperty.RegisterAttached(
            "Entrance", typeof(bool), typeof(MotionAssist),
            new PropertyMetadata(false, OnEntranceChanged));

    public static bool GetEntrance(DependencyObject d) => (bool)d.GetValue(EntranceProperty);
    public static void SetEntrance(DependencyObject d, bool value) => d.SetValue(EntranceProperty, value);

    private static readonly DependencyPropertyDescriptor ContentDescriptor =
        DependencyPropertyDescriptor.FromProperty(ContentControl.ContentProperty, typeof(ContentControl));

    private static void OnEntranceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ContentControl host)
            return;
        if ((bool)e.NewValue)
        {
            host.RenderTransform = new TranslateTransform();
            ContentDescriptor.AddValueChanged(host, OnContentChanged);
        }
        else
        {
            ContentDescriptor.RemoveValueChanged(host, OnContentChanged);
        }
    }

    private static void OnContentChanged(object? sender, EventArgs e)
    {
        if (sender is not ContentControl host)
            return;
        try
        {
            if (host.RenderTransform is not TranslateTransform slide)
                host.RenderTransform = slide = new TranslateTransform();
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            host.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
            slide.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(12.0, 0.0, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease });
        }
        catch
        {
            // presentation-only — never let a transition break navigation
        }
    }
}

/// <summary>A single expanding-and-fading circle from a click point, drawn in the adorner layer over
/// its element and clipped to the element's rounded box. Self-removes when the fade completes.</summary>
internal sealed class RippleAdorner : Adorner
{
    private readonly VisualCollection _children;
    private readonly Ellipse _ellipse;
    private readonly Point _origin;
    private readonly double _radius;

    public RippleAdorner(FrameworkElement adorned, Point origin) : base(adorned)
    {
        _origin = origin;
        // Initialise the child collection FIRST: setting IsHitTestVisible below force-inherits onto
        // children, which reads VisualChildrenCount — a null _children there is an NRE.
        _children = new VisualCollection(this);
        IsHitTestVisible = false;

        double w = adorned.ActualWidth, h = adorned.ActualHeight;
        // Radius = distance to the farthest corner, so the ripple always reaches every edge.
        _radius = Math.Max(
            Math.Max(Dist(origin, 0, 0), Dist(origin, w, 0)),
            Math.Max(Dist(origin, 0, h), Dist(origin, w, h)));

        // Keep the wash inside the control's rounded box (the button chrome is CornerRadius 5).
        Clip = new RectangleGeometry(new Rect(0, 0, w, h), 5, 5);

        var scale = new ScaleTransform(0, 0);
        _ellipse = new Ellipse
        {
            Width = _radius * 2,
            Height = _radius * 2,
            Fill = Brushes.White,
            Opacity = 0.0,
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = scale,
        };
        _children.Add(_ellipse);

        var dur = TimeSpan.FromMilliseconds(460);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var grow = new DoubleAnimation(0.0, 1.0, dur) { EasingFunction = ease };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        var fade = new DoubleAnimation(0.26, 0.0, dur) { EasingFunction = ease };
        fade.Completed += (_, _) =>
        {
            try { AdornerLayer.GetAdornerLayer(AdornedElement)?.Remove(this); }
            catch { /* already gone */ }
        };
        _ellipse.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private static double Dist(Point p, double x, double y) =>
        Math.Sqrt((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y));

    protected override int VisualChildrenCount => _children.Count;
    protected override Visual GetVisualChild(int index) => _children[index];

    protected override Size MeasureOverride(Size constraint)
    {
        _ellipse.Measure(constraint);
        return AdornedElement.RenderSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double d = _radius * 2;
        _ellipse.Arrange(new Rect(_origin.X - _radius, _origin.Y - _radius, d, d));
        return finalSize;
    }
}
