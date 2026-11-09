#nullable enable

using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace YMusic.WinUI.Models;

public sealed class Cover
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("uri")]
    public string? Uri { get; set; }

    [JsonPropertyName("itemsUri")]
    public string[]? ItemsUri { get; set; }

    public string? GetFormattedUrl(int size = 200)
    {
        string? rawUri = Uri;
        if (string.IsNullOrWhiteSpace(rawUri) && ItemsUri != null && ItemsUri.Length > 0)
        {
            rawUri = ItemsUri[0];
        }

        if (string.IsNullOrWhiteSpace(rawUri))
        {
            return null;
        }

        string formatted = rawUri.Replace("%%", $"{size}x{size}");
        if (!formatted.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !formatted.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            formatted = "https://" + formatted;
        }

        return formatted;
    }
}

public sealed partial class Album : ObservableObject
{
    [ObservableProperty]
    [property: JsonIgnore]
    [NotifyPropertyChangedFor(nameof(PlayActionText))]
    [NotifyPropertyChangedFor(nameof(LikeActionText))]
    [NotifyPropertyChangedFor(nameof(ActionGlyph))]
    [NotifyPropertyChangedFor(nameof(ActionTooltip))]
    private bool _isLiked;

    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isPinned;

    [JsonIgnore]
    public string PinKey => $"album:{Id}";

    [JsonIgnore]
    public string PinnedBadgeText => App.Loc?.CurrentLanguage == "en" ? "Pinned" : "Закреплено";

    [JsonPropertyName("available")]
    public bool Available { get; set; } = true;

    [JsonPropertyName("isPreorder")]
    public bool? IsPreorder { get; set; }

    [JsonPropertyName("preorder")]
    public bool? Preorder { get; set; }

    [JsonPropertyName("availablePartially")]
    public bool? AvailablePartially { get; set; }

    [JsonPropertyName("millisecondsUntilRelease")]
    public long? MillisecondsUntilRelease { get; set; }

