using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Companion.App;

/// <summary>Presentation-only behavior for the app's skinned tool-window title bars.</summary>
public static class WindowChromeAssist
{
    public static readonly DependencyProperty IsDragRegionProperty = DependencyProperty.RegisterAttached(
        "IsDragRegion", typeof(bool), typeof(WindowChromeAssist),
        new PropertyMetadata(false, OnIsDragRegionChanged));

    public static readonly DependencyProperty IsCloseButtonProperty = DependencyProperty.RegisterAttached(
        "IsCloseButton", typeof(bool), typeof(WindowChromeAssist),
        new PropertyMetadata(false, OnIsCloseButtonChanged));

    public static void SetIsDragRegion(DependencyObject element, bool value) =>
        element.SetValue(IsDragRegionProperty, value);

    public static bool GetIsDragRegion(DependencyObject element) =>
        (bool)element.GetValue(IsDragRegionProperty);

    public static void SetIsCloseButton(DependencyObject element, bool value) =>
        element.SetValue(IsCloseButtonProperty, value);

    public static bool GetIsCloseButton(DependencyObject element) =>
        (bool)element.GetValue(IsCloseButtonProperty);

    private static void OnIsDragRegionChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not UIElement element)
            return;

        element.MouseLeftButtonDown -= OnDragRegionMouseLeftButtonDown;
        if (e.NewValue is true)
            element.MouseLeftButtonDown += OnDragRegionMouseLeftButtonDown;
    }

    private static void OnIsCloseButtonChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not Button button)
            return;

        button.Click -= OnCloseButtonClick;
        if (e.NewValue is true)
            button.Click += OnCloseButtonClick;
    }

    private static void OnDragRegionMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DependencyObject source || Window.GetWindow(source) is not { } window)
            return;

        if (e.ClickCount == 2 && window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip)
        {
            window.WindowState = window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            e.Handled = true;
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            window.DragMove();
            e.Handled = true;
        }
    }

    private static void OnCloseButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is DependencyObject source)
            Window.GetWindow(source)?.Close();
    }

}
