#nullable enable

using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using YMusic.WinUI.Converters;

namespace YMusic.WinUI.Models;

public sealed class PlaylistOwner
{
    [JsonPropertyName("uid")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long Uid { get; set; }

    [JsonPropertyName("login")]
    public string Login { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }
}

public sealed class PlaylistTrackItem
{
    [JsonPropertyName("id")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("albumId")]
    [JsonConverter(typeof(NullableStringOrNumberConverter))]
    public string? AlbumId { get; set; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    [JsonPropertyName("track")]
    public Track? Track { get; set; }
}

public sealed partial class Playlist : ObservableObject
{
    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isPinned;

    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isLiked;

    [JsonIgnore]
    public string PlaylistKey => $"{Uid}:{Kind}";

    [JsonIgnore]
    public string PinKey => $"playlist:{Uid}:{Kind}";

    [JsonIgnore]
    public string PinnedBadgeText => App.Loc?.CurrentLanguage == "en" ? "Pinned" : "Закреплено";

    [JsonIgnore]
    public string PlayActionText => App.Loc?.CurrentLanguage == "en" ? "Play" : "Слушать";

    [JsonIgnore]
    public string EditActionText => App.Loc?.CurrentLanguage == "en" ? "Edit playlist" : "Редактировать";

    [JsonIgnore]
    public string DeleteActionText => App.Loc?.CurrentLanguage == "en" ? "Delete playlist" : "Удалить";

    private long _uid;

    [JsonPropertyName("uid")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long Uid
    {
        get => _uid != 0 ? _uid : (_owner?.Uid ?? 0);
        set
        {
            _uid = value;
        }
    }

    [JsonPropertyName("kind")]
    public int Kind { get; set; }

    [JsonPropertyName("revision")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int Revision { get; set; } = 1;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("trackCount")]
    public int TrackCount { get; set; }

    [JsonPropertyName("cover")]
    public Cover? Cover { get; set; }

    private PlaylistOwner? _owner;

    [JsonPropertyName("owner")]
    public PlaylistOwner? Owner
    {
        get => _owner;
        set
        {
            _owner = value;
            if (_uid == 0 && value != null && value.Uid != 0)
            {
                _uid = value.Uid;
            }
        }
    }

    [JsonPropertyName("tracks")]
    public List<PlaylistTrackItem> Tracks { get; set; } = new();

    [JsonIgnore]
    public string OwnerName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Owner?.DisplayName)) return Owner.DisplayName;
            if (!string.IsNullOrWhiteSpace(Owner?.Name)) return Owner.Name;
            if (!string.IsNullOrWhiteSpace(Owner?.Login))
            {
                if (Owner.Login.StartsWith("yamusic", StringComparison.OrdinalIgnoreCase) ||
                    Owner.Login.Equals("music.yandex", StringComparison.OrdinalIgnoreCase))
                {
                    return App.Loc?.CurrentLanguage == "en" ? "Yandex Music" : "Яндекс Музыка";
                }
                return Owner.Login;
            }
            return App.Loc?.CurrentLanguage == "en" ? "Yandex Music" : "Яндекс Музыка";
        }
    }

    [JsonIgnore]
    public List<Track> ResolvedTracks => Tracks
        .Where(t => t.Track != null)
        .Select(t => t.Track!)
        .ToList();

    [JsonIgnore]
    public string FormattedTrackCount => Helpers.PluralizationHelper.FormatTrackCount(TrackCount);

    [JsonIgnore]
    public string? CoverUrl => GetCoverUrl(300);

    public string? GetCoverUrl(int size = 300)
    {
        return Cover?.GetFormattedUrl(size);
    }

    public override bool Equals(object? obj) =>
        obj is Playlist other && Kind == other.Kind && Uid == other.Uid;

    public override int GetHashCode() => HashCode.Combine(Uid, Kind);
}

