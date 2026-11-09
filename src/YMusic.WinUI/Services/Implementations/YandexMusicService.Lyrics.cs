#nullable enable

using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.Services.Implementations;

public sealed partial class YandexMusicService
{
    public async Task<List<LyricLine>> GetTrackLyricsAsync(
        string trackId,
        string? title = null,
        string? artist = null,
        double durationSeconds = 0,
        bool? lyricsAvailable = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(trackId))
        {
            return new List<LyricLine>();
        }

        string cleanTrackId = trackId.Contains(':') ? trackId.Split(':')[0] : trackId;

        // 0. Cache hit: instant return (0 ms)
        if (_lyricsCache.TryGetValue(cleanTrackId, out var cached) && cached != null && cached.Count > 0)
        {
            return cached;
        }

        bool isTitleInstrumental = IsLikelyInstrumental(title, null);

        // FckCensor feature: prefer clean, uncensored lyrics from LRCLIB for censored tracks
        bool isReplaced = Track.IsTrackReplacedChecker?.Invoke(cleanTrackId) ?? false;
        if (isReplaced && !string.IsNullOrWhiteSpace(title) && !isTitleInstrumental)
        {
            try
            {
                var (fckSynced, fckPlain, fckInst) = await FetchFromLrclibAsync(title, artist, durationSeconds, cancellationToken).ConfigureAwait(false);
                if (fckInst)
                {
                    _lyricsCache[cleanTrackId] = new List<LyricLine>();
                    return new List<LyricLine>();
                }
                var fckLines = fckSynced ?? fckPlain;
                if (fckLines != null && fckLines.Count > 0)
                {
                    _lyricsCache[cleanTrackId] = fckLines;
                    return fckLines;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LRCLIB Priority Warning] {ex.Message}");
            }
        }

        // 1. Fetch Yandex Signed LRC and Yandex Supplement in parallel (very fast, ~100-150ms)
        var signedTask = FetchYandexSignedLyricsAsync(cleanTrackId, durationSeconds, cancellationToken);
        var supplementTask = FetchYandexSupplementLyricsAsync(cleanTrackId, durationSeconds, cancellationToken);

        await Task.WhenAll(signedTask, supplementTask).ConfigureAwait(false);

        var signedLyrics = await signedTask.ConfigureAwait(false);
        var supplementLyrics = await supplementTask.ConfigureAwait(false);

        // Check if Yandex already gave synchronized lyrics (with timestamps)
        if (signedLyrics != null && signedLyrics.Count > 0 && signedLyrics.Any(l => l.HasTimestamp && l.Timestamp > TimeSpan.Zero))
        {
            _lyricsCache[cleanTrackId] = signedLyrics;
            return signedLyrics;
        }

        if (supplementLyrics != null && supplementLyrics.Count > 0 && supplementLyrics.Any(l => l.HasTimestamp && l.Timestamp > TimeSpan.Zero))
        {
            _lyricsCache[cleanTrackId] = supplementLyrics;
            return supplementLyrics;
        }

        // Keep best plain lyrics from Yandex if available
        var fallbackPlainLyrics = (signedLyrics != null && signedLyrics.Count > 0)
            ? signedLyrics
            : supplementLyrics;

        // If the track title itself indicates an instrumental, do not query external fuzzy sources (prevents matching vocal lyrics)
        if (isTitleInstrumental)
        {
            var res = fallbackPlainLyrics ?? new List<LyricLine>();
            _lyricsCache[cleanTrackId] = res;
            return res;
        }

