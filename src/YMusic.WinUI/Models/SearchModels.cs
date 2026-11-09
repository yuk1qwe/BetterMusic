#nullable enable

using System.Text.Json;
using System.Text.Json.Serialization;

namespace YMusic.WinUI.Models;

public sealed class SearchItemContainer<T>
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("perPage")]
    public int PerPage { get; set; }

    [JsonPropertyName("order")]
    public int Order { get; set; }

    [JsonPropertyName("results")]
    public List<T> Results { get; set; } = new();
}

public sealed class SearchBestResult
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty; // "artist", "track", "album", "playlist"

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("result")]
    public JsonElement ResultElement { get; set; }

    // Strongly-typed deserialized models
    public Artist? Artist { get; set; }
    public Track? Track { get; set; }
    public Album? Album { get; set; }
    public Playlist? Playlist { get; set; }

    public string Title => Type switch
    {
        "artist" => Artist?.Name ?? string.Empty,
        "track" => Track?.FullTitle ?? string.Empty,
        "album" => Album?.Title ?? string.Empty,
        "playlist" => Playlist?.Title ?? string.Empty,
        _ => string.Empty
    };

    public string Subtitle => Type switch
    {
        "artist" => !string.IsNullOrWhiteSpace(Artist?.GenresString) ? Artist.GenresString : (App.Loc?.CurrentLanguage == "en" ? "Artist" : "Артист"),
        "track" => Track?.ArtistNames ?? (App.Loc?.CurrentLanguage == "en" ? "Track" : "Трек"),
        "album" => Album?.IsPreSave == true
            ? (!string.IsNullOrWhiteSpace(Album.ArtistNames) ? $"{Album.ArtistNames} • {Album.FormattedReleaseDate}" : Album.FormattedReleaseDate)
            : (Album?.ArtistNames ?? (App.Loc?.CurrentLanguage == "en" ? "Album" : "Альбом")),
        "playlist" => Playlist?.OwnerName ?? (App.Loc?.CurrentLanguage == "en" ? "Playlist" : "Плейлист"),
        _ => string.Empty
    };

    public string? CoverUrl => Type switch
    {
        "artist" => Artist?.AvatarUrl,
        "track" => Track?.CoverUrl,
        "album" => Album?.CoverUrl,
        "playlist" => Playlist?.CoverUrl,
        _ => null
    };

    public string TypeBadge => App.Loc?.CurrentLanguage == "en"
        ? (Type switch
        {
            "artist" => "ARTIST",
            "track" => "TRACK",
            "album" => (Album?.IsPreSave == true ? "PRE-SAVE" : "ALBUM"),
            "playlist" => "PLAYLIST",
            _ => Type.ToUpperInvariant()
        })
        : (Type switch
        {
            "artist" => "АРТИСТ",
            "track" => "ТРЕК",
            "album" => (Album?.IsPreSave == true ? "ПРЕСЕЙВ" : "АЛЬБОМ"),
            "playlist" => "ПЛЕЙЛИСТ",
            _ => Type.ToUpperInvariant()
        });
}

public sealed class SearchResult
{
    [JsonPropertyName("searchRequestId")]
    public string? SearchRequestId { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("misspellCorrected")]
    public bool? MisspellCorrected { get; set; }

    [JsonPropertyName("nocorrect")]
    public bool? NoCorrect { get; set; }

    [JsonPropertyName("best")]
    public SearchBestResult? Best { get; set; }

    [JsonPropertyName("tracks")]
    public SearchItemContainer<Track>? Tracks { get; set; }

    [JsonPropertyName("albums")]
    public SearchItemContainer<Album>? Albums { get; set; }

    [JsonPropertyName("artists")]
    public SearchItemContainer<Artist>? Artists { get; set; }

    [JsonPropertyName("playlists")]
    public SearchItemContainer<Playlist>? Playlists { get; set; }
}

public sealed class SearchPreviewCoverItem : IEquatable<SearchPreviewCoverItem>
{
    public string? CoverUrl { get; set; }
    public string Title { get; set; } = string.Empty;

    public bool Equals(SearchPreviewCoverItem? other)
    {
        if (other is null) return false;
        return string.Equals(CoverUrl, other.CoverUrl, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(Title, other.Title, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj) => obj is SearchPreviewCoverItem item && Equals(item);

    public override int GetHashCode() => HashCode.Combine(CoverUrl?.ToLowerInvariant(), Title);
}
