#nullable enable

using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.Services.Implementations;

public sealed class TrackPlaybackCoordinator : ITrackPlaybackCoordinator
{
    private readonly IAudioPlayerService _audioPlayer;
    private readonly IYandexMusicService _musicService;
    private readonly IFckCensorService _fckCensorService;
    private readonly IAudioCacheService _audioCacheService;
    private readonly List<WeakReference<IEnumerable<Track>>> _trackedCollections = new();
    private readonly object _lock = new();

    public TrackPlaybackCoordinator(
        IAudioPlayerService audioPlayer,
        IYandexMusicService musicService,
        IFckCensorService fckCensorService,
        IAudioCacheService audioCacheService)
    {
        _audioPlayer = audioPlayer;
        _musicService = musicService;
        _fckCensorService = fckCensorService;
        _audioCacheService = audioCacheService;
        _audioPlayer.CurrentTrackChanged += (_, _) => UpdatePlaybackOnly();
        _audioPlayer.IsPlayingChanged += (_, _) => UpdatePlaybackOnly();
        _musicService.TrackLikeChanged += OnTrackLikeChanged;
        _musicService.LikedTracksLoaded += (_, _) => UpdateAll();
        _musicService.TrackDislikeChanged += OnTrackDislikeChanged;
        _musicService.DislikedTracksLoaded += (_, _) => UpdateAll();
        _fckCensorService.TrackReplacementChanged += (_, _) => UpdateAll();
        _audioCacheService.CacheUpdated += (_, _) => UpdateAll();
    }

    public void Register(IEnumerable<Track> collection)
    {
        if (collection == null) return;

        lock (_lock)
        {
            CleanDeadReferences();
            bool exists = _trackedCollections.Any(r => r.TryGetTarget(out var target) && ReferenceEquals(target, collection));
            if (!exists)
            {
                _trackedCollections.Add(new WeakReference<IEnumerable<Track>>(collection));
            }
        }

        Sync(collection);
    }

    public void Unregister(IEnumerable<Track> collection)
    {
        if (collection == null) return;

        lock (_lock)
        {
            _trackedCollections.RemoveAll(r => !r.TryGetTarget(out var target) || ReferenceEquals(target, collection));
        }
    }

    public void Sync(IEnumerable<Track>? collection = null)
    {
        string? currentId = _audioPlayer.CurrentTrack?.Id;
        bool isPlaying = _audioPlayer.IsPlaying;

        void ApplyTo(IEnumerable<Track> tracks)
        {
            foreach (var track in tracks.ToList())
            {
                bool isCurrent = Track.IsSameTrackId(track.Id, currentId);
                if (track.IsCurrent != isCurrent)
                {
                    track.IsCurrent = isCurrent;
                }
                bool trackPlaying = isCurrent && isPlaying;
                if (track.IsPlaying != trackPlaying)
                {
                    track.IsPlaying = trackPlaying;
                }
                bool isLiked = _musicService.IsTrackLiked(track.Id);
                if (track.IsLiked != isLiked)
                {
                    track.IsLiked = isLiked;
                }
                bool isDisliked = _musicService.IsTrackDisliked(track.Id);
                if (track.IsDisliked != isDisliked)
                {
                    track.IsDisliked = isDisliked;
                }
                bool isReplaced = _fckCensorService.IsReplaced(track.Id);
                if (track.IsReplaced != isReplaced)
                {
                    track.IsReplaced = isReplaced;
                }
                bool isCached = _audioCacheService.IsTrackCached(track.Id);
                if (track.IsCached != isCached)
                {
                    track.IsCached = isCached;
                }
            }
        }

        void Execute()
        {
            if (collection != null)
            {
                ApplyTo(collection);
            }
            else
            {
                List<IEnumerable<Track>> active;
                lock (_lock)
                {
                    CleanDeadReferences();
                    active = _trackedCollections
                        .Select(r => r.TryGetTarget(out var target) ? target : null)
                        .Where(t => t != null)
                        .Select(t => t!)
                        .ToList();
                }

                foreach (var col in active)
                {
                    ApplyTo(col);
                }
            }
        }

        var dispatcher = App.MainDispatcherQueue;
        if (dispatcher != null && !dispatcher.HasThreadAccess)
        {
            dispatcher.TryEnqueue(Execute);
        }
        else
        {
            Execute();
        }
    }

    private void OnTrackLikeChanged(object? sender, (string TrackId, bool IsLiked) e)
    {
        void Execute()
        {
            List<IEnumerable<Track>> active;
            lock (_lock)
            {
                CleanDeadReferences();
                active = _trackedCollections
                    .Select(r => r.TryGetTarget(out var target) ? target : null)
                    .Where(t => t != null)
                    .Select(t => t!)
                    .ToList();
            }

            foreach (var col in active)
            {
                foreach (var track in col.ToList())
                {
                    if (Track.IsSameTrackId(track.Id, e.TrackId))
                    {
                        if (track.IsLiked != e.IsLiked)
                        {
                            track.IsLiked = e.IsLiked;
                        }
                    }
                }
            }
        }

        var dispatcher = App.MainDispatcherQueue;
        if (dispatcher != null && !dispatcher.HasThreadAccess)
        {
            dispatcher.TryEnqueue(Execute);
        }
        else
        {
            Execute();
        }
    }

    private void OnTrackDislikeChanged(object? sender, (string TrackId, bool IsDisliked) e)
    {
        void Execute()
        {
            List<IEnumerable<Track>> active;
            lock (_lock)
            {
                CleanDeadReferences();
                active = _trackedCollections
                    .Select(r => r.TryGetTarget(out var target) ? target : null)
                    .Where(t => t != null)
                    .Select(t => t!)
                    .ToList();
            }

            foreach (var col in active)
            {
                foreach (var track in col.ToList())
                {
                    if (Track.IsSameTrackId(track.Id, e.TrackId))
                    {
                        if (track.IsDisliked != e.IsDisliked)
                        {
                            track.IsDisliked = e.IsDisliked;
                        }
                    }
                }
            }
        }

        var dispatcher = App.MainDispatcherQueue;
        if (dispatcher != null && !dispatcher.HasThreadAccess)
        {
            dispatcher.TryEnqueue(Execute);
        }
        else
        {
            Execute();
        }
    }

    private void UpdateAll()
    {
        Sync();
    }

    private void UpdatePlaybackOnly()
    {
        string? currentId = _audioPlayer.CurrentTrack?.Id;
        bool isPlaying = _audioPlayer.IsPlaying;

        void Execute()
        {
            List<IEnumerable<Track>> active;
            lock (_lock)
            {
                CleanDeadReferences();
                active = _trackedCollections
                    .Select(r => r.TryGetTarget(out var target) ? target : null)
                    .Where(t => t != null)
                    .Select(t => t!)
                    .ToList();
            }

            foreach (var col in active)
            {
                foreach (var track in col.ToList())
                {
                    bool isCurrent = Track.IsSameTrackId(track.Id, currentId);
                    if (track.IsCurrent != isCurrent)
                    {
                        track.IsCurrent = isCurrent;
                    }
                    bool trackPlaying = isCurrent && isPlaying;
                    if (track.IsPlaying != trackPlaying)
                    {
                        track.IsPlaying = trackPlaying;
                    }
                }
            }
        }

        var dispatcher = App.MainDispatcherQueue;
        if (dispatcher != null && !dispatcher.HasThreadAccess)
        {
            dispatcher.TryEnqueue(Execute);
        }
        else
        {
            Execute();
        }
    }

    private void CleanDeadReferences()
    {
        _trackedCollections.RemoveAll(r => !r.TryGetTarget(out _));
    }
}
