#nullable enable

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.Services.Implementations;

public sealed class FckCensorService : IFckCensorService
{
    private const string RemoteListV2Url = "https://raw.githubusercontent.com/Hazzz895/FckCensorData/main/list_v2.json";
    private const string RemoteListLegacyUrl = "https://raw.githubusercontent.com/Hazzz895/FckCensorData/refs/heads/main/list.json";
    private const string SupabaseApiUrl = "https://pzomqvgckpgkshxhpite.supabase.co/rest/v1/reported_tracks";
    private const string SupabaseKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InB6b21xdmdja3Bna3NoeGhwaXRlIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NzUwNTgzNDEsImV4cCI6MjA5MDYzNDM0MX0.ggCxM-ver3gDWUBWyhSBfy3n7rpdW8jtlxRQVCXkhNg";

    private readonly HttpClient _httpClient = new();
    private readonly string _baseDir;
    private readonly string _tracksDir;
    private readonly string _exceptionsFile;
    private readonly string _reportedFile;
    private readonly string _localTracksFile;
    private readonly string _remoteCacheFile;

    private readonly object _lock = new();

    private Dictionary<string, string> _remoteTracks = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, TrackSpoof> _trackSpoofs = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<long, AlbumSpoof> _albumSpoofs = new();
    private Dictionary<long, ArtistInsertion> _artistInsertions = new();
    private Dictionary<string, string> _trackToAlbumCover = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _localTracks = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _remoteExceptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _reportedTracks = new(StringComparer.OrdinalIgnoreCase);

    private sealed class RemoteListModel
    {
        [JsonPropertyName("tracks")]
        public Dictionary<string, string>? Tracks { get; set; }
    }

    private sealed class SupabaseReportPayload
    {
        [JsonPropertyName("track_id")]
        public long TrackId { get; set; }

        [JsonPropertyName("replaced")]
        public bool Replaced { get; set; }
    }

    public event EventHandler<string>? TrackReplacementChanged;

    public FckCensorService()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _baseDir = Path.Combine(localAppData, "YMusic", "FckCensor");
        _tracksDir = Path.Combine(_baseDir, "tracks");
        _exceptionsFile = Path.Combine(_baseDir, "exceptions.json");
        _reportedFile = Path.Combine(_baseDir, "reported.json");
        _localTracksFile = Path.Combine(_baseDir, "local_tracks.json");
        _remoteCacheFile = Path.Combine(_baseDir, "remote_cache.json");

        Directory.CreateDirectory(_baseDir);
        Directory.CreateDirectory(_tracksDir);

