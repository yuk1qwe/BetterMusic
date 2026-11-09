#nullable enable

using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.Services.Implementations;

public sealed partial class YandexMusicService : IYandexMusicService
{
    private const string ApiBaseUrl = "https://api.music.yandex.net/";
    private const string UserAgentHeader = "YandexMusicDesktopAppWindows/5.29.1";
    private const string ClientHeader = "YandexMusicDesktopAppWindows/5.29.1";

    private readonly HttpClient _httpClient;
    private static readonly HttpClient _downloadHttpClient = new();
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ICredentialStorageService? _credentialStorage;
    private readonly HashSet<string> _likedTrackIds = new();
    private readonly HashSet<string> _dislikedTrackIds = new();
    private readonly HashSet<long> _likedAlbumIds = new();
    private readonly HashSet<long> _presavedAlbumIds = new();
    private readonly HashSet<string> _likedPlaylistKeys = new();
    private readonly HashSet<long> _likedArtistIds = new();
    private readonly ConcurrentDictionary<string, List<LyricLine>> _lyricsCache = new();
    private readonly ConcurrentDictionary<long, (Album Album, DateTime CachedAt)> _albumWithTracksCache = new();
    private readonly ConcurrentDictionary<string, (Playlist Playlist, DateTime CachedAt)> _playlistWithTracksCache = new();
    private readonly ConcurrentDictionary<long, Album> _knownUpcomingAlbums = new();
    private readonly ConcurrentDictionary<string, (string Url, StreamAudioQuality Quality, DateTime Expiry)> _streamInfoCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HttpClient _lrclibHttpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(2.5),
        DefaultRequestHeaders =
        {
            { "User-Agent", "WinYandex/1.0 (https://github.com/winyandex)" }
        }
    };
    private static readonly HttpClient _geniusHttpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(3.5),
        DefaultRequestHeaders =
        {
            { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36" }
        }
    };
    private string? _cachedUserId;
    private string? _token;

    public event EventHandler? UnauthorizedResponseReceived;
    public event EventHandler? RegionRestrictionDetected;
    public event EventHandler<(string TrackId, bool IsLiked)>? TrackLikeChanged;
    public event EventHandler<(string TrackId, bool IsDisliked)>? TrackDislikeChanged;
    public event EventHandler<(long AlbumId, bool IsLiked)>? AlbumLikeChanged;
    public event EventHandler<(long OwnerUid, int Kind, bool IsLiked)>? PlaylistLikeChanged;
    public event EventHandler<(long ArtistId, bool IsLiked)>? ArtistLikeChanged;
    public event EventHandler? LikedTracksLoaded;
    public event EventHandler? DislikedTracksLoaded;

    public string? CurrentToken => _token;

    public YandexMusicService(ICredentialStorageService? credentialStorage = null, HttpClient? httpClient = null)
    {
        _credentialStorage = credentialStorage;
        _httpClient = httpClient ?? new HttpClient(YandexBypassProxy.CreateHandler(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(15)));
        _httpClient.BaseAddress = new Uri(ApiBaseUrl);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgentHeader);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.Add("X-Yandex-Music-Client", ClientHeader);
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "ru");

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        if (_credentialStorage != null)
        {
            string? storedToken = _credentialStorage.GetToken();
            if (!string.IsNullOrWhiteSpace(storedToken))
            {
                SetOAuthToken(storedToken);
            }
        }
    }

    public void SetOAuthToken(string token)
    {
        _token = token?.Trim();
        if (string.IsNullOrWhiteSpace(_token))
        {
            _httpClient.DefaultRequestHeaders.Authorization = null;
            _albumWithTracksCache.Clear();
            _playlistWithTracksCache.Clear();
            _lyricsCache.Clear();
            lock (_likedTrackIds) { _likedTrackIds.Clear(); }
            lock (_likedAlbumIds) { _likedAlbumIds.Clear(); }
            lock (_likedPlaylistKeys) { _likedPlaylistKeys.Clear(); }
            lock (_likedArtistIds) { _likedArtistIds.Clear(); }
        }
    }

    public async Task<UserProfile?> GetAccountStatusAsync(CancellationToken cancellationToken = default)
    {
        var (profile, _) = await GetAccountStatusDetailedAsync(cancellationToken).ConfigureAwait(false);
        return profile;
    }

    public async Task<(UserProfile? Profile, string? Error)> GetAccountStatusDetailedAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendRequestAsync(HttpMethod.Get, "account/status", null, cancellationToken).ConfigureAwait(false);
        if (response == null)
        {
            return (null, "No response from server.");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        System.Diagnostics.Debug.WriteLine($"[API account/status] HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {json}");

        if (!response.IsSuccessStatusCode)
        {
            return (null, $"HTTP {(int)response.StatusCode}: {json}");
        }

        try
        {
            var apiResult = JsonSerializer.Deserialize<YandexApiResponse<AccountStatusResult>>(json, _jsonOptions);
            if (apiResult?.Result == null)
            {
                return (null, $"Missing 'result' field in response: {json}");
            }

            var profile = UserProfile.FromAccountStatus(apiResult.Result);

            string? token = _token;
            if (string.IsNullOrWhiteSpace(token) && _credentialStorage != null)
            {
                token = _credentialStorage.GetToken();
            }

            if (!string.IsNullOrWhiteSpace(token))
            {
                try
                {
                    using var infoReq = new HttpRequestMessage(HttpMethod.Get, "https://login.yandex.ru/info?format=json");
                    infoReq.Headers.Authorization = new AuthenticationHeaderValue("OAuth", token);
                    using var infoRes = await _httpClient.SendAsync(infoReq, cancellationToken).ConfigureAwait(false);
                    if (infoRes.IsSuccessStatusCode)
                    {
                        var infoJson = await infoRes.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                        using var infoDoc = JsonDocument.Parse(infoJson);
                        if (infoDoc.RootElement.TryGetProperty("default_avatar_id", out var avatarElem))
                        {
                            string? avatarId = avatarElem.GetString();
                            if (!string.IsNullOrWhiteSpace(avatarId))
                            {
                                profile.AvatarUrl = $"https://avatars.yandex.net/get-yapic/{avatarId}/islands-200";
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[YandexMusicService] Failed to load custom avatar from login.yandex.ru: {ex.Message}");
                }
            }

            return (profile, null);
        }
        catch (Exception ex)
        {
            return (null, $"JSON parse error: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<string>> GetLikedTrackIdsAsync(string userId, CancellationToken cancellationToken = default)
    {
        using var response = await SendRequestAsync(HttpMethod.Get, $"users/{userId}/likes/tracks", null, cancellationToken).ConfigureAwait(false);
        if (response == null || !response.IsSuccessStatusCode)
        {
            return Array.Empty<string>();
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("result", out var resultElement))
        {
            return Array.Empty<string>();
        }

        var ids = new List<string>();

        // Scenario 1: result is an object containing library.tracks
        if (resultElement.ValueKind == JsonValueKind.Object)
        {
            JsonElement tracksElement;
            if (resultElement.TryGetProperty("library", out var libElement) && libElement.TryGetProperty("tracks", out var innerTracks))
            {
                tracksElement = innerTracks;
            }
            else if (resultElement.TryGetProperty("tracks", out var directTracks))
            {
                tracksElement = directTracks;
            }
            else
            {
                tracksElement = default;
            }

            if (tracksElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in tracksElement.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var idProp))
                    {
                        var idStr = idProp.ValueKind == JsonValueKind.String
                            ? idProp.GetString()
                            : (idProp.ValueKind == JsonValueKind.Number ? idProp.GetRawText() : null);
                        if (!string.IsNullOrWhiteSpace(idStr))
                        {
                            ids.Add(idStr);
                        }
                    }
                }
            }
        }
        // Scenario 2: result is directly an array of tracks
        else if (resultElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in resultElement.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var idProp))
                {
                    var idStr = idProp.ValueKind == JsonValueKind.String
                        ? idProp.GetString()
                        : (idProp.ValueKind == JsonValueKind.Number ? idProp.GetRawText() : null);
                    if (!string.IsNullOrWhiteSpace(idStr))
                    {
                        ids.Add(idStr);
                    }
                }
            }
        }

        lock (_likedTrackIds)
        {
            _likedTrackIds.Clear();
            foreach (var id in ids)
            {
                _likedTrackIds.Add(id);
                string cleanId = id.Split(':')[0];
                if (!string.Equals(cleanId, id, StringComparison.OrdinalIgnoreCase))
                {
                    _likedTrackIds.Add(cleanId);
                }
            }
        }
        LikedTracksLoaded?.Invoke(this, EventArgs.Empty);

        return ids;
    }

    public bool IsTrackLiked(string trackId)
    {
        if (string.IsNullOrWhiteSpace(trackId)) return false;
        string cleanId = trackId.Split(':')[0];
        lock (_likedTrackIds)
        {
            return _likedTrackIds.Contains(trackId) || _likedTrackIds.Contains(cleanId);
        }
    }

    public async Task<bool> LikeTrackAsync(string trackId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(trackId)) return false;

        string cleanId = trackId.Split(':')[0];
        lock (_likedTrackIds)
        {
            _likedTrackIds.Add(trackId);
            _likedTrackIds.Add(cleanId);
        }
        TrackLikeChanged?.Invoke(this, (trackId, true));

        try
        {
            string? userId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(userId))
            {
                RollbackLike(trackId, cleanId);
                return false;
            }

            var formValues = new List<KeyValuePair<string, string>>
            {
                new("track-ids", cleanId)
            };
            using var content = new FormUrlEncodedContent(formValues);
            using var response = await SendRequestAsync(HttpMethod.Post, $"users/{userId}/likes/tracks/add-multiple", content, cancellationToken).ConfigureAwait(false);

            if (response != null && response.IsSuccessStatusCode)
            {
                return true;
            }

            RollbackLike(trackId, cleanId);
            return false;
        }
        catch
        {
            RollbackLike(trackId, cleanId);
            throw;
        }
    }

    private void RollbackLike(string trackId, string cleanId)
    {
        lock (_likedTrackIds)
        {
            _likedTrackIds.Remove(trackId);
            _likedTrackIds.Remove(cleanId);
        }
        TrackLikeChanged?.Invoke(this, (trackId, false));
    }

    public async Task<bool> UnlikeTrackAsync(string trackId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(trackId)) return false;

        string cleanId = trackId.Split(':')[0];
        lock (_likedTrackIds)
        {
            _likedTrackIds.Remove(trackId);
            _likedTrackIds.Remove(cleanId);
        }
        TrackLikeChanged?.Invoke(this, (trackId, false));

        try
        {
            string? userId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(userId))
            {
                RollbackUnlike(trackId, cleanId);
                return false;
            }

            var formValues = new List<KeyValuePair<string, string>>
            {
                new("track-ids", cleanId)
            };
            using var content = new FormUrlEncodedContent(formValues);
            using var response = await SendRequestAsync(HttpMethod.Post, $"users/{userId}/likes/tracks/remove", content, cancellationToken).ConfigureAwait(false);

            if (response != null && response.IsSuccessStatusCode)
            {
                return true;
            }

            RollbackUnlike(trackId, cleanId);
            return false;
        }
        catch
        {
            RollbackUnlike(trackId, cleanId);
            throw;
        }
    }

    private void RollbackUnlike(string trackId, string cleanId)
    {
        lock (_likedTrackIds)
        {
            _likedTrackIds.Add(trackId);
            _likedTrackIds.Add(cleanId);
        }
        TrackLikeChanged?.Invoke(this, (trackId, true));
    }

    public async Task EnsureLikedTracksLoadedAsync(CancellationToken cancellationToken = default)
    {
        lock (_likedTrackIds)
        {
            if (_likedTrackIds.Count > 0) return;
        }

        string? userId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(userId))
        {
            await GetLikedTrackIdsAsync(userId, cancellationToken).ConfigureAwait(false);
        }
    }

    public bool IsTrackDisliked(string trackId)
    {
        if (string.IsNullOrWhiteSpace(trackId)) return false;
        string cleanId = trackId.Split(':')[0];
        lock (_dislikedTrackIds)
        {
            return _dislikedTrackIds.Contains(trackId) || _dislikedTrackIds.Contains(cleanId);
        }
    }

    public async Task<IReadOnlyList<string>> GetDislikedTrackIdsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var ids = new List<string>();
        try
        {
            using var response = await SendRequestAsync(HttpMethod.Get, $"users/{userId}/dislikes/tracks", null, cancellationToken).ConfigureAwait(false);
            if (response == null || !response.IsSuccessStatusCode)
            {
                return ids;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("result", out var resultElement))
            {
                return ids;
            }

            JsonElement tracksElement;
            if (resultElement.TryGetProperty("library", out var libElement) &&
                libElement.TryGetProperty("tracks", out tracksElement))
            {
            }
            else if (resultElement.TryGetProperty("tracks", out tracksElement))
            {
            }
            else
            {
                tracksElement = resultElement;
            }

            if (tracksElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in tracksElement.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var idProp))
                    {
                        var idStr = idProp.ValueKind == JsonValueKind.String
                            ? idProp.GetString()
                            : (idProp.ValueKind == JsonValueKind.Number ? idProp.GetRawText() : null);
                        if (!string.IsNullOrWhiteSpace(idStr))
                        {
                            ids.Add(idStr);
                        }
                    }
                }
            }

            lock (_dislikedTrackIds)
            {
                _dislikedTrackIds.Clear();
                foreach (var id in ids)
                {
                    _dislikedTrackIds.Add(id);
                    string cleanId = id.Split(':')[0];
                    if (!string.Equals(cleanId, id, StringComparison.OrdinalIgnoreCase))
                    {
                        _dislikedTrackIds.Add(cleanId);
                    }
                }
            }
            DislikedTracksLoaded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GetDislikedTrackIds Error] {ex.Message}");
        }
        return ids;
    }

    public async Task EnsureDislikedTracksLoadedAsync(CancellationToken cancellationToken = default)
    {
        lock (_dislikedTrackIds)
        {
            if (_dislikedTrackIds.Count > 0) return;
        }

        string? userId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(userId))
        {
            await GetDislikedTrackIdsAsync(userId, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<bool> DislikeTrackAsync(string trackId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(trackId)) return false;

        string cleanId = trackId.Split(':')[0];
        lock (_dislikedTrackIds)
        {
            _dislikedTrackIds.Add(trackId);
            _dislikedTrackIds.Add(cleanId);
        }

        if (IsTrackLiked(trackId))
        {
            _ = UnlikeTrackAsync(trackId, cancellationToken);
        }

        TrackDislikeChanged?.Invoke(this, (trackId, true));

        try
        {
            string? userId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return true;
            }

            var formValues = new List<KeyValuePair<string, string>>
            {
                new("track-ids", cleanId)
            };
            using var content = new FormUrlEncodedContent(formValues);
            using var response = await SendRequestAsync(HttpMethod.Post, $"users/{userId}/dislikes/tracks/add-multiple", content, cancellationToken).ConfigureAwait(false);

            return response != null && response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DislikeTrack Error] {ex.Message}");
            return false;
        }
    }

    public async Task<bool> UndislikeTrackAsync(string trackId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(trackId)) return false;

        string cleanId = trackId.Split(':')[0];
        lock (_dislikedTrackIds)
        {
            _dislikedTrackIds.Remove(trackId);
            _dislikedTrackIds.Remove(cleanId);
        }
        TrackDislikeChanged?.Invoke(this, (trackId, false));

        try
        {
            string? userId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return true;
            }

            var formValues = new List<KeyValuePair<string, string>>
            {
                new("track-ids", cleanId)
            };
            using var content = new FormUrlEncodedContent(formValues);
            using var response = await SendRequestAsync(HttpMethod.Post, $"users/{userId}/dislikes/tracks/remove", content, cancellationToken).ConfigureAwait(false);

            return response != null && response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UndislikeTrack Error] {ex.Message}");
            return false;
        }
    }

    private async Task<string?> GetCurrentUserIdAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(_cachedUserId))
        {
            return _cachedUserId;
        }

        string? stored = _credentialStorage?.GetUserId();
        if (!string.IsNullOrWhiteSpace(stored))
        {
            _cachedUserId = stored;
            return _cachedUserId;
        }

        var profile = await GetAccountStatusAsync(cancellationToken).ConfigureAwait(false);
        if (profile != null && !string.IsNullOrWhiteSpace(profile.UserId))
        {
            _cachedUserId = profile.UserId;
            _credentialStorage?.SaveUserId(profile.UserId);
            return _cachedUserId;
        }

        return null;
    }

    public async Task<IReadOnlyList<Track>> GetTracksAsync(IEnumerable<string> trackIds, CancellationToken cancellationToken = default)
    {
        var idList = trackIds.ToList();
        if (idList.Count == 0)
        {
            return Array.Empty<Track>();
        }

        var allTracks = new List<Track>();
        const int batchSize = 100;

        for (int i = 0; i < idList.Count; i += batchSize)
        {
            var batch = idList.Skip(i).Take(batchSize).ToList();
            var formValues = new List<KeyValuePair<string, string>>();
            foreach (var id in batch)
            {
                formValues.Add(new KeyValuePair<string, string>("track-ids", id));
            }

            using var content = new FormUrlEncodedContent(formValues);
            using var response = await SendRequestAsync(HttpMethod.Post, "tracks", content, cancellationToken).ConfigureAwait(false);

            if (response == null || !response.IsSuccessStatusCode)
            {
                continue;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var apiResult = JsonSerializer.Deserialize<YandexApiResponse<List<Track>>>(json, _jsonOptions);

            if (apiResult?.Result != null)
            {
                allTracks.AddRange(apiResult.Result);
            }
        }

        return allTracks;
    }

    public async Task<IReadOnlyList<Playlist>> GetUserPlaylistsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var allPlaylists = new List<Playlist>();

        // 1. User's created playlists
        try
        {
            using var response = await SendRequestAsync(HttpMethod.Get, $"users/{userId}/playlists/list", null, cancellationToken).ConfigureAwait(false);
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var apiResult = JsonSerializer.Deserialize<YandexApiResponse<List<Playlist>>>(json, _jsonOptions);
                if (apiResult?.Result != null)
                {
                    allPlaylists.AddRange(apiResult.Result);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GetUserPlaylists warning] {ex.Message}");
        }

        // 2. User's liked / saved playlists
        try
        {
            using var likedResponse = await SendRequestAsync(HttpMethod.Get, $"users/{userId}/likes/playlists", null, cancellationToken).ConfigureAwait(false);
            if (likedResponse != null && likedResponse.IsSuccessStatusCode)
            {
                var json = await likedResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("result", out var resEl) && resEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in resEl.EnumerateArray())
                    {
                        JsonElement target = item;
                        if (item.TryGetProperty("playlist", out var plEl))
                        {
                            target = plEl;
                        }

                        var pl = JsonSerializer.Deserialize<Playlist>(target.GetRawText(), _jsonOptions);
                        if (pl != null)
                        {
                            pl.IsLiked = true;
                            lock (_likedPlaylistKeys)
                            {
                                _likedPlaylistKeys.Add(pl.PlaylistKey);
                            }

                            var existing = allPlaylists.FirstOrDefault(e => e.Kind == pl.Kind && e.Uid == pl.Uid);
                            if (existing != null)
                            {
                                existing.IsLiked = true;
                            }
                            else
                            {
                                allPlaylists.Add(pl);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GetLikedPlaylists warning] {ex.Message}");
        }

        foreach (var playlist in allPlaylists)
        {
            playlist.IsLiked = IsPlaylistLiked(playlist.Uid, playlist.Kind);
        }

        return allPlaylists;
    }

    public async Task<Playlist?> CreatePlaylistAsync(
        string title,
        string? description = null,
        bool isPublic = true,
        CancellationToken cancellationToken = default)
    {
        var userId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        try
        {
            var pairs = new List<KeyValuePair<string, string>>
            {
                new("title", title),
                new("visibility", isPublic ? "public" : "private")
            };
            if (!string.IsNullOrWhiteSpace(description))
            {
                pairs.Add(new("description", description));
            }

            var content = new FormUrlEncodedContent(pairs);

            using var response = await SendRequestAsync(
                HttpMethod.Post,
                $"users/{userId}/playlists/create",
                content,
                cancellationToken).ConfigureAwait(false);

            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var apiResult = JsonSerializer.Deserialize<YandexApiResponse<Playlist>>(json, _jsonOptions);
                return apiResult?.Result;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CreatePlaylistAsync error] {ex.Message}");
        }

        return null;
    }

    private void InvalidatePlaylistCache(long ownerUid, int kind)
    {
        _playlistWithTracksCache.TryRemove($"{ownerUid}:{kind}", out _);
        _playlistWithTracksCache.TryRemove($"{ownerUid}_{kind}", out _);
    }

    public async Task<bool> DeletePlaylistAsync(
        long ownerUid,
        int kind,
        CancellationToken cancellationToken = default)
    {
        if (ownerUid <= 0)
        {
            var myUserId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
            if (long.TryParse(myUserId, out var parsed))
            {
                ownerUid = parsed;
            }
        }

        try
        {
            using var response = await SendRequestAsync(
                HttpMethod.Post,
                $"users/{ownerUid}/playlists/{kind}/delete",
                null,
                cancellationToken).ConfigureAwait(false);

            if (response != null && response.IsSuccessStatusCode)
            {
                InvalidatePlaylistCache(ownerUid, kind);
                lock (_likedPlaylistKeys) { _likedPlaylistKeys.Remove($"{ownerUid}:{kind}"); }
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DeletePlaylistAsync error] {ex.Message}");
            return false;
        }
    }

    public async Task<bool> RenamePlaylistAsync(
        long ownerUid,
        int kind,
        string newTitle,
        string? newDescription = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var nameContent = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("value", newTitle)
            });

            using var nameResponse = await SendRequestAsync(
                HttpMethod.Post,
                $"users/{ownerUid}/playlists/{kind}/name",
                nameContent,
                cancellationToken).ConfigureAwait(false);

            bool ok = nameResponse != null && nameResponse.IsSuccessStatusCode;

            if (newDescription != null)
            {
                var descContent = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("value", newDescription)
                });

                using var descResponse = await SendRequestAsync(
                    HttpMethod.Post,
                    $"users/{ownerUid}/playlists/{kind}/description",
                    descContent,
                    cancellationToken).ConfigureAwait(false);
            }

            InvalidatePlaylistCache(ownerUid, kind);
            return ok;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RenamePlaylistAsync error] {ex.Message}");
            return false;
        }
    }

    public async Task<bool> RemoveTrackFromPlaylistAsync(
        long ownerUid,
        int kind,
        int trackIndex,
        CancellationToken cancellationToken = default)
    {
        if (ownerUid <= 0)
        {
            var myUserId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
            if (long.TryParse(myUserId, out var parsed))
            {
                ownerUid = parsed;
            }
        }

        try
        {
            var pl = await GetPlaylistWithTracksAsync(ownerUid, kind, cancellationToken).ConfigureAwait(false);
            int rev = pl?.Revision ?? 1;

            var diffJson = $"[{{\"op\":\"delete\",\"from\":{trackIndex},\"to\":{trackIndex + 1}}}]";
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("diff", diffJson),
                new KeyValuePair<string, string>("revision", rev.ToString())
            });

            using var response = await SendRequestAsync(
                HttpMethod.Post,
                $"users/{ownerUid}/playlists/{kind}/change-relative",
                content,
                cancellationToken).ConfigureAwait(false);

            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var apiResult = JsonSerializer.Deserialize<YandexApiResponse<Playlist>>(json, _jsonOptions);
                if (apiResult?.Result != null && pl != null)
                {
                    pl.Revision = apiResult.Result.Revision;
                }
                InvalidatePlaylistCache(ownerUid, kind);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RemoveTrackFromPlaylistAsync error] {ex.Message}");
            return false;
        }
    }

    public async Task<bool> AddTracksToPlaylistAsync(
        long ownerUid,
        int kind,
        IEnumerable<(string TrackId, string? AlbumId)> tracks,
        CancellationToken cancellationToken = default)
    {
        if (ownerUid <= 0)
        {
            var myUserId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
            if (long.TryParse(myUserId, out var parsed))
            {
                ownerUid = parsed;
            }
        }

        var trackList = tracks.ToList();
        if (trackList.Count == 0) return true;

        var pl = await GetPlaylistWithTracksAsync(ownerUid, kind, cancellationToken).ConfigureAwait(false);
        int currentRevision = pl?.Revision ?? 1;

        bool allSuccess = true;
        foreach (var chunk in trackList.Chunk(50))
        {
            try
            {
                var tracksJson = string.Join(",", chunk.Select(t =>
                    t.AlbumId != null
                        ? $"{{\"id\":\"{t.TrackId}\",\"albumId\":\"{t.AlbumId}\"}}"
                        : $"{{\"id\":\"{t.TrackId}\"}}"));

                var diffJson = $"[{{\"op\":\"insert\",\"at\":0,\"tracks\":[{tracksJson}]}}]";

                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("diff", diffJson),
                    new KeyValuePair<string, string>("revision", currentRevision.ToString())
                });

                using var response = await SendRequestAsync(
                    HttpMethod.Post,
                    $"users/{ownerUid}/playlists/{kind}/change-relative",
                    content,
                    cancellationToken).ConfigureAwait(false);

                if (response != null && response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    var apiResult = JsonSerializer.Deserialize<YandexApiResponse<Playlist>>(json, _jsonOptions);
                    if (apiResult?.Result != null)
                    {
                        currentRevision = apiResult.Result.Revision > 0 ? apiResult.Result.Revision : currentRevision + 1;
                        if (pl != null) pl.Revision = currentRevision;
                    }
                    else
                    {
                        currentRevision++;
                    }

                    InvalidatePlaylistCache(ownerUid, kind);
                }
                else
                {
                    string? errBody = response != null ? await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false) : null;
                    System.Diagnostics.Debug.WriteLine($"[AddTracksToPlaylistAsync failed] Status: {response?.StatusCode}, Body: {errBody}");
                    allSuccess = false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AddTracksToPlaylistAsync error] {ex.Message}");
                allSuccess = false;
            }
        }

        return allSuccess;
    }

    public async Task<bool> AddTrackToPlaylistAsync(
        long ownerUid,
        int kind,
        string trackId,
        string? albumId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var pl = await GetPlaylistWithTracksAsync(ownerUid, kind, cancellationToken).ConfigureAwait(false);
            int rev = pl?.Revision ?? 1;

            var diffJson = albumId != null
                ? $"[{{\"op\":\"insert\",\"at\":0,\"tracks\":[{{\"id\":\"{trackId}\",\"albumId\":\"{albumId}\"}}]}}]"
                : $"[{{\"op\":\"insert\",\"at\":0,\"tracks\":[{{\"id\":\"{trackId}\"}}]}}]";

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("diff", diffJson),
                new KeyValuePair<string, string>("revision", rev.ToString())
            });

            using var response = await SendRequestAsync(
                HttpMethod.Post,
                $"users/{ownerUid}/playlists/{kind}/change-relative",
                content,
                cancellationToken).ConfigureAwait(false);

            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var apiResult = JsonSerializer.Deserialize<YandexApiResponse<Playlist>>(json, _jsonOptions);
                if (apiResult?.Result != null && pl != null)
                {
                    pl.Revision = apiResult.Result.Revision;
                }
                InvalidatePlaylistCache(ownerUid, kind);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AddTrackToPlaylist Error] {ex.Message}");
            return false;
        }
    }

    public async Task<IReadOnlyList<Album>> GetAlbumsAsync(IEnumerable<long> albumIds, CancellationToken cancellationToken = default)
    {
        var idList = albumIds.Distinct().ToList();
        if (idList.Count == 0) return Array.Empty<Album>();

        var result = new List<Album>();
        const int chunkSize = 50;

        for (int i = 0; i < idList.Count; i += chunkSize)
        {
            var chunk = idList.Skip(i).Take(chunkSize);
            string idsStr = string.Join(",", chunk);
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("album-ids", idsStr)
            });

            try
            {
                using var response = await SendRequestAsync(HttpMethod.Post, "albums", content, cancellationToken).ConfigureAwait(false);
                if (response != null && response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    var apiResult = JsonSerializer.Deserialize<YandexApiResponse<List<Album>>>(json, _jsonOptions);
                    if (apiResult?.Result != null)
                    {
                        result.AddRange(apiResult.Result);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GetAlbums Warning] {ex.Message}");
            }
        }

        return result;
    }

    public async Task<Album?> GetAlbumAsync(long albumId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await SendRequestAsync(HttpMethod.Get, $"albums/{albumId}", null, cancellationToken).ConfigureAwait(false);
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var apiResult = JsonSerializer.Deserialize<YandexApiResponse<Album>>(json, _jsonOptions);
                if (apiResult?.Result != null && !apiResult.Result.IsNotFound && !string.IsNullOrWhiteSpace(apiResult.Result.Title))
                {
                    apiResult.Result.IsLiked = IsAlbumLiked(apiResult.Result.Id) || IsAlbumPresaved(apiResult.Result.Id);
                    return apiResult.Result;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GetAlbum Warning] {ex.Message}");
        }

        var albums = await GetAlbumsAsync(new[] { albumId }, cancellationToken).ConfigureAwait(false);
        var valid = albums.FirstOrDefault(a => !a.IsNotFound && !string.IsNullOrWhiteSpace(a.Title));
        return valid;
    }

    public bool IsAlbumPresaved(long albumId)
    {
        lock (_presavedAlbumIds)
        {
            return _presavedAlbumIds.Contains(albumId);
        }
    }

    public IReadOnlyCollection<Album> GetKnownUpcomingAlbums() => _knownUpcomingAlbums.Values.ToList();

    public async Task<bool> AddAlbumPresaveAsync(long albumId, CancellationToken cancellationToken = default)
    {
        lock (_presavedAlbumIds)
        {
            _presavedAlbumIds.Add(albumId);
        }
        lock (_likedAlbumIds)
        {
            _likedAlbumIds.Add(albumId);
        }
        AlbumLikeChanged?.Invoke(this, (albumId, true));

        bool success = false;
        try
        {
            string? userId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(userId))
            {
                var formValues = new List<KeyValuePair<string, string>>
                {
                    new("album_id", albumId.ToString()),
                    new("likeAfterRelease", "true")
                };
                using var content = new FormUrlEncodedContent(formValues);
                using var response = await SendRequestAsync(HttpMethod.Post, $"users/{userId}/presaves/add", content, cancellationToken).ConfigureAwait(false);
                if (response != null && response.IsSuccessStatusCode)
                {
                    success = true;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AddAlbumPresave Warning] {ex.Message}");
        }

        if (!success)
        {
            lock (_presavedAlbumIds) { _presavedAlbumIds.Remove(albumId); }
            lock (_likedAlbumIds) { _likedAlbumIds.Remove(albumId); }
            AlbumLikeChanged?.Invoke(this, (albumId, false));
            return false;
        }

        return true;
    }

    public async Task<bool> RemoveAlbumPresaveAsync(long albumId, CancellationToken cancellationToken = default)
    {
        lock (_presavedAlbumIds)
        {
            _presavedAlbumIds.Remove(albumId);
        }
        lock (_likedAlbumIds)
        {
            _likedAlbumIds.Remove(albumId);
        }
        AlbumLikeChanged?.Invoke(this, (albumId, false));

        bool success = false;
        try
        {
            string? userId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(userId))
            {
                var formValues = new List<KeyValuePair<string, string>>
                {
                    new("album_id", albumId.ToString())
                };
                using var content = new FormUrlEncodedContent(formValues);
                using var response = await SendRequestAsync(HttpMethod.Post, $"users/{userId}/presaves/remove", content, cancellationToken).ConfigureAwait(false);
                if (response != null && response.IsSuccessStatusCode)
                {
                    success = true;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RemoveAlbumPresave Warning] {ex.Message}");
        }

        if (!success)
        {
            lock (_presavedAlbumIds) { _presavedAlbumIds.Add(albumId); }
            lock (_likedAlbumIds) { _likedAlbumIds.Add(albumId); }
            AlbumLikeChanged?.Invoke(this, (albumId, true));
            return false;
        }

        return true;
    }

    public async Task<PresavesResult?> GetPresavesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            string? userId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(userId)) return null;

            using var response = await SendRequestAsync(HttpMethod.Get, $"users/{userId}/presaves?include_released=true&include_upcoming=true", null, cancellationToken).ConfigureAwait(false);
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var apiResult = JsonSerializer.Deserialize<YandexApiResponse<PresavesResult>>(json, _jsonOptions);
                var res = apiResult?.Result;
                if (res != null)
                {
                    lock (_presavedAlbumIds)
                    {
                        _presavedAlbumIds.Clear();
                        if (res.UpcomingAlbums != null)
                        {
                            foreach (var alb in res.UpcomingAlbums)
                            {
                                alb.IsPreorder = true;
                                alb.IsLiked = true;
                                _presavedAlbumIds.Add(alb.Id);
                                _knownUpcomingAlbums[alb.Id] = alb;
                            }
                        }
                    }
                    return res;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GetPresaves Warning] {ex.Message}");
        }
        return null;
    }

    public async Task<IReadOnlyList<Album>> GetLikedAlbumsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var albumIds = new List<long>();
        var presavedAlbums = new List<Album>();

        // 1. Load user's pre-saved upcoming albums
        try
        {
            var presaves = await GetPresavesAsync(cancellationToken).ConfigureAwait(false);
            if (presaves?.UpcomingAlbums != null)
            {
                foreach (var ua in presaves.UpcomingAlbums)
                {
                    ua.IsPreorder = true;
                    ua.IsLiked = true;
                    presavedAlbums.Add(ua);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GetLikedAlbums Presaves Warning] {ex.Message}");
        }

        // 2. Load standard released liked albums
        try
        {
            using var response = await SendRequestAsync(HttpMethod.Get, $"users/{userId}/likes/albums", null, cancellationToken).ConfigureAwait(false);
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("result", out var resEl) && resEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in resEl.EnumerateArray())
                    {
                        if (item.TryGetProperty("album", out var albEl) && albEl.TryGetProperty("id", out var subId) && subId.TryGetInt64(out long sid))
                        {
                            albumIds.Add(sid);
                        }
                        else if (item.TryGetProperty("id", out var idEl) && idEl.TryGetInt64(out long id))
                        {
                            albumIds.Add(id);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GetLikedAlbums warning] {ex.Message}");
        }

        lock (_likedAlbumIds)
        {
            _likedAlbumIds.Clear();
            foreach (var pa in presavedAlbums)
            {
                _likedAlbumIds.Add(pa.Id);
            }
            foreach (var id in albumIds)
            {
                _likedAlbumIds.Add(id);
            }
        }

        var result = new List<Album>();

        // Pre-saved upcoming albums are shown at the very top of Liked Albums
        foreach (var pa in presavedAlbums)
        {
            result.Add(pa);
        }

        if (albumIds.Count > 0)
        {
            var fullAlbums = await GetAlbumsAsync(albumIds, cancellationToken).ConfigureAwait(false);
            var albumDict = fullAlbums.ToDictionary(a => a.Id);

            foreach (var id in albumIds)
            {
                if (albumDict.TryGetValue(id, out var album))
                {
                    if (result.All(x => x.Id != album.Id))
                    {
                        album.IsLiked = true;
                        result.Add(album);
                    }
                }
            }
        }

        return result;
    }

    public bool IsAlbumLiked(long albumId)
    {
        lock (_likedAlbumIds)
        {
            if (_likedAlbumIds.Contains(albumId)) return true;
        }
        lock (_presavedAlbumIds)
        {
            return _presavedAlbumIds.Contains(albumId);
        }
    }

    public async Task<bool> LikeAlbumAsync(long albumId, CancellationToken cancellationToken = default)
    {
        lock (_likedAlbumIds)
        {
            _likedAlbumIds.Add(albumId);
        }
        AlbumLikeChanged?.Invoke(this, (albumId, true));

        try
        {
            string? userId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(userId))
            {
                lock (_likedAlbumIds) { _likedAlbumIds.Remove(albumId); }
                AlbumLikeChanged?.Invoke(this, (albumId, false));
                return false;
            }

            var formValues = new List<KeyValuePair<string, string>>
            {
                new("album-id", albumId.ToString()),
                new("album-ids", albumId.ToString())
            };
            using var content = new FormUrlEncodedContent(formValues);
            using var response = await SendRequestAsync(HttpMethod.Post, $"users/{userId}/likes/albums/add-multiple", content, cancellationToken).ConfigureAwait(false);

            if (response != null && response.IsSuccessStatusCode)
            {
                return true;
            }

            using var fallbackContent = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("album-id", albumId.ToString()) });
            using var fallbackResponse = await SendRequestAsync(HttpMethod.Post, $"users/{userId}/likes/albums/add", fallbackContent, cancellationToken).ConfigureAwait(false);
            if (fallbackResponse != null && fallbackResponse.IsSuccessStatusCode)
            {
                return true;
            }

            lock (_likedAlbumIds) { _likedAlbumIds.Remove(albumId); }
            AlbumLikeChanged?.Invoke(this, (albumId, false));
            return false;
        }
        catch
        {
            lock (_likedAlbumIds) { _likedAlbumIds.Remove(albumId); }
            AlbumLikeChanged?.Invoke(this, (albumId, false));
            throw;
        }
    }

    public async Task<bool> UnlikeAlbumAsync(long albumId, CancellationToken cancellationToken = default)
    {
        lock (_likedAlbumIds)
        {
            _likedAlbumIds.Remove(albumId);
        }
        AlbumLikeChanged?.Invoke(this, (albumId, false));

        try
        {
            string? userId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(userId))
            {
                lock (_likedAlbumIds) { _likedAlbumIds.Add(albumId); }
                AlbumLikeChanged?.Invoke(this, (albumId, true));
                return false;
            }

            var formValues = new List<KeyValuePair<string, string>>
            {
                new("album-id", albumId.ToString()),
                new("album-ids", albumId.ToString())
            };
            using var content = new FormUrlEncodedContent(formValues);
            using var response = await SendRequestAsync(HttpMethod.Post, $"users/{userId}/likes/albums/remove", content, cancellationToken).ConfigureAwait(false);

            if (response != null && response.IsSuccessStatusCode)
            {
                return true;
            }

            using var fallbackResponse = await SendRequestAsync(HttpMethod.Post, $"users/{userId}/likes/albums/{albumId}/remove", null, cancellationToken).ConfigureAwait(false);
            if (fallbackResponse != null && fallbackResponse.IsSuccessStatusCode)
            {
                return true;
            }

            lock (_likedAlbumIds) { _likedAlbumIds.Add(albumId); }
            AlbumLikeChanged?.Invoke(this, (albumId, true));
            return false;
        }
        catch
        {
            lock (_likedAlbumIds) { _likedAlbumIds.Add(albumId); }
            AlbumLikeChanged?.Invoke(this, (albumId, true));
            throw;
        }
    }

    public async Task EnsureLikedAlbumsLoadedAsync(CancellationToken cancellationToken = default)
    {
        lock (_likedAlbumIds)
        {
            if (_likedAlbumIds.Count > 0) return;
        }

        string? userId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(userId)) return;

        try
        {
            using var response = await SendRequestAsync(HttpMethod.Get, $"users/{userId}/likes/albums", null, cancellationToken).ConfigureAwait(false);
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("result", out var resEl) && resEl.ValueKind == JsonValueKind.Array)
                {
                    lock (_likedAlbumIds)
                    {
                        foreach (var item in resEl.EnumerateArray())
                        {
                            if (item.TryGetProperty("album", out var albEl) && albEl.TryGetProperty("id", out var subId) && subId.TryGetInt64(out long sid))
                            {
                                _likedAlbumIds.Add(sid);
                            }
                            else if (item.TryGetProperty("id", out var idEl) && idEl.TryGetInt64(out long id))
                            {
                                _likedAlbumIds.Add(id);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EnsureLikedAlbumsLoaded warning] {ex.Message}");
        }

        try
        {
            await GetPresavesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EnsurePresavesLoaded warning] {ex.Message}");
        }
    }

    public bool IsPlaylistLiked(long ownerUid, int kind)
    {
        lock (_likedPlaylistKeys)
        {
            return _likedPlaylistKeys.Contains($"{ownerUid}:{kind}");
        }
    }

    public async Task EnsureLikedPlaylistsLoadedAsync(CancellationToken cancellationToken = default)
    {
        lock (_likedPlaylistKeys)
        {
            if (_likedPlaylistKeys.Count > 0) return;
        }

        string? userId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(userId)) return;

        try
        {
            using var likedResponse = await SendRequestAsync(HttpMethod.Get, $"users/{userId}/likes/playlists", null, cancellationToken).ConfigureAwait(false);
            if (likedResponse != null && likedResponse.IsSuccessStatusCode)
            {
                var json = await likedResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("result", out var resEl) && resEl.ValueKind == JsonValueKind.Array)
                {
                    lock (_likedPlaylistKeys)
                    {
                        foreach (var item in resEl.EnumerateArray())
                        {
                            long pUid = 0;
                            int pKind = 0;

                            JsonElement target = item;
                            if (item.TryGetProperty("playlist", out var plEl))
                            {
                                target = plEl;
                            }

                            if (target.TryGetProperty("uid", out var uidEl) && uidEl.TryGetInt64(out long uid))
                            {
                                pUid = uid;
                            }
                            else if (target.TryGetProperty("owner", out var ownerEl) && ownerEl.TryGetProperty("uid", out var oUidEl) && oUidEl.TryGetInt64(out long oUid))
                            {
                                pUid = oUid;
                            }

                            if (target.TryGetProperty("kind", out var kindEl) && kindEl.TryGetInt32(out int kind))
                            {
                                pKind = kind;
                            }

                            if (pUid > 0 && pKind > 0)
                            {
                                _likedPlaylistKeys.Add($"{pUid}:{pKind}");
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EnsureLikedPlaylistsLoaded warning] {ex.Message}");
        }
    }

    public async Task<bool> LikePlaylistAsync(long ownerUid, int kind, CancellationToken cancellationToken = default)
    {
        string key = $"{ownerUid}:{kind}";
        lock (_likedPlaylistKeys)
        {
            _likedPlaylistKeys.Add(key);
        }
        PlaylistLikeChanged?.Invoke(this, (ownerUid, kind, true));

        try
        {
            string? userId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(userId))
            {
                lock (_likedPlaylistKeys) { _likedPlaylistKeys.Remove(key); }
                PlaylistLikeChanged?.Invoke(this, (ownerUid, kind, false));
                return false;
            }

            var formValues = new List<KeyValuePair<string, string>>
            {
                new("playlist-id", key),
                new("playlist-ids", key)
            };
            using var content = new FormUrlEncodedContent(formValues);
            using var response = await SendRequestAsync(HttpMethod.Post, $"users/{userId}/likes/playlists/add-multiple", content, cancellationToken).ConfigureAwait(false);

            if (response != null && response.IsSuccessStatusCode)
            {
                return true;
            }

            using var fallbackContent = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("playlist-id", key) });
            using var fallbackResponse = await SendRequestAsync(HttpMethod.Post, $"users/{userId}/likes/playlists/add", fallbackContent, cancellationToken).ConfigureAwait(false);
            if (fallbackResponse != null && fallbackResponse.IsSuccessStatusCode)
            {
                return true;
            }

            lock (_likedPlaylistKeys) { _likedPlaylistKeys.Remove(key); }
            PlaylistLikeChanged?.Invoke(this, (ownerUid, kind, false));
            return false;
        }
        catch
        {
            lock (_likedPlaylistKeys) { _likedPlaylistKeys.Remove(key); }
            PlaylistLikeChanged?.Invoke(this, (ownerUid, kind, false));
            throw;
        }
    }

    public async Task<bool> UnlikePlaylistAsync(long ownerUid, int kind, CancellationToken cancellationToken = default)
    {
        string key = $"{ownerUid}:{kind}";
        lock (_likedPlaylistKeys)
        {
            _likedPlaylistKeys.Remove(key);
        }
        PlaylistLikeChanged?.Invoke(this, (ownerUid, kind, false));

        try
        {
            string? userId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(userId))
            {
                lock (_likedPlaylistKeys) { _likedPlaylistKeys.Add(key); }
                PlaylistLikeChanged?.Invoke(this, (ownerUid, kind, true));
                return false;
            }

            var formValues = new List<KeyValuePair<string, string>>
            {
                new("playlist-id", key),
                new("playlist-ids", key)
            };
            using var content = new FormUrlEncodedContent(formValues);
            using var response = await SendRequestAsync(HttpMethod.Post, $"users/{userId}/likes/playlists/remove", content, cancellationToken).ConfigureAwait(false);

            if (response != null && response.IsSuccessStatusCode)
            {
                return true;
            }

            using var fallbackResponse = await SendRequestAsync(HttpMethod.Post, $"users/{userId}/likes/playlists/{kind}/remove", null, cancellationToken).ConfigureAwait(false);
            if (fallbackResponse != null && fallbackResponse.IsSuccessStatusCode)
            {
                return true;
            }

            lock (_likedPlaylistKeys) { _likedPlaylistKeys.Add(key); }
            PlaylistLikeChanged?.Invoke(this, (ownerUid, kind, true));
            return false;
        }
        catch
        {
            lock (_likedPlaylistKeys) { _likedPlaylistKeys.Add(key); }
            PlaylistLikeChanged?.Invoke(this, (ownerUid, kind, true));
            throw;
        }
    }

    public bool IsArtistLiked(long artistId)
    {
        lock (_likedArtistIds)
        {
            return _likedArtistIds.Contains(artistId);
        }
    }

    public async Task EnsureLikedArtistsLoadedAsync(CancellationToken cancellationToken = default)
    {
        lock (_likedArtistIds)
        {
            if (_likedArtistIds.Count > 0) return;
        }

        string? userId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(userId)) return;

        try
        {
            using var response = await SendRequestAsync(HttpMethod.Get, $"users/{userId}/likes/artists", null, cancellationToken).ConfigureAwait(false);
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("result", out var resEl) && resEl.ValueKind == JsonValueKind.Array)
                {
                    lock (_likedArtistIds)
                    {
                        foreach (var item in resEl.EnumerateArray())
                        {
                            if (item.TryGetProperty("artist", out var artEl) && artEl.TryGetProperty("id", out var subId) && subId.TryGetInt64(out long sid))
                            {
                                _likedArtistIds.Add(sid);
                            }
                            else if (item.TryGetProperty("id", out var idEl) && idEl.TryGetInt64(out long id))
                            {
                                _likedArtistIds.Add(id);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EnsureLikedArtistsLoaded warning] {ex.Message}");
        }
    }

    public async Task<bool> LikeArtistAsync(long artistId, CancellationToken cancellationToken = default)
    {
        lock (_likedArtistIds)
        {
            _likedArtistIds.Add(artistId);
        }
        ArtistLikeChanged?.Invoke(this, (artistId, true));

        try
        {
            string? userId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(userId))
            {
                lock (_likedArtistIds) { _likedArtistIds.Remove(artistId); }
                ArtistLikeChanged?.Invoke(this, (artistId, false));
                return false;
            }

            var formValues = new List<KeyValuePair<string, string>>
            {
                new("artist-id", artistId.ToString())
            };
            using var content = new FormUrlEncodedContent(formValues);
            using var response = await SendRequestAsync(HttpMethod.Post, $"users/{userId}/likes/artists/add", content, cancellationToken).ConfigureAwait(false);

            if (response != null && response.IsSuccessStatusCode)
            {
                return true;
            }

            lock (_likedArtistIds) { _likedArtistIds.Remove(artistId); }
            ArtistLikeChanged?.Invoke(this, (artistId, false));
            return false;
        }
        catch
        {
            lock (_likedArtistIds) { _likedArtistIds.Remove(artistId); }
            ArtistLikeChanged?.Invoke(this, (artistId, false));
            throw;
        }
    }

    public async Task<bool> UnlikeArtistAsync(long artistId, CancellationToken cancellationToken = default)
    {
        lock (_likedArtistIds)
        {
            _likedArtistIds.Remove(artistId);
        }
        ArtistLikeChanged?.Invoke(this, (artistId, false));

        try
        {
            string? userId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(userId))
            {
                lock (_likedArtistIds) { _likedArtistIds.Add(artistId); }
                ArtistLikeChanged?.Invoke(this, (artistId, true));
                return false;
            }

            var formValues = new List<KeyValuePair<string, string>>
            {
                new("artist-id", artistId.ToString())
            };
            using var content = new FormUrlEncodedContent(formValues);
            using var response = await SendRequestAsync(HttpMethod.Post, $"users/{userId}/likes/artists/remove", content, cancellationToken).ConfigureAwait(false);

            if (response != null && response.IsSuccessStatusCode)
            {
                return true;
            }

            lock (_likedArtistIds) { _likedArtistIds.Add(artistId); }
            ArtistLikeChanged?.Invoke(this, (artistId, true));
            return false;
        }
        catch
        {
            lock (_likedArtistIds) { _likedArtistIds.Add(artistId); }
            ArtistLikeChanged?.Invoke(this, (artistId, true));
            throw;
        }
    }

    private AudioQualityPreference _preferredQuality = AudioQualityPreference.AacPreferred;
    public AudioQualityPreference PreferredQuality
    {
        get => _preferredQuality;
        set
        {
            if (_preferredQuality != value)
            {
                _preferredQuality = value;
                _streamInfoCache.Clear();
            }
        }
    }

    public async Task<string?> GetTrackDownloadUrlAsync(string trackId, CancellationToken cancellationToken = default)
    {
        var (url, _) = await GetTrackStreamInfoAsync(trackId, cancellationToken).ConfigureAwait(false);
        return url;
    }

    public async Task<(string? Url, StreamAudioQuality? Quality)> GetTrackStreamInfoAsync(string trackId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(trackId))
        {
            return (null, null);
        }

        if (_streamInfoCache.TryGetValue(trackId, out var cached) && cached.Expiry > DateTime.UtcNow)
        {
            return (cached.Url, cached.Quality);
        }

        using var downloadInfoResponse = await SendRequestAsync(HttpMethod.Get, $"tracks/{trackId}/download-info", null, cancellationToken).ConfigureAwait(false);
        if (downloadInfoResponse == null || !downloadInfoResponse.IsSuccessStatusCode)
        {
            if (downloadInfoResponse?.StatusCode == HttpStatusCode.Forbidden)
            {
                RegionRestrictionDetected?.Invoke(this, EventArgs.Empty);
            }
            return (null, null);
        }

        var json = await downloadInfoResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (json.Contains("not-available-for-other-regions", StringComparison.OrdinalIgnoreCase))
        {
            RegionRestrictionDetected?.Invoke(this, EventArgs.Empty);
            return (null, null);
        }

        var apiResult = JsonSerializer.Deserialize<YandexApiResponse<List<DownloadInfo>>>(json, _jsonOptions);

        if (apiResult?.Result == null || apiResult.Result.Count == 0)
        {
            return (null, null);
        }

        // Filter out 30-second preview tracks if full tracks exist
        var candidates = apiResult.Result.Where(x => !x.Preview).ToList();
        if (candidates.Count == 0)
        {
            candidates = apiResult.Result;
        }

        DownloadInfo? selectedInfo = null;

        switch (PreferredQuality)
        {
            case AudioQualityPreference.AacPreferred:
                selectedInfo = candidates
                    .OrderByDescending(x => string.Equals(x.Codec, "aac", StringComparison.OrdinalIgnoreCase) && x.BitrateInKbps >= 192)
                    .ThenByDescending(x => string.Equals(x.Codec, "mp3", StringComparison.OrdinalIgnoreCase) && x.BitrateInKbps == 320)
                    .ThenByDescending(x => string.Equals(x.Codec, "aac", StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(x => x.BitrateInKbps)
                    .FirstOrDefault();
                break;

            case AudioQualityPreference.Mp3Preferred:
                selectedInfo = candidates
                    .OrderByDescending(x => string.Equals(x.Codec, "mp3", StringComparison.OrdinalIgnoreCase) && x.BitrateInKbps == 320)
                    .ThenByDescending(x => string.Equals(x.Codec, "mp3", StringComparison.OrdinalIgnoreCase) && x.BitrateInKbps == 192)
                    .ThenByDescending(x => string.Equals(x.Codec, "aac", StringComparison.OrdinalIgnoreCase) && x.BitrateInKbps >= 192)
                    .ThenByDescending(x => x.BitrateInKbps)
                    .FirstOrDefault();
                break;

            case AudioQualityPreference.HighestBitrate:
            default:
                selectedInfo = candidates
                    .OrderByDescending(x => x.BitrateInKbps)
                    .ThenByDescending(x => string.Equals(x.Codec, "aac", StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault();
                break;
        }

        if (selectedInfo == null || string.IsNullOrWhiteSpace(selectedInfo.DownloadInfoUrl))
        {
            return (null, null);
        }

        // Download the XML info
        using var xmlResponse = await GetUnauthenticatedDownloadAsync(selectedInfo.DownloadInfoUrl, cancellationToken).ConfigureAwait(false);
        if (!xmlResponse.IsSuccessStatusCode)
        {
            return (null, null);
        }

        var xmlContent = await xmlResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        // Sign and build final direct stream link
        string? directUrl = StreamSigner.SignFromXml(xmlContent);

        var quality = new StreamAudioQuality
        {
            Codec = selectedInfo.Codec,
            BitrateInKbps = selectedInfo.BitrateInKbps
        };

        if (!string.IsNullOrEmpty(directUrl))
        {
            _streamInfoCache[trackId] = (directUrl, quality, DateTime.UtcNow.AddMinutes(45));
        }

        return (directUrl, quality);
    }

    public async Task<RotorStationResult?> GetRotorStationTracksAsync(
        string stationType = "user",
        string stationTag = "onyourwave",
        string? queueTrackId = null,
        CancellationToken cancellationToken = default)
    {
        var endpoint = $"rotor/station/{Uri.EscapeDataString(stationType)}:{Uri.EscapeDataString(stationTag)}/tracks";
        if (!string.IsNullOrWhiteSpace(queueTrackId))
        {
            endpoint += $"?queue={Uri.EscapeDataString(queueTrackId)}";
        }

        using var response = await SendRequestAsync(HttpMethod.Get, endpoint, null, cancellationToken).ConfigureAwait(false);
        if (response == null || !response.IsSuccessStatusCode)
        {
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var apiResult = JsonSerializer.Deserialize<YandexApiResponse<RotorStationResult>>(json, _jsonOptions);
            return apiResult?.Result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Rotor API Error] {ex.Message}");
            return null;
        }
    }

    public async Task SendRotorFeedbackAsync(
        string eventType,
        string stationType,
        string stationTag,
        string trackId,
        string? batchId = null,
        double? totalPlayedSeconds = null,
        CancellationToken cancellationToken = default)
    {
        var endpoint = $"rotor/station/{Uri.EscapeDataString(stationType)}:{Uri.EscapeDataString(stationTag)}/feedback";
        var payload = new RotorFeedbackRequest
        {
            Type = eventType,
            Timestamp = DateTime.UtcNow.ToString("o"),
            TrackId = trackId,
            BatchId = batchId,
            TotalPlayedSeconds = totalPlayedSeconds
        };

        var json = JsonSerializer.Serialize(payload, _jsonOptions);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        using var response = await SendRequestAsync(HttpMethod.Post, endpoint, content, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Playlist>> GetPersonalPlaylistsAsync(CancellationToken cancellationToken = default)
    {
        var playlists = new List<Playlist>();
        try
        {
            using var response = await SendRequestAsync(HttpMethod.Get, "feed", null, cancellationToken).ConfigureAwait(false);
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("result", out var resultEl) &&
                    resultEl.TryGetProperty("generatedPlaylists", out var genEl) &&
                    genEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in genEl.EnumerateArray())
                    {
                        if (item.TryGetProperty("data", out var dataEl))
                        {
                            var playlist = JsonSerializer.Deserialize<Playlist>(dataEl.GetRawText(), _jsonOptions);
                            if (playlist != null)
                            {
                                playlists.Add(playlist);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[API GetPersonalPlaylists Warning] {ex.Message}");
        }

        if (playlists.Count == 0)
        {
            try
            {
                using var landingResponse = await SendRequestAsync(HttpMethod.Get, "landing3?blocks=personal-playlists", null, cancellationToken).ConfigureAwait(false);
                if (landingResponse != null && landingResponse.IsSuccessStatusCode)
                {
                    var json = await landingResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("result", out var resEl) &&
                        resEl.TryGetProperty("blocks", out var blocksEl) &&
                        blocksEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var block in blocksEl.EnumerateArray())
                        {
                            if (block.TryGetProperty("entities", out var entitiesEl) && entitiesEl.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var entity in entitiesEl.EnumerateArray())
                                {
                                    if (entity.TryGetProperty("data", out var dEl))
                                    {
                                        JsonElement target = dEl;
                                        if (dEl.TryGetProperty("data", out var innerEl))
                                        {
                                            target = innerEl;
                                        }
                                        var pl = JsonSerializer.Deserialize<Playlist>(target.GetRawText(), _jsonOptions);
                                        if (pl != null)
                                        {
                                            playlists.Add(pl);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        return playlists;
    }

    public async Task<IReadOnlyList<Playlist>> GetEditorialPlaylistsAsync(CancellationToken cancellationToken = default)
    {
        var playlists = new List<Playlist>();
        try
        {
            using var response = await SendRequestAsync(HttpMethod.Get, "landing3?blocks=new-playlists", null, cancellationToken).ConfigureAwait(false);
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("result", out var resultEl) &&
                    resultEl.TryGetProperty("blocks", out var blocksEl) &&
                    blocksEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var block in blocksEl.EnumerateArray())
                    {
                        if (block.TryGetProperty("entities", out var entitiesEl) && entitiesEl.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var entity in entitiesEl.EnumerateArray())
                            {
                                if (entity.TryGetProperty("data", out var dataEl))
                                {
                                    var pl = JsonSerializer.Deserialize<Playlist>(dataEl.GetRawText(), _jsonOptions);
                                    if (pl != null)
                                    {
                                        playlists.Add(pl);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[API GetEditorialPlaylists Warning] {ex.Message}");
        }

        return playlists;
    }

    public async Task<IReadOnlyList<Album>> GetNewReleasesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. Try full dedicated new-releases endpoint which returns array of IDs (up to 100)
            using var fullResponse = await SendRequestAsync(HttpMethod.Get, "landing3/new-releases", null, cancellationToken).ConfigureAwait(false);
            if (fullResponse != null && fullResponse.IsSuccessStatusCode)
            {
                var fullJson = await fullResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var fullDoc = JsonDocument.Parse(fullJson);
                if (fullDoc.RootElement.TryGetProperty("result", out var resEl) &&
                    resEl.TryGetProperty("newReleases", out var nrEl) &&
                    nrEl.ValueKind == JsonValueKind.Array)
                {
                    var idList = new List<long>();
                    foreach (var item in nrEl.EnumerateArray())
                    {
                        if (item.TryGetInt64(out long albumId))
                        {
                            idList.Add(albumId);
                        }
                    }

                    if (idList.Count > 0)
                    {
                        // Load top 40 new releases to display a rich, smooth horizontal scroll list
                        var loadedAlbums = await GetAlbumsAsync(idList.Take(40), cancellationToken).ConfigureAwait(false);
                        if (loadedAlbums.Count > 0)
                        {
                            return loadedAlbums;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[API GetNewReleases Full Warning] {ex.Message}");
        }

        // 2. Fallback to basic landing3?blocks=new-releases preview block
        var albums = new List<Album>();
        try
        {
            using var response = await SendRequestAsync(HttpMethod.Get, "landing3?blocks=new-releases", null, cancellationToken).ConfigureAwait(false);
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("result", out var resultEl) &&
                    resultEl.TryGetProperty("blocks", out var blocksEl) &&
                    blocksEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var block in blocksEl.EnumerateArray())
                    {
                        if (block.TryGetProperty("entities", out var entitiesEl) && entitiesEl.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var entity in entitiesEl.EnumerateArray())
                            {
                                if (entity.TryGetProperty("data", out var dataEl))
                                {
                                    var album = JsonSerializer.Deserialize<Album>(dataEl.GetRawText(), _jsonOptions);
                                    if (album != null)
                                    {
                                        albums.Add(album);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[API GetNewReleases Warning] {ex.Message}");
        }

        return albums;
    }

    public async Task<IReadOnlyList<Track>> GetChartTracksAsync(CancellationToken cancellationToken = default)
    {
        var tracks = new List<Track>();
        try
        {
            using var response = await SendRequestAsync(HttpMethod.Get, "landing3?blocks=chart", null, cancellationToken).ConfigureAwait(false);
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("result", out var resultEl) &&
                    resultEl.TryGetProperty("blocks", out var blocksEl) &&
                    blocksEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var block in blocksEl.EnumerateArray())
                    {
                        if (block.TryGetProperty("entities", out var entitiesEl) && entitiesEl.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var entity in entitiesEl.EnumerateArray())
                            {
                                if (entity.TryGetProperty("data", out var dataEl) &&
                                    dataEl.TryGetProperty("track", out var trackEl))
                                {
                                    var track = JsonSerializer.Deserialize<Track>(trackEl.GetRawText(), _jsonOptions);
                                    if (track != null)
                                    {
                                        tracks.Add(track);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[API GetChartTracks Warning] {ex.Message}");
        }

        return tracks;
    }

    public async Task<Playlist?> GetPlaylistWithTracksAsync(long ownerUid, int kind, CancellationToken cancellationToken = default)
    {
        if (ownerUid <= 0)
        {
            var myUserId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
            if (long.TryParse(myUserId, out var parsed))
            {
                ownerUid = parsed;
            }
        }

        if (ownerUid <= 0) return null;

        string cacheKey = $"{ownerUid}:{kind}";
        if (_playlistWithTracksCache.TryGetValue(cacheKey, out var cached) && (DateTime.UtcNow - cached.CachedAt).TotalMinutes < 10)
        {
            return cached.Playlist;
        }

        try
        {
            using var response = await SendRequestAsync(HttpMethod.Get, $"users/{ownerUid}/playlists/{kind}?rich-tracks=true", null, cancellationToken).ConfigureAwait(false);
            if (response == null || !response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var apiResult = JsonSerializer.Deserialize<YandexApiResponse<Playlist>>(json, _jsonOptions);
            var playlist = apiResult?.Result;

            if (playlist != null)
            {
                if (playlist.Uid <= 0 && ownerUid > 0)
                {
                    playlist.Uid = ownerUid;
                }

                var missingIds = playlist.Tracks
                    .Where(t => t.Track == null && !string.IsNullOrWhiteSpace(t.Id))
                    .Select(t => t.Id)
                    .ToList();

                if (missingIds.Count > 0)
                {
                    var resolved = await GetTracksAsync(missingIds, cancellationToken).ConfigureAwait(false);
                    var resolvedMap = resolved.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
                    foreach (var item in playlist.Tracks)
                    {
                        if (item.Track == null && resolvedMap.TryGetValue(item.Id, out var found))
                        {
                            item.Track = found;
                        }
                    }
                }

                _playlistWithTracksCache[cacheKey] = (playlist, DateTime.UtcNow);
                _playlistWithTracksCache[$"{ownerUid}_{kind}"] = (playlist, DateTime.UtcNow);
            }

            return playlist;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[API GetPlaylistWithTracks Warning] {ex.Message}");
            return null;
        }
    }

    public async Task<Album?> GetAlbumWithTracksAsync(long albumId, CancellationToken cancellationToken = default)
    {
        if (_albumWithTracksCache.TryGetValue(albumId, out var cached) && (DateTime.UtcNow - cached.CachedAt).TotalMinutes < 15)
        {
            return cached.Album;
        }

        try
        {
            using var response = await SendRequestAsync(HttpMethod.Get, $"albums/{albumId}/with-tracks", null, cancellationToken).ConfigureAwait(false);
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var apiResult = JsonSerializer.Deserialize<YandexApiResponse<Album>>(json, _jsonOptions);
                if (apiResult?.Result != null && !apiResult.Result.IsNotFound && !string.IsNullOrWhiteSpace(apiResult.Result.Title))
                {
                    _albumWithTracksCache[albumId] = (apiResult.Result, DateTime.UtcNow);
                    return apiResult.Result;
                }
            }

            // Fallback: When with-tracks returns 404 or fails (unreleased / preorder albums), fetch album metadata directly
            var fallbackAlbum = await GetAlbumAsync(albumId, cancellationToken).ConfigureAwait(false);
            if (fallbackAlbum != null)
            {
                _albumWithTracksCache[albumId] = (fallbackAlbum, DateTime.UtcNow);
                return fallbackAlbum;
            }

            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[API GetAlbumWithTracks Warning] {ex.Message}");
            var fallbackAlbum = await GetAlbumAsync(albumId, cancellationToken).ConfigureAwait(false);
            if (fallbackAlbum != null)
            {
                _albumWithTracksCache[albumId] = (fallbackAlbum, DateTime.UtcNow);
                return fallbackAlbum;
            }
            return null;
        }
    }

    public async Task<ArtistBriefInfoResult?> GetArtistBriefInfoAsync(long artistId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await SendRequestAsync(HttpMethod.Get, $"artists/{artistId}/brief-info", null, cancellationToken).ConfigureAwait(false);
            if (response == null || !response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var apiResult = JsonSerializer.Deserialize<YandexApiResponse<ArtistBriefInfoResult>>(json, _jsonOptions);
            var res = apiResult?.Result;
            if (res != null)
            {
                if (res.UpcomingAlbum != null)
                {
                    res.UpcomingAlbum.IsPreorder = true;
                    res.UpcomingAlbum.IsLiked = IsAlbumLiked(res.UpcomingAlbum.Id);
                    _knownUpcomingAlbums[res.UpcomingAlbum.Id] = res.UpcomingAlbum;
                }
                if (res.UpcomingAlbums != null)
                {
                    foreach (var ua in res.UpcomingAlbums)
                    {
                        ua.IsPreorder = true;
                        ua.IsLiked = IsAlbumLiked(ua.Id);
                        _knownUpcomingAlbums[ua.Id] = ua;
                    }
                }
            }
            return res;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[API GetArtistBriefInfo Warning] {ex.Message}");
            return null;
        }
    }

    public async Task<List<Album>> GetArtistDirectAlbumsAsync(long artistId, int page = 0, int pageSize = 100, CancellationToken cancellationToken = default)
    {
        try
        {
            var allAlbums = new List<Album>();
            int currentPage = page;
            int total = int.MaxValue;

            while (allAlbums.Count < total && currentPage < 10)
            {
                using var response = await SendRequestAsync(HttpMethod.Get, $"artists/{artistId}/direct-albums?page={currentPage}&page-size={pageSize}", null, cancellationToken).ConfigureAwait(false);
                if (response == null || !response.IsSuccessStatusCode)
                {
                    break;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var apiResult = JsonSerializer.Deserialize<YandexApiResponse<ArtistAlbumsResponse>>(json, _jsonOptions);
                var result = apiResult?.Result;
                if (result?.Albums == null || result.Albums.Count == 0)
                {
                    break;
                }

                foreach (var album in result.Albums)
                {
                    album.IsLiked = IsAlbumLiked(album.Id);
                    allAlbums.Add(album);
                }

                if (result.Pager != null)
                {
                    total = result.Pager.Total;
                }
                else
                {
                    break;
                }

                if (result.Albums.Count < pageSize)
                {
                    break;
                }

                currentPage++;
            }

            return allAlbums;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[API GetArtistDirectAlbums Warning] {ex.Message}");
            return new List<Album>();
        }
    }

    public async Task<List<Album>> GetArtistAlsoAlbumsAsync(long artistId, int page = 0, int pageSize = 100, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await SendRequestAsync(HttpMethod.Get, $"artists/{artistId}/also-albums?page={page}&page-size={pageSize}", null, cancellationToken).ConfigureAwait(false);
            if (response == null || !response.IsSuccessStatusCode)
            {
                return new List<Album>();
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var apiResult = JsonSerializer.Deserialize<YandexApiResponse<ArtistAlbumsResponse>>(json, _jsonOptions);
            var albums = apiResult?.Result?.Albums ?? new List<Album>();
            foreach (var album in albums)
            {
                album.IsLiked = IsAlbumLiked(album.Id);
            }
            return albums;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[API GetArtistAlsoAlbums Warning] {ex.Message}");
            return new List<Album>();
        }
    }

    public async Task<SearchResult?> SearchAsync(
        string query,
        string type = "all",
        int page = 0,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        try
        {
            string endpoint = $"search?text={Uri.EscapeDataString(query.Trim())}&type={type}&page={page}&nocorrect=false";
            using var response = await SendRequestAsync(HttpMethod.Get, endpoint, null, cancellationToken).ConfigureAwait(false);
            if (response == null || !response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var apiResult = JsonSerializer.Deserialize<YandexApiResponse<SearchResult>>(json, _jsonOptions);
            var result = apiResult?.Result;

            if (result != null)
            {
                if (result.Best != null && result.Best.ResultElement.ValueKind == JsonValueKind.Object)
                {
                    var rawText = result.Best.ResultElement.GetRawText();
                    switch (result.Best.Type)
                    {
                        case "artist":
                            result.Best.Artist = JsonSerializer.Deserialize<Artist>(rawText, _jsonOptions);
                            break;
                        case "track":
                            result.Best.Track = JsonSerializer.Deserialize<Track>(rawText, _jsonOptions);
                            break;
                        case "album":
                            result.Best.Album = JsonSerializer.Deserialize<Album>(rawText, _jsonOptions);
                            break;
                        case "playlist":
                            result.Best.Playlist = JsonSerializer.Deserialize<Playlist>(rawText, _jsonOptions);
                            break;
                    }
                }

                if (result.Tracks?.Results != null)
                {
                    foreach (var track in result.Tracks.Results)
                    {
                        track.IsLiked = IsTrackLiked(track.Id);
                    }
                }

                if (result.Best?.Track != null)
                {
                    result.Best.Track.IsLiked = IsTrackLiked(result.Best.Track.Id);
                }

                if (result.Albums?.Results != null)
                {
                    foreach (var album in result.Albums.Results)
                    {
                        album.IsLiked = IsAlbumLiked(album.Id);
                    }
                }

                if (result.Best?.Album != null)
                {
                    result.Best.Album.IsLiked = IsAlbumLiked(result.Best.Album.Id);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[API Search Warning] {ex.Message}");
            return null;
        }
    }

    // URLs returned by the API are data, not authority to receive account credentials.
    private static Task<HttpResponseMessage> GetUnauthenticatedDownloadAsync(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Download URL must use HTTPS.");
        return _downloadHttpClient.GetAsync(uri, cancellationToken);
    }

    private static async Task<string> GetUnauthenticatedDownloadStringAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await GetUnauthenticatedDownloadAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage?> SendRequestAsync(
        HttpMethod method,
        string endpoint,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        var target = new Uri(new Uri(ApiBaseUrl), endpoint);
        if (target.Scheme != Uri.UriSchemeHttps || !string.Equals(target.Host, "api.music.yandex.net", StringComparison.OrdinalIgnoreCase) || target.Port != 443)
            throw new InvalidOperationException("OAuth requests must target the Yandex Music API.");

        HttpRequestMessage CreateRequest()
        {
            var req = new HttpRequestMessage(method, target);
            if (content != null)
            {
                req.Content = content;
            }

            if (!string.IsNullOrWhiteSpace(_token) && req.Headers.Authorization == null)
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("OAuth", _token);
            }
            return req;
        }

        var request = CreateRequest();

        try
        {
            var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                UnauthorizedResponseReceived?.Invoke(this, EventArgs.Empty);
            }
            else if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                YandexBypassProxy.Instance.ReportProxyFailure();
            }
            else if (response.IsSuccessStatusCode)
            {
                YandexBypassProxy.Instance.ReportDirectSuccess();
            }

            return response;
        }
        catch (HttpRequestException) when (!YandexBypassProxy.Instance.FallbackToProxy)
        {
            // Direct connection to Yandex failed (e.g. ISP block); try falling back to system proxy once
            YandexBypassProxy.Instance.ReportDirectFailure();
            if (YandexBypassProxy.Instance.FallbackToProxy)
            {
                using var retryReq = CreateRequest();
                var retryResponse = await _httpClient.SendAsync(retryReq, cancellationToken).ConfigureAwait(false);
                if (retryResponse.StatusCode == HttpStatusCode.Unauthorized)
                {
                    UnauthorizedResponseReceived?.Invoke(this, EventArgs.Empty);
                }
                else if (retryResponse.StatusCode == HttpStatusCode.Forbidden)
                {
                    YandexBypassProxy.Instance.ReportProxyFailure();
                }
                return retryResponse;
            }
            throw;
        }
    }
}