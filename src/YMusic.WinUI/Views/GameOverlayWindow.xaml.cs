#nullable enable

using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using WinRT.Interop;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.Views;

public sealed partial class GameOverlayWindow : Window
{
    private readonly ISettingsService _settingsService;
    private readonly DispatcherTimer _hideTimer;
    private readonly DispatcherTimer _eqTimer;
    private IntPtr _hWnd = IntPtr.Zero;
    private int _eqStep;
    private SubclassProc? _subclassDelegate;

    private bool _isCardVisible;
    private Storyboard? _fadeStoryboard;

    private const int OverlayWidth = 370;
    private const int OverlayHeight = 88;
    private const int ScreenMargin = 20;

    private static readonly int[][] EqPatterns = new[]
    {
        new[] { 14, 6, 12 },
        new[] { 8, 14, 6 },
        new[] { 12, 8, 14 },
        new[] { 6, 12, 8 },
        new[] { 15, 10, 5 },
        new[] { 10, 15, 11 }
    };

    public GameOverlayWindow(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        InitializeComponent();

        AppWindow.Title = "WinYandex Game HUD";
        AppWindow.IsShownInSwitchers = false;

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }

        _hWnd = WindowNative.GetWindowHandle(this);

        _subclassDelegate = OverlaySubclassProc;
        SetWindowSubclass(_hWnd, _subclassDelegate, (UIntPtr)101, IntPtr.Zero);

        ApplyWindowStyles();
        PositionWindow();

        RootCard.Opacity = 0.0;
        RootCard.RequestedTheme = ElementTheme.Dark;

