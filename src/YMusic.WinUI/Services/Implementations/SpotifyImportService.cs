#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.Services.Implementations;

public sealed class SpotifyImportService : ISpotifyImportService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(25)
    };

    private readonly IYandexMusicService _musicService;

    public SpotifyImportService(IYandexMusicService musicService)
    {
        _musicService = musicService;
    }

    public bool IsSupportedUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return ExtractEntityId(url, out _) != null;
    }

    public string? ExtractEntityId(string url, out string entityType)
    {
        entityType = "playlist";
        if (string.IsNullOrWhiteSpace(url)) return null;

        string trimmed = url.Trim();

        // Pattern 1: https://open.spotify.com/(playlist|album|track)/{id}
        var match = Regex.Match(trimmed, @"spotify\.com\/(playlist|album|track)\/([a-zA-Z0-9]+)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            entityType = match.Groups[1].Value.ToLowerInvariant();
            return match.Groups[2].Value;
        }

        // Pattern 2: spotify:(playlist|album|track):{id}
        var uriMatch = Regex.Match(trimmed, @"spotify:(playlist|album|track):([a-zA-Z0-9]+)", RegexOptions.IgnoreCase);
        if (uriMatch.Success)
        {
            entityType = uriMatch.Groups[1].Value.ToLowerInvariant();
            return uriMatch.Groups[2].Value;
        }

        // Pattern 3: raw base62 id (22 characters)
        if (Regex.IsMatch(trimmed, @"^[a-zA-Z0-9]{22}$"))
        {
            return trimmed;
        }

        return null;
    }

    public async Task<SpotifyPlaylistData?> FetchSpotifyPlaylistAsync(string urlOrId, CancellationToken cancellationToken = default)
    {
        string? id = ExtractEntityId(urlOrId, out string entityType);
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        string embedUrl = $"https://open.spotify.com/embed/{entityType}/{id}";

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, embedUrl);
            req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            req.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            req.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9,ru;q=0.8");

            using var response = await HttpClient.SendAsync(req, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[SpotifyImport] Embed request returned HTTP {(int)response.StatusCode}");
                return null;
            }

            string html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            // Extract <script id="__NEXT_DATA__" type="application/json">...</script>
            var match = Regex.Match(html, @"<script\s+id=""__NEXT_DATA__""[^>]*>(.*?)<\/script>", RegexOptions.Singleline);
            if (!match.Success)
            {
                Debug.WriteLine("[SpotifyImport] Could not locate __NEXT_DATA__ in Spotify embed HTML");
                return null;
            }

            string json = match.Groups[1].Value;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("props", out var props) ||
                !props.TryGetProperty("pageProps", out var pageProps) ||
                !pageProps.TryGetProperty("state", out var state) ||
                !state.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("entity", out var entity))
            {
                Debug.WriteLine("[SpotifyImport] Incomplete JSON structure in __NEXT_DATA__");
                return null;
            }

            string title = entity.TryGetProperty("name", out var nameProp)
                ? (nameProp.GetString() ?? "Spotify Playlist")
                : "Spotify Playlist";

            string? description = entity.TryGetProperty("episode_description", out var descProp)
                ? descProp.GetString()
                : null;

            string? coverUrl = null;
            if (entity.TryGetProperty("visualIdentity", out var vi) && vi.TryGetProperty("image", out var imgArr) && imgArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var img in imgArr.EnumerateArray())
                {
                    if (img.TryGetProperty("url", out var u))
                    {
                        coverUrl = u.GetString();
                    }
                }
            }

            var tracks = new List<SpotifyTrackItem>();

            if (entity.TryGetProperty("trackList", out var trackList) && trackList.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in trackList.EnumerateArray())
                {
                    string trackTitle = item.TryGetProperty("title", out var t) ? (t.GetString() ?? "") : "";
                    string trackArtists = item.TryGetProperty("subtitle", out var s) ? (s.GetString() ?? "") : "";
                    long duration = item.TryGetProperty("duration", out var d) && d.TryGetInt64(out var dVal) ? dVal : 0;

                    if (!string.IsNullOrWhiteSpace(trackTitle))
                    {
                        tracks.Add(new SpotifyTrackItem(trackTitle.Trim(), trackArtists.Trim(), duration));
                    }
                }
            }

            // Check if there are more tracks and we have an anonymous token
            if (entityType == "playlist" &&
                state.TryGetProperty("settings", out var settings) &&
                settings.TryGetProperty("session", out var session) &&
                session.TryGetProperty("accessToken", out var tokenProp))
            {
                string? accessToken = tokenProp.GetString();
                if (!string.IsNullOrWhiteSpace(accessToken) && tracks.Count >= 100)
                {
                    // Fetch additional tracks via Spotify Web API
                    await FetchRemainingTracksViaApiAsync(id, accessToken, tracks, cancellationToken).ConfigureAwait(false);
                }
            }

            return new SpotifyPlaylistData(id, entityType, title, description, coverUrl, tracks);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SpotifyImport] Error fetching playlist: {ex.Message}");
            return null;
        }
    }

    private static async Task FetchRemainingTracksViaApiAsync(
        string playlistId,
        string accessToken,
        List<SpotifyTrackItem> tracks,
        CancellationToken cancellationToken)
    {
        int offset = tracks.Count;
        const int limit = 100;
        const int maxTotal = 500; // Cap to 500 for safety

        while (offset < maxTotal)
        {
            try
            {
                string url = $"https://api.spotify.com/v1/playlists/{playlistId}/tracks?offset={offset}&limit={limit}";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                req.Headers.UserAgent.ParseAdd("Mozilla/5.0");

                using var resp = await HttpClient.SendAsync(req, cancellationToken).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) break;

                string json = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                {
                    break;
                }

                int added = 0;
                foreach (var item in items.EnumerateArray())
                {
                    if (item.TryGetProperty("track", out var tr) && tr.ValueKind == JsonValueKind.Object)
                    {
                        string title = tr.TryGetProperty("name", out var name) ? (name.GetString() ?? "") : "";
                        var artistsList = new List<string>();
                        if (tr.TryGetProperty("artists", out var arts) && arts.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var a in arts.EnumerateArray())
                            {
                                if (a.TryGetProperty("name", out var an) && !string.IsNullOrWhiteSpace(an.GetString()))
                                {
                                    artistsList.Add(an.GetString()!);
                                }
                            }
                        }
                        long duration = tr.TryGetProperty("duration_ms", out var dm) && dm.TryGetInt64(out var dVal) ? dVal : 0;

                        if (!string.IsNullOrWhiteSpace(title))
                        {
                            tracks.Add(new SpotifyTrackItem(title.Trim(), string.Join(", ", artistsList), duration));
                            added++;
                        }
                    }
                }

                if (added == 0) break;
                offset += added;
            }
            catch
            {
                break;
            }
        }
    }

    public async Task<ImportResult> ImportToYandexAsync(
        SpotifyPlaylistData spotifyData,
        string? targetPlaylistTitle = null,
        IProgress<ImportProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string playlistTitle = !string.IsNullOrWhiteSpace(targetPlaylistTitle)
            ? targetPlaylistTitle
            : spotifyData.Title;

        var matchedTracks = new List<(string TrackId, string? AlbumId)>();
        var unmatchedNames = new List<string>();
        int total = spotifyData.Tracks.Count;

        for (int i = 0; i < total; i++)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var item = spotifyData.Tracks[i];
            string displayName = $"{item.Artists} - {item.Title}";
            progress?.Report(new ImportProgressReport(i + 1, total, displayName, matchedTracks.Count));

            var matchedTrack = await FindMatchingYandexTrackAsync(item, cancellationToken).ConfigureAwait(false);
            if (matchedTrack != null)
            {
                string? albumId = matchedTrack.Albums.Count > 0 ? matchedTrack.Albums[0].Id.ToString() : null;
                matchedTracks.Add((matchedTrack.Id, albumId));
            }
            else
            {
                unmatchedNames.Add(displayName);
            }

            // Small delay to be polite to Yandex Music search rate limits
            await Task.Delay(40, cancellationToken).ConfigureAwait(false);
        }

        Playlist? createdPlaylist = null;
        if (matchedTracks.Count > 0)
        {
            createdPlaylist = await _musicService.CreatePlaylistAsync(playlistTitle, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (createdPlaylist != null)
            {
                bool added = await _musicService.AddTracksToPlaylistAsync(
                    createdPlaylist.Uid,
                    createdPlaylist.Kind,
                    matchedTracks,
                    cancellationToken).ConfigureAwait(false);

                if (added)
                {
                    var updated = await _musicService.GetPlaylistWithTracksAsync(
                        createdPlaylist.Uid,
                        createdPlaylist.Kind,
                        cancellationToken).ConfigureAwait(false);
                    if (updated != null)
                    {
                        createdPlaylist = updated;
                    }
                }
            }
        }

        return new ImportResult(createdPlaylist, total, matchedTracks.Count, unmatchedNames);
    }

    private async Task<Track?> FindMatchingYandexTrackAsync(SpotifyTrackItem item, CancellationToken cancellationToken)
    {
        // 1. Primary search: Clean title + primary artist
        string cleanTitle = CleanTrackTitle(item.Title);
        string primaryArtist = item.Artists.Split(',', '&', '/').FirstOrDefault()?.Trim() ?? "";

        string query = !string.IsNullOrWhiteSpace(primaryArtist)
            ? $"{primaryArtist} {cleanTitle}"
            : cleanTitle;

        var searchResult = await _musicService.SearchAsync(query, type: "track", cancellationToken: cancellationToken).ConfigureAwait(false);
        var tracks = searchResult?.Tracks?.Results;

        if (tracks != null && tracks.Count > 0)
        {
            // Pick best candidate: exact title match or close match
            foreach (var t in tracks)
            {
                if (IsCloseMatch(t.Title, cleanTitle))
                {
                    return t;
                }
            }
            return tracks[0];
        }

        // 2. Fallback search: just clean title
        if (!string.IsNullOrWhiteSpace(cleanTitle) && cleanTitle != query)
        {
            var fallbackResult = await _musicService.SearchAsync(cleanTitle, type: "track", cancellationToken: cancellationToken).ConfigureAwait(false);
            if (fallbackResult?.Tracks?.Results?.Count > 0)
            {
                return fallbackResult.Tracks.Results[0];
            }
        }

        return null;
    }

    private static string CleanTrackTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "";

        // Remove things like "- Remastered 2011", "(feat. Drake)", "[Bonus Track]"
        string cleaned = Regex.Replace(title, @"\s*-\s*(remaster(ed)?(\s*\d+)?|live|acoustic|radio edit).*", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\s*\((feat|ft|with)\.?\s+[^\)]+\)", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\s*\[[^\]]+\]", "", RegexOptions.IgnoreCase);
        return cleaned.Trim();
    }

    private static bool IsCloseMatch(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        string normA = NormalizeForCompare(a);
        string normB = NormalizeForCompare(b);
        return normA.Equals(normB, StringComparison.OrdinalIgnoreCase) ||
               normA.Contains(normB, StringComparison.OrdinalIgnoreCase) ||
               normB.Contains(normA, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeForCompare(string s)
    {
        return Regex.Replace(s.ToLowerInvariant(), @"[^a-z0-9а-яё]", "");
    }
}