    [JsonPropertyName("presaveDate")]
    public string? PresaveDate { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonIgnore]
    public bool IsNotFound => !string.IsNullOrEmpty(Error) && string.Equals(Error, "not-found", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsPreSave
    {
        get
        {
            if (IsPreorder == true || Preorder == true)
                return true;

            if (MillisecondsUntilRelease.HasValue && MillisecondsUntilRelease.Value > 0)
                return true;

            if (!string.IsNullOrWhiteSpace(ReleaseDate))
            {
                if (DateTimeOffset.TryParse(ReleaseDate, out var dto))
                {
                    if (dto > DateTimeOffset.UtcNow) return true;
                }
                else if (DateTime.TryParse(ReleaseDate, out var dt))
                {
                    if (dt > DateTime.UtcNow) return true;
                }
            }

            if (Year.HasValue && Year.Value > DateTime.UtcNow.Year)
            {
                return true;
            }

            return false;
        }
    }

    [JsonIgnore]
    public bool HasPlayableTracks
    {
        get
        {
            if (Volumes == null || Volumes.Count == 0)
            {
                return !IsPreSave || (AvailablePartially == true);
            }
            return AllTracks.Any(t => t.Available);
        }
    }

    [JsonIgnore]
    public string PreSaveBadgeText => App.Loc?.CurrentLanguage == "en" ? "PRE-SAVE" : "ПРЕСЕЙВ";

    [JsonIgnore]
    public string FormattedReleaseDate
    {
        get
        {
            bool isEn = false;
            try { isEn = App.Loc?.CurrentLanguage == "en"; } catch { }

            DateTime? exactDate = null;
            if (!string.IsNullOrWhiteSpace(ReleaseDate))
            {
                if (DateTimeOffset.TryParse(ReleaseDate, out var dto))
                {
                    exactDate = dto.LocalDateTime;
                }
                else if (DateTime.TryParse(ReleaseDate, out var dt))
                {
                    exactDate = dt;
                }
            }

            if (exactDate.HasValue)
            {
                var dt = exactDate.Value;
                int days = (int)Math.Ceiling((dt.Date - DateTime.Today).TotalDays);
                if (days < 0)
                {
                    return dt.ToString(isEn ? "MMM d, yyyy" : "d MMMM yyyy");
                }
                if (days == 0)
                {
                    return isEn ? "Releases today" : "Релиз сегодня";
                }
                if (days == 1)
                {
                    return isEn ? "Releases tomorrow" : "Релиз завтра";
                }
                if (days <= 30)
                {
                    string dateStr = dt.ToString(isEn ? "MMM d" : "d MMMM");
                    return isEn ? $"Release: {dateStr} ({days} days left)" : $"Релиз: {dateStr} (через {days} дн.)";
                }

                return (isEn ? "Release: " : "Релиз: ") + dt.ToString(isEn ? "MMM d, yyyy" : "d MMMM yyyy");
            }

            if (Year.HasValue)
            {
                return (isEn ? "Release in " : "Релиз в ") + Year.Value;
            }

            return isEn ? "Coming soon" : "Скоро выйдет";
        }
    }

    [JsonIgnore]
    public string PlayActionText
    {
        get
        {
            bool isEn = false;
            try { isEn = App.Loc?.CurrentLanguage == "en"; } catch { }

            if (IsPreSave)
            {
                if (HasPlayableTracks)
                {
                    return isEn ? "Play Singles" : "Слушать синглы";
                }
                return IsLiked
                    ? (isEn ? "Pre-save Added ✓" : "Пресейв добавлен ✓")
                    : (isEn ? "Pre-save Album" : "Предсохранить альбом");
            }

            return isEn ? "Play" : "Слушать";
        }
    }

    [JsonIgnore]
    public string LikeActionText
    {
        get
        {
            bool isEn = false;
            try { isEn = App.Loc?.CurrentLanguage == "en"; } catch { }

            if (IsPreSave)
            {
                return IsLiked
                    ? (isEn ? "Remove from Pre-saves" : "Удалить из пресейвов")
                    : (isEn ? "Pre-save Album" : "Предсохранить альбом");
            }

            return IsLiked
                ? (isEn ? "Remove from Favorites" : "Удалить из избранного")
                : (isEn ? "Add to Favorites" : "Добавить в избранное");
        }
    }

    [JsonIgnore]
    public string ActionGlyph => (IsPreSave && !HasPlayableTracks)
        ? (IsLiked ? "\uE73E" : "\uEA8F")
        : "\uE768";

    [JsonIgnore]
    public string ActionTooltip => (IsPreSave && !HasPlayableTracks)
        ? (IsLiked
            ? (App.Loc?.CurrentLanguage == "en" ? "Pre-save added (click to cancel)" : "Пресейв добавлен (нажмите для отмены)")
            : (App.Loc?.CurrentLanguage == "en" ? "Pre-save album" : "Предсохранить альбом"))
        : PlayActionText;

    [JsonPropertyName("id")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("year")]
    public int? Year { get; set; }

    [JsonPropertyName("releaseDate")]
    public string? ReleaseDate { get; set; }

    [JsonIgnore]
    public DateTime? ParsedReleaseDate
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ReleaseDate) && DateTime.TryParse(ReleaseDate, out var dt))
            {
                return dt;
            }
            if (Year.HasValue && Year.Value > 0)
            {
                return new DateTime(Year.Value, 1, 1);
            }
            return null;
        }
    }

    [JsonIgnore]
    public static Func<long, YMusic.WinUI.Services.Interfaces.AlbumSpoof?>? AlbumSpoofGetter { get; set; }

    [JsonPropertyName("coverUri")]
    public string? CoverUri
    {
        get
        {
            var spoof = AlbumSpoofGetter?.Invoke(Id);
            if (!string.IsNullOrEmpty(spoof?.CoverUri))
            {
                return spoof.CoverUri;
            }
            return _coverUri;
        }
        set => _coverUri = value;
    }
    private string? _coverUri;

    [JsonPropertyName("artists")]
    public List<Artist> Artists { get; set; } = new();

    [JsonPropertyName("trackCount")]
    public int TrackCount { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("metaType")]
    public string? MetaType { get; set; }

    [JsonPropertyName("genre")]
    public string? Genre { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("volumes")]
    public List<List<Track>>? Volumes { get; set; }

    [JsonIgnore]
    public string ArtistNames => Artists.Count > 0
        ? string.Join(", ", Artists.Select(a => a.Name))
        : string.Empty;

    [JsonIgnore]
    public string FormattedTrackCount
    {
        get
        {
            if (string.Equals(Type, "single", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(MetaType, "single", StringComparison.OrdinalIgnoreCase))
            {
                bool isEn = false;
                try { isEn = App.Loc?.CurrentLanguage == "en"; } catch { }
                return isEn ? "Single" : "Сингл";
            }

            if (IsPreSave && TrackCount <= 0)
            {
                return string.Empty;
            }

            return Helpers.PluralizationHelper.FormatTrackCount(TrackCount);
        }
    }

    [JsonIgnore]
    public string FormattedYearAndTracks => Year.HasValue ? $"{Year.Value} • {FormattedTrackCount}" : FormattedTrackCount;

    [JsonIgnore]
    public string? CoverUrl => GetCoverUrl(300);

    [JsonIgnore]
    public List<Track> AllTracks => Volumes != null
        ? Volumes.SelectMany(v => v).ToList()
        : new List<Track>();

    public string? GetCoverUrl(int size = 200)
    {
        if (string.IsNullOrWhiteSpace(CoverUri))
        {
            return null;
        }

        string formatted = CoverUri.Replace("%%", $"{size}x{size}");
        if (!formatted.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !formatted.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            formatted = "https://" + formatted;
        }

        return formatted;
    }
}

public sealed class PresavesResult
{
    [JsonPropertyName("upcomingAlbums")]
    public List<Album>? UpcomingAlbums { get; set; }

    [JsonPropertyName("releasedAlbums")]
    public List<Album>? ReleasedAlbums { get; set; }
}


