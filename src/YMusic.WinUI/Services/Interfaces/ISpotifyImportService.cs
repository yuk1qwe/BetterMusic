#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using YMusic.WinUI.Models;

namespace YMusic.WinUI.Services.Interfaces;

public sealed record SpotifyTrackItem(string Title, string Artists, long DurationMs);

public sealed record SpotifyPlaylistData(
    string Id,
    string EntityType,
    string Title,
    string? Description,
    string? CoverUrl,
    IReadOnlyList<SpotifyTrackItem> Tracks);

public sealed record ImportProgressReport(
    int Current,
    int Total,
    string CurrentTrackName,
    int FoundCount);

public sealed record ImportResult(
    Playlist? CreatedPlaylist,
    int TotalTracks,
    int FoundTracks,
    IReadOnlyList<string> UnmatchedTrackNames);

public interface ISpotifyImportService
{
    bool IsSupportedUrl(string url);
    string? ExtractEntityId(string url, out string entityType);
    Task<SpotifyPlaylistData?> FetchSpotifyPlaylistAsync(string urlOrId, CancellationToken cancellationToken = default);
    Task<ImportResult> ImportToYandexAsync(
        SpotifyPlaylistData spotifyData,
        string? targetPlaylistTitle = null,
        IProgress<ImportProgressReport>? progress = null,
        CancellationToken cancellationToken = default);
}

