#nullable enable

using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using WinRT.Interop;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;
using YMusic.WinUI.ViewModels;
using YMusic.WinUI.Views;

namespace YMusic.WinUI.Services.Implementations;

public sealed class GameOverlayService : IGameOverlayService
{
    private const uint SubclassId = 202;

    private const int HOTKEY_PLAYPAUSE = 2001;
    private const int HOTKEY_NEXT = 2002;
    private const int HOTKEY_PREV = 2003;
    private const int HOTKEY_LIKE = 2004;
    private const int HOTKEY_VOLUP = 2005;
    private const int HOTKEY_VOLDOWN = 2006;
    private const int HOTKEY_OVERLAY = 2007;

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_NOREPEAT = 0x4000;

    private const uint VK_SPACE = 0x20;
    private const uint VK_LEFT = 0x25;
    private const uint VK_UP = 0x26;
    private const uint VK_RIGHT = 0x27;
    private const uint VK_DOWN = 0x28;
    private const uint VK_L = 0x4C;
    private const uint VK_O = 0x4F;

    private readonly IAudioPlayerService _audioPlayer;
    private readonly ISettingsService _settingsService;
    private readonly IServiceProvider _serviceProvider;
    private readonly SubclassProc _subclassProc;

    private IntPtr _mainWindowHandle = IntPtr.Zero;
    private GameOverlayWindow? _overlayWindow;
    private bool _isInitialized;
    private bool _hotkeysRegistered;
    private bool _disposed;

    public GameOverlayService(
        IAudioPlayerService audioPlayer,
        ISettingsService settingsService,
        IServiceProvider serviceProvider)
    {
        _audioPlayer = audioPlayer;
        _settingsService = settingsService;
        _serviceProvider = serviceProvider;
        _subclassProc = SubclassWindowProc;
    }

