using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Companion.App;

namespace Companion.RenderHarness.Tests;

/// <summary>Off-screen render of the tactile motion layer (the "game feel" pass): the enhanced
/// Button / CheckBox / Slider templates carry RenderTransform springs + a click ripple. A broken
/// animated template throws at render, and a bad adorner throws when it's added — both surface here.
/// Self-skips off Windows.</summary>
public sealed class MotionRenderTests
{
    private static (AdornerDecorator Root, Button Button) BuildControls()
    {
        var button = new Button { Content = "Go", Width = 140, Height = 34, Margin = new Thickness(8) };
        var accent = new Button { Content = "Accent", Width = 140, Height = 34 };
        accent.SetResourceReference(FrameworkElement.StyleProperty, "AccentButton");
        var check = new CheckBox { Content = "Wet race", Margin = new Thickness(8) };
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = 40, Width = 220, Margin = new Thickness(8) };

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(button);
        panel.Children.Add(accent);
        panel.Children.Add(check);
        panel.Children.Add(slider);

        // An AdornerDecorator gives the tree a real AdornerLayer (as a Window would) so the ripple
        // adorner has somewhere to attach.
        var root = new AdornerDecorator { Child = panel };
        root.Measure(new Size(500, 400));
        root.Arrange(new Rect(0, 0, 500, 400));
        root.UpdateLayout();
        return (root, button);
    }

    [Fact]
    public void EnhancedControls_RenderWithMotionTemplates()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var (root, button) = BuildControls();

            // Toggling the checkbox fires the pop storyboard; setting the slider exercises the thumb.
            var check = (CheckBox)((StackPanel)root.Child).Children[2];
            check.IsChecked = true;
            WpfRenderHarness.Pump();

            Assert.True(button.ActualWidth > 0);
            Assert.True(root.ActualHeight > 0);
        });
    }

    [Fact]
    public void Entrance_AnimatesContentChange_WithoutCrashing()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var host = new ContentControl { RenderTransformOrigin = new Point(0.5, 0.5) };
            MotionAssist.SetEntrance(host, true);
            var deco = new AdornerDecorator { Child = host };
            deco.Measure(new Size(600, 400));
            deco.Arrange(new Rect(0, 0, 600, 400));

            // Two navigations — each should fire the fade+slide entrance without throwing.
            host.Content = new TextBlock { Text = "Screen one" };
            deco.UpdateLayout();
            WpfRenderHarness.Pump();
            host.Content = new Button { Content = "Screen two" };
            deco.UpdateLayout();
            WpfRenderHarness.Pump();

            Assert.IsType<TranslateTransform>(host.RenderTransform);
        });
    }

    [Fact]
    public void RippleAdorner_AttachesAndRendersWithoutCrashing()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var (_, button) = BuildControls();
            var layer = AdornerLayer.GetAdornerLayer(button);
            Assert.NotNull(layer);

            // A ripple from an off-centre click point — the same object MotionAssist spawns.
            var ripple = new RippleAdorner(button, new Point(30, 12));
            layer!.Add(ripple);
            WpfRenderHarness.Pump();

            // It is present in the layer (it self-removes only after the ~460ms fade completes).
            var adorners = layer.GetAdorners(button);
            Assert.NotNull(adorners);
            Assert.Contains(ripple, adorners!);
        });
    }
}
