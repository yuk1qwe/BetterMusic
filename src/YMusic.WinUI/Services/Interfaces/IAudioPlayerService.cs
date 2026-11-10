#nullable enable

using YMusic.WinUI.Models;

namespace YMusic.WinUI.Services.Interfaces;

public interface IAudioPlayerService : IDisposable
{
    Track? CurrentTrack { get; }
    bool IsPlaying { get; }
    TimeSpan Position { get; }
    TimeSpan Duration { get; }
    double Volume { get; set; }
    bool IsMuted { get; set; }
    RepeatMode RepeatMode { get; set; }
    bool IsShuffle { get; set; }

    StreamAudioQuality? CurrentQuality { get; }
    bool IsPreloadingEnabled { get; set; }

    IReadOnlyList<Track> Queue { get; }
    int CurrentIndex { get; }

    PlaybackMode CurrentPlaybackMode { get; set; }

    event EventHandler<Track?>? CurrentTrackChanged;
    event EventHandler<StreamAudioQuality?>? CurrentQualityChanged;
    event EventHandler<bool>? IsPlayingChanged;
    event EventHandler<TimeSpan>? PositionChanged;
    event EventHandler<TimeSpan>? DurationChanged;
    event EventHandler<double>? VolumeChanged;
    event EventHandler<bool>? MuteChanged;
    event EventHandler<RepeatMode>? RepeatModeChanged;
    event EventHandler<bool>? IsShuffleChanged;
    event EventHandler? QueueChanged;
    event EventHandler<PlaybackMode>? PlaybackModeChanged;
    event EventHandler<string>? PlaybackFailed;
    event EventHandler<string>? TrackSkippedWithReason;

    Task PlayTrackAsync(Track track, IEnumerable<Track>? newQueue = null);
    Task PlayQueueAsync(IEnumerable<Track> queue, int startIndex = 0);
    void AppendToQueue(IEnumerable<Track> tracks);
    void PlayNext(Track track);
    void ReplaceUpcomingQueue(IEnumerable<Track> newUpcomingTracks);
    void Play();
    void Pause();
    void TogglePlayPause();
    Task NextAsync();
    Task PreviousAsync();
    void Seek(TimeSpan position);
    Task ReloadCurrentTrackQualityAsync();
    void SaveCurrentState();
    void RequestSaveState();
    void StopAndClear();
}