    public void Initialize()
    {
        if (_isInitialized || _disposed) return;
        _isInitialized = true;

        try
        {
            if (App.MainWindow != null)
            {
                _mainWindowHandle = WindowNative.GetWindowHandle(App.MainWindow);
                if (_mainWindowHandle != IntPtr.Zero && _settingsService.GlobalHotkeysEnabled)
                {
                    SetWindowSubclass(_mainWindowHandle, _subclassProc, (UIntPtr)SubclassId, IntPtr.Zero);
                    RegisterHotkeys(_mainWindowHandle);
                }
            }

            _audioPlayer.CurrentTrackChanged += OnTrackChanged;
            _settingsService.SettingChanged += OnSettingChanged;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GameOverlayService] Initialize error: {ex.Message}");
        }
    }

    private void OnTrackChanged(object? sender, Track? track)
    {
        if (!_settingsService.GameOverlayEnabled || !_settingsService.ShowOverlayOnTrackChange) return;

        App.MainDispatcherQueue?.TryEnqueue(() =>
        {
            ShowOverlay("СЕЙЧАС ИГРАЕТ");
        });
    }

    private void OnSettingChanged(object? sender, string propName)
    {
        if (propName == nameof(ISettingsService.GlobalHotkeysEnabled))
        {
            if (_mainWindowHandle != IntPtr.Zero)
            {
                if (_settingsService.GlobalHotkeysEnabled)
                {
                    SetWindowSubclass(_mainWindowHandle, _subclassProc, (UIntPtr)SubclassId, IntPtr.Zero);
                    RegisterHotkeys(_mainWindowHandle);
                }
                else
                {
                    UnregisterHotkeys(_mainWindowHandle);
                    RemoveWindowSubclass(_mainWindowHandle, _subclassProc, (UIntPtr)SubclassId);
                }
            }
        }
        else if (propName == nameof(ISettingsService.GameOverlayEnabled))
        {
            if (!_settingsService.GameOverlayEnabled)
            {
                HideOverlay();
            }
        }
    }

    public void ShowOverlay(string? actionBadge = null)
    {
        if (_disposed || !_settingsService.GameOverlayEnabled) return;

        App.MainDispatcherQueue?.TryEnqueue(() =>
        {
            try
            {
                if (_overlayWindow == null)
                {
                    _overlayWindow = new GameOverlayWindow(_settingsService);
                }

                var track = _audioPlayer.CurrentTrack;
                var playerBar = _serviceProvider.GetService<PlayerBarViewModel>();
                bool isLiked = playerBar?.IsCurrentTrackLiked ?? false;
                bool isPlaying = _audioPlayer.IsPlaying;

                _overlayWindow.ShowOverlay(track, actionBadge ?? "WINYANDEX HUD", isLiked, isPlaying);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GameOverlayService] ShowOverlay error: {ex.Message}");
            }
        });
    }

    public void HideOverlay()
    {
        _overlayWindow?.HideWindow();
    }

    private void RegisterHotkeys(IntPtr hWnd)
    {
        if (_hotkeysRegistered || hWnd == IntPtr.Zero) return;

        uint mods = MOD_CONTROL | MOD_ALT | MOD_NOREPEAT;
        RegisterHotKey(hWnd, HOTKEY_PLAYPAUSE, mods, VK_SPACE);
        RegisterHotKey(hWnd, HOTKEY_NEXT, mods, VK_RIGHT);
        RegisterHotKey(hWnd, HOTKEY_PREV, mods, VK_LEFT);
        RegisterHotKey(hWnd, HOTKEY_LIKE, mods, VK_L);
        RegisterHotKey(hWnd, HOTKEY_VOLUP, mods, VK_UP);
        RegisterHotKey(hWnd, HOTKEY_VOLDOWN, mods, VK_DOWN);
        RegisterHotKey(hWnd, HOTKEY_OVERLAY, mods, VK_O);

        _hotkeysRegistered = true;
        Debug.WriteLine("[GameOverlayService] Global hotkeys registered (Ctrl+Alt+Space/Right/Left/L/Up/Down/O).");
    }

    private void UnregisterHotkeys(IntPtr hWnd)
    {
        if (!_hotkeysRegistered || hWnd == IntPtr.Zero) return;

        UnregisterHotKey(hWnd, HOTKEY_PLAYPAUSE);
        UnregisterHotKey(hWnd, HOTKEY_NEXT);
        UnregisterHotKey(hWnd, HOTKEY_PREV);
        UnregisterHotKey(hWnd, HOTKEY_LIKE);
        UnregisterHotKey(hWnd, HOTKEY_VOLUP);
        UnregisterHotKey(hWnd, HOTKEY_VOLDOWN);
        UnregisterHotKey(hWnd, HOTKEY_OVERLAY);

        _hotkeysRegistered = false;
        Debug.WriteLine("[GameOverlayService] Global hotkeys unregistered.");
    }

    private IntPtr SubclassWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData)
    {
        const uint WM_HOTKEY = 0x0312;
        if (uMsg == WM_HOTKEY)
        {
            int hotkeyId = wParam.ToInt32();
            HandleHotkey(hotkeyId);
            return IntPtr.Zero;
        }

        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private void HandleHotkey(int id)
    {
        App.MainDispatcherQueue?.TryEnqueue(() =>
        {
            try
            {
                var playerBar = _serviceProvider.GetService<PlayerBarViewModel>();

                switch (id)
                {
                    case HOTKEY_PLAYPAUSE:
                        _audioPlayer.TogglePlayPause();
                        if (_settingsService.GameOverlayEnabled)
                        {
                            ShowOverlay(_audioPlayer.IsPlaying ? "ВОСПРОИЗВЕДЕНИЕ" : "ПАУЗА");
                        }
                        break;

                    case HOTKEY_NEXT:
                        playerBar?.NextCommand.Execute(null);
                        if (_settingsService.GameOverlayEnabled)
                        {
                            ShowOverlay("СЛЕДУЮЩИЙ ТРЕК");
                        }
                        break;

                    case HOTKEY_PREV:
                        playerBar?.PreviousCommand.Execute(null);
                        if (_settingsService.GameOverlayEnabled)
                        {
                            ShowOverlay("ПРЕДЫДУЩИЙ ТРЕК");
                        }
                        break;

                    case HOTKEY_LIKE:
                        if (playerBar != null)
                        {
                            bool wasLiked = playerBar.IsCurrentTrackLiked;
                            playerBar.ToggleLikeCommand.Execute(null);
                            if (_settingsService.GameOverlayEnabled)
                            {
                                ShowOverlay(!wasLiked ? "ДОБАВЛЕНО В ИЗБРАННОЕ" : "УДАЛЕНО ИЗ ИЗБРАННОГО");
                            }
                        }
                        break;

                    case HOTKEY_VOLUP:
                        double newVolUp = Math.Clamp(_audioPlayer.Volume + 5.0, 0.0, 100.0);
                        _audioPlayer.Volume = newVolUp;
                        if (_settingsService.GameOverlayEnabled)
                        {
                            ShowOverlay($"ГРОМКОСТЬ {(int)Math.Round(newVolUp)}%");
                        }
                        break;

                    case HOTKEY_VOLDOWN:
                        double newVolDown = Math.Clamp(_audioPlayer.Volume - 5.0, 0.0, 100.0);
                        _audioPlayer.Volume = newVolDown;
                        if (_settingsService.GameOverlayEnabled)
                        {
                            ShowOverlay($"ГРОМКОСТЬ {(int)Math.Round(newVolDown)}%");
                        }
                        break;

                    case HOTKEY_OVERLAY:
                        if (_settingsService.GameOverlayEnabled)
                        {
                            ShowOverlay("СЕЙЧАС ИГРАЕТ");
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GameOverlayService] HandleHotkey {id} error: {ex.Message}");
            }
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _audioPlayer.CurrentTrackChanged -= OnTrackChanged;
            _settingsService.SettingChanged -= OnSettingChanged;

            if (_mainWindowHandle != IntPtr.Zero)
            {
                UnregisterHotkeys(_mainWindowHandle);
                RemoveWindowSubclass(_mainWindowHandle, _subclassProc, (UIntPtr)SubclassId);
            }

            App.MainDispatcherQueue?.TryEnqueue(() =>
            {
                try
                {
                    _overlayWindow?.Close();
                    _overlayWindow = null;
                }
                catch { }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GameOverlayService] Dispose error: {ex.Message}");
        }
    }

    #region Win32 P/Invoke

    private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    #endregion
}
