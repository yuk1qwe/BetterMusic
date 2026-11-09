#nullable enable

using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace YMusic.WinUI.Models;

public sealed class TrackLyricsResult
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("downloadUrl")]
    public string? DownloadUrl { get; set; }

    [JsonPropertyName("lyrics")]
    public string? Lyrics { get; set; }

    [JsonPropertyName("fullLyrics")]
    public string? FullLyrics { get; set; }

    [JsonPropertyName("hasRights")]
    public bool HasRights { get; set; }

    [JsonPropertyName("showTranslation")]
    public bool ShowTranslation { get; set; }

    [JsonPropertyName("textLanguage")]
    public string? TextLanguage { get; set; }
}

public sealed class TrackSupplementResult
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("lyrics")]
    public TrackLyricsResult? Lyrics { get; set; }
}

public sealed class TrackLyricsInfo
{
    [JsonPropertyName("hasAvailableSyncLyrics")]
    public bool HasAvailableSyncLyrics { get; set; }

    [JsonPropertyName("hasAvailableTextLyrics")]
    public bool HasAvailableTextLyrics { get; set; }
}

public sealed class LrclibResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("trackName")]
    public string? TrackName { get; set; }

    [JsonPropertyName("artistName")]
    public string? ArtistName { get; set; }

    [JsonPropertyName("albumName")]
    public string? AlbumName { get; set; }

    [JsonPropertyName("duration")]
    public double Duration { get; set; }

    [JsonPropertyName("instrumental")]
    public bool Instrumental { get; set; }

    [JsonPropertyName("plainLyrics")]
    public string? PlainLyrics { get; set; }

    [JsonPropertyName("syncedLyrics")]
    public string? SyncedLyrics { get; set; }
}

public sealed partial class LyricLine : ObservableObject
{
    public TimeSpan Timestamp { get; set; }
    public string Text { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedTimestamp))]
    [NotifyPropertyChangedFor(nameof(EffectiveTimestamp))]
    private double _offsetSeconds;

    public TimeSpan EffectiveTimestamp => HasTimestamp
        ? (Timestamp + TimeSpan.FromSeconds(OffsetSeconds) < TimeSpan.Zero ? TimeSpan.Zero : Timestamp + TimeSpan.FromSeconds(OffsetSeconds))
        : Timestamp;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineOpacity))]
    [NotifyPropertyChangedFor(nameof(LineFontSize))]
    [NotifyPropertyChangedFor(nameof(LineFontWeight))]
    private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineOpacity))]
    private bool _isPast;

    public bool HasTimestamp => Timestamp >= TimeSpan.Zero;

    public string FormattedTimestamp => HasTimestamp
        ? (EffectiveTimestamp.Hours > 0 ? EffectiveTimestamp.ToString(@"h\:mm\:ss") : EffectiveTimestamp.ToString(@"m\:ss"))
        : string.Empty;

    public double LineOpacity => !HasTimestamp ? 0.88 : (IsActive ? 1.0 : (IsPast ? 0.35 : 0.65));

    public double LineFontSize => IsActive ? 32.0 : 24.0;

    public Windows.UI.Text.FontWeight LineFontWeight => IsActive
        ? Microsoft.UI.Text.FontWeights.ExtraBold
        : Microsoft.UI.Text.FontWeights.SemiBold;

    public static List<LyricLine> ParseLrc(string rawLrc, double durationSeconds = 0)
    {
        var result = new List<LyricLine>();
        if (string.IsNullOrWhiteSpace(rawLrc))
        {
            return result;
        }

        // Standard LRC timestamp regex: [mm:ss.xx] or [mm:ss] or [mm:ss.xxx] (accepts both . and , as decimal separator)
        var tagRegex = new Regex(@"\[(\d{1,3}):(\d{2})(?:[\.,](\d{1,3}))?\]", RegexOptions.Compiled);

        bool hasAnyTimestampTags = tagRegex.IsMatch(rawLrc);

        if (!hasAnyTimestampTags)
        {
            // Pure text lyrics without any LRC timestamps.
            // DO NOT fabricate fake linear timestamps, as that causes lyrics to desynchronize by 20-30 seconds!
            var plainLines = rawLrc.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            foreach (var line in plainLines)
            {
                string trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                // Ignore metadata tags like [ar:...], [ti:...] even in plain text
                if (trimmed.StartsWith("[") && trimmed.EndsWith("]") && trimmed.Contains(":"))
                {
                    continue;
                }

                result.Add(new LyricLine
                {
                    Timestamp = TimeSpan.FromSeconds(-1),
                    Text = trimmed
                });
            }
            return result;
        }

        // Check for LRC global offset tag: [offset:+/-ms]
        // In LRC spec: positive offset = delay (add ms), negative offset = advance (subtract ms)
        int offsetMs = 0;
        var offsetMatch = Regex.Match(rawLrc, @"\[offset:\s*([+-]?\d+)\s*\]", RegexOptions.IgnoreCase);
        if (offsetMatch.Success && int.TryParse(offsetMatch.Groups[1].Value, out int parsedOffset))
        {
            offsetMs = parsedOffset;
        }

        var lines = rawLrc.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        foreach (var line in lines)
        {
            var matches = tagRegex.Matches(line);
            if (matches.Count == 0)
            {
                // In a synchronized LRC, ignore non-timestamp lines (such as metadata [ar:...] or blank comments)
                continue;
            }

            // Extract the actual lyric text by stripping all timestamps
            var text = tagRegex.Replace(line, string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            foreach (Match match in matches)
            {
                int minutes = int.Parse(match.Groups[1].Value);
                int seconds = int.Parse(match.Groups[2].Value);
                int millis = 0;
                if (match.Groups[3].Success)
                {
                    string msStr = match.Groups[3].Value;
                    if (msStr.Length == 1) millis = int.Parse(msStr) * 100;
                    else if (msStr.Length == 2) millis = int.Parse(msStr) * 10;
                    else millis = int.Parse(msStr.Substring(0, 3));
                }

                double totalMs = (minutes * 60000.0) + (seconds * 1000.0) + millis + offsetMs;
                if (totalMs < 0) totalMs = 0;

                var timestamp = TimeSpan.FromMilliseconds(totalMs);
                result.Add(new LyricLine
                {
                    Timestamp = timestamp,
                    Text = text
                });
            }
        }

        result.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

        if (result.Count > 0 && result.All(line =>
        {
            string t = line.Text.Trim().Trim('[', ']', '(', ')', '{', '}', '*', '-', ' ', '♪', '•').ToLowerInvariant();
            return t == "instrumental" || t == "инструментал" || t == "this song is an instrumental" || t == "lyrics not available" || t == "no lyrics";
        }))
        {
            return new List<LyricLine>();
        }

        return result;
    }
}
