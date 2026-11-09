#nullable enable

using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.UI.Notifications;
using WinRT.Interop;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;
using YMusic.WinUI.Views;

namespace YMusic.WinUI.Services.Implementations;

public sealed class TrayNotificationService : ITrayNotificationService
{
    private const uint TrayIconId = 4201;
    private const uint SubclassId = 101;

    // Thumbnail toolbar button IDs (101..107)
    private const uint THUMB_SHUFFLE = 101;
    private const uint THUMB_DISLIKE = 102;
    private const uint THUMB_PREV = 103;
    private const uint THUMB_PLAYPAUSE = 104;
    private const uint THUMB_NEXT = 105;
    private const uint THUMB_LIKE = 106;
    private const uint THUMB_REPEAT = 107;

    // Tray context menu IDs
    private const uint ID_HEADER = 1001;
    private const uint ID_PLAYPAUSE = 1002;
    private const uint ID_NEXT = 1003;
    private const uint ID_PREV = 1004;
    private const uint ID_LIKE = 1005;
    private const uint ID_RESTORE = 1006;
    private const uint ID_EXIT = 1007;

    private readonly IAudioPlayerService _audioPlayer;
    private readonly IYandexMusicService _musicService;
    private readonly ISettingsService _settingsService;
    private readonly ILocalizationService _loc;

    private ShellWindow? _shellWindow;
    private AppWindow? _appWindow;
    private IntPtr _hWnd = IntPtr.Zero;
    private IntPtr _hIcon = IntPtr.Zero;
    private bool _isIconAdded;
    private bool _disposed;
    private bool _hasShownMinimizeHint;

    private uint _wmTaskbarCreated;
    private uint _wmTaskbarButtonCreated;

    private ITaskbarList3? _taskbarList;
    private bool _thumbButtonsAdded;
    private readonly Dictionary<string, IntPtr> _iconCache = new();
    private static string? _preferredIconFont;

    private readonly SubclassProc _subclassProc;

    public bool IsExplicitExit { get; set; }

    public TrayNotificationService(
        IAudioPlayerService audioPlayer,
        IYandexMusicService musicService,
        ISettingsService settingsService,
        ILocalizationService loc)
    {
        _audioPlayer = audioPlayer;
        _musicService = musicService;
        _settingsService = settingsService;
        _loc = loc;
        _subclassProc = SubclassWindowProc;
        _loc.LanguageChanged += (s, lang) => RequestUpdateTaskbarButtons();
    }

    public void Initialize(ShellWindow shellWindow)
    {
        if (_hWnd != IntPtr.Zero) return;

        _shellWindow = shellWindow;
        _appWindow = shellWindow.AppWindow;
        _hWnd = WindowNative.GetWindowHandle(shellWindow);

        _wmTaskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        _wmTaskbarButtonCreated = RegisterWindowMessageW("TaskbarButtonCreated");

        try
        {
            ChangeWindowMessageFilterEx(_hWnd, _wmTaskbarButtonCreated, MSGFLT_ALLOW, IntPtr.Zero);
            ChangeWindowMessageFilterEx(_hWnd, _wmTaskbarCreated, MSGFLT_ALLOW, IntPtr.Zero);
            ChangeWindowMessageFilterEx(_hWnd, WM_COMMAND, MSGFLT_ALLOW, IntPtr.Zero);
        }
        catch { }

        InitDarkMode();
        LoadTrayIcon();
        AddTrayIcon();
        SetWindowIcons();

        SetWindowSubclass(_hWnd, _subclassProc, (UIntPtr)SubclassId, IntPtr.Zero);

        _audioPlayer.CurrentTrackChanged += OnCurrentTrackChanged;
        _audioPlayer.IsPlayingChanged += OnIsPlayingChanged;
        _audioPlayer.IsShuffleChanged += OnShuffleChanged;
        _audioPlayer.RepeatModeChanged += OnRepeatModeChanged;
        _musicService.TrackLikeChanged += OnTrackLikeChanged;

        UpdateTooltip("YMusic");

        InitTaskbarList();
        InitializeThumbnailButtons();
    }

