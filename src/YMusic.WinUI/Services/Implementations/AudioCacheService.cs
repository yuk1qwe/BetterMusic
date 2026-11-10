#nullable enable

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.Services.Implementations;

public sealed class AudioCacheEntry
{
    [JsonPropertyName("trackId")]
    public string TrackId { get; set; } = string.Empty;

    [JsonPropertyName("relativePath")]
    public string RelativePath { get; set; } = string.Empty;

    [JsonPropertyName("fileSizeBytes")]
    public long FileSizeBytes { get; set; }

    [JsonPropertyName("codec")]
    public string Codec { get; set; } = string.Empty;

    [JsonPropertyName("bitrateInKbps")]
    public int BitrateInKbps { get; set; }

    [JsonPropertyName("cachedAtUtc")]
    public DateTime CachedAtUtc { get; set; }

    [JsonPropertyName("lastAccessedUtc")]
    public DateTime LastAccessedUtc { get; set; }
}

public sealed class AudioCacheMetadata
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("entries")]
    public Dictionary<string, AudioCacheEntry> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AudioCacheService : IAudioCacheService
{
    private readonly ISettingsService _settingsService;
    private string _cacheBaseDir;
    private string _audioDir;
    private string _indexFilePath;

    public string CacheDirectory => _cacheBaseDir;
    public string AudioDirectory => _audioDir;

    private readonly object _lock = new();
    private AudioCacheMetadata _metadata = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeDownloads = new();
    private readonly SemaphoreSlim _downloadSemaphore = new(2, 2);
    private readonly HttpClient _httpClient;
    private readonly System.Timers.Timer _saveDebounceTimer;
    private bool _isDirty;
    private bool _isDisposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public event EventHandler? CacheUpdated;

    public static string GetDefaultCacheDir()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "YMusic", "Cache");
    }

    private static string ResolveCacheDir(string? configuredDir)
    {
        if (string.IsNullOrWhiteSpace(configuredDir))
        {
            return GetDefaultCacheDir();
        }

        try
        {
            return Path.GetFullPath(configuredDir);
        }
        catch
        {
            return GetDefaultCacheDir();
        }
    }

    public AudioCacheService(ISettingsService settingsService, HttpClient? httpClient = null)
    {
        _settingsService = settingsService;
        _httpClient = httpClient ?? new HttpClient(YandexBypassProxy.CreateHandler(TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(15)))
        {
            Timeout = TimeSpan.FromMinutes(2)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("YMusic/1.0 (Windows NT 10.0; Win64; x64)");

        string configured = _settingsService.AudioCacheLocation;
        _cacheBaseDir = ResolveCacheDir(configured);
        _audioDir = Path.Combine(_cacheBaseDir, "Audio");
        _indexFilePath = Path.Combine(_cacheBaseDir, "audio_cache_index.json");

        try
        {
            Directory.CreateDirectory(_cacheBaseDir);
            Directory.CreateDirectory(_audioDir);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioCache] Failed to initialize directory '{_cacheBaseDir}': {ex.Message}. Falling back to default.");
            _cacheBaseDir = GetDefaultCacheDir();
            _audioDir = Path.Combine(_cacheBaseDir, "Audio");
            _indexFilePath = Path.Combine(_cacheBaseDir, "audio_cache_index.json");
            try
            {
                Directory.CreateDirectory(_cacheBaseDir);
                Directory.CreateDirectory(_audioDir);
            }
            catch { }
        }

        _saveDebounceTimer = new System.Timers.Timer(2000)
        {
            AutoReset = false
        };
        _saveDebounceTimer.Elapsed += (_, _) => FlushIndexIfDirty();

        LoadIndexAndSelfHeal();

        _settingsService.SettingChanged += OnSettingChanged;
    }

    public async Task<bool> ChangeCacheLocationAsync(string? newLocation, bool moveExistingFiles = true)
    {
        string targetBaseDir = ResolveCacheDir(newLocation);
        string currentBaseDir;
        lock (_lock)
        {
            currentBaseDir = _cacheBaseDir;
        }

        if (string.Equals(Path.GetFullPath(targetBaseDir).TrimEnd('\\'), Path.GetFullPath(currentBaseDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string targetAudioDir = Path.Combine(targetBaseDir, "Audio");
        string targetIndexFilePath = Path.Combine(targetBaseDir, "audio_cache_index.json");

        try
        {
            Directory.CreateDirectory(targetBaseDir);
            Directory.CreateDirectory(targetAudioDir);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioCache] Failed to create directories at '{targetBaseDir}': {ex.Message}");
            throw new InvalidOperationException($"Cannot access or create target directory '{targetBaseDir}': {ex.Message}", ex);
        }

        // Cancel pending background downloads
        foreach (var kvp in _activeDownloads)
        {
            try { kvp.Value.Cancel(); } catch { }
        }
        _activeDownloads.Clear();

        await Task.Run(() =>
        {
            lock (_lock)
            {
                _saveDebounceTimer.Stop();
                FlushIndexIfDirty();

                if (moveExistingFiles && Directory.Exists(_audioDir))
                {
                    try
                    {
                        var files = _metadata.Entries.Values.Where(e => IsManagedCachePath(e.RelativePath)).Select(e => Path.Combine(_cacheBaseDir, e.RelativePath)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                        foreach (var file in files)
                        {
                            try
                            {
                                string fileName = Path.GetFileName(file);
                                string destFile = Path.Combine(targetAudioDir, fileName);
                                if (!File.Exists(destFile))
                                {
                                    File.Copy(file, destFile, overwrite: true);
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"[AudioCache] Error copying audio file '{file}': {ex.Message}");
                            }
                        }

                        if (File.Exists(_indexFilePath))
                        {
                            try
                            {
                                File.Copy(_indexFilePath, targetIndexFilePath, overwrite: true);
                                try { File.Delete(_indexFilePath); } catch { }
                            }
                            catch { }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[AudioCache] Error during cache migration: {ex.Message}");
                    }
                }

                _cacheBaseDir = targetBaseDir;
                _audioDir = targetAudioDir;
                _indexFilePath = targetIndexFilePath;

                LoadIndexAndSelfHeal();
            }
        });

        _settingsService.AudioCacheLocation = (string.Equals(targetBaseDir, GetDefaultCacheDir(), StringComparison.OrdinalIgnoreCase) ? string.Empty : targetBaseDir);
        NotifyCacheUpdated();
        return true;
    }

    private void OnSettingChanged(object? sender, string propName)
    {
        if (propName == nameof(ISettingsService.CacheSizeLimitMb))
        {
            EnforceCacheLimit();
        }
    }

    private bool IsManagedCachePath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath)) return false;
        try
        {
            string fullPath = Path.GetFullPath(Path.Combine(_cacheBaseDir, relativePath));
            string? parent = Path.GetDirectoryName(fullPath);
            return string.Equals(parent, Path.GetFullPath(_audioDir).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                && (fullPath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) || fullPath.EndsWith(".aac", StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    private void LoadIndexAndSelfHeal()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_indexFilePath))
                {
                    string json = File.ReadAllText(_indexFilePath);
                    var loaded = JsonSerializer.Deserialize<AudioCacheMetadata>(json, JsonOptions);
                    if (loaded?.Entries != null)
                    {
                        _metadata = loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioCache] Error loading index: {ex.Message}");
                _metadata = new AudioCacheMetadata();
            }

            // Cleanup partial/temporary files from interrupted previous sessions
            try
            {
                var tempFiles = Directory.GetFiles(_audioDir, "temp_*");
                foreach (var tempFile in tempFiles)
                {
                    try { File.Delete(tempFile); } catch { }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioCache] Error cleaning temp files: {ex.Message}");
            }

            // Self-heal: remove missing entries from index
            bool changed = false;
            var missingKeys = new List<string>();

            foreach (var kvp in _metadata.Entries)
            {
                if (!IsManagedCachePath(kvp.Value.RelativePath))
                {
                    missingKeys.Add(kvp.Key);
                    continue;
                }
                string fullPath = Path.Combine(_cacheBaseDir, kvp.Value.RelativePath);
                if (!File.Exists(fullPath))
                {
                    missingKeys.Add(kvp.Key);
                }
                else
                {
                    try
                    {
                        var info = new FileInfo(fullPath);
                        if (info.Length == 0)
                        {
                            missingKeys.Add(kvp.Key);
                            try { File.Delete(fullPath); } catch { }
                        }
                        else
                        {
                            kvp.Value.FileSizeBytes = info.Length;
                        }
                    }
                    catch
                    {
                        missingKeys.Add(kvp.Key);
                    }
                }
            }

            if (missingKeys.Count > 0)
            {
                foreach (var k in missingKeys)
                {
                    _metadata.Entries.Remove(k);
                }
                changed = true;
            }

            if (changed)
            {
                SaveIndexLocked();
            }

            EnforceCacheLimitLocked();
        }
    }

    public bool TryGetCachedTrack(string trackId, out string? filePath, out StreamAudioQuality? quality, bool updateAccessTime = true)
    {
        filePath = null;
        quality = null;

        if (string.IsNullOrWhiteSpace(trackId) || !_settingsService.AudioCacheEnabled)
        {
            return false;
        }

        lock (_lock)
        {
            if (_metadata.Entries.TryGetValue(trackId, out var entry))
            {
                string fullPath = Path.Combine(_cacheBaseDir, entry.RelativePath);
                if (File.Exists(fullPath))
                {
                    if (updateAccessTime)
                    {
                        entry.LastAccessedUtc = DateTime.UtcNow;
                        _isDirty = true;
                        _saveDebounceTimer.Stop();
                        _saveDebounceTimer.Start();
                    }

                    filePath = fullPath;
                    quality = new StreamAudioQuality
                    {
                        Codec = entry.Codec,
                        BitrateInKbps = entry.BitrateInKbps,
                        IsCached = true
                    };
                    return true;
                }

                // If file disappeared, prune entry
                _metadata.Entries.Remove(trackId);
                _isDirty = true;
                _saveDebounceTimer.Stop();
                _saveDebounceTimer.Start();
            }
        }

        return false;
    }

    public bool IsTrackCached(string trackId)
    {
        if (string.IsNullOrWhiteSpace(trackId) || !_settingsService.AudioCacheEnabled)
        {
            return false;
        }

        lock (_lock)
        {
            if (_metadata.Entries.TryGetValue(trackId, out var entry))
            {
                string fullPath = Path.Combine(_cacheBaseDir, entry.RelativePath);
                return File.Exists(fullPath);
            }
        }

        return false;
    }

    public void EnqueueDownload(string trackId, string streamUrl, StreamAudioQuality? quality)
    {
        if (string.IsNullOrWhiteSpace(trackId) ||
            string.IsNullOrWhiteSpace(streamUrl) ||
            !_settingsService.AudioCacheEnabled)
        {
            return;
        }

        // Skip if stream is already a local file
        if (streamUrl.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ||
            (streamUrl.Length > 2 && streamUrl[1] == ':'))
        {
            return;
        }

        lock (_lock)
        {
            if (_metadata.Entries.ContainsKey(trackId))
            {
                return;
            }
        }

        if (_activeDownloads.ContainsKey(trackId))
        {
            return;
        }

        _ = CacheTrackAsync(trackId, streamUrl, quality);
    }

    public async Task<string?> CacheTrackAsync(
        string trackId,
        string streamUrl,
        StreamAudioQuality? quality,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(trackId) ||
            string.IsNullOrWhiteSpace(streamUrl) ||
            !_settingsService.AudioCacheEnabled)
        {
            return null;
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (!_activeDownloads.TryAdd(trackId, cts))
        {
            return null; // Already being downloaded
        }

        string sanitizedId = SanitizeFileName(trackId);
        string codec = string.IsNullOrWhiteSpace(quality?.Codec) ? "mp3" : SanitizeFileName(quality.Codec.ToLowerInvariant());
        int bitrate = quality?.BitrateInKbps ?? 320;
        string ext = codec.Contains("aac") ? ".aac" : ".mp3";
        string targetFileName = $"{sanitizedId}_{codec}_{bitrate}{ext}";
        string targetRelativePath = Path.Combine("Audio", targetFileName);
        string targetFullPath = Path.Combine(_cacheBaseDir, targetRelativePath);

        string tempFileName = $"temp_{sanitizedId}_{Guid.NewGuid():N}.part";
        string tempFullPath = Path.Combine(_audioDir, tempFileName);

        try
        {
            await _downloadSemaphore.WaitAsync(cts.Token).ConfigureAwait(false);
            try
            {
                // Recheck if cached while waiting for semaphore
                lock (_lock)
                {
                    if (_metadata.Entries.ContainsKey(trackId) && File.Exists(targetFullPath))
                    {
                        return targetFullPath;
                    }
                }

                using var response = await _httpClient.GetAsync(streamUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                await using (var remoteStream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false))
                await using (var fileStream = new FileStream(tempFullPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    await remoteStream.CopyToAsync(fileStream, 81920, cts.Token).ConfigureAwait(false);
                    await fileStream.FlushAsync(cts.Token).ConfigureAwait(false);
                }

                var fileInfo = new FileInfo(tempFullPath);
                if (fileInfo.Length < 1024)
                {
                    try { File.Delete(tempFullPath); } catch { }
                    return null;
                }

                // Atomic commit
                if (File.Exists(targetFullPath))
                {
                    try { File.Delete(targetFullPath); } catch { }
                }
                File.Move(tempFullPath, targetFullPath);

                var entry = new AudioCacheEntry
                {
                    TrackId = trackId,
                    RelativePath = targetRelativePath,
                    FileSizeBytes = fileInfo.Length,
                    Codec = codec,
                    BitrateInKbps = bitrate,
                    CachedAtUtc = DateTime.UtcNow,
                    LastAccessedUtc = DateTime.UtcNow
                };

                lock (_lock)
                {
                    _metadata.Entries[trackId] = entry;
                    EnforceCacheLimitLocked();
                    SaveIndexLocked();
                }

                NotifyCacheUpdated();
                return targetFullPath;
            }
            finally
            {
                _downloadSemaphore.Release();
            }
        }
        catch (OperationCanceledException)
        {
            try { if (File.Exists(tempFullPath)) File.Delete(tempFullPath); } catch { }
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioCache] Download failed for track {trackId}: {ex.Message}");
            try { if (File.Exists(tempFullPath)) File.Delete(tempFullPath); } catch { }
            return null;
        }
        finally
        {
            _activeDownloads.TryRemove(trackId, out _);
            cts.Dispose();
        }
    }

    public Task ClearCacheAsync()
    {
        return Task.Run(() =>
        {
            // Cancel active downloads
            foreach (var kvp in _activeDownloads)
            {
                try { kvp.Value.Cancel(); } catch { }
            }
            _activeDownloads.Clear();

            lock (_lock)
            {
                _saveDebounceTimer.Stop();
                _isDirty = false;

                try
                {
                    if (Directory.Exists(_audioDir))
                    {
                        var files = _metadata.Entries.Values.Where(e => IsManagedCachePath(e.RelativePath)).Select(e => Path.Combine(_cacheBaseDir, e.RelativePath)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                        foreach (var file in files)
                        {
                            try { File.Delete(file); } catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AudioCache] Error clearing audio files: {ex.Message}");
                }

                _metadata.Entries.Clear();
                SaveIndexLocked();
            }

            NotifyCacheUpdated();
        });
    }

    public long GetTotalCacheSizeBytes()
    {
        lock (_lock)
        {
            long total = 0;
            foreach (var entry in _metadata.Entries.Values)
            {
                total += entry.FileSizeBytes;
            }
            return total;
        }
    }

    public int GetCachedTrackCount()
    {
        lock (_lock)
        {
            return _metadata.Entries.Count;
        }
    }

    public void EnforceCacheLimit()
    {
        lock (_lock)
        {
            EnforceCacheLimitLocked();
            SaveIndexLocked();
        }
        NotifyCacheUpdated();
    }

    private void EnforceCacheLimitLocked()
    {
        int limitMb = _settingsService.CacheSizeLimitMb;
        if (limitMb <= 0)
        {
            return; // 0 = unlimited
        }

        long limitBytes = (long)limitMb * 1024 * 1024;
        long totalBytes = _metadata.Entries.Values.Sum(e => e.FileSizeBytes);

        if (totalBytes <= limitBytes)
        {
            return;
        }

        // Cushion target: trim down to 85% of limit to prevent constant eviction
        long targetBytes = (long)(limitBytes * 0.85);

        // LRU: evict oldest accessed first
        var sortedEntries = _metadata.Entries.Values
            .OrderBy(e => e.LastAccessedUtc)
            .ToList();

        var evictedKeys = new List<string>();

        foreach (var entry in sortedEntries)
        {
            string fullPath = Path.Combine(_cacheBaseDir, entry.RelativePath);
            try
            {
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioCache] Could not delete evicted file {fullPath}: {ex.Message}");
            }

            evictedKeys.Add(entry.TrackId);
            totalBytes -= entry.FileSizeBytes;

            if (totalBytes <= targetBytes)
            {
                break;
            }
        }

        foreach (var key in evictedKeys)
        {
            _metadata.Entries.Remove(key);
        }
    }

    private void SaveIndexLocked()
    {
        try
        {
            string json = JsonSerializer.Serialize(_metadata, JsonOptions);
            string tmpFile = _indexFilePath + ".tmp";
            File.WriteAllText(tmpFile, json);
            File.Move(tmpFile, _indexFilePath, overwrite: true);
            _isDirty = false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioCache] Error saving index: {ex.Message}");
        }
    }

    private void FlushIndexIfDirty()
    {
        lock (_lock)
        {
            if (_isDirty)
            {
                SaveIndexLocked();
            }
        }
    }

    private void NotifyCacheUpdated()
    {
        CacheUpdated?.Invoke(this, EventArgs.Empty);
    }

    public static string SanitizeFileName(string id)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(id.Length);
        foreach (var c in id)
        {
            sb.Append(invalidChars.Contains(c) ? '_' : c);
        }
        return sb.ToString();
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _settingsService.SettingChanged -= OnSettingChanged;
        _saveDebounceTimer.Stop();
        _saveDebounceTimer.Dispose();

        FlushIndexIfDirty();

        foreach (var kvp in _activeDownloads)
        {
            try { kvp.Value.Cancel(); kvp.Value.Dispose(); } catch { }
        }
        _activeDownloads.Clear();
        _downloadSemaphore.Dispose();
        _httpClient.Dispose();
    }
}
