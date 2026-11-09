#nullable enable

using System.Diagnostics;
using System.Text.Json;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.Services.Implementations;

public sealed class SettingsService : ISettingsService
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private readonly IYandexMusicService _musicService;
    private AppSettings _settings = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public event EventHandler<string>? SettingChanged;

    public SettingsService(IYandexMusicService musicService)
    {
        _musicService = musicService;

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string dir = Path.Combine(appData, "YMusic");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "settings.json");

        Load();
        ApplyToServices();
    }

    public AudioQualityPreference PreferredQuality
    {
        get => _settings.PreferredQuality;
        set
        {
            if (_settings.PreferredQuality != value)
            {
                _settings.PreferredQuality = value;
                _musicService.PreferredQuality = value;
                Save();
                NotifyChanged(nameof(PreferredQuality));
            }
        }
    }

    public bool GaplessPreloadEnabled
    {
        get => _settings.GaplessPreloadEnabled;
        set
        {
            if (_settings.GaplessPreloadEnabled != value)
            {
                _settings.GaplessPreloadEnabled = value;
                Save();
                NotifyChanged(nameof(GaplessPreloadEnabled));
            }
        }
    }

    public bool DiscordRpcEnabled
    {
        get => _settings.DiscordRpcEnabled;
        set
        {
            if (_settings.DiscordRpcEnabled != value)
            {
                _settings.DiscordRpcEnabled = value;
                Save();
                NotifyChanged(nameof(DiscordRpcEnabled));
            }
        }
    }

    public bool MinimizeToTrayOnClose
    {
        get => _settings.MinimizeToTrayOnClose;
        set
        {
            if (_settings.MinimizeToTrayOnClose != value)
            {
                _settings.MinimizeToTrayOnClose = value;
                Save();
                NotifyChanged(nameof(MinimizeToTrayOnClose));
            }
        }
    }

    public bool TrackNotificationsEnabled
    {
        get => _settings.TrackNotificationsEnabled;
        set
        {
            if (_settings.TrackNotificationsEnabled != value)
            {
                _settings.TrackNotificationsEnabled = value;
                Save();
                NotifyChanged(nameof(TrackNotificationsEnabled));
            }
        }
    }

    public bool FckCensorEnabled
    {
        get => _settings.FckCensorEnabled;
        set
        {
            if (_settings.FckCensorEnabled != value)
            {
                _settings.FckCensorEnabled = value;
                Save();
                NotifyChanged(nameof(FckCensorEnabled));
            }
        }
    }

    public bool SloplessEnabled
    {
        get => _settings.SloplessEnabled;
        set
        {
            if (_settings.SloplessEnabled != value)
            {
                _settings.SloplessEnabled = value;
                Save();
                NotifyChanged(nameof(SloplessEnabled));
            }
        }
    }

    public string SloplessBehavior
    {
        get => _settings.SloplessBehavior;
        set
        {
            if (_settings.SloplessBehavior != value)
            {
                _settings.SloplessBehavior = value;
                Save();
                NotifyChanged(nameof(SloplessBehavior));
            }
        }
    }

    public string SloplessDetectionMode
    {
        get => _settings.SloplessDetectionMode;
        set
        {
            if (_settings.SloplessDetectionMode != value)
            {
                _settings.SloplessDetectionMode = value;
                Save();
                NotifyChanged(nameof(SloplessDetectionMode));
            }
        }
    }

    public int SloplessArtistThreshold
    {
        get => _settings.SloplessArtistThreshold;
        set
        {
            if (_settings.SloplessArtistThreshold != value)
            {
                _settings.SloplessArtistThreshold = value;
                Save();
                NotifyChanged(nameof(SloplessArtistThreshold));
            }
        }
    }

    public bool SloplessShowTrackLabels
    {
        get => _settings.SloplessShowTrackLabels;
        set
        {
            if (_settings.SloplessShowTrackLabels != value)
            {
                _settings.SloplessShowTrackLabels = value;
                Save();
                NotifyChanged(nameof(SloplessShowTrackLabels));
            }
        }
    }

    public bool SloplessShowArtistLabels
    {
        get => _settings.SloplessShowArtistLabels;
        set
        {
            if (_settings.SloplessShowArtistLabels != value)
            {
                _settings.SloplessShowArtistLabels = value;
                Save();
                NotifyChanged(nameof(SloplessShowArtistLabels));
            }
        }
    }

    public bool FullVisualEffects
    {
        get => _settings.FullVisualEffects;
        set
        {
            if (_settings.FullVisualEffects != value)
            {
                _settings.FullVisualEffects = value;
                Save();
                NotifyChanged(nameof(FullVisualEffects));
            }
        }
    }

    public bool ExternalLyricsEnabled
    {
        get => _settings.ExternalLyricsEnabled;
        set
        {
            if (_settings.ExternalLyricsEnabled != value)
            {
                _settings.ExternalLyricsEnabled = value;
                Save();
                NotifyChanged(nameof(ExternalLyricsEnabled));
            }
        }
    }

    public string StartupScreen
    {
        get => _settings.StartupScreen;
        set
        {
            if (_settings.StartupScreen != value)
            {
                _settings.StartupScreen = value;
                Save();
                NotifyChanged(nameof(StartupScreen));
            }
        }
    }

    public string AppTheme
    {
        get => _settings.AppTheme;
        set
        {
            if (_settings.AppTheme != value)
            {
                _settings.AppTheme = value;
                Save();
                NotifyChanged(nameof(AppTheme));
            }
        }
    }

    public string AccentColorHex
    {
        get => string.IsNullOrWhiteSpace(_settings.AccentColorHex) ? "#FFED71B1" : _settings.AccentColorHex;
        set
        {
            if (_settings.AccentColorHex != value)
            {
                _settings.AccentColorHex = value;
                Save();
                NotifyChanged(nameof(AccentColorHex));
            }
        }
    }

    public string BackdropKind
    {
        get => string.IsNullOrWhiteSpace(_settings.BackdropKind) ? "Solid" : _settings.BackdropKind;
        set
        {
            if (_settings.BackdropKind != value)
            {
                _settings.BackdropKind = value;
                Save();
                NotifyChanged(nameof(BackdropKind));
            }
        }
    }

    public string AppFont
    {
        get => _settings.AppFont;
        set
        {
            if (_settings.AppFont != value)
            {
                _settings.AppFont = value;
                Save();
                NotifyChanged(nameof(AppFont));
            }
        }
    }

    public string Language
    {
        get => _settings.Language;
        set
        {
            if (_settings.Language != value)
            {
                _settings.Language = value;
                Save();
                NotifyChanged(nameof(Language));
            }
        }
    }

    public bool AudioCacheEnabled
    {
        get => _settings.AudioCacheEnabled;
        set
        {
            if (_settings.AudioCacheEnabled != value)
            {
                _settings.AudioCacheEnabled = value;
                Save();
                NotifyChanged(nameof(AudioCacheEnabled));
            }
        }
    }

    public string AudioCacheLocation
    {
        get => _settings.AudioCacheLocation;
        set
        {
            if (_settings.AudioCacheLocation != value)
            {
                _settings.AudioCacheLocation = value ?? string.Empty;
                Save();
                NotifyChanged(nameof(AudioCacheLocation));
            }
        }
    }

    public int CacheSizeLimitMb
    {
        get => _settings.CacheSizeLimitMb;
        set
        {
            if (_settings.CacheSizeLimitMb != value)
            {
                _settings.CacheSizeLimitMb = value;
                Save();
                NotifyChanged(nameof(CacheSizeLimitMb));
            }
        }
    }

    public bool ObsOverlayEnabled
    {
        get => _settings.ObsOverlayEnabled;
        set
        {
            if (_settings.ObsOverlayEnabled != value)
            {
                _settings.ObsOverlayEnabled = value;
                Save();
                NotifyChanged(nameof(ObsOverlayEnabled));
            }
        }
    }

    public int ObsOverlayPort
    {
        get => _settings.ObsOverlayPort;
        set
        {
            if (_settings.ObsOverlayPort != value)
            {
                _settings.ObsOverlayPort = value;
                Save();
                NotifyChanged(nameof(ObsOverlayPort));
            }
        }
    }

    public string ObsOverlayTheme
    {
        get => _settings.ObsOverlayTheme;
        set
        {
            if (_settings.ObsOverlayTheme != value)
            {
                _settings.ObsOverlayTheme = value;
                Save();
                NotifyChanged(nameof(ObsOverlayTheme));
            }
        }
    }

    public bool ObsOverlayHideOnPause
    {
        get => _settings.ObsOverlayHideOnPause;
        set
        {
            if (_settings.ObsOverlayHideOnPause != value)
            {
                _settings.ObsOverlayHideOnPause = value;
                Save();
                NotifyChanged(nameof(ObsOverlayHideOnPause));
            }
        }
    }

    public bool AutoplayWaveOnAlbumEnd
    {
        get => _settings.AutoplayWaveOnAlbumEnd;
        set
        {
            if (_settings.AutoplayWaveOnAlbumEnd != value)
            {
                _settings.AutoplayWaveOnAlbumEnd = value;
                Save();
                NotifyChanged(nameof(AutoplayWaveOnAlbumEnd));
            }
        }
    }

    public bool AutoBypassProxyForYandex
    {
        get => _settings.AutoBypassProxyForYandex;
        set
        {
            if (_settings.AutoBypassProxyForYandex != value)
            {
                _settings.AutoBypassProxyForYandex = value;
                YandexBypassProxy.Instance.IsEnabled = value;
                Save();
                NotifyChanged(nameof(AutoBypassProxyForYandex));
            }
        }
    }

    public bool CrossfadeEnabled
    {
        get => _settings.CrossfadeEnabled;
        set
        {
            if (_settings.CrossfadeEnabled != value)
            {
                _settings.CrossfadeEnabled = value;
                Save();
                NotifyChanged(nameof(CrossfadeEnabled));
            }
        }
    }

    public int CrossfadeDurationSeconds
    {
        get => _settings.CrossfadeDurationSeconds;
        set
        {
            int clamped = Math.Clamp(value, 1, 10);
            if (_settings.CrossfadeDurationSeconds != clamped)
            {
                _settings.CrossfadeDurationSeconds = clamped;
                Save();
                NotifyChanged(nameof(CrossfadeDurationSeconds));
            }
        }
    }

    public bool GlobalHotkeysEnabled
    {
        get => _settings.GlobalHotkeysEnabled;
        set
        {
            if (_settings.GlobalHotkeysEnabled != value)
            {
                _settings.GlobalHotkeysEnabled = value;
                Save();
                NotifyChanged(nameof(GlobalHotkeysEnabled));
            }
        }
    }

    public bool GameOverlayEnabled
    {
        get => _settings.GameOverlayEnabled;
        set
        {
            if (_settings.GameOverlayEnabled != value)
            {
                _settings.GameOverlayEnabled = value;
                Save();
                NotifyChanged(nameof(GameOverlayEnabled));
            }
        }
    }

    public bool ShowOverlayOnTrackChange
    {
        get => _settings.ShowOverlayOnTrackChange;
        set
        {
            if (_settings.ShowOverlayOnTrackChange != value)
            {
                _settings.ShowOverlayOnTrackChange = value;
                Save();
                NotifyChanged(nameof(ShowOverlayOnTrackChange));
            }
        }
    }

    public int GameOverlayDurationSeconds
    {
        get => _settings.GameOverlayDurationSeconds;
        set
        {
            int clamped = Math.Clamp(value, 1, 10);
            if (_settings.GameOverlayDurationSeconds != clamped)
            {
                _settings.GameOverlayDurationSeconds = clamped;
                Save();
                NotifyChanged(nameof(GameOverlayDurationSeconds));
            }
        }
    }

    public int GameOverlayPosition
    {
        get => _settings.GameOverlayPosition;
        set
        {
            int clamped = Math.Clamp(value, 0, 7);
            if (_settings.GameOverlayPosition != clamped)
            {
                _settings.GameOverlayPosition = clamped;
                Save();
                NotifyChanged(nameof(GameOverlayPosition));
            }
        }
    }

    public bool ShowWaveInNav
    {
        get => _settings.ShowWaveInNav;
        set
        {
            if (_settings.ShowWaveInNav != value)
            {
                _settings.ShowWaveInNav = value;
                Save();
                NotifyChanged(nameof(ShowWaveInNav));
            }
        }
    }

    public bool ShowLikesInNav
    {
        get => _settings.ShowLikesInNav;
        set
        {
            if (_settings.ShowLikesInNav != value)
            {
                _settings.ShowLikesInNav = value;
                Save();
                NotifyChanged(nameof(ShowLikesInNav));
            }
        }
    }

    public bool ShowPlaylistsInNav
    {
        get => _settings.ShowPlaylistsInNav;
        set
        {
            if (_settings.ShowPlaylistsInNav != value)
            {
                _settings.ShowPlaylistsInNav = value;
                Save();
                NotifyChanged(nameof(ShowPlaylistsInNav));
            }
        }
    }

    public bool ShowSearchInNav
    {
        get => _settings.ShowSearchInNav;
        set
        {
            if (_settings.ShowSearchInNav != value)
            {
                _settings.ShowSearchInNav = value;
                Save();
                NotifyChanged(nameof(ShowSearchInNav));
            }
        }
    }

    public bool ShowLyricsButton
    {
        get => _settings.ShowLyricsButton;
        set
        {
            if (_settings.ShowLyricsButton != value)
            {
                _settings.ShowLyricsButton = value;
                Save();
                NotifyChanged(nameof(ShowLyricsButton));
            }
        }
    }

    public bool ShowEqualizerButton
    {
        get => _settings.ShowEqualizerButton;
        set
        {
            if (_settings.ShowEqualizerButton != value)
            {
                _settings.ShowEqualizerButton = value;
                Save();
                NotifyChanged(nameof(ShowEqualizerButton));
            }
        }
    }

    public bool ShowQualityBadge
    {
        get => _settings.ShowQualityBadge;
        set
        {
            if (_settings.ShowQualityBadge != value)
            {
                _settings.ShowQualityBadge = value;
                Save();
                NotifyChanged(nameof(ShowQualityBadge));
            }
        }
    }

    public bool CompactPlayerVolume
    {
        get => _settings.CompactPlayerVolume;
        set
        {
            if (_settings.CompactPlayerVolume != value)
            {
                _settings.CompactPlayerVolume = value;
                Save();
                NotifyChanged(nameof(CompactPlayerVolume));
            }
        }
    }

    public bool ShowPersonalPlaylistsOnHome
    {
        get => _settings.ShowPersonalPlaylistsOnHome;
        set
        {
            if (_settings.ShowPersonalPlaylistsOnHome != value)
            {
                _settings.ShowPersonalPlaylistsOnHome = value;
                Save();
                NotifyChanged(nameof(ShowPersonalPlaylistsOnHome));
            }
        }
    }

    public bool ShowNewReleasesOnHome
    {
        get => _settings.ShowNewReleasesOnHome;
        set
        {
            if (_settings.ShowNewReleasesOnHome != value)
            {
                _settings.ShowNewReleasesOnHome = value;
                Save();
                NotifyChanged(nameof(ShowNewReleasesOnHome));
            }
        }
    }

    public bool ShowEditorialPlaylistsOnHome
    {
        get => _settings.ShowEditorialPlaylistsOnHome;
        set
        {
            if (_settings.ShowEditorialPlaylistsOnHome != value)
            {
                _settings.ShowEditorialPlaylistsOnHome = value;
                Save();
                NotifyChanged(nameof(ShowEditorialPlaylistsOnHome));
            }
        }
    }

    public bool ShowChartOnHome
    {
        get => _settings.ShowChartOnHome;
        set
        {
            if (_settings.ShowChartOnHome != value)
            {
                _settings.ShowChartOnHome = value;
                Save();
                NotifyChanged(nameof(ShowChartOnHome));
            }
        }
    }

    private void Load()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    string json = File.ReadAllText(_filePath);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                    if (loaded != null)
                    {
                        _settings = loaded;
                        if (string.IsNullOrEmpty(_settings.AppFont) || _settings.AppFont == "YandexSans")
                        {
                            _settings.AppFont = "Inter";
                            Save();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SettingsService] Failed to load settings: {ex.Message}");
            }
        }
    }

    private void ApplyToServices()
    {
        try
        {
            _musicService.PreferredQuality = _settings.PreferredQuality;
            YandexBypassProxy.Instance.IsEnabled = _settings.AutoBypassProxyForYandex;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SettingsService] Failed to apply settings to services: {ex.Message}");
        }
    }

    public void Save()
    {
        lock (_lock)
        {
            try
            {
                string json = JsonSerializer.Serialize(_settings, JsonOptions);
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SettingsService] Failed to save settings: {ex.Message}");
            }
        }
    }

    private void NotifyChanged(string propName)
    {
        SettingChanged?.Invoke(this, propName);
    }
}
