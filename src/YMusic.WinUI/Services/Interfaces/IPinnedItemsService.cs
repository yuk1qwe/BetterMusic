#nullable enable

using System.Text.Json.Serialization;
using YMusic.WinUI.Models;

namespace YMusic.WinUI.Services.Interfaces;

public sealed class PinnedItemRecord
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "playlist"; // "playlist" or "album"

    [JsonPropertyName("uid")]
    public long Uid { get; set; }

    [JsonPropertyName("kind")]
    public int Kind { get; set; }

    [JsonPropertyName("albumId")]
    public long AlbumId { get; set; }

    [JsonPropertyName("coverUrl")]
    public string? CoverUrl { get; set; }

    [JsonPropertyName("pinnedAt")]
    public DateTime PinnedAt { get; set; } = DateTime.UtcNow;
}

public interface IPinnedItemsService
{
    bool IsPlaylistPinned(long uid, int kind);
    bool IsAlbumPinned(long albumId);
    bool IsPinned(string pinKey);

    void PinPlaylist(long uid, int kind, string? title = null, string? coverUrl = null);
    void UnpinPlaylist(long uid, int kind);
    bool TogglePlaylistPin(long uid, int kind, string? title = null, string? coverUrl = null);

    void PinAlbum(long albumId, string? title = null, string? coverUrl = null);
    void UnpinAlbum(long albumId);
    bool ToggleAlbumPin(long albumId, string? title = null, string? coverUrl = null);

    void MovePinnedItem(string key, int targetIndex);
    void MovePinnedItemUp(string key);
    void MovePinnedItemDown(string key);

    IReadOnlyList<PinnedItemRecord> GetPinnedItems();
    IReadOnlyList<string> GetPinnedKeys();
    int GetPinOrder(string pinKey);

    event EventHandler<string>? PinStateChanged;
}
