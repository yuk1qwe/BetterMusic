#nullable enable

using System.Diagnostics;
using System.Security.Cryptography;
using Windows.Media;
using Windows.Media.Audio;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.Render;
using Windows.Storage.Streams;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.Services.Implementations;

public sealed class AudioPlayerService : IAudioPlayerService
{
    private readonly IYandexMusicService _yandexMusicService;
    private readonly IEqualizerService _equalizerService;
    private readonly IPlaybackStateStorageService _playbackStateStorage;
    private readonly IFckCensorService _fckCensorService;
    private readonly IAudioCacheService _audioCacheService;
    private readonly ISettingsService _settingsService;
    private readonly ISloplessService _sloplessService;
    private readonly MediaPlayer _mediaPlayer;
    private readonly SystemMediaTransportControls _smtc;
    private readonly System.Timers.Timer _positionTimer;

    private AudioGraph? _audioGraph;
    private AudioDeviceOutputNode? _deviceOutput;
    private MediaSourceAudioInputNode? _currentInputNode;
    private MediaSourceAudioInputNode? _fadingOutNode;
    private CancellationTokenSource? _crossfadeCts;
    private volatile bool _isCrossfading;
    private EqualizerEffectDefinition? _eq1;
    private EqualizerEffectDefinition? _eq2;
    private EqualizerEffectDefinition? _eq3;

    private readonly List<Track> _queue = new();
    private List<int> _playOrder = new();
    private int _orderIndex = -1;

    private Track? _currentTrack;
    private StreamAudioQuality? _currentQuality;
    private (string TrackId, string StreamUrl, StreamAudioQuality? Quality)? _preloadedTrack;
    private int _playRequestId = 0;
    private CancellationTokenSource? _preloadCts;
    private bool _isPlaying;
    private RepeatMode _repeatMode = RepeatMode.None;
    private bool _isShuffle;
    private bool _isDisposed;
    private double _volume = 80.0;
    private bool _isMuted;
    private TimeSpan _restoredPosition = TimeSpan.Zero;
    private DateTime _lastPeriodicSaveTime = DateTime.UtcNow;
    private readonly object _saveLock = new();
    private readonly object _saveTimerLock = new();
    private System.Threading.Timer? _debouncedSaveTimer;
    private int _consecutivePlaybackFailures = 0;

    public Track? CurrentTrack => _currentTrack;
    public StreamAudioQuality? CurrentQuality => _currentQuality;
    public bool IsPreloadingEnabled { get; set; } = true;
    public bool IsPlaying => _isPlaying;
    public TimeSpan Position
    {
        get
        {
            if (_currentInputNode != null)
            {
                return _currentInputNode.Position;
            }
            if (_mediaPlayer.PlaybackSession.Position > TimeSpan.Zero)
            {
                return _mediaPlayer.PlaybackSession.Position;
            }
            return _restoredPosition;
        }
    }

    public TimeSpan Duration
    {
        get
        {
            if (_currentInputNode != null)
            {
                return _currentInputNode.Duration;
            }
            if (_currentTrack != null && _currentTrack.Duration > TimeSpan.Zero)
            {
                return _currentTrack.Duration;
            }
            return _mediaPlayer.PlaybackSession.NaturalDuration;
        }
    }

    public double Volume
    {
        get => _volume;
        set
        {
            double clamped = Math.Clamp(value, 0.0, 100.0);
            if (Math.Abs(_volume - clamped) < 0.01) return;
            _volume = clamped;
            _mediaPlayer.Volume = _volume / 100.0;
            if (_deviceOutput != null)
            {
                _deviceOutput.OutgoingGain = _isMuted ? 0.0 : (_volume / 100.0);
            }
            VolumeChanged?.Invoke(this, _volume);
            RequestSaveState();
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            _isMuted = value;
            _mediaPlayer.IsMuted = value;
            if (_deviceOutput != null)
            {
                _deviceOutput.OutgoingGain = _isMuted ? 0.0 : (_volume / 100.0);
            }
            MuteChanged?.Invoke(this, value);
            RequestSaveState();
        }
    }

    public RepeatMode RepeatMode
    {
        get => _repeatMode;
        set
        {
            if (_repeatMode != value)
            {
                _repeatMode = value;
                RepeatModeChanged?.Invoke(this, value);
                RequestSaveState();
            }
        }
    }

    public bool IsShuffle
    {
        get => _isShuffle;
        set
        {
            if (_isShuffle != value)
            {
                _isShuffle = value;
                RebuildPlayOrder();
                IsShuffleChanged?.Invoke(this, value);
                RequestSaveState();
            }
        }
    }

    public IReadOnlyList<Track> Queue => _queue;
    public int CurrentIndex => (_orderIndex >= 0 && _orderIndex < _playOrder.Count)
        ? _playOrder[_orderIndex]
        : -1;

    private PlaybackMode _currentPlaybackMode = PlaybackMode.None;

    public PlaybackMode CurrentPlaybackMode
    {
        get => _currentPlaybackMode;
        set
        {
            if (_currentPlaybackMode != value)
            {
                _currentPlaybackMode = value;
                PlaybackModeChanged?.Invoke(this, value);
                RequestSaveState();
            }
        }
    }

    public event EventHandler<Track?>? CurrentTrackChanged;
    event EventHandler<StreamAudioQuality?>? IAudioPlayerService.CurrentQualityChanged
    {
        add => CurrentQualityChanged += value;
        remove => CurrentQualityChanged -= value;
    }
    public event EventHandler<StreamAudioQuality?>? CurrentQualityChanged;
    public event EventHandler<bool>? IsPlayingChanged;
    public event EventHandler<TimeSpan>? PositionChanged;
    public event EventHandler<TimeSpan>? DurationChanged;
    public event EventHandler<double>? VolumeChanged;
    public event EventHandler<bool>? MuteChanged;
    public event EventHandler<RepeatMode>? RepeatModeChanged;
    public event EventHandler<bool>? IsShuffleChanged;
    public event EventHandler? QueueChanged;
    public event EventHandler<PlaybackMode>? PlaybackModeChanged;
    public event EventHandler<string>? PlaybackFailed;
    public event EventHandler<string>? TrackSkippedWithReason;

