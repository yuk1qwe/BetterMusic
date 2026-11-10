#nullable enable

namespace YMusic.WinUI.Models;

public enum AudioQualityPreference
{
    AacPreferred,
    Mp3Preferred,
    HighestBitrate
}

public sealed class StreamAudioQuality
{
    public string Codec { get; set; } = string.Empty;
    public int BitrateInKbps { get; set; }
    public bool IsCached { get; set; }

    public string DisplayText => string.IsNullOrWhiteSpace(Codec)
        ? string.Empty
        : $"{Codec.ToUpperInvariant()} • {BitrateInKbps} kbps{(IsCached ? " (Cached)" : string.Empty)}";

    public string BadgeText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Codec)) return "HQ";
            if (Codec.Equals("aac", StringComparison.OrdinalIgnoreCase)) return "AAC";
            if (BitrateInKbps >= 320) return "320k";
            return $"{BitrateInKbps}k";
        }
    }
}
