#nullable enable

using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;
using YMusic.WinUI.ViewModels;

namespace YMusic.WinUI.Views.Controls;

public sealed partial class NowPlayingDrawerControl : UserControl
{
    public PlayerBarViewModel ViewModel => App.GetService<PlayerBarViewModel>();
    public UIElement TitleBarDragRegion => HeaderBarGrid;

    private bool FullEffects => App.GetService<ISettingsService>().FullVisualEffects;
    private Storyboard? _currentTransition;
    private bool _isProgrammaticScrolling;
    private DispatcherQueueTimer? _programmaticScrollResetTimer;
    private DispatcherQueueTimer? _smoothCenterTimer;

    public NowPlayingDrawerControl()
    {
        InitializeComponent();
        AmbientLayer.Visibility = FullEffects ? Visibility.Visible : Visibility.Collapsed;
        App.GetService<ISettingsService>().SettingChanged += (_, name) =>
        {
            if (name == nameof(ISettingsService.FullVisualEffects))
                DispatcherQueue.TryEnqueue(() =>
                {
                    AmbientLayer.Visibility = FullEffects ? Visibility.Visible : Visibility.Collapsed;
                    AmbientGlowCurrent.Source = FullEffects && ViewModel.IsNowPlayingDrawerOpen ? CoverImageCurrent.Source : null;
                    if (!FullEffects) { _crossfadeStoryboard?.Stop(); AmbientGlowNext.Source = null; }
                });
        };

        if (ViewModel.IsNowPlayingDrawerOpen)
        {
            Visibility = Visibility.Visible;
            IsHitTestVisible = true;
            RootDrawerGrid.Opacity = 1.0;
            DrawerTranslateTransform.Y = 0.0;
            CoverScaleTransform.ScaleX = 1.0;
            CoverScaleTransform.ScaleY = 1.0;
        }
        else
        {
            Visibility = Visibility.Collapsed;
            IsHitTestVisible = false;
            RootDrawerGrid.Opacity = 0.0;
            DrawerTranslateTransform.Y = 50.0;
            CoverScaleTransform.ScaleX = 0.94;
            CoverScaleTransform.ScaleY = 0.94;
        }

        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(PlayerBarViewModel.CurrentTrack))
            {
                DispatcherQueue.TryEnqueue(AnimateTrackChange);
            }
        };

        SeekSlider.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(SeekSlider_PointerPressed), true);
        SeekSlider.AddHandler(UIElement.PointerReleasedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(SeekSlider_PointerReleased), true);
        SeekSlider.AddHandler(UIElement.PointerCaptureLostEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(SeekSlider_PointerCaptureLost), true);

        DrawerVolumeContainer.AddHandler(UIElement.PointerWheelChangedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(DrawerVolumeContainer_PointerWheelChanged), true);
    }

    private void DrawerVolumeContainer_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(null).Properties.MouseWheelDelta;
        if (delta != 0)
        {
            double step = delta > 0 ? 2.0 : -2.0;
            ViewModel.ChangeVolume(step);
            e.Handled = true;
        }
    }

    private void DrawerVolumePercentageBadge_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (DrawerVolumePercentageBox != null && DrawerVolumePercentageBadge != null)
        {
            DrawerVolumePercentageBadge.Visibility = Visibility.Collapsed;
            DrawerVolumePercentageBox.Visibility = Visibility.Visible;
            DrawerVolumePercentageBox.Text = Math.Round(ViewModel.Volume).ToString();
            DrawerVolumePercentageBox.Focus(FocusState.Programmatic);
            DrawerVolumePercentageBox.SelectAll();
            e.Handled = true;
        }
    }

    private void DrawerVolumePercentageBox_LostFocus(object sender, RoutedEventArgs e)
    {
        CommitDrawerVolumeText();
    }

    private void DrawerVolumePercentageBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            CommitDrawerVolumeText();
            DrawerVolumeSlider.Focus(FocusState.Programmatic);
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Escape)
        {
            if (DrawerVolumePercentageBox != null && DrawerVolumePercentageBadge != null)
            {
                DrawerVolumePercentageBox.Visibility = Visibility.Collapsed;
                DrawerVolumePercentageBadge.Visibility = Visibility.Visible;
            }
            DrawerVolumeSlider.Focus(FocusState.Programmatic);
            e.Handled = true;
        }
    }

    private void CommitDrawerVolumeText()
    {
        if (DrawerVolumePercentageBox == null || DrawerVolumePercentageBadge == null) return;
        ViewModel.SetVolumeFromInput(DrawerVolumePercentageBox.Text);
        DrawerVolumePercentageBox.Visibility = Visibility.Collapsed;
        DrawerVolumePercentageBadge.Visibility = Visibility.Visible;
    }

    private void SeekSlider_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        ViewModel.StartSeeking();
    }

    private void SeekSlider_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is Slider slider)
        {
            ViewModel.FinishSeeking(slider.Value);
        }
    }

    private void SeekSlider_PointerCaptureLost(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is Slider slider)
        {
            ViewModel.FinishSeeking(slider.Value);
        }
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.ActiveLyricChanged += ViewModel_ActiveLyricChanged;
        LyricsListView.VerticalOffsetChanged += LyricsListView_VerticalOffsetChanged;

        _programmaticScrollResetTimer = DispatcherQueue.CreateTimer();
        _programmaticScrollResetTimer.Interval = TimeSpan.FromMilliseconds(450);
        _programmaticScrollResetTimer.Tick += (s, args) =>
        {
            _programmaticScrollResetTimer.Stop();
            _isProgrammaticScrolling = false;
        };

        _smoothCenterTimer = DispatcherQueue.CreateTimer();
        _smoothCenterTimer.Interval = TimeSpan.FromMilliseconds(80);
        _smoothCenterTimer.Tick += (s, args) =>
        {
            _smoothCenterTimer.Stop();
            if (ViewModel.ActiveLyricLine != null && !ViewModel.IsUserScrolledAway)
            {
                CenterLyricContainer(ViewModel.ActiveLyricLine, false);
            }
        };

        if (ViewModel.ActiveLyricLine != null)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ScrollLyricIntoCenter(ViewModel.ActiveLyricLine);
            });
        }

        UpdateFullscreenIcon();
        UpdateTitleBarInsets();
    }

    private void UserControl_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.ActiveLyricChanged -= ViewModel_ActiveLyricChanged;
        LyricsListView.VerticalOffsetChanged -= LyricsListView_VerticalOffsetChanged;
        _programmaticScrollResetTimer?.Stop();
        _smoothCenterTimer?.Stop();
    }

    public void UpdateTitleBarInsets()
    {
        try
        {
            if (App.MainWindow?.AppWindow != null)
            {
                if (App.MainWindow.IsFullScreen)
                {
                    HeaderBarGrid.Margin = new Thickness(0, 0, 0, 0);
                    return;
                }

                if (Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported() &&
                    App.MainWindow.AppWindow.TitleBar != null)
                {
                    var titleBar = App.MainWindow.AppWindow.TitleBar;
                    double scale = XamlRoot?.RasterizationScale ?? 1.0;
                    if (scale > 0 && titleBar.RightInset > 0)
                    {
                        double rightMargin = titleBar.RightInset / scale;
                        HeaderBarGrid.Margin = new Thickness(0, 0, rightMargin, 0);
                        return;
                    }
                }
            }
        }
        catch { }

        HeaderBarGrid.Margin = new Thickness(0, 0, 140, 0);
    }

    private void ViewModel_ActiveLyricChanged(object? sender, LyricLine? line)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (line != null && !ViewModel.IsUserScrolledAway && ViewModel.IsAutoScrollEnabled && ViewModel.IsNowPlayingDrawerOpen)
            {
                ScrollLyricIntoCenter(line);
            }
        });
    }

    private void LyricsListView_VerticalOffsetChanged(object? sender, double offset)
    {
        if (_isProgrammaticScrolling)
        {
            return;
        }

        if (ViewModel.ActiveLyricLine != null && !ViewModel.IsUserScrolledAway)
        {
            ViewModel.IsUserScrolledAway = true;
        }
    }

    private void ScrollLyricIntoCenter(LyricLine line)
    {
        if (LyricsListView == null || line == null)
        {
            return;
        }

        _isProgrammaticScrolling = true;
        _programmaticScrollResetTimer?.Stop();
        _programmaticScrollResetTimer?.Start();

        bool centered = CenterLyricContainer(line, false);
        if (!centered)
        {
            LyricsListView.ScrollIntoView(line);
            _smoothCenterTimer?.Stop();
            _smoothCenterTimer?.Start();
        }
    }

    private bool CenterLyricContainer(LyricLine line, bool disableAnimation)
    {
        try
        {
            var sv = LyricsListView.InnerScrollViewer;
            if (sv == null)
            {
                return false;
            }

            var container = LyricsListView.ContainerFromItem(line) as ListViewItem;
            if (container == null)
            {
                return false;
            }

            var transform = container.TransformToVisual(sv);
            var pt = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

            double targetOffset = sv.VerticalOffset + pt.Y - (sv.ViewportHeight / 2.0) + (container.ActualHeight / 2.0);
            targetOffset = Math.Max(0, Math.Min(targetOffset, sv.ScrollableHeight));

            sv.ChangeView(null, targetOffset, null, disableAnimation);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void LyricsListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is LyricLine line && line.HasTimestamp && line.Timestamp >= TimeSpan.Zero)
        {
            ViewModel.SeekToLyricCommand.Execute(line);
            ScrollLyricIntoCenter(line);
        }
    }

    private void FullscreenButton_Click(object sender, RoutedEventArgs e)
    {
        App.MainWindow?.ToggleFullScreen();
        UpdateFullscreenIcon();
        UpdateTitleBarInsets();
    }

    private void UpdateFullscreenIcon()
    {
        if (FullscreenIcon == null) return;
        bool isFull = App.MainWindow?.IsFullScreen == true;
        FullscreenIcon.Glyph = isFull ? "\uE1D9" : "\uE740"; // Exit vs Enter Fullscreen
    }

    public void AnimateOpen()
    {
        _currentTransition?.Stop();
        _currentTransition = null;

        UpdateTitleBarInsets();

        Visibility = Visibility.Visible;
        IsHitTestVisible = true;

        UpdateFullscreenIcon();
        string? hdUrl = ViewModel.HdCoverUrl;
        if (!string.IsNullOrEmpty(hdUrl) && (CoverImageCurrent.Source == null || !string.Equals(_currentDisplayedCoverUrl, hdUrl, StringComparison.OrdinalIgnoreCase)))
        {
            _currentDisplayedCoverUrl = hdUrl;
            var bmp = new BitmapImage(new Uri(hdUrl));
            CoverImageCurrent.Source = bmp;
            AmbientGlowCurrent.Source = FullEffects ? bmp : null;
        }

        if (ViewModel.ActiveLyricLine != null)
        {
            ScrollLyricIntoCenter(ViewModel.ActiveLyricLine);
        }

        if (!FullEffects)
        {
            RootDrawerGrid.Opacity = 1;
            DrawerTranslateTransform.Y = 0;
            CoverScaleTransform.ScaleX = CoverScaleTransform.ScaleY = 1;
            return;
        }

        var sb = new Storyboard();

        // 1. Root Slide Up (50 -> 0)
        var slideAnim = new DoubleAnimation
        {
            From = DrawerTranslateTransform.Y != 0 ? DrawerTranslateTransform.Y : 50.0,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(slideAnim, DrawerTranslateTransform);
        Storyboard.SetTargetProperty(slideAnim, "Y");
        sb.Children.Add(slideAnim);

        // 2. Root Fade In (0 -> 1)
        var fadeAnim = new DoubleAnimation
        {
            From = RootDrawerGrid.Opacity < 1.0 ? RootDrawerGrid.Opacity : 0.0,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(260),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(fadeAnim, RootDrawerGrid);
        Storyboard.SetTargetProperty(fadeAnim, "Opacity");
        sb.Children.Add(fadeAnim);

        // 3. Cover Scale In (0.94 -> 1.0)
        var scaleXAnim = new DoubleAnimation
        {
            From = CoverScaleTransform.ScaleX != 1.0 ? CoverScaleTransform.ScaleX : 0.94,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(320),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(scaleXAnim, CoverScaleTransform);
        Storyboard.SetTargetProperty(scaleXAnim, "ScaleX");
        sb.Children.Add(scaleXAnim);

        var scaleYAnim = new DoubleAnimation
        {
            From = CoverScaleTransform.ScaleY != 1.0 ? CoverScaleTransform.ScaleY : 0.94,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(320),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(scaleYAnim, CoverScaleTransform);
        Storyboard.SetTargetProperty(scaleYAnim, "ScaleY");
        sb.Children.Add(scaleYAnim);

        sb.Completed += (s, e) =>
        {
            _currentTransition = null;
            RootDrawerGrid.Opacity = 1.0;
            DrawerTranslateTransform.Y = 0.0;
            CoverScaleTransform.ScaleX = 1.0;
            CoverScaleTransform.ScaleY = 1.0;
        };

        _currentTransition = sb;
        sb.Begin();
    }

    private Storyboard? _crossfadeStoryboard;
    private string? _currentDisplayedCoverUrl;

    public void AnimateTrackChange()
    {
        string? newUrl = ViewModel.HdCoverUrl;

        // If drawer is closed or not visible, do not decode heavy bitmaps
        if (!ViewModel.IsNowPlayingDrawerOpen || Visibility != Visibility.Visible)
        {
            _currentDisplayedCoverUrl = null;
            _crossfadeStoryboard?.Stop();
            _crossfadeStoryboard = null;
            CoverImageNext.Opacity = 0.0;
            CoverImageNext.Source = null;
            AmbientGlowNext.Opacity = 0.0;
            AmbientGlowNext.Source = null;
            CoverImageCurrent.Source = null;
            AmbientGlowCurrent.Source = null;
            return;
        }

        // If URL hasn't changed, don't re-animate
        if (string.Equals(_currentDisplayedCoverUrl, newUrl, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!FullEffects)
        {
            _crossfadeStoryboard?.Stop();
            _currentDisplayedCoverUrl = newUrl;
            CoverImageCurrent.Source = Uri.TryCreate(newUrl, UriKind.Absolute, out var liteUri) ? new BitmapImage(liteUri) : null;
            CoverImageNext.Source = null;
            AmbientGlowCurrent.Source = null;
            AmbientGlowNext.Source = null;
            return;
        }

        // Gentle text refresh: opacity from 0.65 -> 1.0 (no vertical bouncing!)
        AnimateTextSoftRefresh();

        if (string.IsNullOrWhiteSpace(newUrl))
        {
            _currentDisplayedCoverUrl = null;
            CoverImageCurrent.Source = null;
            AmbientGlowCurrent.Source = null;
            return;
        }

        StartTwoLayerCrossfade(newUrl);
    }

    private void StartTwoLayerCrossfade(string newUrl)
    {
        _crossfadeStoryboard?.Stop();
        _crossfadeStoryboard = null;

        if (!Uri.TryCreate(newUrl, UriKind.Absolute, out var uri))
        {
            return;
        }

        var newBitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(uri);
        CoverImageNext.Opacity = 0.0;
        CoverImageNext.Source = newBitmap;

        AmbientGlowNext.Opacity = 0.0;
        AmbientGlowNext.Source = newBitmap;

        bool hasFaded = false;

        void TriggerFade()
        {
            if (hasFaded) return;
            hasFaded = true;

            var sb = new Storyboard();
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            // 1. CoverImageNext: 0.0 -> 1.0 (smoothly dissolves over old cover)
            var coverAnim = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(420),
                EasingFunction = ease
            };
            Storyboard.SetTarget(coverAnim, CoverImageNext);
            Storyboard.SetTargetProperty(coverAnim, "Opacity");
            sb.Children.Add(coverAnim);

            // 2. AmbientGlowNext: 0.0 -> 0.40 (smoothly dissolves over old glow)
            var glowAnim = new DoubleAnimation
            {
                From = 0.0,
                To = 0.40,
                Duration = TimeSpan.FromMilliseconds(480),
                EasingFunction = ease
            };
            Storyboard.SetTarget(glowAnim, AmbientGlowNext);
            Storyboard.SetTargetProperty(glowAnim, "Opacity");
            sb.Children.Add(glowAnim);

            sb.Completed += (s, e) =>
            {
                _crossfadeStoryboard = null;
                // Swap: CoverImageCurrent now displays the new bitmap, and Next is hidden
                CoverImageCurrent.Source = newBitmap;
                CoverImageNext.Opacity = 0.0;
                CoverImageNext.Source = null;

                AmbientGlowCurrent.Source = newBitmap;
                AmbientGlowNext.Opacity = 0.0;
                AmbientGlowNext.Source = null;

                _currentDisplayedCoverUrl = newUrl;
            };

            _crossfadeStoryboard = sb;
            sb.Begin();
        }

        newBitmap.ImageOpened += (s, e) =>
        {
            DispatcherQueue.TryEnqueue(TriggerFade);
        };

        // Fallback: If image fails to load or ImageOpened doesn't fire, trigger fade after 120ms
        var fallbackTimer = DispatcherQueue.CreateTimer();
        fallbackTimer.Interval = TimeSpan.FromMilliseconds(120);
        fallbackTimer.Tick += (s, e) =>
        {
            fallbackTimer.Stop();
            TriggerFade();
        };
        fallbackTimer.Start();
    }

    private void AnimateTextSoftRefresh()
    {
        var sb = new Storyboard();
        var textAnim = new DoubleAnimation
        {
            From = 0.65,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(textAnim, TrackInfoPanel);
        Storyboard.SetTargetProperty(textAnim, "Opacity");
        sb.Children.Add(textAnim);
        sb.Begin();
    }

    public void AnimateClose(Action? onCompleted = null)
    {
        _currentTransition?.Stop();
        _currentTransition = null;

        IsHitTestVisible = false;
        if (!FullEffects)
        {
            Visibility = Visibility.Collapsed;
            RootDrawerGrid.Opacity = 0;
            onCompleted?.Invoke();
            return;
        }

        var sb = new Storyboard();

        // 1. Root Slide Down (current Y -> 50)
        var slideAnim = new DoubleAnimation
        {
            From = DrawerTranslateTransform.Y,
            To = 50.0,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(slideAnim, DrawerTranslateTransform);
        Storyboard.SetTargetProperty(slideAnim, "Y");
        sb.Children.Add(slideAnim);

        // 2. Root Fade Out (current Opacity -> 0)
        var fadeAnim = new DoubleAnimation
        {
            From = RootDrawerGrid.Opacity,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(190),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(fadeAnim, RootDrawerGrid);
        Storyboard.SetTargetProperty(fadeAnim, "Opacity");
        sb.Children.Add(fadeAnim);

        // 3. Cover Scale Out (current Scale -> 0.94)
        var scaleXAnim = new DoubleAnimation
        {
            From = CoverScaleTransform.ScaleX,
            To = 0.94,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(scaleXAnim, CoverScaleTransform);
        Storyboard.SetTargetProperty(scaleXAnim, "ScaleX");
        sb.Children.Add(scaleXAnim);

        var scaleYAnim = new DoubleAnimation
        {
            From = CoverScaleTransform.ScaleY,
            To = 0.94,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(scaleYAnim, CoverScaleTransform);
        Storyboard.SetTargetProperty(scaleYAnim, "ScaleY");
        sb.Children.Add(scaleYAnim);

        sb.Completed += (s, e) =>
        {
            _currentTransition = null;
            Visibility = Visibility.Collapsed;
            IsHitTestVisible = false;
            RootDrawerGrid.Opacity = 0.0;
            DrawerTranslateTransform.Y = 50.0;
            CoverScaleTransform.ScaleX = 0.94;
            CoverScaleTransform.ScaleY = 0.94;
            onCompleted?.Invoke();
        };

        _currentTransition = sb;
        sb.Begin();
    }

    private async void DrawerTrackOptionsFlyout_Opening(object? sender, object e)
    {
        await ViewModel.PopulatePlaylistSubItemAsync(DrawerAddToPlaylistSubItem);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const uint WM_SYSCOMMAND = 0x0112;
    private const int SC_DRAGMOVE = 0xF012;

    private void HeaderBarGrid_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        // Don't drag if clicked on an interactive control (Button, Slider, etc.)
        if (e.OriginalSource is DependencyObject dep)
        {
            var parent = dep;
            while (parent != null && parent != HeaderBarGrid)
            {
                if (parent is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase)
                {
                    return;
                }
                parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(parent);
            }
        }

        var point = e.GetCurrentPoint(HeaderBarGrid);
        if (point.Position.X < 0 || point.Position.X > HeaderBarGrid.ActualWidth ||
            point.Position.Y < 0 || point.Position.Y > HeaderBarGrid.ActualHeight)
        {
            return;
        }

        if (point.Properties.IsLeftButtonPressed)
        {
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            if (hwnd != IntPtr.Zero)
            {
                ReleaseCapture();
                SendMessage(hwnd, WM_SYSCOMMAND, (IntPtr)SC_DRAGMOVE, IntPtr.Zero);
                e.Handled = true;
            }
        }
    }

    private void HeaderBarGrid_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep)
        {
            var parent = dep;
            while (parent != null && parent != HeaderBarGrid)
            {
                if (parent is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase)
                {
                    return;
                }
                parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(parent);
            }
        }

        var point = e.GetPosition(HeaderBarGrid);
        if (point.X < 0 || point.X > HeaderBarGrid.ActualWidth ||
            point.Y < 0 || point.Y > HeaderBarGrid.ActualHeight)
        {
            return;
        }

        if (App.MainWindow?.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            if (presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Maximized)
            {
                presenter.Restore();
            }
            else
            {
                presenter.Maximize();
            }
        }
    }
}