    public AudioPlayerService(
        IYandexMusicService yandexMusicService,
        IEqualizerService equalizerService,
        IPlaybackStateStorageService playbackStateStorage,
        IFckCensorService fckCensorService,
        IAudioCacheService audioCacheService,
        ISettingsService settingsService,
        ISloplessService sloplessService)
    {
        _yandexMusicService = yandexMusicService;
        _equalizerService = equalizerService;
        _playbackStateStorage = playbackStateStorage;
        _fckCensorService = fckCensorService;
        _audioCacheService = audioCacheService;
        _settingsService = settingsService;
        _sloplessService = sloplessService;

        IsPreloadingEnabled = _settingsService.GaplessPreloadEnabled;
        _settingsService.SettingChanged += async (_, prop) =>
        {
            if (prop == nameof(ISettingsService.GaplessPreloadEnabled))
            {
                IsPreloadingEnabled = _settingsService.GaplessPreloadEnabled;
            }
            else if (prop == nameof(ISettingsService.PreferredQuality))
            {
                await ReloadCurrentTrackQualityAsync().ConfigureAwait(false);
            }
        };

        _fckCensorService.TrackReplacementChanged += async (_, changedTrackId) =>
        {
            if (_currentTrack != null && (Track.IsSameTrackId(_currentTrack.Id, changedTrackId) || (string.IsNullOrEmpty(changedTrackId) && _fckCensorService.IsReplaced(_currentTrack.Id))))
            {
                var pos = Position;
                bool wasPlaying = _isPlaying;
                await ReloadCurrentTrackQualityAsync().ConfigureAwait(false);
                if (pos > TimeSpan.Zero)
                {
                    Seek(pos);
                }
                if (wasPlaying)
                {
                    Play();
                }
            }
        };

        _mediaPlayer = new MediaPlayer
        {
            AudioCategory = MediaPlayerAudioCategory.Media
        };

        _mediaPlayer.PlaybackSession.PlaybackStateChanged += OnPlaybackStateChanged;
        _mediaPlayer.PlaybackSession.NaturalDurationChanged += OnNaturalDurationChanged;
        _mediaPlayer.MediaEnded += OnMediaEnded;
        _mediaPlayer.MediaFailed += (s, e) =>
        {
            System.Diagnostics.Debug.WriteLine($"[AudioPlayer] Media failed: {e.Error} - {e.ErrorMessage}");
        };

        _smtc = _mediaPlayer.SystemMediaTransportControls;
        _smtc.IsPlayEnabled = true;
        _smtc.IsPauseEnabled = true;
        _smtc.IsNextEnabled = true;
        _smtc.IsPreviousEnabled = true;
        _smtc.ButtonPressed += OnSmtcButtonPressed;

        _positionTimer = new System.Timers.Timer(250);
        _positionTimer.Elapsed += (_, _) =>
        {
            if (_isPlaying)
            {
                var pos = Position;
                PositionChanged?.Invoke(this, pos);
                CheckPreloadNextTrack(pos);
                CheckCrossfade(pos);

                var dur = Duration;
                if (dur > TimeSpan.Zero && (_currentTrack == null || _currentTrack.DurationMs <= 0))
                {
                    if (_currentTrack != null)
                    {
                        _currentTrack.DurationMs = (long)dur.TotalMilliseconds;
                    }
                    DurationChanged?.Invoke(this, dur);
                }

                if ((DateTime.UtcNow - _lastPeriodicSaveTime).TotalSeconds >= 3.0)
                {
                    _lastPeriodicSaveTime = DateTime.UtcNow;
                    RequestSaveState();
                }
            }
        };

        _equalizerService.EqualizerChanged += (_, _) => ApplyEqualizerGains();
        RestoreSavedState();
        _ = InitializeAudioGraphAsync();
    }

    private void RestoreSavedState()
    {
        try
        {
            var saved = _playbackStateStorage.LoadPlaybackState();
            if (saved == null) return;

            _volume = Math.Clamp(saved.Volume, 0.0, 100.0);
            _mediaPlayer.Volume = _volume / 100.0;
            _isMuted = saved.IsMuted;
            _mediaPlayer.IsMuted = _isMuted;
            _repeatMode = saved.RepeatMode;
            _isShuffle = saved.IsShuffle;
            _currentPlaybackMode = saved.PlaybackMode;

            if (saved.Queue != null && saved.Queue.Count > 0)
            {
                _queue.Clear();
                _queue.AddRange(saved.Queue);

                if (saved.PlayOrder != null && saved.PlayOrder.Count == saved.Queue.Count)
                {
                    _playOrder = new List<int>(saved.PlayOrder);
                }
                else
                {
                    RebuildPlayOrder();
                }

                _orderIndex = Math.Clamp(saved.OrderIndex, 0, _playOrder.Count - 1);
            }

            if (saved.CurrentTrack != null)
            {
                _currentTrack = saved.CurrentTrack;
                _restoredPosition = TimeSpan.FromSeconds(Math.Max(0, saved.PositionSeconds));
                UpdateSmtcMetadata(_currentTrack);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioPlayerService] Failed to restore state: {ex.Message}");
        }
    }

