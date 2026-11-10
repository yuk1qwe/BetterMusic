#nullable enable

using System.Windows.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using YMusic.WinUI.Helpers;

namespace YMusic.WinUI.Views.Controls;

public sealed partial class MarqueeTextControl : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(MarqueeTextControl),
            new PropertyMetadata(string.Empty, OnTextChanged));

    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(
            nameof(Command),
            typeof(ICommand),
            typeof(MarqueeTextControl),
            new PropertyMetadata(null, OnCommandChanged));

    public static readonly DependencyProperty CommandParameterProperty =
        DependencyProperty.Register(
            nameof(CommandParameter),
            typeof(object),
            typeof(MarqueeTextControl),
            new PropertyMetadata(null));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    private Storyboard? _storyboard;
    private bool _isPointerOver;

    public MarqueeTextControl()
    {
        InitializeComponent();
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarqueeTextControl ctrl)
        {
            ctrl.DispatcherQueue?.TryEnqueue(ctrl.UpdateMarquee);
        }
    }

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarqueeTextControl ctrl)
        {
            CursorHelper.SetUseHandCursor(ctrl.RootGrid, e.NewValue != null);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateMarquee();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        StopMarqueeAnimation();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateMarquee();
    }

    private void OnRootPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (Command != null && Command.CanExecute(CommandParameter))
        {
            Command.Execute(CommandParameter);
            e.Handled = true;
        }
    }

    private void OnRootPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _isPointerOver = true;
        if (Command != null)
        {
            DisplayBlock.Opacity = 0.8;
        }

        if (_storyboard != null && _storyboard.GetCurrentState() == ClockState.Active)
        {
            _storyboard.Pause();
        }
    }

    private void OnRootPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _isPointerOver = false;
        DisplayBlock.Opacity = 1.0;

        if (_storyboard != null)
        {
            _storyboard.Resume();
        }
    }

    public void UpdateMarquee()
    {
        if (!IsLoaded) return;

        double containerWidth = RootGrid.ActualWidth;
        double containerHeight = RootGrid.ActualHeight;

        if (containerWidth <= 0 || containerHeight <= 0) return;

        RootGrid.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, containerWidth, containerHeight)
        };

        if (string.IsNullOrEmpty(Text))
        {
            StopMarqueeAnimation();
            TextTransform.X = 0;
            return;
        }

        DisplayBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double contentWidth = DisplayBlock.DesiredSize.Width;

        if (contentWidth > containerWidth + 4)
        {
            double overflow = (contentWidth - containerWidth) + 14;
            StartMarqueeAnimation(overflow);
        }
        else
        {
            StopMarqueeAnimation();
            TextTransform.X = 0;
        }
    }

    private void StartMarqueeAnimation(double overflow)
    {
        StopMarqueeAnimation();

        if (overflow <= 0) return;

        // Comfortable reading speed: ~32 pixels/second
        double scrollDurationSeconds = Math.Max(1.4, overflow / 32.0);

        var storyboard = new Storyboard
        {
            RepeatBehavior = RepeatBehavior.Forever
        };

        var animation = new DoubleAnimationUsingKeyFrames();
        Storyboard.SetTarget(animation, TextTransform);
        Storyboard.SetTargetProperty(animation, "X");

        double t0 = 0.0;
        double t1 = 1.8;
        double t2 = t1 + scrollDurationSeconds;
        double t3 = t2 + 1.8;
        double t4 = t3 + scrollDurationSeconds;
        double tTotal = t4 + 0.8;

        var cubicEase = new CubicEase { EasingMode = EasingMode.EaseInOut };

        // 1. Initial position
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t0)),
            Value = 0
        });

        // 2. Hold at start for 1.8s
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t1)),
            Value = 0
        });

        // 3. Scroll forward to end
        animation.KeyFrames.Add(new EasingDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t2)),
            Value = -overflow,
            EasingFunction = cubicEase
        });

        // 4. Hold at end for 1.8s
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t3)),
            Value = -overflow
        });

        // 5. Scroll backward to start
        animation.KeyFrames.Add(new EasingDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t4)),
            Value = 0,
            EasingFunction = cubicEase
        });

        // 6. Hold at start before loop
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(tTotal)),
            Value = 0
        });

        storyboard.Children.Add(animation);
        _storyboard = storyboard;
        storyboard.Begin();

        if (_isPointerOver)
        {
            storyboard.Pause();
        }
    }

    private void StopMarqueeAnimation()
    {
        if (_storyboard != null)
        {
            _storyboard.Stop();
            _storyboard = null;
        }
        TextTransform.X = 0;
    }
}
