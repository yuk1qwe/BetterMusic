#nullable enable

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.ViewModels;

public sealed partial class WaveViewModel : BaseViewModel
{
    private readonly IYandexMusicService _musicService;
    private readonly IAudioPlayerService _audioPlayer;
    private readonly ITrackPlaybackCoordinator _playbackCoordinator;

    private string? _currentBatchId;
    private bool _isWaveActive;
    private bool _isReplenishing;

    public ObservableCollection<WavePreset> Presets { get; } = new();
    public ObservableCollection<Track> UpcomingTracks { get; } = new();

    [ObservableProperty]
    private WavePreset _selectedPreset;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LikeGlyph))]
    [NotifyPropertyChangedFor(nameof(LikeTooltip))]
    private bool _isCurrentTrackLiked;

    public string LikeGlyph => IsCurrentTrackLiked ? "\uEB52" : "\uEB51";

    private string GetLoc(string key, string fallback)
    {
        var val = Loc?[key];
        return (!string.IsNullOrEmpty(val) && val != key) ? val : fallback;
    }

    public string LikeTooltip => IsCurrentTrackLiked ? GetLoc("Common_RemoveFavorite", "Удалить из избранного") : GetLoc("Common_AddFavorite", "В избранное (Обучать волну)");

    public string WaveTitleText => GetLoc("Wave_Title", "Моя волна");
    public string WaveSubtitleText => GetLoc("Wave_Subtitle", "Бесконечный персональный поток музыки");

    public string MoodAndActivityTitle => Loc?.CurrentLanguage == "en" ? "Mood & Activity" : "Настроение и занятия";
    public string UpNextTitle => Loc?.CurrentLanguage == "en" ? "Up Next in My Wave" : "Следующие треки в Моей волне";
    public string PlayPauseTooltip => Loc?.CurrentLanguage == "en" ? "Play / Pause" : "Воспроизведение / Пауза";
    public string NextTrackTooltip => Loc?.CurrentLanguage == "en" ? "Next Track" : "Следующий трек";
    public string DislikeTooltip => Loc?.CurrentLanguage == "en" ? "Dislike & Skip" : "Не нравится (пропустить)";

    [ObservableProperty]
    private Track? _currentTrack;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayPauseGlyph))]
    private bool _isPlaying;

    public bool IsWavePlaying => _isWaveActive && IsPlaying && _audioPlayer.CurrentPlaybackMode == PlaybackMode.Wave;

    public string PlayPauseGlyph => IsWavePlaying ? "\uE769" : "\uE768";

    [ObservableProperty]
    private bool _isWaveActiveState;

    [ObservableProperty]
    private string _statusText = (App.Loc?.CurrentLanguage == "en")
        ? "Press Play to start your infinite personalized stream"
        : "Нажмите Play для запуска бесконечного персонального потока";

    public WaveViewModel(
        IYandexMusicService musicService,
        IAudioPlayerService audioPlayer,
        ITrackPlaybackCoordinator playbackCoordinator)
    {
        _musicService = musicService;
        _audioPlayer = audioPlayer;
        _playbackCoordinator = playbackCoordinator;

        _playbackCoordinator.Register(UpcomingTracks);

        InitializePresets();
        _selectedPreset = Presets[0];

        _currentTrack = _audioPlayer.CurrentTrack;
        _isCurrentTrackLiked = _currentTrack != null && _musicService.IsTrackLiked(_currentTrack.Id);
        _isPlaying = _audioPlayer.IsPlaying;

        _audioPlayer.CurrentTrackChanged += OnAudioPlayerCurrentTrackChanged;
        _audioPlayer.IsPlayingChanged += (_, playing) => RunOnUi(() =>
        {
            IsPlaying = playing;
            OnPropertyChanged(nameof(IsWavePlaying));
            OnPropertyChanged(nameof(PlayPauseGlyph));
        });

        _audioPlayer.PlaybackModeChanged += (_, mode) =>
        {
            if (mode != PlaybackMode.Wave)
            {
                DeactivateWave();
            }
        };

        _musicService.TrackLikeChanged += (_, e) =>
        {
            RunOnUi(() =>
            {
                if (Track.IsSameTrackId(CurrentTrack?.Id, e.TrackId))
                {
                    IsCurrentTrackLiked = e.IsLiked;
                }
            });
        };

        _musicService.LikedTracksLoaded += (_, _) =>
        {
            RunOnUi(() =>
            {
                if (CurrentTrack != null)
                {
                    IsCurrentTrackLiked = _musicService.IsTrackLiked(CurrentTrack.Id);
                }
            });
        };
    }

    public void DeactivateWave()
    {
        _isWaveActive = false;
        RunOnUi(() =>
        {
            IsWaveActiveState = false;
            UpcomingTracks.Clear();
            StatusText = Loc?.CurrentLanguage == "en"
                ? "Press Play to start your infinite personalized stream"
                : "Нажмите Play для запуска бесконечного персонального потока";
            OnPropertyChanged(nameof(IsWavePlaying));
            OnPropertyChanged(nameof(PlayPauseGlyph));
        });
    }

    public void SyncPlayerState()
    {
        CurrentTrack = _audioPlayer.CurrentTrack;
        IsPlaying = _audioPlayer.IsPlaying;
        IsCurrentTrackLiked = CurrentTrack != null && _musicService.IsTrackLiked(CurrentTrack.Id);

        if (_audioPlayer.CurrentPlaybackMode != PlaybackMode.Wave)
        {
            _isWaveActive = false;
            IsWaveActiveState = false;
            UpcomingTracks.Clear();
            StatusText = Loc?.CurrentLanguage == "en"
                ? "Press Play to start your infinite personalized stream"
                : "Нажмите Play для запуска бесконечного персонального потока";
        }
        else
        {
            _isWaveActive = true;
            IsWaveActiveState = true;
            UpcomingTracks.Clear();
            int curIndex = _audioPlayer.CurrentIndex;
            if (curIndex >= 0 && curIndex < _audioPlayer.Queue.Count)
            {
                foreach (var t in _audioPlayer.Queue.Skip(curIndex + 1))
                {
                    UpcomingTracks.Add(t);
                }
            }
            StatusText = Loc?.CurrentLanguage == "en"
                ? $"\"{SelectedPreset.Title}\" wave active"
                : $"Волна «{SelectedPreset.Title}» активна";
        }

        OnPropertyChanged(nameof(IsWavePlaying));
        OnPropertyChanged(nameof(PlayPauseGlyph));
    }

    private void InitializePresets()
    {
        Presets.Add(new WavePreset
        {
            Title = "My Wave",
            Description = "Personal stream tailored to your music taste",
            StationType = "user",
            StationTag = "onyourwave",
            Glyph = "\uE93E",
            GradientStart = "#FF0844",
            GradientEnd = "#FFB199"
        });

        Presets.Add(new WavePreset
        {
            Title = "Energetic",
            Description = "Upbeat drive and high energy rhythms",
            StationType = "mood",
            StationTag = "energetic",
            Glyph = "\uE735",
            GradientStart = "#F857A6",
            GradientEnd = "#FF5858"
        });

        Presets.Add(new WavePreset
        {
            Title = "Calm",
            Description = "Relaxing chill, soft ambient and melodic vibes",
            StationType = "mood",
            StationTag = "calm",
            Glyph = "\uE945",
            GradientStart = "#00C9FF",
            GradientEnd = "#92FE9D"
        });

        Presets.Add(new WavePreset
        {
            Title = "Focus",
            Description = "Deep focus, concentration and flow without lyrics",
            StationType = "activity",
            StationTag = "work",
            Glyph = "\uE82D",
            GradientStart = "#654EA3",
            GradientEnd = "#EAAFC8"
        });

        Presets.Add(new WavePreset
        {
            Title = "Rock",
            Description = "Driving guitar riffs, alt and rock classics",
            StationType = "genre",
            StationTag = "rock",
            Glyph = "\uE8D6",
            GradientStart = "#232526",
            GradientEnd = "#414345"
        });

        Presets.Add(new WavePreset
        {
            Title = "Electronic",
            Description = "Synthesizers, punchy bass and electronic vibes",
            StationType = "genre",
            StationTag = "electronic",
            Glyph = "\uE8EC",
            GradientStart = "#00B4DB",
            GradientEnd = "#0083B0"
        });

        Presets.Add(new WavePreset
        {
            Title = "Hip-Hop",
            Description = "Groovy beats, punchy flow and top hip-hop",
            StationType = "genre",
            StationTag = "hiphop",
            Glyph = "\uE720",
            GradientStart = "#F12711",
            GradientEnd = "#F5AF19"
        });

        Presets.Add(new WavePreset
        {
            Title = "Indie",
            Description = "Atmospheric indie-pop, indie-rock and fresh releases",
            StationType = "genre",
            StationTag = "indie",
            Glyph = "\uE790",
            GradientStart = "#7F00FF",
            GradientEnd = "#E100FF"
        });

        RefreshPresetNames();
    }

    private void RefreshPresetNames()
    {
        bool isEn = Loc?.CurrentLanguage == "en";
        foreach (var p in Presets)
        {
            switch (p.StationTag)
            {
                case "onyourwave":
                    p.Title = isEn ? "My Wave" : "Моя волна";
                    p.Description = isEn ? "Personal stream tailored to your music taste" : "Персональный поток по вашему музыкальному вкусу";
                    break;
                case "energetic":
                    p.Title = isEn ? "Energetic" : "Бодрое";
                    p.Description = isEn ? "Upbeat drive and high energy rhythms" : "Энергичный бит и танцевальный ритм";
                    break;
                case "calm":
                    p.Title = isEn ? "Calm" : "Спокойное";
                    p.Description = isEn ? "Relaxing chill, soft ambient and melodic vibes" : "Расслабляющий чилл, мягкий эмбиент и уют";
                    break;
                case "work":
                    p.Title = isEn ? "Focus" : "Работа";
                    p.Description = isEn ? "Deep focus, concentration and flow without lyrics" : "Концентрация, фокус и продуктивность";
                    break;
                case "rock":
                    p.Title = isEn ? "Rock" : "Рок";
                    p.Description = isEn ? "Driving guitar riffs, alt and rock classics" : "Драйвовые риффы, альт-рок и классика";
                    break;
                case "electronic":
                    p.Title = isEn ? "Electronic" : "Электроника";
                    p.Description = isEn ? "Synthesizers, punchy bass and electronic vibes" : "Синтезаторы, плотный бас и электронный вайб";
                    break;
                case "hiphop":
                    p.Title = isEn ? "Hip-Hop" : "Хип-хоп";
                    p.Description = isEn ? "Groovy beats, punchy flow and top hip-hop" : "Качающий бит, флоу и топ рэп";
                    break;
                case "indie":
                    p.Title = isEn ? "Indie" : "Инди";
                    p.Description = isEn ? "Atmospheric indie-pop, indie-rock and fresh releases" : "Атмосферный инди-поп, инди-рок и открытия";
                    break;
            }
        }
        OnPropertyChanged(nameof(SelectedPreset));
    }

    protected override void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(WaveTitleText));
        OnPropertyChanged(nameof(WaveSubtitleText));
        OnPropertyChanged(nameof(MoodAndActivityTitle));
        OnPropertyChanged(nameof(UpNextTitle));
        OnPropertyChanged(nameof(PlayPauseTooltip));
        OnPropertyChanged(nameof(NextTrackTooltip));
        OnPropertyChanged(nameof(DislikeTooltip));
        OnPropertyChanged(nameof(LikeTooltip));
        RefreshPresetNames();
        if (!_isWaveActive)
        {
            StatusText = Loc?.CurrentLanguage == "en"
                ? "Press Play to start your infinite personalized stream"
                : "Нажмите Play для запуска бесконечного персонального потока";
        }
        else
        {
            StatusText = Loc?.CurrentLanguage == "en"
                ? $"\"{SelectedPreset.Title}\" wave active"
                : $"Волна «{SelectedPreset.Title}» активна";
        }
    }

    [RelayCommand]
    public async Task StartWaveAsync(WavePreset? preset = null)
    {
        var targetPreset = preset ?? SelectedPreset;
        SelectedPreset = targetPreset;

        SetBusy(true);
        ClearError();
        StatusText = Loc?.CurrentLanguage == "en"
            ? $"Tuning tracks for \"{targetPreset.Title}\"..."
            : $"Подбираем треки для «{targetPreset.Title}»...";

        try
        {
            var stationResult = await _musicService.GetRotorStationTracksAsync(
                targetPreset.StationType,
                targetPreset.StationTag).ConfigureAwait(false);

            if (stationResult?.Sequence == null || stationResult.Sequence.Count == 0)
            {
                SetError("Could not retrieve tracks from station. Please try again.");
                return;
            }

            _currentBatchId = stationResult.BatchId;
            _isWaveActive = true;
            _audioPlayer.CurrentPlaybackMode = PlaybackMode.Wave;

            var tracks = stationResult.Sequence
                .Where(item => item.Track != null)
                .Select(item => item.Track!)
                .ToList();

            if (tracks.Count == 0)
            {
                SetError("No playable tracks received in station response.");
                return;
            }

            RunOnUi(() =>
            {
                UpcomingTracks.Clear();
                foreach (var t in tracks.Skip(1))
                {
                    UpcomingTracks.Add(t);
                }
                IsWaveActiveState = true;
                StatusText = Loc?.CurrentLanguage == "en"
                    ? $"\"{targetPreset.Title}\" wave active"
                    : $"Волна «{targetPreset.Title}» активна";
                OnPropertyChanged(nameof(IsWavePlaying));
                OnPropertyChanged(nameof(PlayPauseGlyph));
            });

            await _audioPlayer.PlayQueueAsync(tracks, 0).ConfigureAwait(false);

            // Send radioStarted and trackStarted telemetry
            var firstTrack = tracks[0];
            _ = _musicService.SendRotorFeedbackAsync(
                "radioStarted",
                targetPreset.StationType,
                targetPreset.StationTag,
                firstTrack.Id,
                _currentBatchId);

            _ = _musicService.SendRotorFeedbackAsync(
                "trackStarted",
                targetPreset.StationType,
                targetPreset.StationTag,
                firstTrack.Id,
                _currentBatchId);
        }
        catch (Exception ex)
        {
            SetError($"Error starting My Wave: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    [RelayCommand]
    public async Task StartWaveForTrackAsync(Track? track = null)
    {
        track ??= _audioPlayer.CurrentTrack;
        if (track == null) return;

        var isEn = Loc?.CurrentLanguage == "en";
        var trackTitle = !string.IsNullOrWhiteSpace(track.Title) ? track.Title : "Трек";
        var trackPreset = new WavePreset
        {
            Title = isEn ? $"Wave by \"{trackTitle}\"" : $"Волна по треку «{trackTitle}»",
            Description = track.ArtistNames ?? (isEn ? "Personal stream" : "Персональный поток"),
            StationType = "track",
            StationTag = track.Id,
            Glyph = "\uE93E",
            GradientStart = "#FF512F",
            GradientEnd = "#DD2476"
        };

        SelectedPreset = trackPreset;

        SetBusy(true);
        ClearError();
        StatusText = isEn
            ? $"Tuning tracks for \"{trackTitle}\"..."
            : $"Подбираем треки для «{trackTitle}»...";

        try
        {
            var stationResult = await _musicService.GetRotorStationTracksAsync(
                "track",
                track.Id).ConfigureAwait(false);

            if (stationResult?.Sequence == null || stationResult.Sequence.Count == 0)
            {
                SetError(isEn ? "Could not retrieve tracks for this wave." : "Не удалось получить треки для волны.");
                return;
            }

            _currentBatchId = stationResult.BatchId;
            _isWaveActive = true;
            _audioPlayer.CurrentPlaybackMode = PlaybackMode.Wave;

            var tracks = stationResult.Sequence
                .Where(item => item.Track != null)
                .Select(item => item.Track!)
                .ToList();

            if (tracks.Count == 0)
            {
                SetError(isEn ? "No playable tracks received in station response." : "В ответе станции нет доступных треков.");
                return;
            }

            if (_audioPlayer.CurrentTrack != null && Track.IsSameTrackId(_audioPlayer.CurrentTrack.Id, track.Id))
            {
                // Current track is already playing; keep it playing seamlessly and replace the upcoming queue
                var upcoming = tracks.Where(t => !Track.IsSameTrackId(t.Id, track.Id)).ToList();
                _audioPlayer.ReplaceUpcomingQueue(upcoming);

                RunOnUi(() =>
                {
                    UpcomingTracks.Clear();
                    foreach (var t in upcoming)
                    {
                        UpcomingTracks.Add(t);
                    }
                    IsWaveActiveState = true;
                    StatusText = isEn
                        ? $"\"{trackPreset.Title}\" active"
                        : $"«{trackPreset.Title}» активна";
                    OnPropertyChanged(nameof(IsWavePlaying));
                    OnPropertyChanged(nameof(PlayPauseGlyph));
                });

                if (!_audioPlayer.IsPlaying)
                {
                    _audioPlayer.Play();
                }
            }
            else
            {
                // Different track or player was empty: start playback from the first track in sequence
                RunOnUi(() =>
                {
                    UpcomingTracks.Clear();
                    foreach (var t in tracks.Skip(1))
                    {
                        UpcomingTracks.Add(t);
                    }
                    IsWaveActiveState = true;
                    StatusText = isEn
                        ? $"\"{trackPreset.Title}\" active"
                        : $"«{trackPreset.Title}» активна";
                    OnPropertyChanged(nameof(IsWavePlaying));
                    OnPropertyChanged(nameof(PlayPauseGlyph));
                });

                await _audioPlayer.PlayQueueAsync(tracks, 0).ConfigureAwait(false);
            }

            // Send radioStarted and trackStarted telemetry
            var playingTrack = _audioPlayer.CurrentTrack ?? track;
            _ = _musicService.SendRotorFeedbackAsync(
                "radioStarted",
                "track",
                track.Id,
                playingTrack.Id,
                _currentBatchId);

            _ = _musicService.SendRotorFeedbackAsync(
                "trackStarted",
                "track",
                track.Id,
                playingTrack.Id,
                _currentBatchId);
        }
        catch (Exception ex)
        {
            SetError($"Error starting track wave: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    [RelayCommand]
    public void TogglePlayPause()
    {
        if (!_isWaveActive || _audioPlayer.CurrentPlaybackMode != PlaybackMode.Wave)
        {
            _ = StartWaveAsync();
            return;
        }

        _audioPlayer.TogglePlayPause();
    }

    [RelayCommand]
    public async Task NextWaveTrackAsync()
    {
        if (!_isWaveActive || _audioPlayer.CurrentPlaybackMode != PlaybackMode.Wave)
        {
            await _audioPlayer.NextAsync().ConfigureAwait(false);
            return;
        }

        if (_audioPlayer.CurrentTrack != null)
        {
            _ = _musicService.SendRotorFeedbackAsync(
                "skip",
                SelectedPreset.StationType,
                SelectedPreset.StationTag,
                _audioPlayer.CurrentTrack.Id,
                _currentBatchId);
        }

        await _audioPlayer.NextAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    public async Task ToggleLikeAsync()
    {
        if (_audioPlayer.CurrentTrack == null)
        {
            return;
        }

        string trackId = _audioPlayer.CurrentTrack.Id;
        bool currentlyLiked = IsCurrentTrackLiked;
        IsCurrentTrackLiked = !currentlyLiked;

        try
        {
            if (!currentlyLiked)
            {
                if (_isWaveActive && _audioPlayer.CurrentPlaybackMode == PlaybackMode.Wave)
                {
                    _ = _musicService.SendRotorFeedbackAsync(
                        "like",
                        SelectedPreset.StationType,
                        SelectedPreset.StationTag,
                        trackId,
                        _currentBatchId);
                }

                bool ok = await _musicService.LikeTrackAsync(trackId).ConfigureAwait(false);
                if (!ok)
                {
                    RunOnUi(() => IsCurrentTrackLiked = false);
                }
            }
            else
            {
                bool ok = await _musicService.UnlikeTrackAsync(trackId).ConfigureAwait(false);
                if (!ok)
                {
                    RunOnUi(() => IsCurrentTrackLiked = true);
                }
            }
        }
        catch
        {
            RunOnUi(() => IsCurrentTrackLiked = currentlyLiked);
        }
    }

    [RelayCommand]
    public async Task PlayUpcomingTrackAsync(Track? track)
    {
        if (track == null) return;

        int index = UpcomingTracks.IndexOf(track);
        if (index < 0) return;

        // Tracks starting from the clicked track
        var tracksToPlay = UpcomingTracks.Skip(index).ToList();

        // Send skip feedback for the track currently playing
        if (_audioPlayer.CurrentTrack != null && _isWaveActive && _audioPlayer.CurrentPlaybackMode == PlaybackMode.Wave)
        {
            _ = _musicService.SendRotorFeedbackAsync(
                "skip",
                SelectedPreset.StationType,
                SelectedPreset.StationTag,
                _audioPlayer.CurrentTrack.Id,
                _currentBatchId);
        }

        // Remove tracks up to and including the clicked one from UpcomingTracks
        RunOnUi(() =>
        {
            for (int i = 0; i <= index; i++)
            {
                UpcomingTracks.RemoveAt(0);
            }
        });

        _isWaveActive = true;
        _audioPlayer.CurrentPlaybackMode = PlaybackMode.Wave;
        RunOnUi(() =>
        {
            IsWaveActiveState = true;
            OnPropertyChanged(nameof(IsWavePlaying));
            OnPropertyChanged(nameof(PlayPauseGlyph));
        });

        // Play the remaining queue starting with the clicked track
        await _audioPlayer.PlayQueueAsync(tracksToPlay, 0).ConfigureAwait(false);

        // Send trackStarted feedback
        _ = _musicService.SendRotorFeedbackAsync(
            "trackStarted",
            SelectedPreset.StationType,
            SelectedPreset.StationTag,
            track.Id,
            _currentBatchId);

        // Check if queue needs replenishment
        CheckReplenishWave(track.Id);
    }

    [RelayCommand]
    public async Task SendDislikeAndSkipAsync()
    {
        if (!_isWaveActive || _audioPlayer.CurrentPlaybackMode != PlaybackMode.Wave)
        {
            return;
        }

        if (_audioPlayer.CurrentTrack == null)
        {
            return;
        }

        var track = _audioPlayer.CurrentTrack;
        _ = _musicService.SendRotorFeedbackAsync(
            "dislike",
            SelectedPreset.StationType,
            SelectedPreset.StationTag,
            track.Id,
            _currentBatchId);

        _ = _musicService.DislikeTrackAsync(track.Id);

        await _audioPlayer.NextAsync().ConfigureAwait(false);
    }

    private void OnAudioPlayerCurrentTrackChanged(object? sender, Track? track)
    {
        if (_audioPlayer.CurrentPlaybackMode != PlaybackMode.Wave)
        {
            if (_isWaveActive)
            {
                DeactivateWave();
            }
            RunOnUi(() =>
            {
                CurrentTrack = track;
                IsCurrentTrackLiked = track != null && _musicService.IsTrackLiked(track.Id);
                OnPropertyChanged(nameof(IsWavePlaying));
                OnPropertyChanged(nameof(PlayPauseGlyph));
            });
            return;
        }

        RunOnUi(() =>
        {
            CurrentTrack = track;
            IsCurrentTrackLiked = track != null && _musicService.IsTrackLiked(track.Id);
            OnPropertyChanged(nameof(IsWavePlaying));
            OnPropertyChanged(nameof(PlayPauseGlyph));

            if (_isWaveActive && track != null)
            {
                // Remove passed track and all preceding ones from UpcomingTracks
                var existing = UpcomingTracks.FirstOrDefault(t => t.Id == track.Id);
                if (existing != null)
                {
                    int idx = UpcomingTracks.IndexOf(existing);
                    for (int i = 0; i <= idx; i++)
                    {
                        UpcomingTracks.RemoveAt(0);
                    }
                }
            }
        });

        if (!_isWaveActive || track == null)
        {
            return;
        }

        // Send trackStarted feedback
        _ = _musicService.SendRotorFeedbackAsync(
            "trackStarted",
            SelectedPreset.StationType,
            SelectedPreset.StationTag,
            track.Id,
            _currentBatchId);

        // Check if we need to replenish the queue
        CheckReplenishWave(track.Id);
    }

    private void CheckReplenishWave(string currentTrackId)
    {
        if (_isReplenishing)
        {
            return;
        }

        int currentIndex = _audioPlayer.CurrentIndex;
        int queueCount = _audioPlayer.Queue.Count;

        // If 3 or fewer tracks remain in the queue, load the next batch
        if (queueCount - currentIndex <= 3)
        {
            _isReplenishing = true;
            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await _musicService.GetRotorStationTracksAsync(
                        SelectedPreset.StationType,
                        SelectedPreset.StationTag,
                        currentTrackId).ConfigureAwait(false);

                    if (result?.Sequence != null && result.Sequence.Count > 0)
                    {
                        var newTracks = result.Sequence
                            .Where(i => i.Track != null)
                            .Select(i => i.Track!)
                            .ToList();

                        _audioPlayer.AppendToQueue(newTracks);

                        RunOnUi(() =>
                        {
                            foreach (var nt in newTracks)
                            {
                                UpcomingTracks.Add(nt);
                            }
                        });
                    }
                }
                catch
                {
                    // Non-fatal, will retry on next track change
                }
                finally
                {
                    _isReplenishing = false;
                }
            });
        }
    }
}
