#nullable enable

using System.Text.Json.Serialization;
using YMusic.WinUI.Converters;

namespace YMusic.WinUI.Models;

public sealed class DownloadInfo
{
    [JsonPropertyName("codec")]
    public string Codec { get; set; } = string.Empty;

    [JsonPropertyName("bitrateInKbps")]
    public int BitrateInKbps { get; set; }

    [JsonPropertyName("gain")]
    public bool Gain { get; set; }

    [JsonPropertyName("preview")]
    public bool Preview { get; set; }

    [JsonPropertyName("downloadInfoUrl")]
    public string DownloadInfoUrl { get; set; } = string.Empty;

    [JsonPropertyName("direct")]
    public bool Direct { get; set; }
}

public sealed class LikedTracksLibrary
{
    [JsonPropertyName("uid")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long Uid { get; set; }

    [JsonPropertyName("tracks")]
    public List<LikedTrackItem> Tracks { get; set; } = new();
}

public sealed class LikedTrackItem
{
    [JsonPropertyName("id")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("albumId")]
    [JsonConverter(typeof(NullableStringOrNumberConverter))]
    public string? AlbumId { get; set; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }
}

public sealed class LikedTracksResult
{
    [JsonPropertyName("library")]
    public LikedTracksLibrary? Library { get; set; }

    [JsonPropertyName("tracks")]
    public List<LikedTrackItem>? Tracks { get; set; }
}
