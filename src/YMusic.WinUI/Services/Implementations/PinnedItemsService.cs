#nullable enable

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.Services.Implementations;

public sealed class PinnedItemsData
{
    [JsonPropertyName("items")]
    public List<PinnedItemRecord> Items { get; set; } = new();
}

public sealed class PinnedItemsService : IPinnedItemsService
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private readonly List<PinnedItemRecord> _records = new();

    public event EventHandler<string>? PinStateChanged;

    public PinnedItemsService()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string dir = Path.Combine(appData, "YMusic");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "pinned_items.json");

        Load();
    }

    private void Load()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    string json = File.ReadAllText(_filePath);
                    var data = JsonSerializer.Deserialize<PinnedItemsData>(json);
                    if (data?.Items != null)
                    {
                        _records.Clear();
                        _records.AddRange(data.Items);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PinnedItemsService] Error loading pinned items: {ex.Message}");
            }
        }
    }

    private void Save()
    {
        List<PinnedItemRecord> snapshot;
        lock (_lock)
        {
            snapshot = _records.ToList();
        }

        _ = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var data = new PinnedItemsData { Items = snapshot };
                string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PinnedItemsService] Error saving pinned items: {ex.Message}");
            }
        });
    }

    public static string MakePlaylistKey(long uid, int kind) => $"playlist:{uid}:{kind}";
    public static string MakeAlbumKey(long albumId) => $"album:{albumId}";

    public bool IsPlaylistPinned(long uid, int kind) => IsPinned(MakePlaylistKey(uid, kind));
    public bool IsAlbumPinned(long albumId) => IsPinned(MakeAlbumKey(albumId));

    public bool IsPinned(string pinKey)
    {
        lock (_lock)
        {
            return _records.Any(r => string.Equals(r.Key, pinKey, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void PinPlaylist(long uid, int kind, string? title = null, string? coverUrl = null)
    {
        string key = MakePlaylistKey(uid, kind);
        lock (_lock)
        {
            int index = _records.FindIndex(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                var existing = _records[index];
                existing.PinnedAt = DateTime.UtcNow;
                if (!string.IsNullOrWhiteSpace(title)) existing.Title = title;
                if (!string.IsNullOrWhiteSpace(coverUrl)) existing.CoverUrl = coverUrl;
                _records.RemoveAt(index);
                _records.Insert(0, existing);
            }
            else
            {
                _records.Insert(0, new PinnedItemRecord
                {
                    Key = key,
                    Title = !string.IsNullOrWhiteSpace(title) ? title : "Playlist",
                    Type = "playlist",
                    Uid = uid,
                    Kind = kind,
                    CoverUrl = coverUrl,
                    PinnedAt = DateTime.UtcNow
                });
            }
            Save();
        }
        PinStateChanged?.Invoke(this, key);
    }

    public void UnpinPlaylist(long uid, int kind)
    {
        string key = MakePlaylistKey(uid, kind);
        Unpin(key);
    }

    public bool TogglePlaylistPin(long uid, int kind, string? title = null, string? coverUrl = null)
    {
        string key = MakePlaylistKey(uid, kind);
        if (IsPinned(key))
        {
            Unpin(key);
            return false;
        }
        else
        {
            PinPlaylist(uid, kind, title, coverUrl);
            return true;
        }
    }

    public void PinAlbum(long albumId, string? title = null, string? coverUrl = null)
    {
        string key = MakeAlbumKey(albumId);
        lock (_lock)
        {
            int index = _records.FindIndex(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                var existing = _records[index];
                existing.PinnedAt = DateTime.UtcNow;
                if (!string.IsNullOrWhiteSpace(title)) existing.Title = title;
                if (!string.IsNullOrWhiteSpace(coverUrl)) existing.CoverUrl = coverUrl;
                _records.RemoveAt(index);
                _records.Insert(0, existing);
            }
            else
            {
                _records.Insert(0, new PinnedItemRecord
                {
                    Key = key,
                    Title = !string.IsNullOrWhiteSpace(title) ? title : "Album",
                    Type = "album",
                    AlbumId = albumId,
                    CoverUrl = coverUrl,
                    PinnedAt = DateTime.UtcNow
                });
            }
            Save();
        }
        PinStateChanged?.Invoke(this, key);
    }

    public void UnpinAlbum(long albumId)
    {
        string key = MakeAlbumKey(albumId);
        Unpin(key);
    }

    public bool ToggleAlbumPin(long albumId, string? title = null, string? coverUrl = null)
    {
        string key = MakeAlbumKey(albumId);
        if (IsPinned(key))
        {
            Unpin(key);
            return false;
        }
        else
        {
            PinAlbum(albumId, title, coverUrl);
            return true;
        }
    }

    private void Unpin(string key)
    {
        bool changed = false;
        lock (_lock)
        {
            int removed = _records.RemoveAll(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
            {
                changed = true;
                Save();
            }
        }

        if (changed)
        {
            PinStateChanged?.Invoke(this, key);
        }
    }

    public void MovePinnedItem(string key, int targetIndex)
    {
        bool changed = false;
        lock (_lock)
        {
            int oldIndex = _records.FindIndex(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));
            if (oldIndex < 0) return;

            var item = _records[oldIndex];
            _records.RemoveAt(oldIndex);

            if (targetIndex < 0) targetIndex = 0;
            if (targetIndex > _records.Count) targetIndex = _records.Count;

            _records.Insert(targetIndex, item);
            changed = (oldIndex != targetIndex);
            if (changed)
            {
                Save();
            }
        }

        if (changed)
        {
            PinStateChanged?.Invoke(this, key);
        }
    }

    public void MovePinnedItemUp(string key)
    {
        bool changed = false;
        lock (_lock)
        {
            int index = _records.FindIndex(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));
            if (index > 0)
            {
                var item = _records[index];
                _records.RemoveAt(index);
                _records.Insert(index - 1, item);
                changed = true;
                Save();
            }
        }

        if (changed)
        {
            PinStateChanged?.Invoke(this, key);
        }
    }

    public void MovePinnedItemDown(string key)
    {
        bool changed = false;
        lock (_lock)
        {
            int index = _records.FindIndex(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));
            if (index >= 0 && index < _records.Count - 1)
            {
                var item = _records[index];
                _records.RemoveAt(index);
                _records.Insert(index + 1, item);
                changed = true;
                Save();
            }
        }

        if (changed)
        {
            PinStateChanged?.Invoke(this, key);
        }
    }

    public IReadOnlyList<PinnedItemRecord> GetPinnedItems()
    {
        lock (_lock)
        {
            return _records.ToList();
        }
    }

    public IReadOnlyList<string> GetPinnedKeys()
    {
        lock (_lock)
        {
            return _records.Select(r => r.Key).ToList();
        }
    }

    public int GetPinOrder(string pinKey)
    {
        lock (_lock)
        {
            int index = _records.FindIndex(r => string.Equals(r.Key, pinKey, StringComparison.OrdinalIgnoreCase));
            return index >= 0 ? index : int.MaxValue;
        }
    }
}
