#nullable enable

using YMusic.WinUI.Models;

namespace YMusic.WinUI.Services.Interfaces;

public interface ISettingsService
{
    AudioQualityPreference PreferredQuality { get; set; }
    bool GaplessPreloadEnabled { get; set; }
    bool DiscordRpcEnabled { get; set; }
    bool MinimizeToTrayOnClose { get; set; }
    bool TrackNotificationsEnabled { get; set; }
    bool FckCensorEnabled { get; set; }
    bool SloplessEnabled { get; set; }
    string SloplessBehavior { get; set; }
    string SloplessDetectionMode { get; set; }
    int SloplessArtistThreshold { get; set; }
    bool SloplessShowTrackLabels { get; set; }
    bool SloplessShowArtistLabels { get; set; }
    bool FullVisualEffects { get; set; }
    bool ExternalLyricsEnabled { get; set; }
    string StartupScreen { get; set; }
    string AppTheme { get; set; }
    string AccentColorHex { get; set; }
    string BackdropKind { get; set; }
    string AppFont { get; set; }
    string Language { get; set; }
    bool AudioCacheEnabled { get; set; }
    string AudioCacheLocation { get; set; }
    int CacheSizeLimitMb { get; set; }

    bool ObsOverlayEnabled { get; set; }
    int ObsOverlayPort { get; set; }
    string ObsOverlayTheme { get; set; }
    bool ObsOverlayHideOnPause { get; set; }
    bool AutoplayWaveOnAlbumEnd { get; set; }
    bool AutoBypassProxyForYandex { get; set; }
    bool CrossfadeEnabled { get; set; }
    int CrossfadeDurationSeconds { get; set; }

    bool GlobalHotkeysEnabled { get; set; }
    bool GameOverlayEnabled { get; set; }
    bool ShowOverlayOnTrackChange { get; set; }
    int GameOverlayDurationSeconds { get; set; }
    int GameOverlayPosition { get; set; }

    bool ShowWaveInNav { get; set; }
    bool ShowLikesInNav { get; set; }
    bool ShowPlaylistsInNav { get; set; }
    bool ShowSearchInNav { get; set; }

    bool ShowLyricsButton { get; set; }
    bool ShowEqualizerButton { get; set; }
    bool ShowQualityBadge { get; set; }
    bool CompactPlayerVolume { get; set; }

    bool ShowPersonalPlaylistsOnHome { get; set; }
    bool ShowNewReleasesOnHome { get; set; }
    bool ShowEditorialPlaylistsOnHome { get; set; }
    bool ShowChartOnHome { get; set; }

    event EventHandler<string>? SettingChanged;

    void Save();
}
