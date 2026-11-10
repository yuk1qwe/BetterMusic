#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.Services.Implementations;

public sealed class SloplessService : ISloplessService
{
    private const string ApiBaseUrl = "https://slopless.art/api";
    private const double AiScoreThreshold = 0.5;
    private static readonly TimeSpan CacheTtlSuccess = TimeSpan.FromHours(1);
    private static readonly TimeSpan CacheTtlError = TimeSpan.FromSeconds(30);

    private readonly HttpClient _httpClient;
    private readonly ISettingsService _settingsService;

    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<object?>> _inFlight = new(StringComparer.OrdinalIgnoreCase);

    public SloplessService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", $"WinYandex/{UpdateService.AppVersion} (Windows NT 10.0; Win64; x64)");
    }

    private sealed record CacheEntry(object? Data, DateTime Expiration);

    private sealed class ApiTrackDto
    {
        [JsonPropertyName("id")]
        public object? Id { get; set; }

        [JsonPropertyName("score")]
        public double? Score { get; set; }
    }

    private sealed class ApiArtistTrackItemDto
    {
        [JsonPropertyName("id")]
        public object? Id { get; set; }

        [JsonPropertyName("score")]
        public double? Score { get; set; }
    }

    private sealed class ApiArtistDto
    {
        [JsonPropertyName("id")]
        public object? Id { get; set; }

        [JsonPropertyName("aiTracks")]
        public int? AiTracks { get; set; }

        [JsonPropertyName("totalTracks")]
        public int? TotalTracks { get; set; }

        [JsonPropertyName("aiTrackList")]
        public List<ApiArtistTrackItemDto>? AiTrackList { get; set; }
    }

    private sealed class ApiAlbumDto
    {
        [JsonPropertyName("id")]
        public object? Id { get; set; }

        [JsonPropertyName("aiTracks")]
        public int? AiTracks { get; set; }

        [JsonPropertyName("totalTracks")]
        public int? TotalTracks { get; set; }
    }

    private async Task<T?> FetchWithCacheAsync<T>(string cacheKey, string url, CancellationToken ct) where T : class
    {
        var now = DateTime.UtcNow;
        if (_cache.TryGetValue(cacheKey, out var entry))
        {
            if (now < entry.Expiration)
            {
                return entry.Data as T;
            }
            _cache.TryRemove(cacheKey, out _);
        }

        // Deduplicate concurrent requests
        if (_inFlight.TryGetValue(cacheKey, out var existingTask))
        {
            var res = await existingTask.ConfigureAwait(false);
            return res as T;
        }

        var fetchTask = Task.Run<object?>(async () =>
        {
            try
            {
                using var response = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    _cache[cacheKey] = new CacheEntry(null, DateTime.UtcNow.Add(CacheTtlError));
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var data = JsonSerializer.Deserialize<T>(json);
                _cache[cacheKey] = new CacheEntry(data, DateTime.UtcNow.Add(CacheTtlSuccess));
                return (object?)data;
            }
            catch
            {
                _cache[cacheKey] = new CacheEntry(null, DateTime.UtcNow.Add(CacheTtlError));
                return null;
            }
            finally
            {
                _inFlight.TryRemove(cacheKey, out _);
            }
        }, ct);

        _inFlight[cacheKey] = fetchTask;
        return (await fetchTask.ConfigureAwait(false)) as T;
    }

    public async Task<SloplessTrackResult> CheckTrackAsync(string trackId, IEnumerable<string>? artistIds = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(trackId))
            return new SloplessTrackResult { IsAi = false, Score = 0 };

        string cleanTrackId = trackId.Split(':')[0];
        string cacheKey = $"track_{cleanTrackId}";

        var now = DateTime.UtcNow;
        if (_cache.TryGetValue(cacheKey, out var entry) && now < entry.Expiration)
        {
            if (entry.Data is ApiTrackDto cachedDto && cachedDto.Score.HasValue)
            {
                double score = cachedDto.Score.Value;
                return new SloplessTrackResult { IsAi = score > AiScoreThreshold, Score = score };
            }
            return new SloplessTrackResult { IsAi = false, Score = 0 };
        }

        // Cross-check cached artist aiTrackList before making network calls
        if (artistIds != null)
        {
            bool hadCachedArtist = false;
            foreach (var artistId in artistIds)
            {
                if (string.IsNullOrWhiteSpace(artistId)) continue;
                string artistKey = $"artist_{artistId}";
                if (_cache.TryGetValue(artistKey, out var artistEntry) && now < artistEntry.Expiration)
                {
                    hadCachedArtist = true;
                    if (artistEntry.Data is ApiArtistDto artistDto && artistDto.AiTrackList != null)
                    {
                        var found = artistDto.AiTrackList.FirstOrDefault(t => t.Id?.ToString() == cleanTrackId);
                        if (found != null && found.Score.HasValue)
                        {
                            var hitDto = new ApiTrackDto { Id = cleanTrackId, Score = found.Score.Value };
                            _cache[cacheKey] = new CacheEntry(hitDto, DateTime.UtcNow.Add(CacheTtlSuccess));
                            return new SloplessTrackResult { IsAi = found.Score.Value > AiScoreThreshold, Score = found.Score.Value };
                        }
                    }
                }
            }

            // If we inspected a cached artist and the track was NOT in their AI list, it's 0% AI
            if (hadCachedArtist)
            {
                var cleanDto = new ApiTrackDto { Id = cleanTrackId, Score = 0 };
                _cache[cacheKey] = new CacheEntry(cleanDto, DateTime.UtcNow.Add(CacheTtlSuccess));
                return new SloplessTrackResult { IsAi = false, Score = 0 };
            }
        }

        var dto = await FetchWithCacheAsync<ApiTrackDto>(cacheKey, $"{ApiBaseUrl}/track/{cleanTrackId}", ct).ConfigureAwait(false);
        if (dto == null || !dto.Score.HasValue)
        {
            return new SloplessTrackResult { IsAi = false, Score = 0 };
        }

        double finalScore = dto.Score.Value;
        return new SloplessTrackResult
        {
            IsAi = finalScore > AiScoreThreshold,
            Score = finalScore
        };
    }

    public async Task<SloplessArtistResult> CheckArtistAsync(string artistId, double threshold, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(artistId) || artistId == "0")
            return new SloplessArtistResult { IsAi = false };

        string cacheKey = $"artist_{artistId}";
        var dto = await FetchWithCacheAsync<ApiArtistDto>(cacheKey, $"{ApiBaseUrl}/artist/{artistId}", ct).ConfigureAwait(false);
        if (dto == null || !dto.TotalTracks.HasValue || dto.TotalTracks.Value == 0)
        {
            return new SloplessArtistResult { IsAi = false };
        }

        int total = dto.TotalTracks.Value;
        int aiTracks = dto.AiTracks ?? 0;

        // If artist provided an aiTrackList, compute and pre-populate track cache
        if (dto.AiTrackList != null && dto.AiTrackList.Count > 0)
        {
            aiTracks = 0;
            foreach (var item in dto.AiTrackList)
            {
                if (item.Score.HasValue && item.Score.Value > AiScoreThreshold)
                {
                    aiTracks++;
                }

                if (item.Id != null && item.Score.HasValue)
                {
                    string tKey = $"track_{item.Id}";
                    if (!_cache.ContainsKey(tKey))
                    {
                        var trackDto = new ApiTrackDto { Id = item.Id, Score = item.Score.Value };
                        _cache[tKey] = new CacheEntry(trackDto, DateTime.UtcNow.Add(CacheTtlSuccess));
                    }
                }
            }
        }

        double ratio = (double)aiTracks / total;
        return new SloplessArtistResult
        {
            IsAi = ratio >= threshold,
            AiTracks = aiTracks,
            TotalTracks = total
        };
    }

    public async Task<bool> CheckAlbumIsAiAsync(string albumId, double threshold, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(albumId) || albumId == "0")
            return false;

        string cacheKey = $"album_{albumId}";
        var dto = await FetchWithCacheAsync<ApiAlbumDto>(cacheKey, $"{ApiBaseUrl}/album/{albumId}", ct).ConfigureAwait(false);
        if (dto == null || !dto.TotalTracks.HasValue || dto.TotalTracks.Value == 0)
        {
            return false;
        }

        double ratio = (double)(dto.AiTracks ?? 0) / dto.TotalTracks.Value;
        return ratio >= threshold;
    }

    public async Task<SloplessEvaluationResult> EvaluateTrackAsync(Track track, CancellationToken ct = default)
    {
        if (track == null || string.IsNullOrWhiteSpace(track.Id))
        {
            return new SloplessEvaluationResult { IsAi = false };
        }

        string detectionMode = _settingsService.SloplessDetectionMode; // "balance", "paranoid", "track_only"
        double threshold = Math.Clamp(_settingsService.SloplessArtistThreshold, 1, 100) / 100.0;

        var artistIds = track.Artists?.Select(a => a.Id.ToString()).Where(id => !string.IsNullOrEmpty(id) && id != "0").ToList()
                        ?? new List<string>();

        bool anyArtistAi = false;
        bool allArtistsAi = artistIds.Count > 0;
        int maxArtistPercent = 0;

        if (detectionMode != "track_only" && artistIds.Count > 0)
        {
            foreach (var artistId in artistIds)
            {
                var artistResult = await CheckArtistAsync(artistId, threshold, ct).ConfigureAwait(false);
                if (artistResult.IsAi)
                {
                    anyArtistAi = true;
                    if (artistResult.Percent > maxArtistPercent)
                    {
                        maxArtistPercent = artistResult.Percent;
                    }
                }
                else
                {
                    allArtistsAi = false;
                }

                if (detectionMode == "paranoid" && anyArtistAi)
                {
                    break;
                }
            }
        }
        else
        {
            allArtistsAi = false;
        }

        var trackResult = await CheckTrackAsync(track.Id, artistIds, ct).ConfigureAwait(false);

        // Check album if available
        bool albumAi = false;
        if (track.Albums != null && track.Albums.Count > 0 && track.Albums[0].Id > 0)
        {
            albumAi = await CheckAlbumIsAiAsync(track.Albums[0].Id.ToString(), threshold, ct).ConfigureAwait(false);
        }

        bool isAi = false;
        string reason = string.Empty;

        if (trackResult.IsAi)
        {
            isAi = true;
            reason = $"Track score: {trackResult.Percent}%";
        }
        else if (detectionMode == "paranoid" && anyArtistAi)
        {
            isAi = true;
            reason = $"Paranoid mode: Artist AI ratio >= {threshold * 100:0}%";
        }
        else if (detectionMode == "balance" && allArtistsAi)
        {
            isAi = true;
            reason = $"Balance mode: All artists AI ratio >= {threshold * 100:0}%";
        }
        else if (albumAi)
        {
            isAi = true;
            reason = $"Album AI ratio >= {threshold * 100:0}%";
        }

        bool isEn = App.Loc?.CurrentLanguage == "en";
        string tooltip = isEn
            ? (trackResult.IsAi
                ? $"This track was generated by AI ({trackResult.Percent}% probability)"
                : $"AI-generated music detected ({reason})")
            : (trackResult.IsAi
                ? $"Этот трек создан с помощью ИИ (вероятность {trackResult.Percent}%)"
                : $"Обнаружена ИИ-музыка ({reason})");

        var evaluation = new SloplessEvaluationResult
        {
            IsAi = isAi,
            Reason = reason,
            Score = trackResult.Percent,
            Tooltip = tooltip
        };

        if (_settingsService.SloplessShowTrackLabels)
        {
            track.IsAiTrack = isAi;
            track.AiScore = trackResult.Percent;
            track.AiTooltip = tooltip;
        }

        return evaluation;
    }

    public async Task CheckTracksAsync(IEnumerable<Track> tracks, CancellationToken ct = default)
    {
        if (!_settingsService.SloplessEnabled || !_settingsService.SloplessShowTrackLabels)
            return;

        var trackList = tracks?.Where(t => t != null && !string.IsNullOrWhiteSpace(t.Id)).Take(50).ToList();
        if (trackList == null || trackList.Count == 0)
            return;

        foreach (var track in trackList)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                await EvaluateTrackAsync(track, ct).ConfigureAwait(false);
            }
            catch
            {
                // Silently continue for background list labeling
            }
        }
    }
}

