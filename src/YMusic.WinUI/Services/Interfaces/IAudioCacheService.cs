#nullable enable

using YMusic.WinUI.Models;

namespace YMusic.WinUI.Services.Interfaces;

public interface IAudioCacheService : IDisposable
{
    /// <summary>
    /// Checks if a track is available in local disk cache and returns its local file path and quality.
    /// If updateAccessTime is true, updates the track's LastAccessed timestamp for LRU ordering.
    /// </summary>
    bool TryGetCachedTrack(string trackId, out string? filePath, out StreamAudioQuality? quality, bool updateAccessTime = true);

    /// <summary>
    /// Checks whether a track is currently cached on disk.
    /// </summary>
    bool IsTrackCached(string trackId);

    /// <summary>
    /// Enqueues background downloading of a track into disk cache.
    /// If the track is already cached or currently downloading, does nothing.
    /// </summary>
    void EnqueueDownload(string trackId, string streamUrl, StreamAudioQuality? quality);

    /// <summary>
    /// Asynchronously downloads and caches a track into local disk cache.
    /// </summary>
    Task<string?> CacheTrackAsync(string trackId, string streamUrl, StreamAudioQuality? quality, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all cached audio files and resets the cache index.
    /// </summary>
    Task ClearCacheAsync();

    /// <summary>
    /// Gets total bytes occupied by cached audio tracks.
    /// </summary>
    long GetTotalCacheSizeBytes();

    /// <summary>
    /// Gets total number of cached tracks.
    /// </summary>
    int GetCachedTrackCount();

    /// <summary>
    /// Enforces the LRU cache limit based on configured CacheSizeLimitMb.
    /// </summary>
    void EnforceCacheLimit();

    /// <summary>
    /// Gets the base directory currently used for the audio cache.
    /// </summary>
    string CacheDirectory { get; }

    /// <summary>
    /// Gets the directory where audio files are saved.
    /// </summary>
    string AudioDirectory { get; }

    /// <summary>
    /// Changes the cache location on disk (e.g. to another drive), optionally moving existing files.
    /// </summary>
    Task<bool> ChangeCacheLocationAsync(string? newLocation, bool moveExistingFiles = true);

    /// <summary>
    /// Fired whenever the cache changes (track cached, evicted, or cache cleared).
    /// </summary>
    event EventHandler? CacheUpdated;
}
