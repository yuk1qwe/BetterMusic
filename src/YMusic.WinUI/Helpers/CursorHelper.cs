#nullable enable

using System.Reflection;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace YMusic.WinUI.Helpers;

public static class CursorHelper
{
    private static InputCursor? _handCursor;
    public static InputCursor HandCursor => _handCursor ??= InputSystemCursor.Create(InputSystemCursorShape.Hand);

    public static readonly DependencyProperty UseHandCursorProperty =
        DependencyProperty.RegisterAttached(
            "UseHandCursor",
            typeof(bool),
            typeof(CursorHelper),
            new PropertyMetadata(false, OnUseHandCursorChanged));

    public static bool GetUseHandCursor(DependencyObject obj) => (bool)obj.GetValue(UseHandCursorProperty);
    public static void SetUseHandCursor(DependencyObject obj, bool value) => obj.SetValue(UseHandCursorProperty, value);

    private static readonly PropertyInfo? ProtectedCursorPropertyInfo =
        typeof(UIElement).GetProperty("ProtectedCursor", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    private static void OnUseHandCursorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        bool useHand = e.NewValue is true;

        SetCursor(element, useHand);

        if (element is FrameworkElement fe)
        {
            if (useHand)
            {
                fe.Loaded -= OnElementLoaded;
                fe.Loaded += OnElementLoaded;
                if (fe.IsLoaded)
                {
                    ApplyCursorDeep(fe);
                }
            }
            else
            {
                fe.Loaded -= OnElementLoaded;
            }
        }
    }

    private static void OnElementLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && GetUseHandCursor(fe))
        {
            ApplyCursorDeep(fe);
        }
    }

    public static void SetCursor(UIElement element, InputCursor? cursor)
    {
        try
        {
            ProtectedCursorPropertyInfo?.SetValue(element, cursor);
        }
        catch
        {
            // Fallback if platform cursor API not supported
        }
    }

    private static void SetCursor(UIElement element, bool useHand)
    {
        SetCursor(element, useHand ? HandCursor : null);
    }

    private static void ApplyCursorDeep(DependencyObject parent)
    {
        if (parent is UIElement ui)
        {
            SetCursor(ui, true);
        }

        if (parent is ContentControl cc && cc.Content is UIElement contentUI)
        {
            SetCursor(contentUI, true);
            ApplyCursorDeep(contentUI);
        }

        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            ApplyCursorDeep(child);
        }
    }
}
