#nullable enable

using System.Text.Json.Serialization;

namespace YMusic.WinUI.Models;

public sealed class PlaybackSessionState
{
    [JsonPropertyName("volume")]
    public double Volume { get; set; } = 80.0;

    [JsonPropertyName("isMuted")]
    public bool IsMuted { get; set; }

    [JsonPropertyName("repeatMode")]
    public RepeatMode RepeatMode { get; set; } = RepeatMode.None;

    [JsonPropertyName("isShuffle")]
    public bool IsShuffle { get; set; }

    [JsonPropertyName("playbackMode")]
    public PlaybackMode PlaybackMode { get; set; } = PlaybackMode.None;

    [JsonPropertyName("orderIndex")]
    public int OrderIndex { get; set; } = -1;

    [JsonPropertyName("positionSeconds")]
    public double PositionSeconds { get; set; }

    [JsonPropertyName("currentTrack")]
    public Track? CurrentTrack { get; set; }

    [JsonPropertyName("queue")]
    public List<Track> Queue { get; set; } = new();

    [JsonPropertyName("playOrder")]
    public List<int> PlayOrder { get; set; } = new();
}
