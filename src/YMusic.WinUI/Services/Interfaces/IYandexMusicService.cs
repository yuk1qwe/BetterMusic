#nullable enable

using YMusic.WinUI.Models;

namespace YMusic.WinUI.Services.Interfaces;

public interface IYandexMusicService
{
    string? CurrentToken { get; }
    void SetOAuthToken(string token);

    event EventHandler? UnauthorizedResponseReceived;
    event EventHandler? RegionRestrictionDetected;

    Task<UserProfile?> GetAccountStatusAsync(CancellationToken cancellationToken = default);
    Task<(UserProfile? Profile, string? Error)> GetAccountStatusDetailedAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetLikedTrackIdsAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> LikeTrackAsync(string trackId, CancellationToken cancellationToken = default);
    Task<bool> UnlikeTrackAsync(string trackId, CancellationToken cancellationToken = default);
    bool IsTrackLiked(string trackId);
    Task EnsureLikedTracksLoadedAsync(CancellationToken cancellationToken = default);
    event EventHandler<(string TrackId, bool IsLiked)>? TrackLikeChanged;
    event EventHandler? LikedTracksLoaded;

    Task<IReadOnlyList<string>> GetDislikedTrackIdsAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> DislikeTrackAsync(string trackId, CancellationToken cancellationToken = default);
    Task<bool> UndislikeTrackAsync(string trackId, CancellationToken cancellationToken = default);
    bool IsTrackDisliked(string trackId);
    Task EnsureDislikedTracksLoadedAsync(CancellationToken cancellationToken = default);
    event EventHandler<(string TrackId, bool IsDisliked)>? TrackDislikeChanged;
    event EventHandler? DislikedTracksLoaded;

    Task<IReadOnlyList<Track>> GetTracksAsync(IEnumerable<string> trackIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Playlist>> GetUserPlaylistsAsync(string userId, CancellationToken cancellationToken = default);
    Task<Playlist?> CreatePlaylistAsync(string title, string? description = null, bool isPublic = true, CancellationToken cancellationToken = default);
    Task<bool> DeletePlaylistAsync(long ownerUid, int kind, CancellationToken cancellationToken = default);
    Task<bool> RenamePlaylistAsync(long ownerUid, int kind, string newTitle, string? newDescription = null, CancellationToken cancellationToken = default);
    Task<bool> RemoveTrackFromPlaylistAsync(long ownerUid, int kind, int trackIndex, CancellationToken cancellationToken = default);
    Task<bool> AddTrackToPlaylistAsync(long ownerUid, int kind, string trackId, string? albumId = null, CancellationToken cancellationToken = default);
    Task<bool> AddTracksToPlaylistAsync(long ownerUid, int kind, IEnumerable<(string TrackId, string? AlbumId)> tracks, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Album>> GetLikedAlbumsAsync(string userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Album>> GetAlbumsAsync(IEnumerable<long> albumIds, CancellationToken cancellationToken = default);
    Task<Album?> GetAlbumAsync(long albumId, CancellationToken cancellationToken = default);
    Task<bool> LikeAlbumAsync(long albumId, CancellationToken cancellationToken = default);
    Task<bool> UnlikeAlbumAsync(long albumId, CancellationToken cancellationToken = default);
    bool IsAlbumLiked(long albumId);
    Task EnsureLikedAlbumsLoadedAsync(CancellationToken cancellationToken = default);
    event EventHandler<(long AlbumId, bool IsLiked)>? AlbumLikeChanged;

    Task<bool> AddAlbumPresaveAsync(long albumId, CancellationToken cancellationToken = default);
    Task<bool> RemoveAlbumPresaveAsync(long albumId, CancellationToken cancellationToken = default);
    Task<PresavesResult?> GetPresavesAsync(CancellationToken cancellationToken = default);
    bool IsAlbumPresaved(long albumId);
    IReadOnlyCollection<Album> GetKnownUpcomingAlbums();

    Task<bool> LikePlaylistAsync(long ownerUid, int kind, CancellationToken cancellationToken = default);
    Task<bool> UnlikePlaylistAsync(long ownerUid, int kind, CancellationToken cancellationToken = default);
    bool IsPlaylistLiked(long ownerUid, int kind);
    Task EnsureLikedPlaylistsLoadedAsync(CancellationToken cancellationToken = default);
    event EventHandler<(long OwnerUid, int Kind, bool IsLiked)>? PlaylistLikeChanged;

    Task<bool> LikeArtistAsync(long artistId, CancellationToken cancellationToken = default);
    Task<bool> UnlikeArtistAsync(long artistId, CancellationToken cancellationToken = default);
    bool IsArtistLiked(long artistId);
    Task EnsureLikedArtistsLoadedAsync(CancellationToken cancellationToken = default);
    event EventHandler<(long ArtistId, bool IsLiked)>? ArtistLikeChanged;

    AudioQualityPreference PreferredQuality { get; set; }

    Task<string?> GetTrackDownloadUrlAsync(string trackId, CancellationToken cancellationToken = default);
    Task<(string? Url, StreamAudioQuality? Quality)> GetTrackStreamInfoAsync(string trackId, CancellationToken cancellationToken = default);

    Task<RotorStationResult?> GetRotorStationTracksAsync(string stationType = "user", string stationTag = "onyourwave", string? queueTrackId = null, CancellationToken cancellationToken = default);
    Task SendRotorFeedbackAsync(string eventType, string stationType, string stationTag, string trackId, string? batchId = null, double? totalPlayedSeconds = null, CancellationToken cancellationToken = default);

    Task<List<LyricLine>> GetTrackLyricsAsync(string trackId, string? title = null, string? artist = null, double durationSeconds = 0, bool? lyricsAvailable = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Playlist>> GetPersonalPlaylistsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Playlist>> GetEditorialPlaylistsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Album>> GetNewReleasesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Track>> GetChartTracksAsync(CancellationToken cancellationToken = default);
    Task<Playlist?> GetPlaylistWithTracksAsync(long ownerUid, int kind, CancellationToken cancellationToken = default);
    Task<Album?> GetAlbumWithTracksAsync(long albumId, CancellationToken cancellationToken = default);
    Task<ArtistBriefInfoResult?> GetArtistBriefInfoAsync(long artistId, CancellationToken cancellationToken = default);
    Task<List<Album>> GetArtistDirectAlbumsAsync(long artistId, int page = 0, int pageSize = 100, CancellationToken cancellationToken = default);
    Task<List<Album>> GetArtistAlsoAlbumsAsync(long artistId, int page = 0, int pageSize = 100, CancellationToken cancellationToken = default);
    Task<SearchResult?> SearchAsync(string query, string type = "all", int page = 0, CancellationToken cancellationToken = default);
}

