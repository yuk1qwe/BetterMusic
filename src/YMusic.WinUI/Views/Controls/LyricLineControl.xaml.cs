#nullable enable

using System;
using System.ComponentModel;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using YMusic.WinUI.Models;

namespace YMusic.WinUI.Views.Controls;

public sealed partial class LyricLineControl : UserControl
{
    private LyricLine? _line;
    private Storyboard? _currentStoryboard;
    private bool _isPointerOver;

    public static readonly DependencyProperty LineProperty =
        DependencyProperty.Register(
            nameof(Line),
            typeof(LyricLine),
            typeof(LyricLineControl),
            new PropertyMetadata(null, OnLinePropertyChanged));

    public LyricLine? Line
    {
        get => (LyricLine?)GetValue(LineProperty);
        set => SetValue(LineProperty, value);
    }

    private static void OnLinePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is LyricLineControl control)
        {
            control.BindToLine(e.NewValue as LyricLine);
        }
    }

    public LyricLineControl()
    {
        InitializeComponent();
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        BindToLine(Line ?? DataContext as LyricLine);
    }

    private void UserControl_Unloaded(object sender, RoutedEventArgs e)
    {
        UnbindFromLine();
    }

    private void UserControl_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (Line == null)
        {
            BindToLine(args.NewValue as LyricLine);
        }
    }

    private void BindToLine(LyricLine? line)
    {
        if (_line == line && _line != null)
        {
            UpdateVisualState(animate: false);
            return;
        }

        UnbindFromLine();

        _line = line;
        if (_line != null)
        {
            _line.PropertyChanged += Line_PropertyChanged;
            LineTextBlock.Text = _line.Text;
            TimestampTextBlock.Text = _line.FormattedTimestamp;
            TimestampTextBlock.Visibility = _line.HasTimestamp ? Visibility.Visible : Visibility.Collapsed;
            UpdateVisualState(animate: false);
        }
        else
        {
            LineTextBlock.Text = string.Empty;
            TimestampTextBlock.Text = string.Empty;
            TimestampTextBlock.Visibility = Visibility.Collapsed;
        }
    }

    private void UnbindFromLine()
    {
        if (_line != null)
        {
            _line.PropertyChanged -= Line_PropertyChanged;
            _line = null;
        }
        _currentStoryboard?.Stop();
        _currentStoryboard = null;
    }

    private void Line_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LyricLine.IsActive) ||
            e.PropertyName == nameof(LyricLine.IsPast))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                UpdateVisualState(animate: true);
            });
        }
        else if (e.PropertyName == nameof(LyricLine.Text))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                LineTextBlock.Text = _line?.Text ?? string.Empty;
            });
        }
        else if (e.PropertyName == nameof(LyricLine.FormattedTimestamp))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                TimestampTextBlock.Text = _line?.FormattedTimestamp ?? string.Empty;
                TimestampTextBlock.Visibility = _line?.HasTimestamp == true ? Visibility.Visible : Visibility.Collapsed;
            });
        }
    }

    private void UserControl_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _isPointerOver = true;
        if (_line != null && !_line.IsActive)
        {
            UpdateVisualState(animate: true);
        }
    }

    private void UserControl_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _isPointerOver = false;
        if (_line != null && !_line.IsActive)
        {
            UpdateVisualState(animate: true);
        }
    }

    private void UpdateVisualState(bool animate)
    {
        if (_line == null) return;

        if (!_line.HasTimestamp)
        {
            // Plain text lyrics: crisp and readable, subtle hover, no past/active dimming
            double plainScale = _isPointerOver ? 0.98 : 0.96;
            double plainTextOpacity = _isPointerOver ? 1.0 : 0.88;
            double plainPillOpacity = _isPointerOver ? 0.20 : 0.0;

            LineTextBlock.FontWeight = FontWeights.SemiBold;
            TimestampTextBlock.Visibility = Visibility.Collapsed;

            if (!animate)
            {
                _currentStoryboard?.Stop();
                _currentStoryboard = null;

                LineScale.ScaleX = plainScale;
                LineScale.ScaleY = plainScale;
                LineTranslate.X = 0;
                LineTextBlock.Opacity = plainTextOpacity;
                ActivePillBorder.Opacity = plainPillOpacity;
                return;
            }

            _currentStoryboard?.Stop();
            var sbPlain = new Storyboard();
            var durPlain = TimeSpan.FromMilliseconds(250);
            var easePlain = new CubicEase { EasingMode = EasingMode.EaseOut };

            var aScaleX = new DoubleAnimation { To = plainScale, Duration = durPlain, EasingFunction = easePlain };
            Storyboard.SetTarget(aScaleX, LineScale);
            Storyboard.SetTargetProperty(aScaleX, "ScaleX");
            sbPlain.Children.Add(aScaleX);

            var aScaleY = new DoubleAnimation { To = plainScale, Duration = durPlain, EasingFunction = easePlain };
            Storyboard.SetTarget(aScaleY, LineScale);
            Storyboard.SetTargetProperty(aScaleY, "ScaleY");
            sbPlain.Children.Add(aScaleY);

            var aTextOp = new DoubleAnimation { To = plainTextOpacity, Duration = durPlain, EasingFunction = easePlain };
            Storyboard.SetTarget(aTextOp, LineTextBlock);
            Storyboard.SetTargetProperty(aTextOp, "Opacity");
            sbPlain.Children.Add(aTextOp);

            var aPillOp = new DoubleAnimation { To = plainPillOpacity, Duration = durPlain, EasingFunction = easePlain };
            Storyboard.SetTarget(aPillOp, ActivePillBorder);
            Storyboard.SetTargetProperty(aPillOp, "Opacity");
            sbPlain.Children.Add(aPillOp);

            _currentStoryboard = sbPlain;
            sbPlain.Begin();
            return;
        }

        bool isActive = _line.IsActive;
        bool isPast = _line.IsPast;

        double targetScale = isActive ? 1.0 : (_isPointerOver ? 0.98 : 0.96);
        double targetTranslateX = 0.0;
        double targetTextOpacity = isActive ? 1.0 : (_isPointerOver ? 0.80 : (isPast ? 0.32 : 0.55));
        double targetPillOpacity = isActive ? 1.0 : (_isPointerOver ? 0.35 : 0.0);
        double targetTimestampOpacity = isActive ? 0.85 : (_isPointerOver ? 0.60 : 0.30);

        LineTextBlock.FontWeight = isActive ? FontWeights.Bold : FontWeights.SemiBold;
        TimestampTextBlock.Visibility = Visibility.Visible;

        if (!animate)
        {
            _currentStoryboard?.Stop();
            _currentStoryboard = null;

            LineScale.ScaleX = targetScale;
            LineScale.ScaleY = targetScale;
            LineTranslate.X = targetTranslateX;
            LineTextBlock.Opacity = targetTextOpacity;
            ActivePillBorder.Opacity = targetPillOpacity;
            TimestampTextBlock.Opacity = targetTimestampOpacity;
            return;
        }

        _currentStoryboard?.Stop();

        var sb = new Storyboard();
        var duration = TimeSpan.FromMilliseconds(350);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        // ScaleX
        var animScaleX = new DoubleAnimation
        {
            To = targetScale,
            Duration = duration,
            EasingFunction = ease
        };
        Storyboard.SetTarget(animScaleX, LineScale);
        Storyboard.SetTargetProperty(animScaleX, "ScaleX");
        sb.Children.Add(animScaleX);

        // ScaleY
        var animScaleY = new DoubleAnimation
        {
            To = targetScale,
            Duration = duration,
            EasingFunction = ease
        };
        Storyboard.SetTarget(animScaleY, LineScale);
        Storyboard.SetTargetProperty(animScaleY, "ScaleY");
        sb.Children.Add(animScaleY);

        // TranslateX
        var animTranslateX = new DoubleAnimation
        {
            To = targetTranslateX,
            Duration = duration,
            EasingFunction = ease
        };
        Storyboard.SetTarget(animTranslateX, LineTranslate);
        Storyboard.SetTargetProperty(animTranslateX, "X");
        sb.Children.Add(animTranslateX);

        // Text Opacity
        var animTextOpacity = new DoubleAnimation
        {
            To = targetTextOpacity,
            Duration = duration,
            EasingFunction = ease
        };
        Storyboard.SetTarget(animTextOpacity, LineTextBlock);
        Storyboard.SetTargetProperty(animTextOpacity, "Opacity");
        sb.Children.Add(animTextOpacity);

        // Active Pill Opacity
        var animPillOpacity = new DoubleAnimation
        {
            To = targetPillOpacity,
            Duration = duration,
            EasingFunction = ease
        };
        Storyboard.SetTarget(animPillOpacity, ActivePillBorder);
        Storyboard.SetTargetProperty(animPillOpacity, "Opacity");
        sb.Children.Add(animPillOpacity);

        // Timestamp Opacity
        var animTimestampOpacity = new DoubleAnimation
        {
            To = targetTimestampOpacity,
            Duration = duration,
            EasingFunction = ease
        };
        Storyboard.SetTarget(animTimestampOpacity, TimestampTextBlock);
        Storyboard.SetTargetProperty(animTimestampOpacity, "Opacity");
        sb.Children.Add(animTimestampOpacity);

        _currentStoryboard = sb;
        sb.Begin();
    }
}