        LoadLocalState();
        _ = InitializeAsync();
    }

    private void LoadLocalState()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_localTracksFile))
                {
                    string json = File.ReadAllText(_localTracksFile);
                    var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (dict != null)
                    {
                        foreach (var kvp in dict)
                        {
                            if (File.Exists(kvp.Value))
                            {
                                _localTracks[kvp.Key] = kvp.Value;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FckCensor] Error loading local_tracks: {ex.Message}");
            }

            try
            {
                if (File.Exists(_exceptionsFile))
                {
                    string json = File.ReadAllText(_exceptionsFile);
                    var list = JsonSerializer.Deserialize<List<string>>(json);
                    if (list != null)
                    {
                        foreach (var id in list) _remoteExceptions.Add(id);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FckCensor] Error loading exceptions: {ex.Message}");
            }

            try
            {
                if (File.Exists(_reportedFile))
                {
                    string json = File.ReadAllText(_reportedFile);
                    var list = JsonSerializer.Deserialize<List<string>>(json);
                    if (list != null)
                    {
                        foreach (var id in list) _reportedTracks.Add(id);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FckCensor] Error loading reported: {ex.Message}");
            }

            try
            {
                if (File.Exists(_remoteCacheFile))
                {
                    string json = File.ReadAllText(_remoteCacheFile);
                    ParseRemoteData(json, out var cachedTracks, out var cachedSpoofs, out var cachedAlbums, out var cachedArtists, out var cachedCovers);
                    if (cachedTracks.Count > 0)
                    {
                        _remoteTracks = cachedTracks;
                        _trackSpoofs = cachedSpoofs;
                        _albumSpoofs = cachedAlbums;
                        _artistInsertions = cachedArtists;
                        _trackToAlbumCover = cachedCovers;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FckCensor] Error loading cached remote tracks: {ex.Message}");
            }
        }
    }

    public async Task InitializeAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            string? v2Json = null;
            try
            {
                var response = await _httpClient.GetAsync(RemoteListV2Url, cts.Token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    v2Json = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FckCensor] Failed to fetch list_v2: {ex.Message}");
            }

            if (!string.IsNullOrEmpty(v2Json))
            {
                ParseRemoteData(v2Json, out var tracksV2, out var spoofsV2, out var albumSpoofsV2, out var artistInsertionsV2, out var trackToCoverV2);
                if (tracksV2.Count > 0)
                {
                    lock (_lock)
                    {
                        _remoteTracks = tracksV2;
                        _trackSpoofs = spoofsV2;
                        _albumSpoofs = albumSpoofsV2;
                        _artistInsertions = artistInsertionsV2;
                        _trackToAlbumCover = trackToCoverV2;
                    }

                    try { File.WriteAllText(_remoteCacheFile, v2Json); } catch { }

                    Debug.WriteLine($"[FckCensor] Successfully loaded {_remoteTracks.Count} tracks from FckCensor v2 list.");
                    TrackReplacementChanged?.Invoke(this, string.Empty);
                    return;
                }
            }

            // Fallback to legacy v1 list if v2 failed
            var legacyResp = await _httpClient.GetAsync(RemoteListLegacyUrl, cts.Token).ConfigureAwait(false);
            if (legacyResp.IsSuccessStatusCode)
            {
                var legacyJson = await legacyResp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                ParseRemoteData(legacyJson, out var legacyTracks, out var legacySpoofs, out var legacyAlbums, out var legacyArtists, out var legacyCovers);
                if (legacyTracks.Count > 0)
                {
                    lock (_lock)
                    {
                        _remoteTracks = legacyTracks;
                        _trackSpoofs = legacySpoofs;
                        _albumSpoofs = legacyAlbums;
                        _artistInsertions = legacyArtists;
                        _trackToAlbumCover = legacyCovers;
                    }
                    try { File.WriteAllText(_remoteCacheFile, legacyJson); } catch { }
                    Debug.WriteLine($"[FckCensor] Loaded {_remoteTracks.Count} tracks from legacy v1 list.");
                    TrackReplacementChanged?.Invoke(this, string.Empty);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FckCensor] Failed to fetch remote track list: {ex.Message}");
        }
    }

    private static void ParseRemoteData(
        string json,
        out Dictionary<string, string> tracks,
        out Dictionary<string, TrackSpoof> spoofs,
        out Dictionary<long, AlbumSpoof> albumSpoofs,
        out Dictionary<long, ArtistInsertion> artistInsertions,
        out Dictionary<string, string> trackToAlbumCover)
    {
        tracks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        spoofs = new Dictionary<string, TrackSpoof>(StringComparer.OrdinalIgnoreCase);
        albumSpoofs = new Dictionary<long, AlbumSpoof>();
        artistInsertions = new Dictionary<long, ArtistInsertion>();
        trackToAlbumCover = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("sources", out var sources) && sources.ValueKind == JsonValueKind.Array)
            {
                foreach (var source in sources.EnumerateArray())
                {
                    // 1. Tracks metadata spoofs
                    if (source.TryGetProperty("tracks", out var metaTracks) && metaTracks.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in metaTracks.EnumerateObject())
                        {
                            string id = prop.Name;
                            var elem = prop.Value;
                            if (elem.ValueKind == JsonValueKind.Object)
                            {
                                var spoof = new TrackSpoof();
                                if (elem.TryGetProperty("title", out var titleProp) && titleProp.ValueKind == JsonValueKind.String)
                                {
                                    spoof.Title = titleProp.GetString();
                                }
                                if (elem.TryGetProperty("coverUri", out var coverProp) && coverProp.ValueKind == JsonValueKind.String)
                                {
                                    spoof.CoverUri = coverProp.GetString();
                                }
                                if (!string.IsNullOrEmpty(spoof.Title) || !string.IsNullOrEmpty(spoof.CoverUri))
                                {
                                    spoofs[id] = spoof;
                                }
                            }
                        }
                    }

                    // 2. Albums spoofs (covers & restored tracklists)
                    if (source.TryGetProperty("albums", out var albumsProp) && albumsProp.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in albumsProp.EnumerateObject())
                        {
                            if (long.TryParse(prop.Name, out long albumId) && prop.Value.ValueKind == JsonValueKind.Object)
                            {
                                var elem = prop.Value;
                                var albumSpoof = new AlbumSpoof();
                                if (elem.TryGetProperty("coverUri", out var cProp) && cProp.ValueKind == JsonValueKind.String)
                                {
                                    albumSpoof.CoverUri = cProp.GetString();
                                }
                                if (elem.TryGetProperty("volumes", out var volsProp) && volsProp.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var volItem in volsProp.EnumerateArray())
                                    {
                                        if (volItem.ValueKind == JsonValueKind.Array)
                                        {
                                            var volList = new List<long>();
                                            foreach (var trackElem in volItem.EnumerateArray())
                                            {
                                                if (trackElem.ValueKind == JsonValueKind.Object && trackElem.TryGetProperty("id", out var idProp))
                                                {
                                                    if (idProp.ValueKind == JsonValueKind.Number) volList.Add(idProp.GetInt64());
                                                    else if (idProp.ValueKind == JsonValueKind.String && long.TryParse(idProp.GetString(), out var tid)) volList.Add(tid);
                                                }
                                                else if (trackElem.ValueKind == JsonValueKind.Number)
                                                {
                                                    volList.Add(trackElem.GetInt64());
                                                }
                                            }
                                            albumSpoof.Volumes.Add(volList);
                                        }
                                    }
                                }
                                albumSpoofs[albumId] = albumSpoof;

                                if (!string.IsNullOrEmpty(albumSpoof.CoverUri))
                                {
                                    foreach (var vol in albumSpoof.Volumes)
                                    {
                                        foreach (var tid in vol)
                                        {
                                            trackToAlbumCover[tid.ToString()] = albumSpoof.CoverUri;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // 3. Artist Insertions
                    if (source.TryGetProperty("artistsInsertions", out var insertionsProp) && insertionsProp.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in insertionsProp.EnumerateObject())
                        {
                            if (long.TryParse(prop.Name, out long artistId) && prop.Value.ValueKind == JsonValueKind.Object)
                            {
                                var elem = prop.Value;
                                if (elem.TryGetProperty("tracks", out var trksProp) && trksProp.ValueKind == JsonValueKind.Array)
                                {
                                    var insertion = new ArtistInsertion();
                                    foreach (var item in trksProp.EnumerateArray())
                                    {
                                        if (item.ValueKind == JsonValueKind.Object)
                                        {
                                            string relId = item.TryGetProperty("releaseId", out var r) ? r.GetString() ?? "" : "";
                                            int idx = item.TryGetProperty("index", out var i) && i.ValueKind == JsonValueKind.Number ? i.GetInt32() : 0;
                                            if (!string.IsNullOrEmpty(relId))
                                            {
                                                insertion.Tracks.Add(new ArtistTrackInsertion { ReleaseId = relId, Index = idx });
                                            }
                                        }
                                    }
                                    if (insertion.Tracks.Count > 0)
                                    {
                                        artistInsertions[artistId] = insertion;
                                    }
                                }
                            }
                        }
                    }

                    if (source.TryGetProperty("tracksStorages", out var storages) && storages.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var storage in storages.EnumerateArray())
                        {
                            string? tmpl = null;
                            if (storage.TryGetProperty("urlTemplate", out var tmplProp) && tmplProp.ValueKind == JsonValueKind.String)
                            {
                                tmpl = tmplProp.GetString();
                            }

                            if (storage.TryGetProperty("trackIds", out var trackIds) && trackIds.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var item in trackIds.EnumerateArray())
                                {
                                    string? tid = null;
                                    if (item.ValueKind == JsonValueKind.Number)
                                    {
                                        tid = item.GetInt64().ToString();
                                    }
                                    else if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out var idProp))
                                    {
                                        tid = idProp.ToString();
                                    }

                                    if (!string.IsNullOrEmpty(tid))
                                    {
                                        string url = !string.IsNullOrEmpty(tmpl)
                                            ? Httpsify(tmpl.Replace("%%", tid))
                                            : string.Empty;

                                        if (!string.IsNullOrEmpty(url))
                                        {
                                            tracks[tid] = url;
                                        }
                                    }
                                }
                            }

                            if (storage.TryGetProperty("tracks", out var stracks) && stracks.ValueKind == JsonValueKind.Object)
                            {
                                foreach (var prop in stracks.EnumerateObject())
                                {
                                    string tid = prop.Name;
                                    var val = prop.Value;
                                    string? raw = null;
                                    if (val.ValueKind == JsonValueKind.String)
                                    {
                                        raw = val.GetString();
                                    }
                                    else if (val.ValueKind == JsonValueKind.Number)
                                    {
                                        raw = val.GetInt64().ToString();
                                    }
                                    else if (val.ValueKind == JsonValueKind.Object && val.TryGetProperty("url", out var urlProp))
                                    {
                                        raw = urlProp.GetString() ?? urlProp.ToString();
                                    }

                                    if (!string.IsNullOrEmpty(raw))
                                    {
                                        string url = raw.Contains("://", StringComparison.Ordinal)
                                            ? raw
                                            : (!string.IsNullOrEmpty(tmpl) ? Httpsify(tmpl.Replace("%%", raw)) : Httpsify(raw));

                                        tracks[tid] = url;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            else if (root.TryGetProperty("tracks", out var legacyTracks) && legacyTracks.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in legacyTracks.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        tracks[prop.Name] = prop.Value.GetString()!;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FckCensor] ParseRemoteData error: {ex.Message}");
        }
    }

    private static string Httpsify(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;
        if (url.Contains("://", StringComparison.Ordinal)) return url;
        return "https://" + url.TrimStart('/');
    }

    public TrackSpoof? GetTrackSpoof(string trackId)
    {
        if (string.IsNullOrWhiteSpace(trackId)) return null;

        lock (_lock)
        {
            if (_remoteExceptions.Contains(trackId))
            {
                return null;
            }

            _trackSpoofs.TryGetValue(trackId, out var spoof);
            if ((spoof == null || string.IsNullOrEmpty(spoof.CoverUri)) && _trackToAlbumCover.TryGetValue(trackId, out var albumCover))
            {
                spoof = new TrackSpoof
                {
                    Title = spoof?.Title,
                    CoverUri = albumCover
                };
            }
            return spoof;
        }
    }

    public AlbumSpoof? GetAlbumSpoof(long albumId)
    {
        lock (_lock)
        {
            return _albumSpoofs.TryGetValue(albumId, out var spoof) ? spoof : null;
        }
    }

    public ArtistInsertion? GetArtistInsertions(long artistId)
    {
        lock (_lock)
        {
            return _artistInsertions.TryGetValue(artistId, out var insertion) ? insertion : null;
        }
    }

    public bool IsReplaced(string trackId)
    {
        if (string.IsNullOrWhiteSpace(trackId)) return false;

        lock (_lock)
        {
            if (_localTracks.ContainsKey(trackId))
            {
                return true;
            }

            if (_remoteExceptions.Contains(trackId))
            {
                return false;
            }

            return _remoteTracks.ContainsKey(trackId);
        }
    }

    public string? GetReplacementUrl(string trackId)
    {
        if (string.IsNullOrWhiteSpace(trackId)) return null;

        lock (_lock)
        {
            if (_localTracks.TryGetValue(trackId, out var localPath))
            {
                if (File.Exists(localPath))
                {
                    return localPath;
                }
            }

            if (_remoteExceptions.Contains(trackId))
            {
                return null;
            }

            if (_remoteTracks.TryGetValue(trackId, out var remoteUrl))
            {
                return remoteUrl;
            }

            return null;
        }
    }

    public async Task<bool> ReplaceTrackWithFileAsync(string trackId, string sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(trackId) || !File.Exists(sourceFilePath))
        {
            return false;
        }

        try
        {
            string ext = Path.GetExtension(sourceFilePath);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".mp3";

            string destFileName = $"{trackId}{ext}";
            string destFilePath = Path.Combine(_tracksDir, destFileName);

            File.Copy(sourceFilePath, destFilePath, true);

            lock (_lock)
            {
                _localTracks[trackId] = destFilePath;
                _remoteExceptions.Remove(trackId);
                SaveLocalTracks();
                SaveExceptions();
            }

            _ = ReportCensorshipAsync(trackId, replaced: true);

            TrackReplacementChanged?.Invoke(this, trackId);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FckCensor] ReplaceTrackWithFileAsync error: {ex.Message}");
            return false;
        }
    }

    public Task<bool> RemoveReplacementAsync(string trackId)
    {
        if (string.IsNullOrWhiteSpace(trackId)) return Task.FromResult(false);

        lock (_lock)
        {
            bool hadLocal = _localTracks.TryGetValue(trackId, out var path);
            if (hadLocal)
            {
                _localTracks.Remove(trackId);
                SaveLocalTracks();
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    try { File.Delete(path); } catch { }
                }
            }
            else if (_remoteTracks.ContainsKey(trackId))
            {
                _remoteExceptions.Add(trackId);
                SaveExceptions();
            }
        }

        TrackReplacementChanged?.Invoke(this, trackId);
        return Task.FromResult(true);
    }

    public async Task<bool> ReportCensorshipAsync(string trackId, bool replaced = false)
    {
        if (string.IsNullOrWhiteSpace(trackId)) return false;
        if (!long.TryParse(trackId, out long numericId)) return false;

        lock (_lock)
        {
            if (_reportedTracks.Contains(trackId))
            {
                return true;
            }
        }

        try
        {
            var payload = new SupabaseReportPayload
            {
                TrackId = numericId,
                Replaced = replaced
            };

            string json = JsonSerializer.Serialize(payload);
            using var request = new HttpRequestMessage(HttpMethod.Post, SupabaseApiUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("apikey", SupabaseKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", SupabaseKey);

            using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                lock (_lock)
                {
                    _reportedTracks.Add(trackId);
                    SaveReported();
                }
                Debug.WriteLine($"[FckCensor] Successfully reported track {trackId} (replaced: {replaced})");
                return true;
            }
            else
            {
                Debug.WriteLine($"[FckCensor] Report failed with HTTP {response.StatusCode}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FckCensor] Report error: {ex.Message}");
            return false;
        }
    }

    public bool IsReported(string trackId)
    {
        if (string.IsNullOrWhiteSpace(trackId)) return false;

        lock (_lock)
        {
            return _reportedTracks.Contains(trackId);
        }
    }

    private void SaveLocalTracks()
    {
        try
        {
            string json = JsonSerializer.Serialize(_localTracks, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_localTracksFile, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FckCensor] Save local_tracks error: {ex.Message}");
        }
    }

    private void SaveExceptions()
    {
        try
        {
            string json = JsonSerializer.Serialize(_remoteExceptions.ToList(), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_exceptionsFile, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FckCensor] Save exceptions error: {ex.Message}");
        }
    }

    private void SaveReported()
    {
        try
        {
            string json = JsonSerializer.Serialize(_reportedTracks.ToList(), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_reportedFile, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FckCensor] Save reported error: {ex.Message}");
        }
    }
}
