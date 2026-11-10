#nullable enable

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace YMusic.WinUI.Views.Controls;

public sealed partial class MiniEqualizerControl : UserControl
{
    public static readonly DependencyProperty IsPlayingProperty =
        DependencyProperty.Register(
            nameof(IsPlaying),
            typeof(bool),
            typeof(MiniEqualizerControl),
            new PropertyMetadata(false, OnIsPlayingChanged));

    public bool IsPlaying
    {
        get => (bool)GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    public MiniEqualizerControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateAnimationState();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        try
        {
            BarsStoryboard?.Stop();
        }
        catch
        {
        }
    }

    private static void OnIsPlayingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MiniEqualizerControl control)
        {
            control.UpdateAnimationState();
        }
    }

    private void UpdateAnimationState()
    {
        if (BarsStoryboard == null) return;

        if (IsPlaying)
        {
            try
            {
                BarsStoryboard.Begin();
            }
            catch
            {
            }
        }
        else
        {
            try
            {
                BarsStoryboard.Stop();
            }
            catch
            {
            }

            ResetRestingState();
        }
    }

    private void ResetRestingState()
    {
        if (Bar1Scale != null) Bar1Scale.ScaleY = 0.30;
        if (Bar2Scale != null) Bar2Scale.ScaleY = 0.75;
        if (Bar3Scale != null) Bar3Scale.ScaleY = 0.45;
        if (Bar4Scale != null) Bar4Scale.ScaleY = 0.20;
    }
}