        // 2. Query LRCLIB for synchronized (or fallback plain) lyrics
        List<LyricLine>? lrclibPlainFallback = null;
        if (!string.IsNullOrWhiteSpace(title))
        {
            try
            {
                var (lrclibSynced, lrclibPlain, lrclibInst) = await FetchFromLrclibAsync(title, artist, durationSeconds, cancellationToken).ConfigureAwait(false);
                if (lrclibInst)
                {
                    var res = fallbackPlainLyrics ?? new List<LyricLine>();
                    _lyricsCache[cleanTrackId] = res;
                    return res;
                }

                if (lrclibSynced != null && lrclibSynced.Count > 0)
                {
                    _lyricsCache[cleanTrackId] = lrclibSynced;
                    return lrclibSynced;
                }
                lrclibPlainFallback = lrclibPlain;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LRCLIB Warning] {ex.Message}");
            }
        }

        // 3. Fallback to plain text lyrics: Yandex first, then LRCLIB plain
        if (fallbackPlainLyrics != null && fallbackPlainLyrics.Count > 0)
        {
            _lyricsCache[cleanTrackId] = fallbackPlainLyrics;
            return fallbackPlainLyrics;
        }

        if (lrclibPlainFallback != null && lrclibPlainFallback.Count > 0)
        {
            _lyricsCache[cleanTrackId] = lrclibPlainFallback;
            return lrclibPlainFallback;
        }

        // 4. Fallback to Genius if neither Yandex nor LRCLIB had lyrics (requires non-empty artist to prevent hallucinations)
        if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(artist))
        {
            try
            {
                var (geniusLines, isGeniusInst) = await FetchFromGeniusAsync(title, artist, durationSeconds, cancellationToken).ConfigureAwait(false);
                if (isGeniusInst)
                {
                    _lyricsCache[cleanTrackId] = new List<LyricLine>();
                    return new List<LyricLine>();
                }
                if (geniusLines != null && geniusLines.Count > 0)
                {
                    _lyricsCache[cleanTrackId] = geniusLines;
                    return geniusLines;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Genius Warning] {ex.Message}");
            }
        }

        return new List<LyricLine>();
    }

    private async Task<List<LyricLine>?> FetchYandexSignedLyricsAsync(
        string cleanTrackId,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        try
        {
            long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string msg = $"{cleanTrackId}{ts}";
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("kzqU4XhfCaY6B6JTHODeq5"));
            string sign = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(msg)));
            string signedUrl = $"tracks/{Uri.EscapeDataString(cleanTrackId)}/lyrics?format=LRC&timeStamp={ts}&sign={Uri.EscapeDataString(sign)}";

            using var response = await SendRequestAsync(HttpMethod.Get, signedUrl, null, cancellationToken).ConfigureAwait(false);
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var apiResult = JsonSerializer.Deserialize<YandexApiResponse<TrackLyricsResult>>(json, _jsonOptions);
                if (apiResult?.Result != null)
                {
                    if (!string.IsNullOrWhiteSpace(apiResult.Result.DownloadUrl))
                    {
                        try
                        {
                            var lrcContent = await _httpClient.GetStringAsync(apiResult.Result.DownloadUrl, cancellationToken).ConfigureAwait(false);
                            var parsed = LyricLine.ParseLrc(lrcContent, durationSeconds);
                            if (parsed.Count > 0) return parsed;
                        }
                        catch { }
                    }

                    if (!string.IsNullOrWhiteSpace(apiResult.Result.FullLyrics))
                    {
                        var parsed = LyricLine.ParseLrc(apiResult.Result.FullLyrics, durationSeconds);
                        if (parsed.Count > 0) return parsed;
                    }

                    if (!string.IsNullOrWhiteSpace(apiResult.Result.Lyrics))
                    {
                        var parsed = LyricLine.ParseLrc(apiResult.Result.Lyrics, durationSeconds);
                        if (parsed.Count > 0) return parsed;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Yandex Signed Lyrics Warning] {ex.Message}");
        }

        return null;
    }

    private async Task<List<LyricLine>?> FetchYandexSupplementLyricsAsync(
        string cleanTrackId,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendRequestAsync(HttpMethod.Get, $"tracks/{Uri.EscapeDataString(cleanTrackId)}/supplement", null, cancellationToken).ConfigureAwait(false);
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var apiResult = JsonSerializer.Deserialize<YandexApiResponse<TrackSupplementResult>>(json, _jsonOptions);
                var lyrics = apiResult?.Result?.Lyrics;
                if (lyrics != null)
                {
                    string? rawLyrics = null;
                    if (!string.IsNullOrWhiteSpace(lyrics.DownloadUrl))
                    {
                        try
                        {
                            rawLyrics = await _httpClient.GetStringAsync(lyrics.DownloadUrl, cancellationToken).ConfigureAwait(false);
                        }
                        catch { }
                    }

                    if (string.IsNullOrWhiteSpace(rawLyrics)) rawLyrics = lyrics.FullLyrics;
                    if (string.IsNullOrWhiteSpace(rawLyrics)) rawLyrics = lyrics.Lyrics;

                    if (!string.IsNullOrWhiteSpace(rawLyrics))
                    {
                        var parsed = LyricLine.ParseLrc(rawLyrics, durationSeconds);
                        if (parsed.Count > 0) return parsed;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Supplement API Warning] {ex.Message}");
        }

        return null;
    }

    private async Task<(List<LyricLine>? Synced, List<LyricLine>? Plain, bool IsInstrumental)> FetchFromLrclibAsync(
        string title,
        string? artist,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        string rawTitle = title.Trim();
        string rawArtist = (artist ?? string.Empty).Trim();

        string cleanTitle = CleanTitleForLyrics(rawTitle);
        string primaryArtist = GetPrimaryArtist(rawArtist);

        List<LyricLine>? fallbackPlain = null;

        // 1. Exact query: /api/get?track_name=...&artist_name=...&duration=...
        var (exactSynced, exactPlain, exactInst) = await TryLrclibGetAsync(rawTitle, rawArtist, durationSeconds, cancellationToken).ConfigureAwait(false);
        if (exactInst) return (null, null, true);
        if (exactSynced != null) return (exactSynced, exactPlain, false);
        if (exactPlain != null && fallbackPlain == null) fallbackPlain = exactPlain;

        // If title or artist had extra info, try /api/get with cleaned title & primary artist
        if (!string.Equals(cleanTitle, rawTitle, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(primaryArtist, rawArtist, StringComparison.OrdinalIgnoreCase))
        {
            var (cleanSynced, cleanPlain, cleanInst) = await TryLrclibGetAsync(cleanTitle, primaryArtist, durationSeconds, cancellationToken).ConfigureAwait(false);
            if (cleanInst) return (null, null, true);
            if (cleanSynced != null) return (cleanSynced, cleanPlain, false);
            if (cleanPlain != null && fallbackPlain == null) fallbackPlain = cleanPlain;
        }

        // 2. Search query: /api/search?track_name=...&artist_name=...
        // First search with clean title and primary artist
        var (searchSynced, searchPlain, searchInst) = await TryLrclibSearchAsync(cleanTitle, primaryArtist, durationSeconds, cancellationToken).ConfigureAwait(false);
        if (searchInst) return (null, null, true);
        if (searchSynced != null) return (searchSynced, searchPlain, false);
        if (searchPlain != null && fallbackPlain == null) fallbackPlain = searchPlain;

        // If clean search failed, also try raw title search
        if (!string.Equals(cleanTitle, rawTitle, StringComparison.OrdinalIgnoreCase))
        {
            var (rawSearchSynced, rawSearchPlain, rawSearchInst) = await TryLrclibSearchAsync(rawTitle, primaryArtist, durationSeconds, cancellationToken).ConfigureAwait(false);
            if (rawSearchInst) return (null, null, true);
            if (rawSearchSynced != null) return (rawSearchSynced, rawSearchPlain, false);
            if (rawSearchPlain != null && fallbackPlain == null) fallbackPlain = rawSearchPlain;
        }

        return (null, fallbackPlain, false);
    }

    private async Task<(List<LyricLine>? Synced, List<LyricLine>? Plain, bool IsInstrumental)> TryLrclibGetAsync(
        string trackName,
        string artistName,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(trackName)) return (null, null, false);

        try
        {
            var queryBuilder = new StringBuilder("https://lrclib.net/api/get?");
            queryBuilder.Append($"track_name={Uri.EscapeDataString(trackName)}");
            if (!string.IsNullOrWhiteSpace(artistName))
            {
                queryBuilder.Append($"&artist_name={Uri.EscapeDataString(artistName)}");
            }
            if (durationSeconds > 0)
            {
                queryBuilder.Append($"&duration={(int)Math.Round(durationSeconds)}");
            }

            using var resp = await _lrclibHttpClient.GetAsync(queryBuilder.ToString(), cancellationToken).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var lrclibItem = JsonSerializer.Deserialize<LrclibResponse>(json, _jsonOptions);
                if (lrclibItem != null)
                {
                    // Check if explicitly marked instrumental in LRCLIB
                    if (lrclibItem.Instrumental)
                    {
                        return (null, null, true);
                    }

                    // Strict duration check: if duration differs by > 4.5 seconds, reject (different edit/cut)
                    if (durationSeconds > 0 && lrclibItem.Duration > 0 && Math.Abs(lrclibItem.Duration - durationSeconds) > 4.5)
                    {
                        return (null, null, false);
                    }

                    // Strict title & artist verification
                    if (!TitlesMatch(trackName, lrclibItem.TrackName ?? ""))
                    {
                        return (null, null, false);
                    }
                    if (!string.IsNullOrWhiteSpace(artistName) && !ArtistsMatch(artistName, lrclibItem.ArtistName ?? ""))
                    {
                        return (null, null, false);
                    }

                    List<LyricLine>? synced = null;
                    if (!string.IsNullOrWhiteSpace(lrclibItem.SyncedLyrics))
                    {
                        var lines = LyricLine.ParseLrc(lrclibItem.SyncedLyrics, durationSeconds);
                        if (lines.Count > 0 && lines.Any(l => l.HasTimestamp && l.Timestamp > TimeSpan.Zero))
                        {
                            synced = lines;
                        }
                    }

                    List<LyricLine>? plain = null;
                    if (!string.IsNullOrWhiteSpace(lrclibItem.PlainLyrics))
                    {
                        var lines = LyricLine.ParseLrc(lrclibItem.PlainLyrics, durationSeconds);
                        if (lines.Count > 0)
                        {
                            plain = lines;
                        }
                    }

                    return (synced, plain, false);
                }
            }
        }
        catch { }

        return (null, null, false);
    }

    private async Task<(List<LyricLine>? Synced, List<LyricLine>? Plain, bool IsInstrumental)> TryLrclibSearchAsync(
        string trackName,
        string artistName,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(trackName)) return (null, null, false);

        try
        {
            string searchUrl = $"https://lrclib.net/api/search?track_name={Uri.EscapeDataString(trackName)}&artist_name={Uri.EscapeDataString(artistName)}";
            using var resp = await _lrclibHttpClient.GetAsync(searchUrl, cancellationToken).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var items = JsonSerializer.Deserialize<List<LrclibResponse>>(json, _jsonOptions);
                if (items == null || items.Count == 0)
                {
                    string fallbackQuery = string.IsNullOrWhiteSpace(artistName)
                        ? trackName.Trim()
                        : $"{artistName} {trackName}".Trim();
                    string qSearchUrl = $"https://lrclib.net/api/search?q={Uri.EscapeDataString(fallbackQuery)}";
                    try
                    {
                        using var qResp = await _lrclibHttpClient.GetAsync(qSearchUrl, cancellationToken).ConfigureAwait(false);
                        if (qResp.IsSuccessStatusCode)
                        {
                            var qJson = await qResp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                            items = JsonSerializer.Deserialize<List<LrclibResponse>>(qJson, _jsonOptions);
                        }
                    }
                    catch { }
                }

                if (items != null && items.Count > 0)
                {
                    // Strict filtering: candidate MUST match title and artist!
                    var validCandidates = items.Where(x =>
                        TitlesMatch(trackName, x.TrackName ?? "") &&
                        (string.IsNullOrWhiteSpace(artistName) || ArtistsMatch(artistName, x.ArtistName ?? ""))
                    ).ToList();

                    if (validCandidates.Count == 0) return (null, null, false);

                    // Check if an instrumental candidate was identified
                    if (validCandidates.Any(x => x.Instrumental &&
                        (durationSeconds <= 0 || x.Duration <= 0 || Math.Abs(x.Duration - durationSeconds) <= 4.0)))
                    {
                        return (null, null, true);
                    }

                    // 1. Filter candidates that actually have SyncedLyrics
                    List<LyricLine>? bestSynced = null;
                    var syncedCandidates = validCandidates.Where(x => !string.IsNullOrWhiteSpace(x.SyncedLyrics)).ToList();
                    if (syncedCandidates.Count > 0)
                    {
                        var compatible = syncedCandidates
                            .Where(x => durationSeconds <= 0 || x.Duration <= 0 || Math.Abs(x.Duration - durationSeconds) <= 3.5)
                            .OrderBy(x => durationSeconds > 0 && x.Duration > 0 ? Math.Abs(x.Duration - durationSeconds) : 0.0)
                            .ThenBy(x =>
                            {
                                bool requestedLive = trackName.Contains("live", StringComparison.OrdinalIgnoreCase);
                                bool isLive = (x.TrackName?.Contains("live", StringComparison.OrdinalIgnoreCase) == true) ||
                                              (x.AlbumName?.Contains("live", StringComparison.OrdinalIgnoreCase) == true);
                                return (!requestedLive && isLive) ? 1 : 0;
                            })
                            .ThenBy(x =>
                            {
                                bool requestedAcoustic = trackName.Contains("acoustic", StringComparison.OrdinalIgnoreCase);
                                bool isAcoustic = (x.TrackName?.Contains("acoustic", StringComparison.OrdinalIgnoreCase) == true) ||
                                                  (x.AlbumName?.Contains("acoustic", StringComparison.OrdinalIgnoreCase) == true);
                                return (!requestedAcoustic && isAcoustic) ? 1 : 0;
                            })
                            .ToList();

                        foreach (var best in compatible)
                        {
                            if (string.IsNullOrWhiteSpace(best.SyncedLyrics)) continue;
                            var lines = LyricLine.ParseLrc(best.SyncedLyrics, durationSeconds);
                            if (lines.Count > 0 && lines.Any(l => l.HasTimestamp && l.Timestamp > TimeSpan.Zero))
                            {
                                bestSynced = lines;
                                break;
                            }
                        }
                    }

                    // 2. Filter candidates that have PlainLyrics as fallback (ONLY if duration matches within 3s!)
                    List<LyricLine>? bestPlain = null;
                    var plainCandidates = validCandidates.Where(x => !string.IsNullOrWhiteSpace(x.PlainLyrics)).ToList();
                    if (plainCandidates.Count > 0)
                    {
                        var compatiblePlain = plainCandidates
                            .Where(x => durationSeconds <= 0 || x.Duration <= 0 || Math.Abs(x.Duration - durationSeconds) <= 3.0)
                            .OrderBy(x => durationSeconds > 0 && x.Duration > 0 ? Math.Abs(x.Duration - durationSeconds) : 0.0)
                            .ToList();

                        var plainText = compatiblePlain.Count > 0 ? compatiblePlain[0].PlainLyrics : null;
                        if (!string.IsNullOrWhiteSpace(plainText))
                        {
                            var lines = LyricLine.ParseLrc(plainText, durationSeconds);
                            if (lines.Count > 0)
                            {
                                bestPlain = lines;
                            }
                        }
                    }

                    return (bestSynced, bestPlain, false);
                }
            }
        }
        catch { }

        return (null, null, false);
    }

    private async Task<(List<LyricLine>? Lines, bool IsInstrumental)> FetchFromGeniusAsync(
        string title,
        string? artist,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(artist)) return (null, false); // NEVER query Genius without an artist (prevents matching random popular songs)

        string rawTitle = title.Trim();
        string rawArtist = (artist ?? string.Empty).Trim();
        string cleanTitle = CleanTitleForLyrics(rawTitle);
        string primaryArtist = GetPrimaryArtist(rawArtist);

        // Try primary artist + clean title
        var (lines, isInst) = await TryGeniusSearchAndExtractAsync(cleanTitle, primaryArtist, cancellationToken).ConfigureAwait(false);
        if (isInst) return (null, true);
        if (lines != null && lines.Count > 0) return (lines, false);

        // Try primary artist + raw title if clean was different
        if (!string.Equals(cleanTitle, rawTitle, StringComparison.OrdinalIgnoreCase))
        {
            (lines, isInst) = await TryGeniusSearchAndExtractAsync(rawTitle, primaryArtist, cancellationToken).ConfigureAwait(false);
            if (isInst) return (null, true);
            if (lines != null && lines.Count > 0) return (lines, false);
        }

        // Try full artist name + clean title if composite artist
        if (!string.IsNullOrWhiteSpace(primaryArtist) && !string.Equals(primaryArtist, rawArtist, StringComparison.OrdinalIgnoreCase))
        {
            (lines, isInst) = await TryGeniusSearchAndExtractAsync(cleanTitle, rawArtist, cancellationToken).ConfigureAwait(false);
            if (isInst) return (null, true);
            if (lines != null && lines.Count > 0) return (lines, false);
        }

        return (null, false);
    }

    private async Task<(List<LyricLine>? Lines, bool IsInstrumental)> TryGeniusSearchAndExtractAsync(
        string trackName,
        string artistName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(trackName) || string.IsNullOrWhiteSpace(artistName)) return (null, false);

        try
        {
            string query = $"{artistName} {trackName}";
            string searchUrl = $"https://genius.com/api/search/multi?q={Uri.EscapeDataString(query)}";

            string? json = await FetchGeniusStringAsync(searchUrl, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json)) return (null, false);

            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("response", out var respProp) ||
                !respProp.TryGetProperty("sections", out var sectionsProp))
            {
                return (null, false);
            }

            string? targetUrl = null;

            // Search priority: check "song" section, then "top_hit"
            foreach (var section in sectionsProp.EnumerateArray())
            {
                string secType = section.TryGetProperty("type", out var st) ? st.GetString() ?? "" : "";
                if (secType != "song" && secType != "top_hit") continue;

                if (!section.TryGetProperty("hits", out var hitsProp)) continue;

                foreach (var hit in hitsProp.EnumerateArray())
                {
                    if (!hit.TryGetProperty("result", out var resultProp)) continue;

                    string hitType = resultProp.TryGetProperty("_type", out var tp) ? tp.GetString() ?? "" : "";
                    if (hitType != "song") continue;

                    string hitTitle = resultProp.TryGetProperty("title", out var tit) ? tit.GetString() ?? "" : "";
                    string hitArtist = resultProp.TryGetProperty("artist_names", out var art) ? art.GetString() ?? "" : "";

                    // Filter out Romanized or foreign translations if original is available
                    if (hitTitle.Contains("(Romanized)", StringComparison.OrdinalIgnoreCase) ||
                        hitArtist.Contains("Genius Romanizations", StringComparison.OrdinalIgnoreCase) ||
                        hitArtist.Contains("Genius Translations", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Strict matching: BOTH artist AND title must match!
                    bool artistMatches = ArtistsMatch(artistName, hitArtist);
                    bool titleMatches = TitlesMatch(trackName, hitTitle);

                    if (artistMatches && titleMatches)
                    {
                        if (resultProp.TryGetProperty("instrumental", out var instProp) && instProp.GetBoolean())
                        {
                            return (null, true);
                        }

                        if (!resultProp.TryGetProperty("url", out var urlProp)) continue;
                        string? url = urlProp.GetString();
                        if (string.IsNullOrWhiteSpace(url) || !url.Contains("-lyrics")) continue;

                        targetUrl = url;
                        break;
                    }
                }

                if (targetUrl != null) break;
            }

            if (string.IsNullOrWhiteSpace(targetUrl)) return (null, false);

            // Fetch the lyrics webpage from Genius
            string? html = await FetchGeniusStringAsync(targetUrl, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(html)) return (null, false);

            var containers = ExtractGeniusLyricsContainers(html);
            if (containers.Count == 0) return (null, false);

            var sb = new StringBuilder();
            foreach (var container in containers)
            {
                string content = container;
                content = Regex.Replace(content, @"<br\s*/?>", "\n");
                content = Regex.Replace(content, @"<.*?>", "");
                content = System.Net.WebUtility.HtmlDecode(content).Trim();
                if (!string.IsNullOrWhiteSpace(content))
                {
                    sb.AppendLine(content);
                    sb.AppendLine();
                }
            }

            string rawLyrics = sb.ToString().Trim();
            rawLyrics = CleanGeniusLyrics(rawLyrics);

            if (string.IsNullOrWhiteSpace(rawLyrics)) return (null, false);

            // Check if page indicates it's purely instrumental
            string lowerTrimmed = rawLyrics.Trim().Trim('[', ']', '(', ')', '{', '}', '*', '-', ' ', '♪', '•').ToLowerInvariant();
            if (lowerTrimmed == "instrumental" || lowerTrimmed == "инструментал" ||
                lowerTrimmed == "this song is an instrumental" || lowerTrimmed.Contains("this song is an instrumental"))
            {
                return (null, true);
            }

            var lines = LyricLine.ParseLrc(rawLyrics, 0);
            return (lines.Count > 0 ? lines : null, false);
        }
        catch { }

        return (null, false);
    }

    private static List<string> ExtractGeniusLyricsContainers(string html)
    {
        var containers = new List<string>();
        if (string.IsNullOrWhiteSpace(html)) return containers;

        var tagRegex = new Regex(@"<div[^>]*data-lyrics-container=[""']true[""'][^>]*>", RegexOptions.IgnoreCase);
        var matches = tagRegex.Matches(html);

        foreach (Match match in matches)
        {
            int startIndex = match.Index + match.Length;
            int depth = 1;
            int currentIndex = startIndex;

            while (depth > 0 && currentIndex < html.Length)
            {
                int nextOpen = html.IndexOf("<div", currentIndex, StringComparison.OrdinalIgnoreCase);
                int nextClose = html.IndexOf("</div>", currentIndex, StringComparison.OrdinalIgnoreCase);

                if (nextClose == -1) break;

                if (nextOpen != -1 && nextOpen < nextClose)
                {
                    depth++;
                    currentIndex = nextOpen + 4;
                }
                else
                {
                    depth--;
                    if (depth == 0)
                    {
                        string inner = html.Substring(startIndex, nextClose - startIndex);
                        containers.Add(inner);
                        break;
                    }
                    currentIndex = nextClose + 6;
                }
            }
        }

        return containers;
    }

    private static async Task<string?> FetchGeniusStringAsync(string url, CancellationToken cancellationToken)
    {
        // 1. Try Windows built-in curl.exe (native TLS stack safely handles Cloudflare on genius.com)
        string? curlOutput = await FetchViaCurlAsync(url, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(curlOutput) && !curlOutput.Contains("Enable JavaScript and cookies to continue"))
        {
            return curlOutput;
        }

        // 2. Fallback to HttpClient
        try
        {
            using var resp = await _geniusHttpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                return await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch { }

        return null;
    }

    private static async Task<string?> FetchViaCurlAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            string systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string curlPath = Path.Combine(systemRoot, "curl.exe");
            if (!File.Exists(curlPath))
            {
                curlPath = "curl.exe";
            }

            var psi = new ProcessStartInfo
            {
                FileName = curlPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            psi.ArgumentList.Add("-s");
            psi.ArgumentList.Add("-m");
            psi.ArgumentList.Add("5");
            psi.ArgumentList.Add("-A");
            psi.ArgumentList.Add("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
            psi.ArgumentList.Add(url);

            using var process = new Process { StartInfo = psi };
            if (!process.Start())
            {
                return null;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            using var reg = cancellationToken.Register(() =>
            {
                try { process.Kill(); } catch { }
            });

            string output = await outputTask.ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                return output;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Genius curl] {ex.Message}");
        }

        return null;
    }

    private static string CleanGeniusLyrics(string lyrics)
    {
        if (string.IsNullOrWhiteSpace(lyrics)) return string.Empty;

        // 1. Remove contributor header, e.g. "23 Contributors... Lyrics" or "4 Contributors... Текст песни"
        lyrics = Regex.Replace(lyrics, @"^\d+\s*Contributors.*?(?:Lyrics|Текст песни)", "", RegexOptions.IgnoreCase | RegexOptions.Singleline).Trim();

        // 2. Remove [Текст песни «...»] or [Lyrics to "..."] header line
        lyrics = Regex.Replace(lyrics, @"^\[(?:Текст песни|Lyrics)\s+[^\]]+\]\s*", "", RegexOptions.IgnoreCase).Trim();

        // 3. Remove "Read More" preamble if bio text leaked into the container
        int readMoreIdx = lyrics.IndexOf("Read More", StringComparison.OrdinalIgnoreCase);
        if (readMoreIdx >= 0 && readMoreIdx < 400)
        {
            lyrics = lyrics.Substring(readMoreIdx + "Read More".Length).Trim();
        }

        // 4. Remove trailing Genius metadata tags like "123Embed" or "Embed"
        lyrics = Regex.Replace(lyrics, @"\d*\s*Embed$", "", RegexOptions.IgnoreCase).Trim();
        lyrics = Regex.Replace(lyrics, @"You might also like.*$", "", RegexOptions.IgnoreCase).Trim();

        return lyrics;
    }

    private static string CleanTitleForLyrics(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;

        // Strip parenthetical/bracketed suffixes like "(Remastered 2021)", "[Deluxe Edition]", "(feat. Drake)"
        string cleaned = Regex.Replace(title, @"\s*[\(\[](?:remastered|remaster|deluxe|bonus|anniversary|feat\.?|featuring|prod\.?|produced)[^\)\]]*[\)\]]", "", RegexOptions.IgnoreCase);

        // Strip trailing " - Remastered..." etc.
        cleaned = Regex.Replace(cleaned, @"\s+-\s+(?:remastered|remaster|deluxe|mono|stereo|bonus track).*$", "", RegexOptions.IgnoreCase);

        return cleaned.Trim();
    }

    private static string GetPrimaryArtist(string artist)
    {
        if (string.IsNullOrWhiteSpace(artist)) return string.Empty;
        int commaIndex = artist.IndexOfAny(new[] { ',', '&', '/' });
        if (commaIndex > 0)
        {
            return artist.Substring(0, commaIndex).Trim();
        }
        return artist.Trim();
    }

    private static string NormalizeForComparison(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var sb = new StringBuilder(text.Length);
        foreach (char c in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static string NormalizeTitleForComparison(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;
        string cleaned = Regex.Replace(title, @"\s*[\(\[][^\)\]]*[\)\]]", " ").Trim();
        return NormalizeForComparison(cleaned);
    }

    private static List<string> SplitArtistNames(string artist)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(artist)) return list;

        string[] parts = Regex.Split(artist, @"\s*(?:,|&|/|\bfeat\.?\b|\bft\.?\b|\bfeaturing\b|\bx\b|\bwith\b|\band\b)\s*", RegexOptions.IgnoreCase);
        foreach (var p in parts)
        {
            string norm = NormalizeForComparison(p);
            if (!string.IsNullOrWhiteSpace(norm))
            {
                list.Add(norm);
            }
        }
        return list;
    }

    private static bool ArtistsMatch(string requested, string candidate)
    {
        if (string.IsNullOrWhiteSpace(requested) || string.IsNullOrWhiteSpace(candidate))
            return false;

        string normReq = NormalizeForComparison(requested);
        string normCand = NormalizeForComparison(candidate);

        if (normReq == normCand) return true;

        string stripReq = StripArtistPrefixes(normReq);
        string stripCand = StripArtistPrefixes(normCand);
        if (stripReq == stripCand && !string.IsNullOrWhiteSpace(stripReq)) return true;

        var reqArtists = SplitArtistNames(requested);
        var candArtists = SplitArtistNames(candidate);

        foreach (var r in reqArtists)
        {
            if (r.Length < 3) continue;
            string stripR = StripArtistPrefixes(r);
            foreach (var c in candArtists)
            {
                if (c.Length < 3) continue;
                string stripC = StripArtistPrefixes(c);
                if (r == c || stripR == stripC) return true;

                // Close match: only if both are long (>= 5 chars) and length ratio >= 0.85
                if (r.Length >= 5 && c.Length >= 5)
                {
                    double ratio = (double)Math.Min(r.Length, c.Length) / Math.Max(r.Length, c.Length);
                    if (ratio >= 0.85 && (r.Contains(c) || c.Contains(r)))
                        return true;
                }
            }
        }

        return false;
    }

    private static string StripArtistPrefixes(string text)
    {
        if (text.StartsWith("the") && text.Length > 3) return text.Substring(3);
        if (text.StartsWith("dj") && text.Length > 2) return text.Substring(2);
        return text;
    }

    private static bool TitlesMatch(string requested, string candidate)
    {
        if (string.IsNullOrWhiteSpace(requested) || string.IsNullOrWhiteSpace(candidate))
            return false;

        string normReq = NormalizeTitleForComparison(requested);
        string normCand = NormalizeTitleForComparison(candidate);

        if (normReq == normCand) return true;

        if (normReq.Length >= 4 && normCand.Length >= 4)
        {
            double ratio = (double)Math.Min(normReq.Length, normCand.Length) / Math.Max(normReq.Length, normCand.Length);
            if (ratio >= 0.85 && (normReq.Contains(normCand) || normCand.Contains(normReq)))
                return true;
        }

        return false;
    }

    private static bool IsLikelyInstrumental(string? title, string? version)
    {
        string combined = $"{title} {version}".ToLowerInvariant();
        return combined.Contains("instrumental") ||
               combined.Contains("инструментал") ||
               combined.Contains("минус") ||
               combined.Contains("minus") ||
               combined.Contains("караоке") ||
               combined.Contains("karaoke") ||
               combined.Contains("orchestral version") ||
               combined.Contains("piano version") ||
               combined.Contains("backing track");
    }

}