    public void SaveCurrentState()
    {
        lock (_saveTimerLock)
        {
            _debouncedSaveTimer?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
        }

        lock (_saveLock)
        {
            try
            {
                if (_currentTrack == null && _queue.Count == 0)
                {
                    _playbackStateStorage.ClearPlaybackState();
                    return;
                }

                var state = new PlaybackSessionState
                {
                    Volume = _volume,
                    IsMuted = _isMuted,
                    RepeatMode = _repeatMode,
                    IsShuffle = _isShuffle,
                    PlaybackMode = _currentPlaybackMode,
                    OrderIndex = _orderIndex,
                    PositionSeconds = Position.TotalSeconds,
                    CurrentTrack = _currentTrack,
                    Queue = new List<Track>(_queue),
                    PlayOrder = new List<int>(_playOrder)
                };

                _playbackStateStorage.SavePlaybackState(state);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AudioPlayerService] Failed to save state: {ex.Message}");
            }
        }
    }

    public void RequestSaveState()
    {
        lock (_saveTimerLock)
        {
            if (_debouncedSaveTimer == null)
            {
                _debouncedSaveTimer = new System.Threading.Timer(_ =>
                {
                    SaveCurrentState();
                }, null, 1000, System.Threading.Timeout.Infinite);
            }
            else
            {
                _debouncedSaveTimer.Change(1000, System.Threading.Timeout.Infinite);
            }
        }
    }

    private async Task InitializeAudioGraphAsync()
    {
        try
        {
            var settings = new AudioGraphSettings(AudioRenderCategory.Media);
            var result = await AudioGraph.CreateAsync(settings);
            if (result.Status == AudioGraphCreationStatus.Success)
            {
                _audioGraph = result.Graph;
                var outputResult = await _audioGraph.CreateDeviceOutputNodeAsync();
                if (outputResult.Status == AudioDeviceNodeCreationStatus.Success)
                {
                    _deviceOutput = outputResult.DeviceOutputNode;

                    _eq1 = new EqualizerEffectDefinition(_audioGraph);
                    _eq2 = new EqualizerEffectDefinition(_audioGraph);
                    _eq3 = new EqualizerEffectDefinition(_audioGraph);

                    SetupEqualizerFrequencies();
                    ApplyEqualizerGains();

                    _deviceOutput.EffectDefinitions.Add(_eq1);
                    _deviceOutput.EffectDefinitions.Add(_eq2);
                    _deviceOutput.EffectDefinitions.Add(_eq3);

                    _deviceOutput.OutgoingGain = _isMuted ? 0.0 : (_volume / 100.0);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioGraph Init Warning] {ex.Message}");
        }
    }

    private void SetupEqualizerFrequencies()
    {
        if (_eq1 == null || _eq2 == null || _eq3 == null) return;

        // 10 bands mapped across 3 4-band equalizer definitions:
        // eq1: 32Hz, 64Hz, 125Hz, 250Hz
        _eq1.Bands[0].FrequencyCenter = 32;
        _eq1.Bands[0].Bandwidth = 1.0;
        _eq1.Bands[1].FrequencyCenter = 64;
        _eq1.Bands[1].Bandwidth = 1.0;
        _eq1.Bands[2].FrequencyCenter = 125;
        _eq1.Bands[2].Bandwidth = 1.0;
        _eq1.Bands[3].FrequencyCenter = 250;
        _eq1.Bands[3].Bandwidth = 1.0;

        // eq2: 500Hz, 1kHz, 2kHz, 4kHz
        _eq2.Bands[0].FrequencyCenter = 500;
        _eq2.Bands[0].Bandwidth = 1.0;
        _eq2.Bands[1].FrequencyCenter = 1000;
        _eq2.Bands[1].Bandwidth = 1.0;
        _eq2.Bands[2].FrequencyCenter = 2000;
        _eq2.Bands[2].Bandwidth = 1.0;
        _eq2.Bands[3].FrequencyCenter = 4000;
        _eq2.Bands[3].Bandwidth = 1.0;

        // eq3: 8kHz, 16kHz
        _eq3.Bands[0].FrequencyCenter = 8000;
        _eq3.Bands[0].Bandwidth = 1.0;
        _eq3.Bands[1].FrequencyCenter = 16000;
        _eq3.Bands[1].Bandwidth = 1.0;
    }

    private void ApplyEqualizerGains()
    {
        if (_eq1 == null || _eq2 == null || _eq3 == null) return;

        try
        {
            if (!_equalizerService.IsEnabled)
            {
                for (int i = 0; i < 4; i++) { _eq1.Bands[i].Gain = 1.0; _eq2.Bands[i].Gain = 1.0; }
                for (int i = 0; i < 2; i++) { _eq3.Bands[i].Gain = 1.0; }
                return;
            }

            var bands = _equalizerService.Bands;
            if (bands.Count >= 10)
            {
                _eq1.Bands[0].Gain = ToLinearGain(bands[0].Gain);
                _eq1.Bands[1].Gain = ToLinearGain(bands[1].Gain);
                _eq1.Bands[2].Gain = ToLinearGain(bands[2].Gain);
                _eq1.Bands[3].Gain = ToLinearGain(bands[3].Gain);

                _eq2.Bands[0].Gain = ToLinearGain(bands[4].Gain);
                _eq2.Bands[1].Gain = ToLinearGain(bands[5].Gain);
                _eq2.Bands[2].Gain = ToLinearGain(bands[6].Gain);
                _eq2.Bands[3].Gain = ToLinearGain(bands[7].Gain);

                _eq3.Bands[0].Gain = ToLinearGain(bands[8].Gain);
                _eq3.Bands[1].Gain = ToLinearGain(bands[9].Gain);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ApplyEqualizerGains Error] {ex.Message}");
        }
    }

    private static double ToLinearGain(double gainDb)
    {
        // 0 dB = 1.0 (unity gain)
        // Valid range for Windows.Media.Audio.EqualizerBand.Gain is [0.158, 7.079] (-16 dB to +17 dB)
        double clampedDb = Math.Clamp(gainDb, -15.0, 15.0);
        return Math.Pow(10.0, clampedDb / 20.0);
    }

    private static bool IsSameQueue(List<Track> current, IList<Track> incoming)
    {
        if (ReferenceEquals(current, incoming)) return true;
        if (current.Count == 0 || incoming.Count == 0) return false;
        if (current.Count != incoming.Count) return false;

        if (!Track.IsSameTrackId(current[0].Id, incoming[0].Id)) return false;
        if (!Track.IsSameTrackId(current[^1].Id, incoming[^1].Id)) return false;
        int mid = current.Count / 2;
        if (!Track.IsSameTrackId(current[mid].Id, incoming[mid].Id)) return false;

        return true;
    }

    public async Task PlayTrackAsync(Track track, IEnumerable<Track>? newQueue = null)
    {
        if (track == null || !track.Available) return;
        _consecutivePlaybackFailures = 0;

        if (newQueue != null)
        {
            var newQueueList = newQueue as IList<Track> ?? newQueue.ToList();
            bool isSame = IsSameQueue(_queue, newQueueList);

            if (!isSame)
            {
                _queue.Clear();
                _queue.AddRange(newQueueList);
                RebuildPlayOrder();
                QueueChanged?.Invoke(this, EventArgs.Empty);
            }

            int trackIndex = _queue.FindIndex(t => Track.IsSameTrackId(t.Id, track.Id));
            if (trackIndex >= 0)
            {
                _orderIndex = _playOrder.IndexOf(trackIndex);
            }
            else
            {
                _queue.Insert(0, track);
                RebuildPlayOrder();
                _orderIndex = 0;
                QueueChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        else
        {
            int existing = _queue.FindIndex(t => Track.IsSameTrackId(t.Id, track.Id));
            if (existing >= 0)
            {
                _orderIndex = _playOrder.IndexOf(existing);
            }
            else
            {
                _queue.Add(track);
                RebuildPlayOrder();
                _orderIndex = _playOrder.IndexOf(_queue.Count - 1);
                QueueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        await LoadAndPlayTrackAsync(track).ConfigureAwait(false);
    }

    public async Task PlayQueueAsync(IEnumerable<Track> queue, int startIndex = 0)
    {
        var tracks = queue as IList<Track> ?? queue.ToList();
        if (tracks.Count == 0)
        {
            return;
        }
        _consecutivePlaybackFailures = 0;

        bool isSame = IsSameQueue(_queue, tracks);
        if (!isSame)
        {
            _queue.Clear();
            _queue.AddRange(tracks);
            RebuildPlayOrder();
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }

        int targetIndex = Math.Clamp(startIndex, 0, _queue.Count - 1);
        if (!_queue[targetIndex].Available)
        {
            int firstAvailable = _queue.FindIndex(targetIndex, t => t.Available);
            if (firstAvailable < 0) firstAvailable = _queue.FindIndex(t => t.Available);
            if (firstAvailable >= 0)
            {
                targetIndex = firstAvailable;
            }
            else
            {
                return;
            }
        }
        _orderIndex = _playOrder.IndexOf(targetIndex);

        var trackToPlay = _queue[targetIndex];
        await LoadAndPlayTrackAsync(trackToPlay).ConfigureAwait(false);
    }

    public void AppendToQueue(IEnumerable<Track> tracks)
    {
        var list = tracks.ToList();
        if (list.Count == 0) return;

        int previousCount = _queue.Count;
        _queue.AddRange(list);

        if (_isShuffle)
        {
            var newIndices = Enumerable.Range(previousCount, list.Count).ToList();
            for (int i = newIndices.Count - 1; i > 0; i--)
            {
                int j = RandomNumberGenerator.GetInt32(i + 1);
                (newIndices[i], newIndices[j]) = (newIndices[j], newIndices[i]);
            }
            _playOrder.AddRange(newIndices);
        }
        else
        {
            for (int i = previousCount; i < _queue.Count; i++)
            {
                _playOrder.Add(i);
            }
        }

        QueueChanged?.Invoke(this, EventArgs.Empty);
        RequestSaveState();
    }

    public void PlayNext(Track track)
    {
        if (_queue.Count == 0 || _orderIndex < 0)
        {
            _ = PlayTrackAsync(track);
            return;
        }

        int currentTrackQueueIndex = (_orderIndex >= 0 && _orderIndex < _playOrder.Count)
            ? _playOrder[_orderIndex]
            : -1;

        int insertQueueIndex = currentTrackQueueIndex >= 0 ? currentTrackQueueIndex + 1 : _queue.Count;
        if (insertQueueIndex > _queue.Count) insertQueueIndex = _queue.Count;

        _queue.Insert(insertQueueIndex, track);

        for (int i = 0; i < _playOrder.Count; i++)
        {
            if (_playOrder[i] >= insertQueueIndex)
            {
                _playOrder[i]++;
            }
        }

        int insertOrderIndex = _orderIndex + 1;
        if (insertOrderIndex > _playOrder.Count) insertOrderIndex = _playOrder.Count;
        _playOrder.Insert(insertOrderIndex, insertQueueIndex);

        _preloadedTrack = null;
        QueueChanged?.Invoke(this, EventArgs.Empty);
        RequestSaveState();
    }

    public void ReplaceUpcomingQueue(IEnumerable<Track> newUpcomingTracks)
    {
        var list = newUpcomingTracks.ToList();
        if (_currentTrack == null)
        {
            if (list.Count > 0)
            {
                _ = PlayQueueAsync(list, 0);
            }
            return;
        }

        _queue.Clear();
        _queue.Add(_currentTrack);
        _queue.AddRange(list);

        _playOrder = Enumerable.Range(0, _queue.Count).ToList();
        _orderIndex = 0;
        _preloadedTrack = null;

        QueueChanged?.Invoke(this, EventArgs.Empty);
        RequestSaveState();
    }

    public void Play()
    {
        if (_currentInputNode != null && _audioGraph != null)
        {
            _currentInputNode.Start();
            _fadingOutNode?.Start();
            _audioGraph.Start();
            _isPlaying = true;
            _smtc.PlaybackStatus = MediaPlaybackStatus.Playing;
            IsPlayingChanged?.Invoke(this, true);
            _positionTimer.Start();
            return;
        }

        if (_currentInputNode == null && _mediaPlayer.Source == null && _currentTrack != null)
        {
            _ = LoadAndPlayTrackAsync(_currentTrack, _restoredPosition);
            return;
        }

        _mediaPlayer.Play();
    }

    public void Pause()
    {
        if (_currentInputNode != null && _audioGraph != null)
        {
            _currentInputNode.Stop();
            _fadingOutNode?.Stop();
            _isPlaying = false;
            _smtc.PlaybackStatus = MediaPlaybackStatus.Paused;
            IsPlayingChanged?.Invoke(this, false);
            _positionTimer.Stop();
            RequestSaveState();
            return;
        }

        _mediaPlayer.Pause();
        RequestSaveState();
    }

    public void TogglePlayPause()
    {
        if (_isPlaying)
        {
            Pause();
        }
        else
        {
            Play();
        }
    }

    public void StopAndClear()
    {
        CancelActiveCrossfade();
        lock (_saveLock)
        {
            try
            {
                _positionTimer.Stop();

                if (_currentInputNode != null)
                {
                    _currentInputNode.Stop();
                    if (_deviceOutput != null)
                    {
                        try { _currentInputNode.RemoveOutgoingConnection(_deviceOutput); } catch { }
                    }
                    _currentInputNode.MediaSourceCompleted -= OnInputNodeMediaCompleted;
                    _currentInputNode.Dispose();
                    _currentInputNode = null;
                }

                if (_audioGraph != null)
                {
                    try { _audioGraph.Stop(); } catch { }
                }

                try
                {
                    _mediaPlayer.Pause();
                    _mediaPlayer.Source = null;
                }
                catch { }

                _currentTrack = null;
                _currentQuality = null;
                _preloadedTrack = null;
                _isPlaying = false;
                _queue.Clear();
                _playOrder.Clear();
                _orderIndex = -1;
                _restoredPosition = TimeSpan.Zero;

                try
                {
                    _smtc.PlaybackStatus = MediaPlaybackStatus.Stopped;
                    _smtc.DisplayUpdater.ClearAll();
                    _smtc.DisplayUpdater.Update();
                }
                catch { }

                _playbackStateStorage.ClearPlaybackState();

                CurrentTrackChanged?.Invoke(this, null);
                CurrentQualityChanged?.Invoke(this, null);
                IsPlayingChanged?.Invoke(this, false);
                PositionChanged?.Invoke(this, TimeSpan.Zero);
                DurationChanged?.Invoke(this, TimeSpan.Zero);
                QueueChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AudioPlayerService] StopAndClear error: {ex.Message}");
            }
        }
    }

    public async Task NextAsync()
    {
        CancelActiveCrossfade();
        if (_queue.Count == 0 || _playOrder.Count == 0)
        {
            return;
        }

        int nextOrderIndex = _orderIndex + 1;
        if (nextOrderIndex >= _playOrder.Count)
        {
            if (_repeatMode == RepeatMode.All)
            {
                nextOrderIndex = 0;
            }
            else
            {
                // If album or queue ended, seamlessly continue with Wave if enabled
                if (_settingsService.AutoplayWaveOnAlbumEnd && await TryStartAlbumWaveOnQueueEndAsync().ConfigureAwait(false))
                {
                    return;
                }

                Pause();
                Seek(TimeSpan.Zero);
                return;
            }
        }

        _orderIndex = nextOrderIndex;
        int trackIndex = _playOrder[_orderIndex];
        var track = _queue[trackIndex];
        await LoadAndPlayTrackAsync(track).ConfigureAwait(false);
    }

    private async Task<bool> TryStartAlbumWaveOnQueueEndAsync()
    {
        try
        {
            var lastTrack = _currentTrack ?? (_queue.Count > 0 ? _queue[^1] : null);
            if (lastTrack == null) return false;

            // Determine target station: album station if available, otherwise track station
            string stationType = "album";
            string stationTag = string.Empty;

            if (lastTrack.Albums.Count > 0 && lastTrack.Albums[0].Id > 0)
            {
                stationType = "album";
                stationTag = lastTrack.Albums[0].Id.ToString();
            }
            else if (!string.IsNullOrWhiteSpace(lastTrack.Id))
            {
                stationType = "track";
                stationTag = lastTrack.Id;
            }
            else
            {
                stationType = "user";
                stationTag = "onyourwave";
            }

            Debug.WriteLine($"[AudioPlayerService] Album ended. Starting Wave for {stationType}:{stationTag}...");
            var stationResult = await _yandexMusicService.GetRotorStationTracksAsync(stationType, stationTag).ConfigureAwait(false);

            // If album station returned no tracks, fallback to track wave or user onyourwave
            if ((stationResult?.Sequence == null || stationResult.Sequence.Count == 0) && stationType == "album")
            {
                stationType = "track";
                stationTag = lastTrack.Id;
                stationResult = await _yandexMusicService.GetRotorStationTracksAsync(stationType, stationTag).ConfigureAwait(false);
            }

            if (stationResult?.Sequence != null && stationResult.Sequence.Count > 0)
            {
                var waveTracks = stationResult.Sequence
                    .Where(i => i.Track != null)
                    .Select(i => i.Track!)
                    .ToList();

                if (waveTracks.Count > 0)
                {
                    CurrentPlaybackMode = PlaybackMode.Wave;
                    AppendToQueue(waveTracks);

                    int newOrderIndex = _orderIndex + 1;
                    if (newOrderIndex < _playOrder.Count)
                    {
                        _orderIndex = newOrderIndex;
                        int trackIdx = _playOrder[_orderIndex];
                        var nextTrack = _queue[trackIdx];
                        await LoadAndPlayTrackAsync(nextTrack).ConfigureAwait(false);
                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioPlayerService] Failed to autoplay wave on album end: {ex.Message}");
        }

        return false;
    }

    public async Task PreviousAsync()
    {
        CancelActiveCrossfade();
        if (_queue.Count == 0 || _playOrder.Count == 0)
        {
            return;
        }

        // If played more than 3 seconds, restart current track
        if (Position.TotalSeconds > 3.0)
        {
            Seek(TimeSpan.Zero);
            Play();
            return;
        }

        int prevOrderIndex = _orderIndex - 1;
        if (prevOrderIndex < 0)
        {
            if (_repeatMode == RepeatMode.All)
            {
                prevOrderIndex = _playOrder.Count - 1;
            }
            else
            {
                Seek(TimeSpan.Zero);
                Play();
                return;
            }
        }

        _orderIndex = prevOrderIndex;
        int trackIndex = _playOrder[_orderIndex];
        var track = _queue[trackIndex];
        await LoadAndPlayTrackAsync(track).ConfigureAwait(false);
    }

    public void Seek(TimeSpan position)
    {
        CancelActiveCrossfade();
        var duration = Duration;
        double maxSec = duration.TotalSeconds > 0 ? duration.TotalSeconds : double.MaxValue;
        var clamped = TimeSpan.FromSeconds(Math.Clamp(position.TotalSeconds, 0, maxSec));
        _restoredPosition = clamped;

        if (_currentInputNode != null)
        {
            try
            {
                _currentInputNode.Seek(clamped);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AudioPlayer Seek Warning] {ex.Message}");
            }
            PositionChanged?.Invoke(this, clamped);
            RequestSaveState();
            return;
        }

        try
        {
            _mediaPlayer.PlaybackSession.Position = clamped;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MediaPlayer Seek Warning] {ex.Message}");
        }
        PositionChanged?.Invoke(this, clamped);
        RequestSaveState();
    }

    public async Task ReloadCurrentTrackQualityAsync()
    {
        if (_currentTrack == null)
        {
            return;
        }

        var currentPos = Position;
        var wasPlaying = _isPlaying;

        var (streamUrl, quality) = await ResolveTrackStreamAsync(_currentTrack.Id, forceRefresh: true).ConfigureAwait(false);
        _currentQuality = quality;
        CurrentQualityChanged?.Invoke(this, quality);

        if (!string.IsNullOrWhiteSpace(streamUrl))
        {
            var source = MediaSource.CreateFromUri(new Uri(streamUrl));

            if (_audioGraph != null && _deviceOutput != null)
            {
                try
                {
                    var inputResult = await _audioGraph.CreateMediaSourceAudioInputNodeAsync(source);
                    if (inputResult.Status == MediaSourceAudioInputNodeCreationStatus.Success)
                    {
                        if (_currentInputNode != null)
                        {
                            _currentInputNode.Stop();
                            _currentInputNode.RemoveOutgoingConnection(_deviceOutput);
                            _currentInputNode.Dispose();
                        }

                        _currentInputNode = inputResult.Node;
                        _currentInputNode.AddOutgoingConnection(_deviceOutput);
                        _currentInputNode.MediaSourceCompleted += OnInputNodeMediaCompleted;
                        _currentInputNode.Seek(currentPos);
                        if (wasPlaying)
                        {
                            _currentInputNode.Start();
                            _audioGraph.Start();
                            _isPlaying = true;
                            _smtc.PlaybackStatus = MediaPlaybackStatus.Playing;
                            IsPlayingChanged?.Invoke(this, true);
                        }
                        return;
                    }
                }
                catch
                {
                }
            }

            _mediaPlayer.Source = source;
            Seek(currentPos);
            if (wasPlaying)
            {
                _mediaPlayer.Play();
            }
        }
    }

    private void TriggerUpcomingTracksPreload()
    {
        if (!IsPreloadingEnabled)
        {
            return;
        }

        _preloadCts?.Cancel();
        _preloadCts?.Dispose();
        var cts = new CancellationTokenSource();
        _preloadCts = cts;
        var token = cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                // Debounce slightly (1000 ms) so rapid skipping doesn't thrash network
                await Task.Delay(1000, token).ConfigureAwait(false);
                if (token.IsCancellationRequested) return;

                await PreloadUpcomingTracksInternalAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AudioPlayerService] Preload error: {ex.Message}");
            }
        }, token);
    }

    private async Task PreloadUpcomingTracksInternalAsync(CancellationToken token)
    {
        if (_queue.Count == 0 || _playOrder.Count == 0) return;

        // 1. Resolve upcoming track 1 (orderIndex + 1)
        int nextOrderIndex = _orderIndex + 1;
        if (nextOrderIndex >= _playOrder.Count)
        {
            if (_repeatMode == RepeatMode.All)
            {
                nextOrderIndex = 0;
            }
            else
            {
                return;
            }
        }

        if (nextOrderIndex < 0 || nextOrderIndex >= _playOrder.Count) return;

        int nextTrackIndex = _playOrder[nextOrderIndex];
        if (nextTrackIndex < 0 || nextTrackIndex >= _queue.Count) return;

        var nextTrack = _queue[nextTrackIndex];
        if (token.IsCancellationRequested) return;

        // Prefetch lyrics in background
        _ = _yandexMusicService.GetTrackLyricsAsync(
            nextTrack.Id,
            nextTrack.FullTitle,
            nextTrack.ArtistNames,
            nextTrack.Duration.TotalSeconds,
            nextTrack.HasLyricsOnYandex,
            token);

        // Check if nextTrack is in cache or resolve and enqueue
        string? cachedPath = null;
        StreamAudioQuality? cachedQuality = null;
        bool isNextCached = _audioCacheService != null && _audioCacheService.TryGetCachedTrack(nextTrack.Id, out cachedPath, out cachedQuality);
        if (isNextCached && !string.IsNullOrEmpty(cachedPath))
        {
            _preloadedTrack = (nextTrack.Id, cachedPath, cachedQuality);
        }
        else
        {
            var (url, quality) = await ResolveTrackStreamAsync(nextTrack.Id).ConfigureAwait(false);
            if (token.IsCancellationRequested) return;

            if (!string.IsNullOrEmpty(url))
            {
                _preloadedTrack = (nextTrack.Id, url, quality);
            }
        }

        if (token.IsCancellationRequested) return;

        // 2. Resolve upcoming track 2 (orderIndex + 2) to pre-cache as well
        int nextNextOrderIndex = nextOrderIndex + 1;
        if (nextNextOrderIndex >= _playOrder.Count && _repeatMode == RepeatMode.All)
        {
            nextNextOrderIndex = 0;
        }

        if (nextNextOrderIndex >= 0 && nextNextOrderIndex < _playOrder.Count && nextNextOrderIndex != _orderIndex)
        {
            int nextNextTrackIndex = _playOrder[nextNextOrderIndex];
            if (nextNextTrackIndex >= 0 && nextNextTrackIndex < _queue.Count)
            {
                var nextNextTrack = _queue[nextNextTrackIndex];
                if (_audioCacheService != null && !_audioCacheService.IsTrackCached(nextNextTrack.Id))
                {
                    _ = ResolveTrackStreamAsync(nextNextTrack.Id);
                }
            }
        }
    }

    private void CheckPreloadNextTrack(TimeSpan pos)
    {
        if (!IsPreloadingEnabled)
        {
            return;
        }

        var duration = Duration;
        if (duration.TotalSeconds < 10)
        {
            return;
        }

        // At 80% mark, verify preload is fresh and lyrics are loaded
        if (pos.TotalSeconds >= duration.TotalSeconds * 0.80)
        {
            if (_preloadedTrack == null)
            {
                TriggerUpcomingTracksPreload();
            }
        }
    }

    private void CancelActiveCrossfade()
    {
        _crossfadeCts?.Cancel();
        _crossfadeCts?.Dispose();
        _crossfadeCts = null;
        _isCrossfading = false;

        if (_fadingOutNode != null)
        {
            try
            {
                _fadingOutNode.Stop();
                if (_deviceOutput != null)
                {
                    _fadingOutNode.RemoveOutgoingConnection(_deviceOutput);
                }
                _fadingOutNode.Dispose();
            }
            catch { }
            _fadingOutNode = null;
        }

        if (_currentInputNode != null)
        {
            try
            {
                _currentInputNode.OutgoingGain = 1.0;
            }
            catch { }
        }
    }

    private void CheckCrossfade(TimeSpan pos)
    {
        if (!_settingsService.CrossfadeEnabled) return;
        if (_isCrossfading) return;
        if (!_isPlaying) return;
        if (_currentInputNode == null || _audioGraph == null || _deviceOutput == null) return;
        if (_repeatMode == RepeatMode.One) return;

        var dur = Duration;
        if (dur <= TimeSpan.FromSeconds(5)) return;

        int configuredSeconds = _settingsService.CrossfadeDurationSeconds;
        // Don't crossfade more than 40% of the entire track duration
        double maxAllowedSeconds = dur.TotalSeconds * 0.4;
        double crossfadeSec = Math.Min(configuredSeconds, maxAllowedSeconds);
        if (crossfadeSec < 1.0) return;

        var remaining = dur - pos;
        if (remaining.TotalSeconds <= crossfadeSec && remaining.TotalSeconds > 0.3 && pos.TotalSeconds > 3.0)
        {
            _ = TryStartCrossfadeAsync((int)Math.Round(crossfadeSec));
        }
    }

    private async Task TryStartCrossfadeAsync(int crossfadeSeconds)
    {
        if (_isCrossfading) return;
        _isCrossfading = true;

        try
        {
            if (_queue.Count == 0 || _playOrder.Count == 0)
            {
                _isCrossfading = false;
                return;
            }

            int nextOrderIndex = _orderIndex + 1;
            if (nextOrderIndex >= _playOrder.Count)
            {
                if (_repeatMode == RepeatMode.All)
                {
                    nextOrderIndex = 0;
                }
                else
                {
                    _isCrossfading = false;
                    return;
                }
            }

            if (nextOrderIndex < 0 || nextOrderIndex >= _playOrder.Count)
            {
                _isCrossfading = false;
                return;
            }

            int nextTrackIndex = _playOrder[nextOrderIndex];
            if (nextTrackIndex < 0 || nextTrackIndex >= _queue.Count)
            {
                _isCrossfading = false;
                return;
            }

            var nextTrack = _queue[nextTrackIndex];
            if (nextTrack == null || !nextTrack.Available)
            {
                _isCrossfading = false;
                return;
            }

            string? streamUrl = null;
            StreamAudioQuality? quality = null;

            if (_preloadedTrack.HasValue && Track.IsSameTrackId(_preloadedTrack.Value.TrackId, nextTrack.Id))
            {
                streamUrl = _preloadedTrack.Value.StreamUrl;
                quality = _preloadedTrack.Value.Quality;
            }
            else if (_audioCacheService != null && _audioCacheService.TryGetCachedTrack(nextTrack.Id, out var cachedPath, out var cachedQuality))
            {
                streamUrl = cachedPath;
                quality = cachedQuality;
            }
            else
            {
                (streamUrl, quality) = await ResolveTrackStreamAsync(nextTrack.Id).ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(streamUrl) || _audioGraph == null || _deviceOutput == null)
            {
                _isCrossfading = false;
                return;
            }

            var nextSource = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(streamUrl));
            var inputResult = await _audioGraph.CreateMediaSourceAudioInputNodeAsync(nextSource);
            if (inputResult.Status != Windows.Media.Audio.MediaSourceAudioInputNodeCreationStatus.Success || inputResult.Node == null)
            {
                _isCrossfading = false;
                return;
            }

            var nextNode = inputResult.Node;
            nextNode.OutgoingGain = 0.0;
            nextNode.AddOutgoingConnection(_deviceOutput);
            nextNode.Start();

            _crossfadeCts?.Cancel();
            _crossfadeCts?.Dispose();
            _crossfadeCts = new CancellationTokenSource();
            var ct = _crossfadeCts.Token;

            var oldNode = _currentInputNode;
            if (oldNode != null)
            {
                oldNode.MediaSourceCompleted -= OnInputNodeMediaCompleted;
            }
            _fadingOutNode = oldNode;
            _currentInputNode = nextNode;

            _orderIndex = nextOrderIndex;
            _currentTrack = nextTrack;
            _currentQuality = quality;
            CurrentTrackChanged?.Invoke(this, nextTrack);
            CurrentQualityChanged?.Invoke(this, quality);
            UpdateSmtcMetadata(nextTrack);
            RequestSaveState();

            if (nextNode.Duration > TimeSpan.Zero)
            {
                DurationChanged?.Invoke(this, nextNode.Duration);
            }
            PositionChanged?.Invoke(this, TimeSpan.Zero);

            int totalSteps = Math.Max(10, crossfadeSeconds * 20); // 20 updates per second (50ms)
            int stepDelayMs = (crossfadeSeconds * 1000) / totalSteps;

            for (int step = 1; step <= totalSteps; step++)
            {
                if (ct.IsCancellationRequested) break;
                await Task.Delay(stepDelayMs, ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested) break;

                double t = (double)step / totalSteps;
                double gainOut = Math.Cos(t * Math.PI * 0.5);
                double gainIn = Math.Sin(t * Math.PI * 0.5);

                if (_fadingOutNode != null)
                {
                    _fadingOutNode.OutgoingGain = gainOut;
                }
                if (_currentInputNode != null)
                {
                    _currentInputNode.OutgoingGain = gainIn;
                }
            }

            if (_fadingOutNode != null)
            {
                try
                {
                    _fadingOutNode.Stop();
                    _fadingOutNode.RemoveOutgoingConnection(_deviceOutput);
                    _fadingOutNode.Dispose();
                }
                catch { }
                _fadingOutNode = null;
            }

            if (_currentInputNode != null)
            {
                _currentInputNode.OutgoingGain = 1.0;
                _currentInputNode.MediaSourceCompleted += OnInputNodeMediaCompleted;
            }

            _isCrossfading = false;
            TriggerUpcomingTracksPreload();
        }
        catch (OperationCanceledException)
        {
            // Expected when user pauses or skips
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Crossfade Error] {ex.Message}");
        }
        finally
        {
            _isCrossfading = false;
        }
    }

    private async Task<(string? Url, StreamAudioQuality? Quality)> ResolveTrackStreamAsync(string trackId, bool forceRefresh = false)
    {
        if (_fckCensorService != null && _fckCensorService.IsReplaced(trackId))
        {
            var replacementUrl = _fckCensorService.GetReplacementUrl(trackId);
            if (!string.IsNullOrEmpty(replacementUrl))
            {
                return (replacementUrl, new StreamAudioQuality { Codec = "MP3", BitrateInKbps = 320 });
            }
        }

        // Instant local playback from LRU Audio Disk Cache (0 ms network latency)
        if (!forceRefresh && _audioCacheService != null && _audioCacheService.TryGetCachedTrack(trackId, out var cachedPath, out var cachedQuality))
        {
            return (cachedPath, cachedQuality);
        }

        var streamInfo = await _yandexMusicService.GetTrackStreamInfoAsync(trackId).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(streamInfo.Url) && _audioCacheService != null)
        {
            // Non-blocking background caching to disk for subsequent instant replays
            _audioCacheService.EnqueueDownload(trackId, streamInfo.Url, streamInfo.Quality);
        }

        return streamInfo;
    }

    private async Task LoadAndPlayTrackAsync(Track track, TimeSpan? startPosition = null)
    {
        CancelActiveCrossfade();
        int requestId = Interlocked.Increment(ref _playRequestId);

        // Cancel previous background preloads
        _preloadCts?.Cancel();
        _preloadCts?.Dispose();
        _preloadCts = null;

        _currentTrack = track;
        CurrentTrackChanged?.Invoke(this, track);
        UpdateSmtcMetadata(track);
        RequestSaveState();

        if (_sloplessService != null && _settingsService.SloplessEnabled)
        {
            _ = HandleSloplessAiCheckAsync(track, requestId);
        }

        string? streamUrl = null;
        StreamAudioQuality? quality = null;

        // Prefer local disk cache if available (0 ms latency)
        if (_audioCacheService != null && _audioCacheService.TryGetCachedTrack(track.Id, out var cachedPath, out var cachedQuality))
        {
            streamUrl = cachedPath;
            quality = cachedQuality;
            _preloadedTrack = null;
        }
        else if (_preloadedTrack?.TrackId == track.Id && !string.IsNullOrEmpty(_preloadedTrack.Value.StreamUrl))
        {
            streamUrl = _preloadedTrack.Value.StreamUrl;
            quality = _preloadedTrack.Value.Quality;
            _preloadedTrack = null;
        }

        // If track is cold (not in cache and not preloaded), stop old audio immediately and show immediate transition
        if (string.IsNullOrWhiteSpace(streamUrl))
        {
            if (_currentInputNode != null)
            {
                try
                {
                    _currentInputNode.Stop();
                    if (_deviceOutput != null)
                    {
                        try { _currentInputNode.RemoveOutgoingConnection(_deviceOutput); } catch { }
                    }
                    _currentInputNode.MediaSourceCompleted -= OnInputNodeMediaCompleted;
                    _currentInputNode.Dispose();
                }
                catch { }
                _currentInputNode = null;
            }
            try
            {
                _mediaPlayer.Pause();
                _mediaPlayer.Source = null;
            }
            catch { }

            _positionTimer.Stop();
            PositionChanged?.Invoke(this, TimeSpan.Zero);

            (streamUrl, quality) = await ResolveTrackStreamAsync(track.Id).ConfigureAwait(false);
            if (requestId != _playRequestId)
            {
                return;
            }
        }

        _currentQuality = quality;
        CurrentQualityChanged?.Invoke(this, quality);

        if (string.IsNullOrWhiteSpace(streamUrl))
        {
            if (requestId == _playRequestId)
            {
                _consecutivePlaybackFailures++;
                if (_consecutivePlaybackFailures >= 3)
                {
                    _consecutivePlaybackFailures = 0;
                    _isPlaying = false;
                    IsPlayingChanged?.Invoke(this, false);
                    try
                    {
                        _mediaPlayer.Pause();
                    }
                    catch { }
                    PlaybackFailed?.Invoke(this, "RegionRestrictionOrNetworkError");
                    return;
                }

                await NextAsync().ConfigureAwait(false);
            }
            return;
        }

        _consecutivePlaybackFailures = 0;

        var source = MediaSource.CreateFromUri(new Uri(streamUrl));
        source.OpenOperationCompleted += (s, args) =>
        {
            if (s.Duration.HasValue && s.Duration.Value > TimeSpan.Zero)
            {
                var realDur = s.Duration.Value;
                if (_currentTrack != null && _currentTrack.DurationMs <= 0)
                {
                    _currentTrack.DurationMs = (long)realDur.TotalMilliseconds;
                }
                DurationChanged?.Invoke(this, realDur);
            }
        };

        if (_audioGraph != null && _deviceOutput != null)
        {
            try
            {
                var inputResult = await _audioGraph.CreateMediaSourceAudioInputNodeAsync(source);
                if (requestId != _playRequestId)
                {
                    inputResult.Node?.Dispose();
                    return;
                }

                if (inputResult.Status == MediaSourceAudioInputNodeCreationStatus.Success)
                {
                    if (_currentInputNode != null)
                    {
                        _currentInputNode.Stop();
                        _currentInputNode.RemoveOutgoingConnection(_deviceOutput);
                        _currentInputNode.MediaSourceCompleted -= OnInputNodeMediaCompleted;
                        _currentInputNode.Dispose();
                    }

                    _currentInputNode = inputResult.Node;
                    _currentInputNode.AddOutgoingConnection(_deviceOutput);
                    _currentInputNode.MediaSourceCompleted += OnInputNodeMediaCompleted;

                    if (_currentInputNode.Duration > TimeSpan.Zero)
                    {
                        var nodeDur = _currentInputNode.Duration;
                        if (_currentTrack != null && _currentTrack.DurationMs <= 0)
                        {
                            _currentTrack.DurationMs = (long)nodeDur.TotalMilliseconds;
                        }
                        DurationChanged?.Invoke(this, nodeDur);
                    }

                    if (startPosition.HasValue && startPosition.Value > TimeSpan.Zero)
                    {
                        try
                        {
                            _currentInputNode.Seek(startPosition.Value);
                        }
                        catch { }
                    }

                    _currentInputNode.Start();
                    _audioGraph.Start();
                    _isPlaying = true;
                    _smtc.PlaybackStatus = MediaPlaybackStatus.Playing;
                    IsPlayingChanged?.Invoke(this, true);
                    _positionTimer.Start();
                    if (startPosition.HasValue && startPosition.Value > TimeSpan.Zero)
                    {
                        PositionChanged?.Invoke(this, startPosition.Value);
                    }
                    _restoredPosition = TimeSpan.Zero;

                    TriggerUpcomingTracksPreload();
                    return;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AudioGraph Playback Fallback] {ex.Message}");
            }
        }

        if (requestId != _playRequestId) return;

        _mediaPlayer.Source = source;
        if (startPosition.HasValue && startPosition.Value > TimeSpan.Zero)
        {
            try
            {
                _mediaPlayer.PlaybackSession.Position = startPosition.Value;
            }
            catch { }
        }
        _mediaPlayer.Play();
        if (startPosition.HasValue && startPosition.Value > TimeSpan.Zero)
        {
            PositionChanged?.Invoke(this, startPosition.Value);
        }
        _restoredPosition = TimeSpan.Zero;

        TriggerUpcomingTracksPreload();
    }

    private void OnInputNodeMediaCompleted(MediaSourceAudioInputNode sender, object args)
    {
        if (_isCrossfading)
        {
            return;
        }

        if (_repeatMode == RepeatMode.One)
        {
            Seek(TimeSpan.Zero);
            Play();
            return;
        }

        _ = NextAsync();
    }

    private void UpdateSmtcMetadata(Track track)
    {
        try
        {
            _smtc.DisplayUpdater.Type = MediaPlaybackType.Music;
            _smtc.DisplayUpdater.MusicProperties.Title = track.FullTitle;
            _smtc.DisplayUpdater.MusicProperties.Artist = track.ArtistNames;
            _smtc.DisplayUpdater.MusicProperties.AlbumTitle = track.AlbumTitle;

            var coverUrl = track.GetCoverUrl(400);
            if (!string.IsNullOrWhiteSpace(coverUrl))
            {
                _smtc.DisplayUpdater.Thumbnail = RandomAccessStreamReference.CreateFromUri(new Uri(coverUrl));
            }
            else
            {
                _smtc.DisplayUpdater.Thumbnail = null;
            }

            _smtc.DisplayUpdater.Update();
        }
        catch
        {
            // Defensive: SMTC display update can fail if OS component is busy
        }
    }

    private void OnPlaybackStateChanged(MediaPlaybackSession session, object args)
    {
        var state = session.PlaybackState;
        _isPlaying = state == MediaPlaybackState.Playing;

        switch (state)
        {
            case MediaPlaybackState.Playing:
                _smtc.PlaybackStatus = MediaPlaybackStatus.Playing;
                _positionTimer.Start();
                break;
            case MediaPlaybackState.Paused:
                _smtc.PlaybackStatus = MediaPlaybackStatus.Paused;
                _positionTimer.Stop();
                break;
            case MediaPlaybackState.None:
                _smtc.PlaybackStatus = MediaPlaybackStatus.Closed;
                _positionTimer.Stop();
                break;
            default:
                _smtc.PlaybackStatus = MediaPlaybackStatus.Stopped;
                _positionTimer.Stop();
                break;
        }

        IsPlayingChanged?.Invoke(this, _isPlaying);
    }

    private void OnNaturalDurationChanged(MediaPlaybackSession session, object args)
    {
        DurationChanged?.Invoke(this, session.NaturalDuration);
    }

    private void OnMediaEnded(MediaPlayer sender, object args)
    {
        if (_repeatMode == RepeatMode.One)
        {
            Seek(TimeSpan.Zero);
            Play();
            return;
        }

        _ = NextAsync();
    }

    private void OnSmtcButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        switch (args.Button)
        {
            case SystemMediaTransportControlsButton.Play:
                Play();
                break;
            case SystemMediaTransportControlsButton.Pause:
                Pause();
                break;
            case SystemMediaTransportControlsButton.Next:
                _ = NextAsync();
                break;
            case SystemMediaTransportControlsButton.Previous:
                _ = PreviousAsync();
                break;
        }
    }

    private void RebuildPlayOrder()
    {
        int count = _queue.Count;
        _playOrder = Enumerable.Range(0, count).ToList();

        if (_isShuffle && count > 1)
        {
            // Fisher-Yates shuffle
            for (int i = count - 1; i > 0; i--)
            {
                int j = RandomNumberGenerator.GetInt32(i + 1);
                (_playOrder[i], _playOrder[j]) = (_playOrder[j], _playOrder[i]);
            }
        }
    }

    private async Task HandleSloplessAiCheckAsync(Track track, int requestId)
    {
        try
        {
            var evaluation = await _sloplessService.EvaluateTrackAsync(track).ConfigureAwait(false);
            if (!evaluation.IsAi) return;

            if (requestId != _playRequestId || _currentTrack?.Id != track.Id) return;

            string behavior = _settingsService.SloplessBehavior;
            if (behavior == "nothing") return;

            await Task.Delay(350).ConfigureAwait(false);
            if (requestId != _playRequestId || _currentTrack?.Id != track.Id) return;

            bool isLiked = track.IsLiked || _yandexMusicService.IsTrackLiked(track.Id);

            if (behavior == "dislike" || (behavior == "dislike_if_not_liked" && !isLiked))
            {
                try
                {
                    await _yandexMusicService.DislikeTrackAsync(track.Id).ConfigureAwait(false);
                    track.IsDisliked = true;
                }
                catch { }

                TrackSkippedWithReason?.Invoke(this, App.Loc?.CurrentLanguage == "en" ? "AI track skipped & disliked (Slopless)" : "ИИ-трек пропущен и заблокирован (Slopless)");
                await NextAsync().ConfigureAwait(false);
            }
            else if (behavior == "skip" || (behavior == "skip_if_not_liked" && !isLiked))
            {
                TrackSkippedWithReason?.Invoke(this, App.Loc?.CurrentLanguage == "en" ? "AI track skipped (Slopless)" : "ИИ-трек пропущен (Slopless)");
                await NextAsync().ConfigureAwait(false);
            }
            else if (behavior == "like")
            {
                try
                {
                    await _yandexMusicService.LikeTrackAsync(track.Id).ConfigureAwait(false);
                    track.IsLiked = true;
                }
                catch { }
            }
        }
        catch { }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        SaveCurrentState();
        _isDisposed = true;
        CancelActiveCrossfade();
        _positionTimer.Stop();
        _positionTimer.Dispose();

        _currentInputNode?.Dispose();
        _fadingOutNode?.Dispose();
        _deviceOutput?.Dispose();
        _audioGraph?.Dispose();

        _smtc.ButtonPressed -= OnSmtcButtonPressed;
        _mediaPlayer.PlaybackSession.PlaybackStateChanged -= OnPlaybackStateChanged;
        _mediaPlayer.PlaybackSession.NaturalDurationChanged -= OnNaturalDurationChanged;
        _mediaPlayer.MediaEnded -= OnMediaEnded;
        _mediaPlayer.Dispose();
    }
}