    private void SetWindowIcons()
    {
        try
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath))
            {
                IntPtr hBig = LoadImageW(IntPtr.Zero, iconPath, IMAGE_ICON, 32, 32, LR_LOADFROMFILE);
                IntPtr hSmall = LoadImageW(IntPtr.Zero, iconPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
                if (hBig != IntPtr.Zero) SendMessageW(_hWnd, WM_SETICON, (IntPtr)ICON_BIG, hBig);
                if (hSmall != IntPtr.Zero) SendMessageW(_hWnd, WM_SETICON, (IntPtr)ICON_SMALL, hSmall);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TrayService] SetWindowIcons failed: {ex.Message}");
        }
    }

    private void InitDarkMode()
    {
        try
        {
            // Enable dark mode for Win32 popup menus & window elements
            SetPreferredAppMode(2 /* ForceDark */);
            AllowDarkModeForWindow(_hWnd, true);
            SetWindowTheme(_hWnd, "DarkMode_Explorer", null);
            FlushMenuThemes();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TrayService] Dark mode init: {ex.Message}");
        }
    }

    private void InitTaskbarList()
    {
        try
        {
            _taskbarList = (ITaskbarList3)new TaskbarInstance();
            _taskbarList.HrInit();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TrayService] InitTaskbarList failed: {ex.Message}");
        }
    }

    public void InitializeThumbnailButtons()
    {
        if (_hWnd == IntPtr.Zero) return;

        if (_taskbarList == null)
        {
            InitTaskbarList();
        }

        if (_taskbarList == null) return;

        try
        {
            var buttons = BuildThumbButtons();
            int hr = _taskbarList.ThumbBarAddButtons(_hWnd, (uint)buttons.Length, buttons);
            Debug.WriteLine($"[TrayService] ThumbBarAddButtons: 0x{hr:X8}");
            if (hr == 0)
            {
                _thumbButtonsAdded = true;
            }
            else
            {
                // If buttons were already added previously, update them instead
                hr = _taskbarList.ThumbBarUpdateButtons(_hWnd, (uint)buttons.Length, buttons);
                Debug.WriteLine($"[TrayService] ThumbBarUpdateButtons: 0x{hr:X8}");
                if (hr == 0)
                {
                    _thumbButtonsAdded = true;
                }
            }

            if (!_thumbButtonsAdded && _shellWindow?.DispatcherQueue != null)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(400);
                    _shellWindow?.DispatcherQueue.TryEnqueue(() =>
                    {
                        if (!_thumbButtonsAdded)
                        {
                            InitializeThumbnailButtons();
                        }
                    });
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TrayService] InitializeThumbnailButtons failed: {ex.Message}");
        }
    }

    private void UpdateTaskbarButtons()
    {
        if (_hWnd == IntPtr.Zero || _taskbarList == null) return;

        if (!_thumbButtonsAdded)
        {
            InitializeThumbnailButtons();
            return;
        }

        try
        {
            var buttons = BuildThumbButtons();
            _taskbarList.ThumbBarUpdateButtons(_hWnd, (uint)buttons.Length, buttons);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TrayService] UpdateTaskbarButtons error: {ex.Message}");
        }
    }

    private void RequestUpdateTaskbarButtons()
    {
        if (_shellWindow?.DispatcherQueue != null)
        {
            _shellWindow.DispatcherQueue.TryEnqueue(UpdateTaskbarButtons);
        }
        else
        {
            UpdateTaskbarButtons();
        }
    }

    private THUMBBUTTON[] BuildThumbButtons()
    {
        bool hasTrack = _audioPlayer.CurrentTrack != null;
        bool isPlaying = _audioPlayer.IsPlaying;
        bool isShuffle = _audioPlayer.IsShuffle;
        RepeatMode repeatMode = _audioPlayer.RepeatMode;
        bool isLiked = hasTrack && _musicService.IsTrackLiked(_audioPlayer.CurrentTrack!.Id);

        // 1. Shuffle
        IntPtr hShuffle = isShuffle
            ? GetCachedGlyphIcon("\uE8B1", 0x2E, 0xFD, 0x6C) // Highlighted when active
            : GetCachedGlyphIcon("\uE8B1");

        // 2. Dislike
        IntPtr hDislike = GetCachedGlyphIcon("\uE1E0");

        // 3. Previous
        IntPtr hPrev = GetCachedGlyphIcon("\uE892");

        // 4. Play / Pause
        IntPtr hPlayPause = GetCachedGlyphIcon(isPlaying ? "\uE769" : "\uE768");

        // 5. Next
        IntPtr hNext = GetCachedGlyphIcon("\uE893");

        // 6. Like: outline white or red filled heart
        IntPtr hLike = isLiked
            ? GetCachedGlyphIcon("\uEB52", 0xFF, 0x33, 0x66)
            : GetCachedGlyphIcon("\uEB51");

        // 7. Repeat
        string repeatGlyph = repeatMode == RepeatMode.One ? "\uE8ED" : "\uE8EE";
        bool isRepeatActive = repeatMode != RepeatMode.None;
        IntPtr hRepeat = isRepeatActive
            ? GetCachedGlyphIcon(repeatGlyph, 0x2E, 0xFD, 0x6C)
            : GetCachedGlyphIcon(repeatGlyph);

        IntPtr ValidIcon(IntPtr h) => h != IntPtr.Zero ? h : (_hIcon != IntPtr.Zero ? _hIcon : LoadIconW(IntPtr.Zero, (IntPtr)32512));

        var flags = hasTrack ? THUMBBUTTONFLAGS.THBF_ENABLED : THUMBBUTTONFLAGS.THBF_DISABLED;

        return new THUMBBUTTON[]
        {
            new()
            {
                dwMask = THUMBBUTTONMASK.THB_ICON | THUMBBUTTONMASK.THB_TOOLTIP | THUMBBUTTONMASK.THB_FLAGS,
                iId = THUMB_SHUFFLE,
                hIcon = ValidIcon(hShuffle),
                szTip = isShuffle ? _loc["Thumb_ShuffleOn"] : _loc["Thumb_ShuffleOff"],
                dwFlags = THUMBBUTTONFLAGS.THBF_ENABLED
            },
            new()
            {
                dwMask = THUMBBUTTONMASK.THB_ICON | THUMBBUTTONMASK.THB_TOOLTIP | THUMBBUTTONMASK.THB_FLAGS,
                iId = THUMB_DISLIKE,
                hIcon = ValidIcon(hDislike),
                szTip = _loc["Thumb_Dislike"],
                dwFlags = flags
            },
            new()
            {
                dwMask = THUMBBUTTONMASK.THB_ICON | THUMBBUTTONMASK.THB_TOOLTIP | THUMBBUTTONMASK.THB_FLAGS,
                iId = THUMB_PREV,
                hIcon = ValidIcon(hPrev),
                szTip = _loc["Tray_Prev"],
                dwFlags = flags
            },
            new()
            {
                dwMask = THUMBBUTTONMASK.THB_ICON | THUMBBUTTONMASK.THB_TOOLTIP | THUMBBUTTONMASK.THB_FLAGS,
                iId = THUMB_PLAYPAUSE,
                hIcon = ValidIcon(hPlayPause),
                szTip = isPlaying ? _loc["Tray_Pause"] : _loc["Tray_Play"],
                dwFlags = flags
            },
            new()
            {
                dwMask = THUMBBUTTONMASK.THB_ICON | THUMBBUTTONMASK.THB_TOOLTIP | THUMBBUTTONMASK.THB_FLAGS,
                iId = THUMB_NEXT,
                hIcon = ValidIcon(hNext),
                szTip = _loc["Tray_Next"],
                dwFlags = flags
            },
            new()
            {
                dwMask = THUMBBUTTONMASK.THB_ICON | THUMBBUTTONMASK.THB_TOOLTIP | THUMBBUTTONMASK.THB_FLAGS,
                iId = THUMB_LIKE,
                hIcon = ValidIcon(hLike),
                szTip = isLiked ? _loc["Tray_Liked"] : _loc["Tray_Like"],
                dwFlags = flags
            },
            new()
            {
                dwMask = THUMBBUTTONMASK.THB_ICON | THUMBBUTTONMASK.THB_TOOLTIP | THUMBBUTTONMASK.THB_FLAGS,
                iId = THUMB_REPEAT,
                hIcon = ValidIcon(hRepeat),
                szTip = repeatMode switch
                {
                    RepeatMode.One => _loc["Thumb_RepeatOne"],
                    RepeatMode.All => _loc["Thumb_RepeatAll"],
                    _ => _loc["Thumb_RepeatOff"]
                },
                dwFlags = THUMBBUTTONFLAGS.THBF_ENABLED
            }
        };
    }

    private void HandleThumbButtonClick(uint buttonId)
    {
        switch (buttonId)
        {
            case THUMB_SHUFFLE:
                _audioPlayer.IsShuffle = !_audioPlayer.IsShuffle;
                RequestUpdateTaskbarButtons();
                break;

            case THUMB_DISLIKE:
                {
                    var track = _audioPlayer.CurrentTrack;
                    if (track != null)
                    {
                        if (_audioPlayer.CurrentPlaybackMode == PlaybackMode.Wave)
                        {
                            _ = _musicService.SendRotorFeedbackAsync("dislike", "user", "onyourwave", track.Id);
                        }
                        _ = _audioPlayer.NextAsync();
                    }
                }
                break;

            case THUMB_PREV:
                _ = _audioPlayer.PreviousAsync();
                break;

            case THUMB_PLAYPAUSE:
                _audioPlayer.TogglePlayPause();
                RequestUpdateTaskbarButtons();
                break;

            case THUMB_NEXT:
                _ = _audioPlayer.NextAsync();
                break;

            case THUMB_LIKE:
                {
                    var track = _audioPlayer.CurrentTrack;
                    if (track != null)
                    {
                        string trackId = track.Id;
                        bool currentlyLiked = _musicService.IsTrackLiked(trackId);
                        if (!currentlyLiked)
                        {
                            _ = _musicService.LikeTrackAsync(trackId);
                        }
                        else
                        {
                            _ = _musicService.UnlikeTrackAsync(trackId);
                        }
                    }
                }
                break;

            case THUMB_REPEAT:
                _audioPlayer.RepeatMode = _audioPlayer.RepeatMode switch
                {
                    RepeatMode.None => RepeatMode.All,
                    RepeatMode.All => RepeatMode.One,
                    _ => RepeatMode.None
                };
                RequestUpdateTaskbarButtons();
                break;
        }
    }

    private IntPtr GetCachedGlyphIcon(string glyph, byte? tintR = null, byte? tintG = null, byte? tintB = null)
    {
        string key = $"{glyph}_{tintR}_{tintG}_{tintB}";
        if (_iconCache.TryGetValue(key, out IntPtr cached) && cached != IntPtr.Zero)
        {
            return cached;
        }

        IntPtr icon = CreateGlyphIcon(glyph, tintR, tintG, tintB);
        if (icon != IntPtr.Zero)
        {
            _iconCache[key] = icon;
        }
        return icon;
    }

    private static string GetIconFont()
    {
        if (_preferredIconFont != null) return _preferredIconFont;

        string fontsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        if (File.Exists(Path.Combine(fontsFolder, "SegoeIcons.ttf")))
        {
            _preferredIconFont = "Segoe Fluent Icons";
        }
        else
        {
            _preferredIconFont = "Segoe MDL2 Assets";
        }
        return _preferredIconFont;
    }

    private IntPtr CreateGlyphIcon(string glyph, byte? tintR = null, byte? tintG = null, byte? tintB = null)
    {
        int cx = GetSystemMetrics(SM_CXSMICON);
        int size = cx > 0 ? cx : 20;

        IntPtr hdcMem = CreateCompatibleDC(IntPtr.Zero);
        if (hdcMem == IntPtr.Zero) return IntPtr.Zero;

        BITMAPINFO bmi = default;
        bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
        bmi.bmiHeader.biWidth = size;
        bmi.bmiHeader.biHeight = -size; // top-down
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = 0; // BI_RGB

        IntPtr hbmColor = CreateDIBSection(hdcMem, ref bmi, 0, out IntPtr pBits, IntPtr.Zero, 0);
        if (hbmColor == IntPtr.Zero || pBits == IntPtr.Zero)
        {
            DeleteDC(hdcMem);
            return IntPtr.Zero;
        }

        IntPtr oldBmp = SelectObject(hdcMem, hbmColor);

        string fontName = GetIconFont();
        int fontHeight = -size + (size <= 16 ? 2 : 4);
        IntPtr hFont = CreateFontW(
            fontHeight, 0, 0, 0, 400,
            0, 0, 0, 1 /* DEFAULT_CHARSET */,
            0, 0, 5 /* CLEARTYPE_QUALITY */,
            0, fontName);

        IntPtr oldFont = SelectObject(hdcMem, hFont);

        SetBkMode(hdcMem, 1 /* TRANSPARENT */);
        SetTextColor(hdcMem, 0x00FFFFFF);

        RECT rc = new RECT { left = 0, top = 0, right = size, bottom = size };
        DrawTextW(hdcMem, glyph, glyph.Length, ref rc, 1 | 4 | 0x20 /* DT_CENTER | DT_VCENTER | DT_SINGLELINE */);

        // Convert rendered text buffer into 32-bit premultiplied ARGB icon with alpha
        int totalPixels = size * size;
        int[] pixels = new int[totalPixels];
        Marshal.Copy(pBits, pixels, 0, totalPixels);

        for (int i = 0; i < totalPixels; i++)
        {
            uint val = (uint)pixels[i];
            byte b = (byte)(val & 0xFF);
            byte g = (byte)((val >> 8) & 0xFF);
            byte r = (byte)((val >> 16) & 0xFF);
            byte max = Math.Max(r, Math.Max(g, b));

            if (max > 0)
            {
                if (tintR.HasValue)
                {
                    byte tr = (byte)(tintR.Value * max / 255);
                    byte tg = (byte)(tintG!.Value * max / 255);
                    byte tb = (byte)(tintB!.Value * max / 255);
                    pixels[i] = (int)(((uint)max << 24) | ((uint)tr << 16) | ((uint)tg << 8) | (uint)tb);
                }
                else
                {
                    pixels[i] = (int)(((uint)max << 24) | ((uint)max << 16) | ((uint)max << 8) | (uint)max);
                }
            }
            else
            {
                pixels[i] = 0;
            }
        }
        Marshal.Copy(pixels, 0, pBits, totalPixels);

        // Deselect font and bitmap from DC BEFORE creating icon
        SelectObject(hdcMem, oldFont);
        DeleteObject(hFont);
        SelectObject(hdcMem, oldBmp);

        int maskStride = ((size + 15) / 16) * 2;
        byte[] maskBits = new byte[maskStride * size]; // 0-initialized
        IntPtr hbmMask = CreateBitmap(size, size, 1, 1, maskBits);

        ICONINFO ii = new ICONINFO
        {
            fIcon = true,
            hbmColor = hbmColor,
            hbmMask = hbmMask
        };

        IntPtr hIcon = CreateIconIndirect(ref ii);

        DeleteObject(hbmColor);
        DeleteObject(hbmMask);
        DeleteDC(hdcMem);

        if (hIcon == IntPtr.Zero && _hIcon != IntPtr.Zero)
        {
            return _hIcon;
        }

        return hIcon;
    }

    private void LoadTrayIcon()
    {
        try
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath))
            {
                _hIcon = LoadImageW(IntPtr.Zero, iconPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
            }

            if (_hIcon == IntPtr.Zero)
            {
                _hIcon = LoadImageW(IntPtr.Zero, iconPath, IMAGE_ICON, 32, 32, LR_LOADFROMFILE);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TrayService] Error loading icon: {ex.Message}");
        }
    }

    private void AddTrayIcon()
    {
        if (_hWnd == IntPtr.Zero || _isIconAdded) return;

        var nid = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _hWnd,
            uID = TrayIconId,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _hIcon,
            szTip = "YMusic"
        };

        _isIconAdded = Shell_NotifyIconW(NIM_ADD, ref nid);

        nid.uTimeoutOrVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIconW(NIM_SETVERSION, ref nid);
    }

    private void RemoveTrayIcon()
    {
        if (!_isIconAdded || _hWnd == IntPtr.Zero) return;

        var nid = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _hWnd,
            uID = TrayIconId
        };

        Shell_NotifyIconW(NIM_DELETE, ref nid);
        _isIconAdded = false;
    }

    private void UpdateTooltip(string text)
    {
        if (!_isIconAdded || _hWnd == IntPtr.Zero) return;

        var nid = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _hWnd,
            uID = TrayIconId,
            uFlags = NIF_TIP,
            szTip = Truncate(text, 127)
        };

        Shell_NotifyIconW(NIM_MODIFY, ref nid);
    }

    public void UpdateTrack(Track? track, bool isPlaying)
    {
        string tooltip = track != null
            ? $"{(isPlaying ? "▶" : "⏸")} {track.FullTitle} • {track.ArtistNames}"
            : "YMusic — Native Windows Client";

        UpdateTooltip(tooltip);
        RequestUpdateTaskbarButtons();
    }

    private void OnCurrentTrackChanged(object? sender, Track? track)
    {
        bool isPlaying = _audioPlayer.IsPlaying;
        UpdateTrack(track, isPlaying);

        if (_settingsService.TrackNotificationsEnabled && track != null)
        {
            bool isForeground = _hWnd != IntPtr.Zero &&
                                GetForegroundWindow() == _hWnd &&
                                _appWindow != null &&
                                _appWindow.IsVisible &&
                                !IsIconic(_hWnd);

            if (!isForeground)
            {
                string title = track.FullTitle;
                string subtitle = !string.IsNullOrWhiteSpace(track.AlbumTitle)
                    ? $"{track.ArtistNames} • {track.AlbumTitle}"
                    : track.ArtistNames;

                ShowNotification(title, subtitle);
            }
        }
    }

    private void OnIsPlayingChanged(object? sender, bool isPlaying)
    {
        UpdateTrack(_audioPlayer.CurrentTrack, isPlaying);
    }

    private void OnShuffleChanged(object? sender, bool isShuffle)
    {
        RequestUpdateTaskbarButtons();
    }

    private void OnRepeatModeChanged(object? sender, RepeatMode repeatMode)
    {
        RequestUpdateTaskbarButtons();
    }

    private void OnTrackLikeChanged(object? sender, (string TrackId, bool IsLiked) e)
    {
        RequestUpdateTaskbarButtons();
    }

    public void ShowNotification(string title, string message)
    {
        if (_disposed) return;

        // Native Win32 tray notification is instantaneous, safe, and never blocks the UI thread
        if (_isIconAdded && _hWnd != IntPtr.Zero)
        {
            try
            {
                var nid = new NOTIFYICONDATAW
                {
                    cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
                    hWnd = _hWnd,
                    uID = TrayIconId,
                    uFlags = NIF_INFO,
                    szInfoTitle = Truncate(title, 63),
                    szInfo = Truncate(message, 255),
                    dwInfoFlags = NIIF_INFO | NIIF_RESPECT_QUIET_TIME,
                    uTimeoutOrVersion = 3000
                };

                Shell_NotifyIconW(NIM_MODIFY, ref nid);
                return;
            }
            catch { }
        }

        // Background fallback for WinRT ToastNotification in unpackaged mode
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var template = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText02);
                var textNodes = template.GetElementsByTagName("text");
                if (textNodes.Length > 0)
                {
                    textNodes[0].AppendChild(template.CreateTextNode(title));
                }
                if (textNodes.Length > 1)
                {
                    textNodes[1].AppendChild(template.CreateTextNode(message));
                }

                var toast = new ToastNotification(template);
                ToastNotificationManager.CreateToastNotifier().Show(toast);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TrayService] WinRT Toast fallback: {ex.Message}");
            }
        });
    }

    public void RestoreWindow()
    {
        if (_shellWindow == null || _hWnd == IntPtr.Zero) return;

        try
        {
            if (_appWindow != null)
            {
                _appWindow.Show();
            }

            ForceForegroundWindow(_hWnd);
            _shellWindow.Activate();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TrayNotificationService] RestoreWindow error: {ex}");
        }
    }

    private static void ForceForegroundWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return;

        try
        {
            ShowWindow(hWnd, SW_SHOWNORMAL);
            ShowWindow(hWnd, SW_RESTORE);

            IntPtr foreWnd = GetForegroundWindow();
            if (foreWnd != hWnd)
            {
                uint foreThread = GetWindowThreadProcessId(foreWnd, IntPtr.Zero);
                uint appThread = GetCurrentThreadId();

                if (foreThread != 0 && foreThread != appThread)
                {
                    AttachThreadInput(appThread, foreThread, true);
                    BringWindowToTop(hWnd);
                    SetForegroundWindow(hWnd);
                    AttachThreadInput(appThread, foreThread, false);
                }
                else
                {
                    BringWindowToTop(hWnd);
                    SetForegroundWindow(hWnd);
                }
            }

            try
            {
                SwitchToThisWindow(hWnd, true);
            }
            catch { }
        }
        catch { }
    }

    public void MinimizeToTray()
    {
        if (_appWindow != null && _appWindow.IsVisible)
        {
            _appWindow.Hide();

            if (!_hasShownMinimizeHint)
            {
                _hasShownMinimizeHint = true;
                ShowNotification(
                    _loc["Tray_MinimizedTitle"],
                    _loc["Tray_MinimizedHint"]);
            }
        }
    }

    public void ExitApplication()
    {
        IsExplicitExit = true;
        try
        {
            _audioPlayer.SaveCurrentState();
        }
        catch { }

        try
        {
            App.GetService<IDiscordRpcService>()?.Dispose();
            App.GetService<IObsOverlayService>()?.Dispose();
            App.GetService<IGameOverlayService>()?.Dispose();
            _audioPlayer.Dispose();
        }
        catch { }

        Dispose();

        try
        {
            _appWindow?.Destroy();
        }
        catch { }

        try
        {
            Application.Current.Exit();
        }
        catch { }

        Environment.Exit(0);
    }

    private void ShowContextMenu()
    {
        if (_hWnd == IntPtr.Zero) return;

        GetCursorPos(out POINT pt);
        SetForegroundWindow(_hWnd);

        // Ensure modern dark mode is applied before creating popup menu
        try
        {
            SetPreferredAppMode(2 /* ForceDark */);
            AllowDarkModeForWindow(_hWnd, true);
            SetWindowTheme(_hWnd, "DarkMode_Explorer", null);
            FlushMenuThemes();
        }
        catch { }

        IntPtr hMenu = CreatePopupMenu();
        if (hMenu == IntPtr.Zero) return;

        try
        {
            var currentTrack = _audioPlayer.CurrentTrack;
            bool isPlaying = _audioPlayer.IsPlaying;
            bool hasTrack = currentTrack != null;
            bool isLiked = hasTrack && _musicService.IsTrackLiked(currentTrack!.Id);

            // 1. Current Track Header (Disabled)
            string trackTitle = hasTrack
                ? $"{currentTrack!.FullTitle} — {currentTrack.ArtistNames}"
                : _loc["Tray_NothingPlaying"];

            if (trackTitle.Length > 44)
            {
                trackTitle = trackTitle.Substring(0, 41) + "...";
            }

            AppendMenuW(hMenu, MF_STRING | MF_DISABLED, (UIntPtr)ID_HEADER, $"🎵  {trackTitle}");
            AppendMenuW(hMenu, MF_SEPARATOR, UIntPtr.Zero, string.Empty);

            // 2. Play / Pause
            string playPauseText = isPlaying ? $"⏸  {_loc["Tray_Pause"]}" : $"▶  {_loc["Tray_Play"]}";
            AppendMenuW(hMenu, MF_STRING, (UIntPtr)ID_PLAYPAUSE, playPauseText);

            // 3. Next / Previous Track
            AppendMenuW(hMenu, MF_STRING, (UIntPtr)ID_NEXT, $"⏭  {_loc["Tray_Next"]}");
            AppendMenuW(hMenu, MF_STRING, (UIntPtr)ID_PREV, $"⏮  {_loc["Tray_Prev"]}");

            // 4. Like / Unlike (if track present)
            if (hasTrack)
            {
                string likeText = isLiked ? $"❤️  {_loc["Tray_Liked"]}" : $"🤍  {_loc["Tray_Like"]}";
                AppendMenuW(hMenu, MF_STRING, (UIntPtr)ID_LIKE, likeText);
            }

            AppendMenuW(hMenu, MF_SEPARATOR, UIntPtr.Zero, string.Empty);

            // 5. Show Window
            AppendMenuW(hMenu, MF_STRING, (UIntPtr)ID_RESTORE, $"🗖  {_loc["Tray_Open"]}");
            AppendMenuW(hMenu, MF_SEPARATOR, UIntPtr.Zero, string.Empty);

            // 6. Exit
            AppendMenuW(hMenu, MF_STRING, (UIntPtr)ID_EXIT, $"✕  {_loc["Tray_Exit"]}");

            uint cmd = TrackPopupMenuEx(
                hMenu,
                TPM_LEFTALIGN | TPM_BOTTOMALIGN | TPM_RIGHTBUTTON | TPM_RETURNCMD,
                pt.X,
                pt.Y,
                _hWnd,
                IntPtr.Zero);

            switch (cmd)
            {
                case ID_PLAYPAUSE:
                    _audioPlayer.TogglePlayPause();
                    RequestUpdateTaskbarButtons();
                    break;

                case ID_NEXT:
                    _ = _audioPlayer.NextAsync();
                    break;

                case ID_PREV:
                    _ = _audioPlayer.PreviousAsync();
                    break;

                case ID_LIKE:
                    if (_audioPlayer.CurrentTrack != null)
                    {
                        string trackId = _audioPlayer.CurrentTrack.Id;
                        if (!_musicService.IsTrackLiked(trackId))
                        {
                            _ = _musicService.LikeTrackAsync(trackId);
                        }
                        else
                        {
                            _ = _musicService.UnlikeTrackAsync(trackId);
                        }
                    }
                    break;

                case ID_RESTORE:
                    RestoreWindow();
                    break;

                case ID_EXIT:
                    ExitApplication();
                    break;
            }
        }
        finally
        {
            DestroyMenu(hMenu);
        }
    }

    private IntPtr SubclassWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData)
    {
        if (uMsg == WM_TRAYICON)
        {
            uint mouseMsg = (uint)(lParam.ToInt64() & 0xFFFF);
            if (mouseMsg == WM_LBUTTONUP || mouseMsg == WM_LBUTTONDBLCLK || mouseMsg == NIN_BALLOONUSERCLICK)
            {
                _shellWindow?.DispatcherQueue?.TryEnqueue(RestoreWindow);
                return IntPtr.Zero;
            }
            else if (mouseMsg == WM_RBUTTONUP || mouseMsg == WM_CONTEXTMENU)
            {
                _shellWindow?.DispatcherQueue?.TryEnqueue(ShowContextMenu);
                return IntPtr.Zero;
            }
        }
        else if (uMsg == WM_COMMAND)
        {
            uint hiword = (uint)((wParam.ToInt64() >> 16) & 0xFFFF);
            uint loword = (uint)(wParam.ToInt64() & 0xFFFF);
            if (hiword == THBN_CLICKED)
            {
                uint buttonId = loword;
                _shellWindow?.DispatcherQueue?.TryEnqueue(() => HandleThumbButtonClick(buttonId));
                return IntPtr.Zero;
            }
        }
        else if (uMsg != 0 && uMsg == _wmTaskbarButtonCreated)
        {
            _thumbButtonsAdded = false;
            InitializeThumbnailButtons();
            return IntPtr.Zero;
        }
        else if (uMsg != 0 && uMsg == _wmTaskbarCreated)
        {
            _isIconAdded = false;
            AddTrayIcon();
            UpdateTrack(_audioPlayer.CurrentTrack, _audioPlayer.IsPlaying);

            _thumbButtonsAdded = false;
            InitializeThumbnailButtons();
            return IntPtr.Zero;
        }

        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private static string Truncate(string str, int maxLength)
    {
        if (string.IsNullOrEmpty(str)) return string.Empty;
        return str.Length <= maxLength ? str : str.Substring(0, maxLength);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _audioPlayer.CurrentTrackChanged -= OnCurrentTrackChanged;
        _audioPlayer.IsPlayingChanged -= OnIsPlayingChanged;
        _audioPlayer.IsShuffleChanged -= OnShuffleChanged;
        _audioPlayer.RepeatModeChanged -= OnRepeatModeChanged;
        _musicService.TrackLikeChanged -= OnTrackLikeChanged;

        if (_hWnd != IntPtr.Zero)
        {
            RemoveWindowSubclass(_hWnd, _subclassProc, (UIntPtr)SubclassId);
            RemoveTrayIcon();
        }

        if (_hIcon != IntPtr.Zero)
        {
            DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }

        foreach (var icon in _iconCache.Values)
        {
            if (icon != IntPtr.Zero)
            {
                DestroyIcon(icon);
            }
        }
        _iconCache.Clear();
    }

    #region Win32 API Definitions & COM Interfaces

    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    [ClassInterface(ClassInterfaceType.None)]
    private class TaskbarInstance { }

    [ComImport]
    [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        // ITaskbarList
        [PreserveSig] int HrInit();
        [PreserveSig] int AddTab(IntPtr hWnd);
        [PreserveSig] int DeleteTab(IntPtr hWnd);
        [PreserveSig] int ActivateTab(IntPtr hWnd);
        [PreserveSig] int SetActiveAlt(IntPtr hWnd);

        // ITaskbarList2
        [PreserveSig] int MarkFullscreenWindow(IntPtr hWnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);

        // ITaskbarList3
        [PreserveSig] int SetProgressValue(IntPtr hWnd, ulong ullCompleted, ulong ullTotal);
        [PreserveSig] int SetProgressState(IntPtr hWnd, int tbpFlags);
        [PreserveSig] int RegisterTab(IntPtr hWndTab, IntPtr hWndMDI);
        [PreserveSig] int UnregisterTab(IntPtr hWndTab);
        [PreserveSig] int SetTabOrder(IntPtr hWndTab, IntPtr hWndInsertBefore);
        [PreserveSig] int SetTabActive(IntPtr hWndTab, IntPtr hWndMDI, uint dwReserved);
        [PreserveSig] int ThumbBarAddButtons(IntPtr hWnd, uint cButtons, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] THUMBBUTTON[] pButton);
        [PreserveSig] int ThumbBarUpdateButtons(IntPtr hWnd, uint cButtons, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] THUMBBUTTON[] pButton);
        [PreserveSig] int ThumbBarSetImageList(IntPtr hWnd, IntPtr himl);
        [PreserveSig] int SetOverlayIcon(IntPtr hWnd, IntPtr hIcon, [MarshalAs(UnmanagedType.LPWStr)] string pszDescription);
        [PreserveSig] int SetThumbnailTooltip(IntPtr hWnd, [MarshalAs(UnmanagedType.LPWStr)] string pszTip);
        [PreserveSig] int SetThumbnailClip(IntPtr hWnd, IntPtr prcClip);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct THUMBBUTTON
    {
        public THUMBBUTTONMASK dwMask;
        public uint iId;
        public uint iBitmap;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szTip;
        public THUMBBUTTONFLAGS dwFlags;
    }

    [Flags]
    private enum THUMBBUTTONFLAGS : uint
    {
        THBF_ENABLED = 0x00000000,
        THBF_DISABLED = 0x00000001,
        THBF_DISMISSONCLICK = 0x00000002,
        THBF_NOBACKGROUND = 0x00000004,
        THBF_HIDDEN = 0x00000008,
        THBF_NONINTERACTIVE = 0x00000010
    }

    [Flags]
    private enum THUMBBUTTONMASK : uint
    {
        THB_BITMAP = 0x0001,
        THB_ICON = 0x0002,
        THB_TOOLTIP = 0x0004,
        THB_FLAGS = 0x0008
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData);

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIM_SETVERSION = 0x00000004;

    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_INFO = 0x00000010;

    private const uint NOTIFYICON_VERSION_4 = 4;

    private const uint NIIF_INFO = 0x00000001;
    private const uint NIIF_RESPECT_QUIET_TIME = 0x00000080;

    private const uint WM_USER = 0x0400;
    private const uint WM_TRAYICON = WM_USER + 2048;

    private const uint WM_COMMAND = 0x0111;
    private const uint THBN_CLICKED = 0x1800;

    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_CONTEXTMENU = 0x007B;
    private const uint NIN_BALLOONUSERCLICK = WM_USER + 5;

    private const int SW_RESTORE = 9;

    private const uint MF_STRING = 0x00000000;
    private const uint MF_DISABLED = 0x00000002;
    private const uint MF_SEPARATOR = 0x00000800;

    private const uint TPM_LEFTALIGN = 0x0000;
    private const uint TPM_BOTTOMALIGN = 0x0020;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_RETURNCMD = 0x0100;

    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;

    private const int SM_CXSMICON = 49;

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImageW(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private const int SW_SHOWNORMAL = 1;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void SwitchToThisWindow(IntPtr hWnd, bool fUnknown);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string lpNewItem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hWnd, IntPtr lptpm);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string lpString);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
    private static extern int SetPreferredAppMode(int appMode);

    [DllImport("uxtheme.dll", EntryPoint = "#133", SetLastError = true)]
    private static extern bool AllowDarkModeForWindow(IntPtr hWnd, bool allow);

    [DllImport("uxtheme.dll", EntryPoint = "#136", SetLastError = true)]
    private static extern void FlushMenuThemes();

    [DllImport("uxtheme.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string? pszSubIdList);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFontW(
        int nHeight, int nWidth, int nEscapement, int nOrientation, int fnWeight,
        uint fdwItalic, uint fdwUnderline, uint fdwStrikeOut, uint fdwCharSet,
        uint fdwOutputPrecision, uint fdwClipPrecision, uint fdwQuality,
        uint fdwPitchAndFamily, string lpszFace);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(
        IntPtr hdc, ref BITMAPINFO pbmi, uint iUsage, out IntPtr ppvBits, IntPtr hSection, uint dwOffset);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int SetBkMode(IntPtr hdc, int iBkMode);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern uint SetTextColor(IntPtr hdc, uint crColor);

    private const uint MSGFLT_ALLOW = 1;
    private const uint WM_SETICON = 0x0080;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr hWnd, uint msg, uint action, IntPtr pChangeFilterStruct);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIconW(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int DrawTextW(IntPtr hdc, string lpchText, int cchText, ref RECT lprc, uint dwDTFormat);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateIconIndirect(ref ICONINFO piconinfo);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateBitmap(int nWidth, int nHeight, uint cPlanes, uint cBitsPerPel, byte[]? lpvBits);

    #endregion
}