        _eqTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(240) };
        _eqTimer.Tick += (s, e) =>
        {
            _eqStep = (_eqStep + 1) % EqPatterns.Length;
            var pat = EqPatterns[_eqStep];
            if (Bar1 != null && Bar2 != null && Bar3 != null)
            {
                Bar1.Height = pat[0];
                Bar2.Height = pat[1];
                Bar3.Height = pat[2];
            }
        };

        _hideTimer = new DispatcherTimer();
        _hideTimer.Tick += (s, e) =>
        {
            _hideTimer.Stop();
            _eqTimer.Stop();
            HideWindow();
        };
    }

    public void ShowOverlay(Track? track, string actionBadge, bool isLiked, bool isPlaying)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                ActionBadgeText.Text = string.IsNullOrWhiteSpace(actionBadge) ? "WINYANDEX HUD" : actionBadge.ToUpperInvariant();

                if (track != null)
                {
                    TitleText.Text = track.Title ?? "Без названия";
                    ArtistText.Text = track.Artists?.Count > 0
                        ? string.Join(", ", track.Artists.Select(a => a.Name))
                        : "Неизвестный исполнитель";

                    string? coverUrl = track.CoverUrl;
                    if (!string.IsNullOrWhiteSpace(coverUrl) && Uri.TryCreate(coverUrl, UriKind.Absolute, out var uri))
                    {
                        CoverImage.Source = new BitmapImage(uri)
                        {
                            DecodePixelWidth = 132,
                            DecodePixelHeight = 132
                        };
                    }
                    else
                    {
                        CoverImage.Source = null;
                    }
                }
                else
                {
                    TitleText.Text = "Трек не выбран";
                    ArtistText.Text = "Ожидание воспроизведения";
                    CoverImage.Source = null;
                }

                LikeIcon.Visibility = isLiked ? Visibility.Visible : Visibility.Collapsed;
                EqualizerBars.Visibility = isPlaying ? Visibility.Visible : Visibility.Collapsed;

                PositionWindow();

                // Show window without stealing focus
                ShowWindow(_hWnd, SW_SHOWNOACTIVATE);

                int duration = Math.Max(2, _settingsService.GameOverlayDurationSeconds);
                _hideTimer.Interval = TimeSpan.FromSeconds(duration);
                _hideTimer.Stop();
                _hideTimer.Start();

                if (isPlaying)
                {
                    _eqTimer.Start();
                }
                else
                {
                    _eqTimer.Stop();
                }

                if (!_isCardVisible)
                {
                    PlayEntranceAnimation();
                }
                else
                {
                    PlayUpdatePulse();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GameOverlayWindow] ShowOverlay error: {ex.Message}");
            }
        });
    }

    public void HideWindow()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                PlayExitAnimation(() =>
                {
                    ShowWindow(_hWnd, SW_HIDE);
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GameOverlayWindow] HideWindow error: {ex.Message}");
            }
        });
    }

    private void PlayEntranceAnimation()
    {
        _isCardVisible = true;
        _fadeStoryboard?.Stop();

        var fade = new DoubleAnimation
        {
            From = RootCard.Opacity,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(fade, RootCard);
        Storyboard.SetTargetProperty(fade, "Opacity");

        var slide = new DoubleAnimation
        {
            From = -6.0,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(slide, CardTransform);
        Storyboard.SetTargetProperty(slide, "TranslateY");

        _fadeStoryboard = new Storyboard();
        _fadeStoryboard.Children.Add(fade);
        _fadeStoryboard.Children.Add(slide);
        _fadeStoryboard.Begin();
    }

    private void PlayExitAnimation(Action? onCompleted)
    {
        if (!_isCardVisible)
        {
            onCompleted?.Invoke();
            return;
        }

        _fadeStoryboard?.Stop();

        var fade = new DoubleAnimation
        {
            From = RootCard.Opacity,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(160),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(fade, RootCard);
        Storyboard.SetTargetProperty(fade, "Opacity");

        _fadeStoryboard = new Storyboard();
        _fadeStoryboard.Children.Add(fade);

        void OnCompletedHandler(object? sender, object e)
        {
            if (_fadeStoryboard != null)
            {
                _fadeStoryboard.Completed -= OnCompletedHandler;
            }
            _isCardVisible = false;
            onCompleted?.Invoke();
        }

        _fadeStoryboard.Completed += OnCompletedHandler;
        _fadeStoryboard.Begin();
    }

    private void PlayUpdatePulse()
    {
        var sb = new Storyboard();
        var pulse = new DoubleAnimation
        {
            From = 0.4,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(pulse, ActionBadgeText);
        Storyboard.SetTargetProperty(pulse, "Opacity");
        sb.Children.Add(pulse);
        sb.Begin();
    }

    private void PositionWindow()
    {
        try
        {
            var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;

            uint dpi = GetDpiForWindow(_hWnd);
            double scale = dpi > 0 ? dpi / 96.0 : 1.0;
            int physWidth = (int)Math.Round(OverlayWidth * scale);
            int physHeight = (int)Math.Round(OverlayHeight * scale);

            int x = _settingsService.GameOverlayPosition switch
            {
                1 /* TopLeft */ => workArea.X + (int)(ScreenMargin * scale),
                4 /* TopCenter */ => workArea.X + (workArea.Width - physWidth) / 2,
                0 /* TopRight */ => (workArea.X + workArea.Width) - physWidth - (int)(ScreenMargin * scale),
                6 /* MiddleLeft */ => workArea.X + (int)(ScreenMargin * scale),
                7 /* MiddleRight */ => (workArea.X + workArea.Width) - physWidth - (int)(ScreenMargin * scale),
                3 /* BottomLeft */ => workArea.X + (int)(ScreenMargin * scale),
                5 /* BottomCenter */ => workArea.X + (workArea.Width - physWidth) / 2,
                2 /* BottomRight */ => (workArea.X + workArea.Width) - physWidth - (int)(ScreenMargin * scale),
                _ /* Default: TopRight */ => (workArea.X + workArea.Width) - physWidth - (int)(ScreenMargin * scale)
            };

            int y = _settingsService.GameOverlayPosition switch
            {
                1 or 4 or 0 /* Top */ => workArea.Y + (int)(ScreenMargin * scale),
                6 or 7 /* Middle */ => workArea.Y + (workArea.Height - physHeight) / 2,
                3 or 5 or 2 /* Bottom */ => (workArea.Y + workArea.Height) - physHeight - (int)(ScreenMargin * scale),
                _ => workArea.Y + (int)(ScreenMargin * scale)
            };

            AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, physWidth, physHeight));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GameOverlayWindow] PositionWindow error: {ex.Message}");
        }
    }

    private const uint WM_NCHITTEST = 0x0084;
    private const int HTTRANSPARENT = -1;

    private IntPtr OverlaySubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData)
    {
        if (uMsg == WM_NCHITTEST)
        {
            return new IntPtr(HTTRANSPARENT);
        }
        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private void ApplyWindowStyles()
    {
        try
        {
            const int GWL_STYLE = -16;
            const long WS_CAPTION = 0x00C00000L;
            const long WS_THICKFRAME = 0x00040000L;
            const long WS_BORDER = 0x00800000L;
            const long WS_DLGFRAME = 0x00400000L;
            const long WS_POPUP = 0x80000000L;

            IntPtr curStyle = GetWindowLongPtrUniversal(_hWnd, GWL_STYLE);
            long newStyle = (curStyle.ToInt64() & ~(WS_CAPTION | WS_THICKFRAME | WS_BORDER | WS_DLGFRAME)) | WS_POPUP;
            SetWindowLongPtrUniversal(_hWnd, GWL_STYLE, new IntPtr(newStyle));

            const int GWL_EXSTYLE = -20;
            const long WS_EX_TOOLWINDOW = 0x00000080L;
            const long WS_EX_NOACTIVATE = 0x08000000L;
            const long WS_EX_TOPMOST = 0x00000008L;
            const long WS_EX_TRANSPARENT = 0x00000020L;

            IntPtr curEx = GetWindowLongPtrUniversal(_hWnd, GWL_EXSTYLE);
            long newEx = curEx.ToInt64() | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST | WS_EX_TRANSPARENT;
            SetWindowLongPtrUniversal(_hWnd, GWL_EXSTYLE, new IntPtr(newEx));

            SetWindowPos(_hWnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);

            var margins = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
            DwmExtendFrameIntoClientArea(_hWnd, ref margins);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GameOverlayWindow] ApplyWindowStyles error: {ex.Message}");
        }
    }

    #region Win32 P/Invoke

    private const int SW_HIDE = 0;
    private const int SW_SHOWNOACTIVATE = 4;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

    private static IntPtr GetWindowLongPtrUniversal(IntPtr hWnd, int nIndex)
    {
        return IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);
    }

    private static IntPtr SetWindowLongPtrUniversal(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        return IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : SetWindowLong32(hWnd, nIndex, dwNewLong);
    }

    #endregion
}
