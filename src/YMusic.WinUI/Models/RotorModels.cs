#nullable enable

using System.Text.Json.Serialization;

namespace YMusic.WinUI.Models;

public sealed class RotorStationResult
{
    [JsonPropertyName("radioIsAvailable")]
    public bool RadioIsAvailable { get; set; }

    [JsonPropertyName("id")]
    public RotorStationId? Id { get; set; }

    [JsonPropertyName("sequence")]
    public List<RotorTrackItem>? Sequence { get; set; }

    [JsonPropertyName("batchId")]
    public string? BatchId { get; set; }
}

public sealed class RotorStationId
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("tag")]
    public string Tag { get; set; } = string.Empty;
}

public sealed class RotorTrackItem
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("track")]
    public Track? Track { get; set; }

    [JsonPropertyName("liked")]
    public bool Liked { get; set; }
}

public sealed class RotorFeedbackRequest
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = DateTime.UtcNow.ToString("o");

    [JsonPropertyName("trackId")]
    public string? TrackId { get; set; }

    [JsonPropertyName("totalPlayedSeconds")]
    public double? TotalPlayedSeconds { get; set; }

    [JsonPropertyName("batchId")]
    public string? BatchId { get; set; }
}

public sealed partial class WavePreset : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string _title = string.Empty;

    public string StationType { get; set; } = "user";
    public string StationTag { get; set; } = "onyourwave";
    public string Glyph { get; set; } = "\uE93E";

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string _description = string.Empty;

    public string GradientStart { get; set; } = "#FF1361";
    public string GradientEnd { get; set; } = "#FFF800";
}
