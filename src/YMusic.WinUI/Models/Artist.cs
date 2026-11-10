#nullable enable

using System.Text.Json.Serialization;

namespace YMusic.WinUI.Models;

public sealed class Artist
{
    [JsonPropertyName("id")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("cover")]
    public Cover? Cover { get; set; }

    [JsonPropertyName("ogImage")]
    public string? OgImage { get; set; }

    [JsonPropertyName("genres")]
    public List<string>? Genres { get; set; }

    [JsonPropertyName("likesCount")]
    public long? LikesCount { get; set; }

    [JsonPropertyName("counts")]
    public ArtistCounts? Counts { get; set; }

    [JsonPropertyName("description")]
    public ArtistDescription? Description { get; set; }

    [JsonIgnore]
    public string? AvatarUrl
    {
        get
        {
            if (Cover != null)
            {
                var formatted = Cover.GetFormattedUrl(300);
                if (!string.IsNullOrWhiteSpace(formatted)) return formatted;
            }

            if (!string.IsNullOrWhiteSpace(OgImage))
            {
                string formatted = OgImage.Replace("%%", "300x300");
                if (!formatted.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !formatted.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    formatted = "https://" + formatted;
                }
                return formatted;
            }

            return null;
        }
    }

    [JsonIgnore]
    public string FormattedGenres => Genres != null && Genres.Count > 0 ? string.Join(", ", Genres) : string.Empty;

    [JsonIgnore]
    public string GenresString => !string.IsNullOrWhiteSpace(FormattedGenres) ? FormattedGenres : (App.Loc?.CurrentLanguage == "en" ? "Artist" : "Исполнитель");

    [JsonIgnore]
    public string SubtitleText => App.Loc?.CurrentLanguage == "en" ? "Artist" : "Артист";
}

public sealed class ArtistCounts
{
    [JsonPropertyName("tracks")]
    public int Tracks { get; set; }

    [JsonPropertyName("directAlbums")]
    public int DirectAlbums { get; set; }

    [JsonPropertyName("alsoAlbums")]
    public int AlsoAlbums { get; set; }

    [JsonPropertyName("alsoTracks")]
    public int AlsoTracks { get; set; }
}

public sealed class ArtistDescription
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("uri")]
    public string? Uri { get; set; }
}

public sealed class ArtistStats
{
    [JsonPropertyName("lastMonthListeners")]
    public long? LastMonthListeners { get; set; }

    [JsonPropertyName("lastMonthListenersDelta")]
    public long? LastMonthListenersDelta { get; set; }

    [JsonIgnore]
    public string FormattedListeners => FormatListeners(LastMonthListeners, App.Loc?.CurrentLanguage == "en");

    public static string FormatListeners(long? listeners, bool isEn)
    {
        if (listeners == null) return string.Empty;
        if (isEn)
        {
            return listeners.Value switch
            {
                >= 1_000_000 => $"{listeners.Value / 1_000_000.0:0.#}M monthly listeners",
                >= 1_000 => $"{listeners.Value / 1_000.0:0.#}K monthly listeners",
                _ => $"{listeners.Value:N0} monthly listeners"
            };
        }
        return listeners.Value switch
        {
            >= 1_000_000 => $"{listeners.Value / 1_000_000.0:0.#} млн слушателей в месяц",
            >= 1_000 => $"{listeners.Value / 1_000.0:0.#} тыс. слушателей в месяц",
            _ => $"{listeners.Value:N0} слушателей в месяц"
        };
    }
}

public sealed class ArtistBriefInfoResult
{
    [JsonPropertyName("artist")]
    public Artist? Artist { get; set; }

    [JsonPropertyName("albums")]
    public List<Album>? Albums { get; set; }

    [JsonPropertyName("popularTracks")]
    public List<Track>? PopularTracks { get; set; }

    [JsonPropertyName("stats")]
    public ArtistStats? Stats { get; set; }

    [JsonPropertyName("upcomingAlbum")]
    public Album? UpcomingAlbum { get; set; }

    [JsonPropertyName("upcomingAlbums")]
    public List<Album>? UpcomingAlbums { get; set; }
}

public sealed class ArtistAlbumsResponse
{
    [JsonPropertyName("albums")]
    public List<Album>? Albums { get; set; }

    [JsonPropertyName("pager")]
    public PagerInfo? Pager { get; set; }
}

public sealed class PagerInfo
{
    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("perPage")]
    public int PerPage { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }
}
