#nullable enable

using System.Text.Json.Serialization;

namespace YMusic.WinUI.Models;

public sealed class AppSettings
{
    [JsonPropertyName("preferredQuality")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AudioQualityPreference PreferredQuality { get; set; } = AudioQualityPreference.AacPreferred;

    [JsonPropertyName("gaplessPreloadEnabled")]
    public bool GaplessPreloadEnabled { get; set; } = true;

    [JsonPropertyName("discordRpcEnabled")]
    public bool DiscordRpcEnabled { get; set; } = false;

    [JsonPropertyName("minimizeToTrayOnClose")]
    public bool MinimizeToTrayOnClose { get; set; } = true;

    [JsonPropertyName("trackNotificationsEnabled")]
    public bool TrackNotificationsEnabled { get; set; } = true;

    [JsonPropertyName("fckCensorEnabled")]
    public bool FckCensorEnabled { get; set; } = false;

    [JsonPropertyName("sloplessEnabled")]
    public bool SloplessEnabled { get; set; } = false;

    [JsonPropertyName("sloplessBehavior")]
    public string SloplessBehavior { get; set; } = "dislike"; // "dislike", "dislike_if_not_liked", "skip", "skip_if_not_liked", "nothing", "like"

    [JsonPropertyName("sloplessDetectionMode")]
    public string SloplessDetectionMode { get; set; } = "balance"; // "balance", "paranoid", "track_only"

    [JsonPropertyName("sloplessArtistThreshold")]
    public int SloplessArtistThreshold { get; set; } = 10; // percent 1..100

    [JsonPropertyName("sloplessShowTrackLabels")]
    public bool SloplessShowTrackLabels { get; set; } = true;

    [JsonPropertyName("sloplessShowArtistLabels")]
    public bool SloplessShowArtistLabels { get; set; } = true;

    [JsonPropertyName("appTheme")]
    public string AppTheme { get; set; } = "Dark"; // "Default", "Light", "Dark", "OLED", "Midnight", "Sepia"

    [JsonPropertyName("accentColorHex")]
    public string AccentColorHex { get; set; } = "#FFED71B1";

    [JsonPropertyName("backdropKind")]
    public string BackdropKind { get; set; } = "Solid"; // "Mica", "MicaAlt", "Acrylic", "Solid"

    [JsonPropertyName("appFont")]
    public string AppFont { get; set; } = "Inter"; // "Inter", "System", "YandexSans"

    [JsonPropertyName("language")]
    public string Language { get; set; } = "ru"; // "ru" or "en"

    [JsonPropertyName("audioCacheEnabled")]
    public bool AudioCacheEnabled { get; set; } = true;

    [JsonPropertyName("audioCacheLocation")]
    public string AudioCacheLocation { get; set; } = string.Empty;

    [JsonPropertyName("cacheSizeLimitMb")]
    public int CacheSizeLimitMb { get; set; } = 1024;

    [JsonPropertyName("obsOverlayEnabled")]
    public bool ObsOverlayEnabled { get; set; } = false;

    [JsonPropertyName("obsOverlayPort")]
    public int ObsOverlayPort { get; set; } = 21337;

    [JsonPropertyName("obsOverlayTheme")]
    public string ObsOverlayTheme { get; set; } = "glass"; // "glass", "pill", "vinyl"

    [JsonPropertyName("obsOverlayHideOnPause")]
    public bool ObsOverlayHideOnPause { get; set; } = false;

    [JsonPropertyName("autoplayWaveOnAlbumEnd")]
    public bool AutoplayWaveOnAlbumEnd { get; set; } = true;

    [JsonPropertyName("autoBypassProxyForYandex")]
    public bool AutoBypassProxyForYandex { get; set; } = true;

    [JsonPropertyName("crossfadeEnabled")]
    public bool CrossfadeEnabled { get; set; } = true;

    [JsonPropertyName("crossfadeDurationSeconds")]
    public int CrossfadeDurationSeconds { get; set; } = 4;

    [JsonPropertyName("globalHotkeysEnabled")]
    public bool GlobalHotkeysEnabled { get; set; } = true;

    [JsonPropertyName("gameOverlayEnabled")]
    public bool GameOverlayEnabled { get; set; } = true;

    [JsonPropertyName("showOverlayOnTrackChange")]
    public bool ShowOverlayOnTrackChange { get; set; } = true;

    [JsonPropertyName("gameOverlayDurationSeconds")]
    public int GameOverlayDurationSeconds { get; set; } = 3;

    [JsonPropertyName("gameOverlayPosition")]
    public int GameOverlayPosition { get; set; } = 0; // 0 = TopRight, 1 = TopLeft, 2 = BottomRight, 3 = BottomLeft

    // Modular UI settings
    [JsonPropertyName("showWaveInNav")]
    public bool ShowWaveInNav { get; set; } = true;

    [JsonPropertyName("showLikesInNav")]
    public bool ShowLikesInNav { get; set; } = true;

    [JsonPropertyName("showPlaylistsInNav")]
    public bool ShowPlaylistsInNav { get; set; } = true;

    [JsonPropertyName("showSearchInNav")]
    public bool ShowSearchInNav { get; set; } = true;

    [JsonPropertyName("showLyricsButton")]
    public bool ShowLyricsButton { get; set; } = true;

    [JsonPropertyName("showEqualizerButton")]
    public bool ShowEqualizerButton { get; set; } = true;

    [JsonPropertyName("showQualityBadge")]
    public bool ShowQualityBadge { get; set; } = true;

    [JsonPropertyName("compactPlayerVolume")]
    public bool CompactPlayerVolume { get; set; } = false;

    // Home / Explore Page Modular settings
    [JsonPropertyName("showPersonalPlaylistsOnHome")]
    public bool ShowPersonalPlaylistsOnHome { get; set; } = true;

    [JsonPropertyName("showNewReleasesOnHome")]
    public bool ShowNewReleasesOnHome { get; set; } = true;

    [JsonPropertyName("showEditorialPlaylistsOnHome")]
    public bool ShowEditorialPlaylistsOnHome { get; set; } = true;

    [JsonPropertyName("showChartOnHome")]
    public bool ShowChartOnHome { get; set; } = true;
}
