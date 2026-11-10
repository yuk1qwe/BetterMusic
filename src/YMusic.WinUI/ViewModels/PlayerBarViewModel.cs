using System.IO;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using YMusic.WinUI.Helpers;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Implementations;
using YMusic.WinUI.Services.Interfaces;
using YMusic.WinUI.Views.Controls;

namespace YMusic.WinUI.ViewModels;

public sealed partial class PlayerBarViewModel : BaseViewModel
{
    private readonly IAudioPlayerService _audioPlayer;
    private readonly IYandexMusicService _musicService;
    private readonly IEqualizerService _equalizer;
    private readonly INavigationService _navigationService;
    private bool _isUserSeeking;
    private DateTime _lastRegionWarningTime = DateTime.MinValue;

    public IEqualizerService Equalizer => _equalizer;
    public IReadOnlyList<EqualizerBand> EqualizerBands => _equalizer.Bands;
    public IReadOnlyList<EqualizerPreset> EqualizerPresets => _equalizer.Presets;
    public IReadOnlyList<string> EqualizerPresetNames { get; } = DefaultEqualizerPresets.Presets
        .Select(p => p.Name)
        .ToList();

    [ObservableProperty]
    private bool _isEqualizerEnabled;

    [ObservableProperty]
    private string _currentEqualizerPresetName = "Flat";

    partial void OnCurrentEqualizerPresetNameChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            if (!string.Equals(_equalizer.CurrentPresetName, value, StringComparison.OrdinalIgnoreCase))
            {
                SelectEqualizerPreset(value);
            }
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrackTitle))]
    [NotifyPropertyChangedFor(nameof(ArtistNames))]
    [NotifyPropertyChangedFor(nameof(HasTrack))]
    private Track? _currentTrack;

    public string TrackTitle => !string.IsNullOrWhiteSpace(CurrentTrack?.Title)
        ? CurrentTrack.FullTitle
        : (Loc?.CurrentLanguage == "en" ? "No track playing" : "Трек не выбран");

    public string ArtistNames => !string.IsNullOrWhiteSpace(CurrentTrack?.ArtistNames)
        ? CurrentTrack.ArtistNames
        : (Loc?.CurrentLanguage == "en" ? "Select a track to start" : "Выберите трек для воспроизведения");

    public bool HasTrack => CurrentTrack != null;

    [ObservableProperty]
    private string? _coverUrl;

    [ObservableProperty]
    private string? _hdCoverUrl;

    [ObservableProperty]
    private bool _isNowPlayingDrawerOpen;

    partial void OnIsNowPlayingDrawerOpenChanged(bool value)
    {
        if (value && CurrentTrack != null && Lyrics.Count == 0 && !IsLoadingLyrics)
        {
            _ = LoadLyricsForTrackAsync(CurrentTrack);
        }
    }

    [ObservableProperty]
    private bool _isAutoScrollEnabled = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSyncLyricsButton))]
    private bool _isUserScrolledAway;

    public event EventHandler<LyricLine?>? ActiveLyricChanged;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LikeGlyph))]
    [NotifyPropertyChangedFor(nameof(LikeTooltip))]
    [NotifyPropertyChangedFor(nameof(LikeBrush))]
    private bool _isCurrentTrackLiked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LikeGlyph))]
    [NotifyPropertyChangedFor(nameof(LikeTooltip))]
    [NotifyPropertyChangedFor(nameof(LikeBrush))]
    [NotifyPropertyChangedFor(nameof(DislikeMenuText))]
    [NotifyPropertyChangedFor(nameof(DislikeMenuGlyph))]
    private bool _isCurrentTrackDisliked;

    public string LikeGlyph => IsCurrentTrackDisliked ? "\uE006" : (IsCurrentTrackLiked ? "\uEB52" : "\uEB51");

    public string LikeTooltip
    {
        get
        {
            bool isEn = Loc?.CurrentLanguage == "en";
            if (IsCurrentTrackDisliked)
            {
                return isEn ? "Remove from disliked" : "Убрать из «Не нравится»";
            }
            return IsCurrentTrackLiked 
                ? (isEn ? "Unlike" : "Удалить из избранного") 
                : (isEn ? "Like" : "В избранное");
        }
    }

    public Microsoft.UI.Xaml.Media.Brush LikeBrush
    {
        get
        {
            if (IsCurrentTrackDisliked)
            {
                return new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 85, 85));
            }
            if (IsCurrentTrackLiked)
            {
                return new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 51, 102));
            }
            return (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        }
    }

    public string NowPlayingDrawerHeader => Loc?.CurrentLanguage == "en" ? "NOW PLAYING" : "СЕЙЧАС ИГРАЕТ";
    public string SyncToCurrentLineText => Loc?.CurrentLanguage == "en" ? "Sync to current line" : "К текущей строке";
    public string LoadingLyricsTitle => Loc?.CurrentLanguage == "en" ? "Loading lyrics..." : "Загрузка текста...";
    public string LoadingLyricsSubtitle => Loc?.CurrentLanguage == "en" ? "Looking for synchronized timecodes" : "Ищем синхронизированные таймкоды";
    public string EnjoyTheMusicText => Loc?.CurrentLanguage == "en" ? "Enjoy the music" : "Наслаждайтесь музыкой";

    public string AudioEqualizerTooltip => Loc?.CurrentLanguage == "en" ? "Audio Equalizer" : "Эквалайзер";
    public string EqualizerTitle => Loc?.CurrentLanguage == "en" ? "Equalizer" : "Эквалайзер";
    public string EqualizerResetText => Loc?.CurrentLanguage == "en" ? "Reset" : "Сброс";
    public string EqualizerResetTooltip => Loc?.CurrentLanguage == "en" ? "Reset all bands to 0 dB" : "Сбросить все полосы в 0 дБ";
    public string EqualizerPresetLabel => Loc?.CurrentLanguage == "en" ? "Preset:" : "Пресет:";
    public string ToggleOnContent => Loc?.CurrentLanguage == "en" ? "On" : "Вкл";
    public string ToggleOffContent => Loc?.CurrentLanguage == "en" ? "Off" : "Выкл";
    public string BestBitrateMenuText => Loc?.CurrentLanguage == "en" ? "Best Available Bitrate" : "Максимальный битрейт";

    public string ShuffleTooltip => Loc?.CurrentLanguage == "en" ? "Shuffle" : "Перемешать";
    public string PreviousTooltip => Loc?.CurrentLanguage == "en" ? "Previous" : "Предыдущий трек";
    public string PlayPauseTooltip => Loc?.CurrentLanguage == "en" ? "Play / Pause" : "Воспроизведение / Пауза";
    public string NextTooltip => Loc?.CurrentLanguage == "en" ? "Next" : "Следующий трек";
    public string RepeatTooltip => Loc?.CurrentLanguage == "en" ? "Repeat" : "Повтор";
    public string LyricsDrawerTooltip => Loc?.CurrentLanguage == "en" ? "Lyrics & Now Playing (Esc to close)" : "Текст песни и плеер (Esc для закрытия)";
    public string CollapseDrawerTooltip => Loc?.CurrentLanguage == "en" ? "Collapse Now Playing (Esc)" : "Свернуть плеер (Esc)";
    public string RecenterLyricsTooltip => Loc?.CurrentLanguage == "en" ? "Re-center lyrics on playing line" : "Вернуться к играющей строке";
    public string ViewAlbumTooltip => Loc?.CurrentLanguage == "en" ? "View Album" : "Перейти к альбому";
    public string FullscreenTooltip => Loc?.CurrentLanguage == "en" ? "Toggle Fullscreen" : "Полноэкранный режим";
    public string ArtistInfoTooltip
    {
        get
        {
            bool isEn = Loc?.CurrentLanguage == "en";
            if (CurrentTrack?.Artists?.Count > 1)
            {
                return isEn ? "Artists (click to choose)" : "Исполнители (нажмите для выбора)";
            }
            return isEn ? "About Artist" : "Об исполнителе";
        }
    }
    public string VolumeInputTooltip => Loc?.CurrentLanguage == "en" ? "Click to enter volume percentage" : "Нажмите, чтобы ввести процент громкости";
    public string StartTrackWaveMenuText => Loc?["Wave_ByTrack"] is { } val && !string.IsNullOrEmpty(val) && val != "Wave_ByTrack"
        ? val
        : (Loc?.CurrentLanguage == "en" ? "My Wave by track" : "Моя волна по треку");
    public string PlayNextMenuText => Loc?["TrackMenu_PlayNext"] is { } val && !string.IsNullOrEmpty(val) && val != "TrackMenu_PlayNext"
        ? val
        : (Loc?.CurrentLanguage == "en" ? "Play next" : "Слушать следующим");
    public string AddToQueueMenuText => Loc?["TrackMenu_AddToQueue"] is { } val && !string.IsNullOrEmpty(val) && val != "TrackMenu_AddToQueue"
        ? val
        : (Loc?.CurrentLanguage == "en" ? "Add to end of queue" : "В конец очереди");
    public string DislikeMenuText => IsCurrentTrackDisliked
        ? (Loc?["TrackMenu_Undislike"] is { } uVal && !string.IsNullOrEmpty(uVal) && uVal != "TrackMenu_Undislike"
            ? uVal
            : (Loc?.CurrentLanguage == "en" ? "Remove from disliked" : "Убрать из «Не нравится»"))
        : (Loc?["TrackMenu_Dislike"] is { } val && !string.IsNullOrEmpty(val) && val != "TrackMenu_Dislike"
            ? val
            : (Loc?.CurrentLanguage == "en" ? "I don't like it" : "Не нравится"));
    public string DislikeMenuGlyph => IsCurrentTrackDisliked ? "\uE006" : "\uE1E0";
    public string AddToPlaylistMenuText => Loc?["TrackMenu_AddToPlaylist"] is { } val && !string.IsNullOrEmpty(val) && val != "TrackMenu_AddToPlaylist"
        ? val
        : (Loc?.CurrentLanguage == "en" ? "Add to playlist" : "Добавить в плейлист");
    public string ShareMenuText => Loc?["TrackMenu_Share"] is { } val && !string.IsNullOrEmpty(val) && val != "TrackMenu_Share"
        ? val
        : (Loc?.CurrentLanguage == "en" ? "Share" : "Поделиться");
    public string DownloadFileMenuText => Loc?["TrackMenu_DownloadFile"] is { } val && !string.IsNullOrEmpty(val) && val != "TrackMenu_DownloadFile"
        ? val
        : (Loc?.CurrentLanguage == "en" ? "Download to file" : "Скачать в файл");
    public string TrackDetailsMenuText => Loc?["TrackMenu_Details"] is { } val && !string.IsNullOrEmpty(val) && val != "TrackMenu_Details"
        ? val
        : (Loc?.CurrentLanguage == "en" ? "Track details" : "Информация о треке");
    public string FckCensorRepoMenuText => Loc?.CurrentLanguage == "en" ? "Original FckCensor project (GitHub)" : "Оригинальный проект FckCensor (GitHub)";
    public string FckOptionsTooltip => Loc?.CurrentLanguage == "en" ? "More options" : "Дополнительно";
    public string FckRemoveReplacementText => Loc?.CurrentLanguage == "en" ? "Remove replacement (FckCensor)" : "Удалить замену (FckCensor)";
    public string FckChooseAnotherFileText => Loc?.CurrentLanguage == "en" ? "Choose another audio file..." : "Выбрать другой аудиофайл...";
    public string FckReplaceTrackText => Loc?.CurrentLanguage == "en" ? "Replace track (FckCensor)..." : "Подменить трек (FckCensor)...";
    public string FckReportCensorshipText => Loc?.CurrentLanguage == "en" ? "Report censorship" : "Сообщить о цензуре";
    public string FckBadgeTooltip => Loc?.CurrentLanguage == "en" ? "Track replaced by FckCensor (Uncensored)" : "Трек подменен FckCensor (Uncensored)";

    public System.Collections.ObjectModel.ObservableCollection<LyricLine> Lyrics { get; } = new();

    [ObservableProperty]
    private LyricLine? _activeLyricLine;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoLyricsPlaceholder))]
    private bool _hasLyrics;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSyncLyricsButton))]
    [NotifyPropertyChangedFor(nameof(LyricsBadgeText))]
    [NotifyPropertyChangedFor(nameof(LyricsBadgeGlyph))]
    private bool _hasSyncedLyrics;

    public string LyricsBadgeText => HasSyncedLyrics
        ? (Loc?.CurrentLanguage == "en" ? "Synced Lyrics" : "Синхр. текст")
        : (Loc?.CurrentLanguage == "en" ? "Plain Lyrics" : "Текст");
    public string LyricsBadgeGlyph => HasSyncedLyrics ? "\uEC4F" : "\uE8D2";
    public bool ShowSyncLyricsButton => HasSyncedLyrics && IsUserScrolledAway;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoLyricsPlaceholder))]
    private bool _isLoadingLyrics;

    public bool ShowNoLyricsPlaceholder => !IsLoadingLyrics && !HasLyrics;

    [ObservableProperty]
    private string _lyricsStatusText = "Loading lyrics...";

    [ObservableProperty]
    private StreamAudioQuality? _currentQuality;

    [ObservableProperty]
    private string _qualityBadge = "AAC";

    [ObservableProperty]
    private string _qualityTooltip = "Stream Quality: AAC (High Quality)";

    [ObservableProperty]
    private AudioQualityPreference _selectedQualityPreference = AudioQualityPreference.AacPreferred;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayPauseGlyph))]
    private bool _isPlaying;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedPosition))]
    private TimeSpan _position;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedDuration))]
    private TimeSpan _duration;

    [ObservableProperty]
    private double _positionSeconds;

    [ObservableProperty]
    private double _durationSeconds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeGlyph))]
    [NotifyPropertyChangedFor(nameof(VolumePercentageText))]
    [NotifyPropertyChangedFor(nameof(VolumeTooltip))]
    private double _volume = 80;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeGlyph))]
    [NotifyPropertyChangedFor(nameof(VolumePercentageText))]
    [NotifyPropertyChangedFor(nameof(VolumeTooltip))]
    [NotifyPropertyChangedFor(nameof(MuteTooltip))]
    private bool _isMuted;

    [ObservableProperty]
    private bool _isShuffle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RepeatGlyph))]
    private RepeatMode _repeatMode = RepeatMode.None;

    public string PlayPauseGlyph => IsPlaying ? "\uE769" : "\uE768";

    public string VolumeGlyph => IsMuted || Volume == 0
        ? "\uE74F" // Mute
        : (Volume < 33 ? "\uE993" : (Volume < 66 ? "\uE994" : "\uE767"));

    private bool IsRussian => Loc?.CurrentLanguage != "en";

    public string VolumePercentageText => $"{Math.Round(Volume):0}%";

    public string VolumeTooltip => IsMuted
        ? (IsRussian ? "Без звука" : "Muted")
        : (IsRussian ? $"Громкость: {Math.Round(Volume):0}%" : $"Volume: {Math.Round(Volume):0}%");

    public string MuteTooltip => IsMuted
        ? (IsRussian ? "Включить звук" : "Unmute")
        : (IsRussian ? "Выключить звук" : "Mute");

    public string RepeatGlyph => RepeatMode switch
    {
        RepeatMode.One => "\uE8ED", // Repeat One (solid Segoe Fluent)
        RepeatMode.All => "\uE8EE", // Repeat All (solid Segoe Fluent)
        _ => "\uE8EE"
    };

    public string FormattedPosition => Position.Hours > 0
        ? Position.ToString(@"h\:mm\:ss")
        : Position.ToString(@"m\:ss");

    public string FormattedDuration => Duration.Hours > 0
        ? Duration.ToString(@"h\:mm\:ss")
        : Duration.ToString(@"m\:ss");

    [ObservableProperty]
    private bool _isCurrentTrackReplaced;

    [ObservableProperty]
    private bool _canReportCurrentTrack;

    [ObservableProperty]
    private string? _toastMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToastVisibility))]
    private bool _isToastOpen;

    public Visibility ToastVisibility => IsToastOpen ? Visibility.Visible : Visibility.Collapsed;

    public bool ShowLyricsButton => _settingsService.ShowLyricsButton;
    public bool ShowEqualizerButton => _settingsService.ShowEqualizerButton;
    public bool ShowQualityBadge => _settingsService.ShowQualityBadge;
    public bool CompactPlayerVolume => _settingsService.CompactPlayerVolume;
    public double VolumeSliderWidth => CompactPlayerVolume ? 80 : 160;

    private readonly ISettingsService _settingsService;
    private readonly IFckCensorService _fckCensorService;
    private readonly ILyricsOffsetService _lyricsOffsetService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LyricsOffsetFormattedText))]
    [NotifyPropertyChangedFor(nameof(LyricsOffsetBadgeText))]
    private double _lyricsOffsetSeconds;

    public string LyricsOffsetFormattedText
    {
        get
        {
            if (Math.Abs(LyricsOffsetSeconds) < 0.05)
            {
                return IsRussian ? "0.0 сек (нет)" : "0.0 s (none)";
            }
            string sign = LyricsOffsetSeconds > 0 ? "+" : "";
            return $"{sign}{LyricsOffsetSeconds:F1} {(IsRussian ? "сек" : "s")}";
        }
    }

    public string LyricsOffsetBadgeText
    {
        get
        {
            if (Math.Abs(LyricsOffsetSeconds) < 0.05)
            {
                return IsRussian ? "Тайминг" : "Timing";
            }
            string sign = LyricsOffsetSeconds > 0 ? "+" : "";
            return $"{sign}{LyricsOffsetSeconds:F1}с";
        }
    }

    public string LyricsSyncTitle => IsRussian ? "Синхронизация текста" : "Lyrics Timing & Sync";
    public string LyricsSyncDescription => IsRussian
        ? "Скорректируйте тайминг, если текст спешит (вступление/скит) или отстает. Смещение сохранится для этого трека:"
        : "Adjust timing if lyrics run ahead (intro skit) or lag behind. Offset is saved for this track:";
    public string LyricsSyncCurrentOffsetLabel => IsRussian ? "Текущее смещение:" : "Current offset:";
    public string LyricsSyncResetText => IsRussian ? "Сбросить смещение" : "Reset offset";
    public string LyricsSyncTooltip => IsRussian ? "Настройка синхронизации текста (тайминг)" : "Adjust lyrics sync timing";

    partial void OnLyricsOffsetSecondsChanged(double value)
    {
        if (CurrentTrack != null)
        {
            _lyricsOffsetService.SetOffset(CurrentTrack.Id, CurrentTrack.FullTitle, CurrentTrack.ArtistNames, value);
        }
        foreach (var line in Lyrics)
        {
            line.OffsetSeconds = value;
        }
        UpdateActiveLyric(Position);
    }

    [RelayCommand]
    public void AdjustLyricsOffset(object? parameter)
    {
        if (parameter != null && double.TryParse(parameter.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double delta))
        {
            double newOffset = Math.Clamp(Math.Round(LyricsOffsetSeconds + delta, 1), -60.0, 60.0);
            LyricsOffsetSeconds = newOffset;
        }
    }

    [RelayCommand]
    public void ResetLyricsOffset()
    {
        if (CurrentTrack != null)
        {
            _lyricsOffsetService.ResetOffset(CurrentTrack.Id, CurrentTrack.FullTitle, CurrentTrack.ArtistNames);
            LyricsOffsetSeconds = _lyricsOffsetService.GetOffset(CurrentTrack.Id, CurrentTrack.FullTitle, CurrentTrack.ArtistNames);
        }
        else
        {
            LyricsOffsetSeconds = 0.0;
        }
        UpdateActiveLyric(Position);
    }

    public PlayerBarViewModel(
        IAudioPlayerService audioPlayer,
        IYandexMusicService musicService,
        IEqualizerService equalizer,
        INavigationService navigationService,
        ISettingsService settingsService,
        IFckCensorService fckCensorService,
        ILyricsOffsetService lyricsOffsetService)
    {
        _audioPlayer = audioPlayer;
        _musicService = musicService;
        _equalizer = equalizer;
        _navigationService = navigationService;
        _settingsService = settingsService;
        _fckCensorService = fckCensorService;
        _lyricsOffsetService = lyricsOffsetService;

        _volume = _audioPlayer.Volume;
        _isMuted = _audioPlayer.IsMuted;
        _repeatMode = _audioPlayer.RepeatMode;
        _isShuffle = _audioPlayer.IsShuffle;
        _selectedQualityPreference = _settingsService.PreferredQuality;
        _qualityBadge = _selectedQualityPreference switch
        {
            AudioQualityPreference.AacPreferred => "AAC",
            AudioQualityPreference.Mp3Preferred => "320k",
            _ => "HQ"
        };
        _isEqualizerEnabled = _equalizer.IsEnabled;
        _currentEqualizerPresetName = _equalizer.CurrentPresetName;

        _currentTrack = _audioPlayer.CurrentTrack;
        _isCurrentTrackLiked = _currentTrack != null && _musicService.IsTrackLiked(_currentTrack.Id);
        _coverUrl = _currentTrack?.CoverUrl;
        _hdCoverUrl = _currentTrack?.HdCoverUrl ?? _currentTrack?.CoverUrl;
        _duration = _currentTrack?.Duration ?? TimeSpan.Zero;
        _durationSeconds = _duration.TotalSeconds;
        _position = _audioPlayer.Position;
        _positionSeconds = _position.TotalSeconds;
        _isPlaying = _audioPlayer.IsPlaying;
        if (_currentTrack != null)
        {
            _ = LoadLyricsForTrackAsync(_currentTrack);
        }

        _settingsService.SettingChanged += (s, prop) =>
        {
            if (prop == nameof(ISettingsService.PreferredQuality))
            {
                RunOnUi(() =>
                {
                    SelectedQualityPreference = _settingsService.PreferredQuality;
                    QualityBadge = SelectedQualityPreference switch
                    {
                        AudioQualityPreference.AacPreferred => "AAC",
                        AudioQualityPreference.Mp3Preferred => "320k",
                        _ => "HQ"
                    };
                });
            }
            else if (prop == nameof(ISettingsService.ShowLyricsButton))
            {
                RunOnUi(() => OnPropertyChanged(nameof(ShowLyricsButton)));
            }
            else if (prop == nameof(ISettingsService.ShowEqualizerButton))
            {
                RunOnUi(() => OnPropertyChanged(nameof(ShowEqualizerButton)));
            }
            else if (prop == nameof(ISettingsService.ShowQualityBadge))
            {
                RunOnUi(() => OnPropertyChanged(nameof(ShowQualityBadge)));
            }
            else if (prop == nameof(ISettingsService.CompactPlayerVolume))
            {
                RunOnUi(() =>
                {
                    OnPropertyChanged(nameof(CompactPlayerVolume));
                    OnPropertyChanged(nameof(VolumeSliderWidth));
                });
            }
        };

        _fckCensorService.TrackReplacementChanged += (s, id) =>
        {
            if (CurrentTrack != null && Track.IsSameTrackId(CurrentTrack.Id, id))
            {
                UpdateFckCensorState();
            }
        };
        UpdateFckCensorState();

        _audioPlayer.CurrentTrackChanged += OnCurrentTrackChanged;
        _audioPlayer.CurrentQualityChanged += OnCurrentQualityChanged;
        _audioPlayer.IsPlayingChanged += OnIsPlayingChanged;
        _audioPlayer.PositionChanged += OnPositionChanged;
        _audioPlayer.DurationChanged += OnDurationChanged;
        _audioPlayer.VolumeChanged += OnVolumeChanged;
        _audioPlayer.MuteChanged += OnMuteChanged;
        _audioPlayer.RepeatModeChanged += OnRepeatModeChanged;
        _audioPlayer.IsShuffleChanged += OnIsShuffleChanged;
        _audioPlayer.PlaybackFailed += (s, reason) =>
        {
            RunOnUi(() =>
            {
                var isEn = Loc?.CurrentLanguage == "en";
                ShowToast(isEn
                    ? "Playback paused: tracks unavailable in current region (check VPN)"
                    : "Воспроизведение приостановлено: треки недоступны в регионе (проверьте VPN)");
            });
        };
        _audioPlayer.TrackSkippedWithReason += (s, reason) =>
        {
            RunOnUi(() =>
            {
                ShowToast(reason);
            });
        };
        _musicService.RegionRestrictionDetected += (s, e) =>
        {
            if (DateTime.UtcNow - _lastRegionWarningTime < TimeSpan.FromSeconds(15)) return;
            _lastRegionWarningTime = DateTime.UtcNow;

            RunOnUi(() =>
            {
                var isEn = Loc?.CurrentLanguage == "en";
                ShowToast(isEn
                    ? "Track unavailable: foreign region or VPN detected"
                    : "Трек недоступен: обнаружен зарубежный регион или активен VPN");
            });
        };
        _equalizer.EqualizerChanged += OnEqualizerChanged;

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

        _musicService.TrackDislikeChanged += (_, e) =>
        {
            RunOnUi(() =>
            {
                if (Track.IsSameTrackId(CurrentTrack?.Id, e.TrackId))
                {
                    IsCurrentTrackDisliked = e.IsDisliked;
                }
            });
        };

        _musicService.DislikedTracksLoaded += (_, _) =>
        {
            RunOnUi(() =>
            {
                if (CurrentTrack != null)
                {
                    IsCurrentTrackDisliked = _musicService.IsTrackDisliked(CurrentTrack.Id);
                }
            });
        };
    }

    private void OnEqualizerChanged(object? sender, EventArgs e)
    {
        RunOnUi(() =>
        {
            if (IsEqualizerEnabled != _equalizer.IsEnabled)
            {
                IsEqualizerEnabled = _equalizer.IsEnabled;
            }

            if (!string.Equals(CurrentEqualizerPresetName, _equalizer.CurrentPresetName, StringComparison.OrdinalIgnoreCase))
            {
                CurrentEqualizerPresetName = _equalizer.CurrentPresetName;
            }
        });
    }

    private void OnCurrentQualityChanged(object? sender, StreamAudioQuality? quality)
    {
        RunOnUi(() =>
        {
            CurrentQuality = quality;
            if (quality != null)
            {
                QualityBadge = quality.BadgeText;
                string cacheStatus = quality.IsCached ? " • Local Disk Cache (0 ms)" : string.Empty;
                QualityTooltip = $"Preferred: {GetPreferenceDisplayName(SelectedQualityPreference)} • Active: {quality.DisplayText}{cacheStatus}";
            }
        });
    }

    private static string GetPreferenceDisplayName(AudioQualityPreference pref) => pref switch
    {
        AudioQualityPreference.AacPreferred => "AAC",
        AudioQualityPreference.Mp3Preferred => "MP3",
        _ => "Best Available"
    };

    private void OnCurrentTrackChanged(object? sender, Track? track)
    {
        RunOnUi(() =>
        {
            CurrentTrack = track;
            CoverUrl = track?.CoverUrl;
            HdCoverUrl = track?.HdCoverUrl ?? track?.CoverUrl;
            var initialDur = (track != null && track.Duration > TimeSpan.Zero) ? track.Duration : _audioPlayer.Duration;
            Duration = initialDur > TimeSpan.Zero ? initialDur : TimeSpan.Zero;
            DurationSeconds = Duration.TotalSeconds;
            Position = TimeSpan.Zero;
            PositionSeconds = 0;
            Lyrics.Clear();
            ActiveLyricLine = null;
            HasLyrics = false;
            HasSyncedLyrics = false;
            IsUserScrolledAway = false;
            IsCurrentTrackLiked = track != null && _musicService.IsTrackLiked(track.Id);
            IsCurrentTrackDisliked = track != null && _musicService.IsTrackDisliked(track.Id);
            if (track == null)
            {
                IsNowPlayingDrawerOpen = false;
                IsPlaying = false;
            }
        });

        UpdateFckCensorState();

        if (track != null && IsNowPlayingDrawerOpen)
        {
            _ = LoadLyricsForTrackAsync(track);
        }
    }

    private CancellationTokenSource? _lyricsCts;

    private async Task LoadLyricsForTrackAsync(Track track)
    {
        _lyricsCts?.Cancel();
        _lyricsCts?.Dispose();
        var cts = new CancellationTokenSource();
        _lyricsCts = cts;
        var token = cts.Token;

        try
        {
            // Debounce rapid track switching (150ms) so we don't spam network requests
            await Task.Delay(150, token).ConfigureAwait(false);
            if (token.IsCancellationRequested) return;

            RunOnUi(() =>
            {
                if (token.IsCancellationRequested) return;
                IsLoadingLyrics = true;
                LyricsStatusText = "Loading lyrics...";
            });

            double durationSec = track.DurationMs > 0 ? track.DurationMs / 1000.0 : DurationSeconds;
            var lines = await _musicService.GetTrackLyricsAsync(track.Id, track.FullTitle, track.ArtistNames, durationSec, track.HasLyricsOnYandex, token).ConfigureAwait(false);
            if (token.IsCancellationRequested) return;

            double offset = _lyricsOffsetService.GetOffset(track.Id, track.FullTitle, track.ArtistNames);

            RunOnUi(() =>
            {
                if (token.IsCancellationRequested) return;
                LyricsOffsetSeconds = offset;

                Lyrics.Clear();
                if (lines.Count > 0)
                {
                    foreach (var line in lines)
                    {
                        line.OffsetSeconds = offset;
                        Lyrics.Add(line);
                    }
                    HasLyrics = true;
                    HasSyncedLyrics = lines.Any(l => l.HasTimestamp && l.Timestamp >= TimeSpan.Zero);
                    LyricsStatusText = string.Empty;
                    UpdateActiveLyric(Position);
                }
                else
                {
                    HasLyrics = false;
                    HasSyncedLyrics = false;
                    LyricsStatusText = "No lyrics available for this track";
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Ignored, track changed
        }
        catch
        {
            if (!token.IsCancellationRequested)
            {
                RunOnUi(() =>
                {
                    HasLyrics = false;
                    HasSyncedLyrics = false;
                    LyricsStatusText = "Failed to load lyrics";
                });
            }
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                RunOnUi(() => IsLoadingLyrics = false);
            }
        }
    }

    private void OnIsPlayingChanged(object? sender, bool isPlaying)
    {
        RunOnUi(() => IsPlaying = isPlaying);
    }

    private void OnPositionChanged(object? sender, TimeSpan position)
    {
        if (_isUserSeeking)
        {
            return;
        }

        RunOnUi(() =>
        {
            Position = position;
            PositionSeconds = position.TotalSeconds;
            if (IsNowPlayingDrawerOpen)
            {
                UpdateActiveLyric(position);
            }
        });
    }

    private void UpdateActiveLyric(TimeSpan position)
    {
        if (Lyrics.Count == 0 || !HasSyncedLyrics) return;

        LyricLine? currentActive = null;
        int activeIndex = -1;

        for (int i = 0; i < Lyrics.Count; i++)
        {
            var line = Lyrics[i];
            var nextTimestamp = (i + 1 < Lyrics.Count) ? Lyrics[i + 1].EffectiveTimestamp : TimeSpan.MaxValue;

            if (position >= line.EffectiveTimestamp && position < nextTimestamp)
            {
                currentActive = line;
                activeIndex = i;
                break;
            }
        }

        // Fast path: if the active lyric line hasn't changed, skip heavy collection passes
        if (currentActive == ActiveLyricLine)
        {
            return;
        }

        // Update IsPast for all lines
        for (int i = 0; i < Lyrics.Count; i++)
        {
            var line = Lyrics[i];
            bool isPast = activeIndex >= 0 && i < activeIndex;
            if (line.IsPast != isPast)
            {
                line.IsPast = isPast;
            }
        }

        if (currentActive == null)
        {
            if (ActiveLyricLine != null)
            {
                ActiveLyricLine.IsActive = false;
                ActiveLyricLine = null;
                ActiveLyricChanged?.Invoke(this, null);
            }
        }
        else if (currentActive != ActiveLyricLine)
        {
            if (ActiveLyricLine != null)
            {
                ActiveLyricLine.IsActive = false;
            }
            currentActive.IsActive = true;
            ActiveLyricLine = currentActive;
            ActiveLyricChanged?.Invoke(this, currentActive);
        }
    }

    [RelayCommand]
    private void SeekToLyric(LyricLine line)
    {
        if (line != null && line.HasTimestamp && line.Timestamp >= TimeSpan.Zero)
        {
            IsUserScrolledAway = false;
            var targetPos = line.EffectiveTimestamp;
            _audioPlayer.Seek(targetPos);
            Position = targetPos;
            PositionSeconds = targetPos.TotalSeconds;
            UpdateActiveLyric(targetPos);
        }
    }

    private void OnDurationChanged(object? sender, TimeSpan duration)
    {
        RunOnUi(() =>
        {
            if (duration > TimeSpan.Zero)
            {
                Duration = duration;
                DurationSeconds = duration.TotalSeconds;
                if (CurrentTrack != null && CurrentTrack.DurationMs <= 0)
                {
                    CurrentTrack.DurationMs = (long)duration.TotalMilliseconds;
                }
            }
        });
    }

    private void OnVolumeChanged(object? sender, double volume)
    {
        if (Math.Abs(Volume - volume) > 0.5)
        {
            RunOnUi(() => Volume = volume);
        }
    }

    private void OnMuteChanged(object? sender, bool isMuted)
    {
        RunOnUi(() =>
        {
            IsMuted = isMuted;
            OnPropertyChanged(nameof(VolumePercentageText));
            OnPropertyChanged(nameof(VolumeTooltip));
            OnPropertyChanged(nameof(MuteTooltip));
        });
    }

    private void OnRepeatModeChanged(object? sender, RepeatMode repeatMode)
    {
        RunOnUi(() => RepeatMode = repeatMode);
    }

    private void OnIsShuffleChanged(object? sender, bool isShuffle)
    {
        RunOnUi(() => IsShuffle = isShuffle);
    }

    [RelayCommand]
    private void TogglePlayPause()
    {
        _audioPlayer.TogglePlayPause();
    }

    [RelayCommand]
    private async Task NextAsync()
    {
        await _audioPlayer.NextAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task PreviousAsync()
    {
        await _audioPlayer.PreviousAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private void ToggleShuffle()
    {
        _audioPlayer.IsShuffle = !_audioPlayer.IsShuffle;
    }

    [RelayCommand]
    private void CycleRepeatMode()
    {
        _audioPlayer.RepeatMode = _audioPlayer.RepeatMode switch
        {
            RepeatMode.None => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            _ => RepeatMode.None
        };
    }

    [RelayCommand]
    private void ToggleMute()
    {
        _audioPlayer.IsMuted = !_audioPlayer.IsMuted;
    }

    [RelayCommand]
    private async Task SetAudioQualityPreference(string preferenceString)
    {
        if (Enum.TryParse<AudioQualityPreference>(preferenceString, true, out var preference))
        {
            SelectedQualityPreference = preference;
            _settingsService.PreferredQuality = preference;
            QualityBadge = preference switch
            {
                AudioQualityPreference.AacPreferred => "AAC",
                AudioQualityPreference.Mp3Preferred => "320k",
                _ => "HQ"
            };
            QualityTooltip = preference switch
            {
                AudioQualityPreference.AacPreferred => "Stream Quality: AAC Preferred (High Quality)",
                AudioQualityPreference.Mp3Preferred => "Stream Quality: MP3 Preferred (320 kbps)",
                _ => "Stream Quality: Best Available Bitrate"
            };

            if (CurrentTrack != null)
            {
                await _audioPlayer.ReloadCurrentTrackQualityAsync().ConfigureAwait(false);
            }
        }
    }

    partial void OnVolumeChanged(double value)
    {
        if (Math.Abs(_audioPlayer.Volume - value) > 0.5)
        {
            _audioPlayer.Volume = value;
        }
        OnPropertyChanged(nameof(VolumePercentageText));
        OnPropertyChanged(nameof(VolumeTooltip));
    }

    public void ChangeVolume(double delta)
    {
        if (IsMuted && delta > 0)
        {
            IsMuted = false;
            _audioPlayer.IsMuted = false;
        }
        Volume = Math.Clamp(Math.Round(Volume + delta), 0.0, 100.0);
    }

    public void SetVolumeFromInput(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            OnPropertyChanged(nameof(VolumePercentageText));
            return;
        }

        string cleaned = input.Replace("%", "").Trim();
        if (double.TryParse(cleaned, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed) ||
            double.TryParse(cleaned, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out parsed))
        {
            if (IsMuted && parsed > 0)
            {
                IsMuted = false;
                _audioPlayer.IsMuted = false;
            }
            Volume = Math.Clamp(Math.Round(parsed), 0.0, 100.0);
        }
        OnPropertyChanged(nameof(VolumePercentageText));
    }

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        OnPropertyChanged(nameof(TrackTitle));
        OnPropertyChanged(nameof(ArtistNames));
        OnPropertyChanged(nameof(LikeTooltip));
        OnPropertyChanged(nameof(VolumeTooltip));
        OnPropertyChanged(nameof(MuteTooltip));
        OnPropertyChanged(nameof(NowPlayingDrawerHeader));
        OnPropertyChanged(nameof(SyncToCurrentLineText));
        OnPropertyChanged(nameof(LoadingLyricsTitle));
        OnPropertyChanged(nameof(LoadingLyricsSubtitle));
        OnPropertyChanged(nameof(EnjoyTheMusicText));
        OnPropertyChanged(nameof(AudioEqualizerTooltip));
        OnPropertyChanged(nameof(EqualizerTitle));
        OnPropertyChanged(nameof(EqualizerResetText));
        OnPropertyChanged(nameof(EqualizerResetTooltip));
        OnPropertyChanged(nameof(EqualizerPresetLabel));
        OnPropertyChanged(nameof(ToggleOnContent));
        OnPropertyChanged(nameof(ToggleOffContent));
        OnPropertyChanged(nameof(BestBitrateMenuText));
        OnPropertyChanged(nameof(ShuffleTooltip));
        OnPropertyChanged(nameof(PreviousTooltip));
        OnPropertyChanged(nameof(PlayPauseTooltip));
        OnPropertyChanged(nameof(NextTooltip));
        OnPropertyChanged(nameof(RepeatTooltip));
        OnPropertyChanged(nameof(LyricsDrawerTooltip));
        OnPropertyChanged(nameof(CollapseDrawerTooltip));
        OnPropertyChanged(nameof(RecenterLyricsTooltip));
        OnPropertyChanged(nameof(ViewAlbumTooltip));
        OnPropertyChanged(nameof(FullscreenTooltip));
        OnPropertyChanged(nameof(ArtistInfoTooltip));
        OnPropertyChanged(nameof(VolumeInputTooltip));
        OnPropertyChanged(nameof(StartTrackWaveMenuText));
        OnPropertyChanged(nameof(PlayNextMenuText));
        OnPropertyChanged(nameof(AddToQueueMenuText));
        OnPropertyChanged(nameof(DislikeMenuText));
        OnPropertyChanged(nameof(DislikeMenuGlyph));
        OnPropertyChanged(nameof(LikeGlyph));
        OnPropertyChanged(nameof(LikeBrush));
        OnPropertyChanged(nameof(AddToPlaylistMenuText));
        OnPropertyChanged(nameof(ShareMenuText));
        OnPropertyChanged(nameof(DownloadFileMenuText));
        OnPropertyChanged(nameof(TrackDetailsMenuText));
        OnPropertyChanged(nameof(FckCensorRepoMenuText));
        OnPropertyChanged(nameof(FckOptionsTooltip));
        OnPropertyChanged(nameof(FckRemoveReplacementText));
        OnPropertyChanged(nameof(FckChooseAnotherFileText));
        OnPropertyChanged(nameof(FckReplaceTrackText));
        OnPropertyChanged(nameof(FckReportCensorshipText));
        OnPropertyChanged(nameof(FckBadgeTooltip));
    }

    [RelayCommand]
    private void ToggleEqualizer()
    {
        IsEqualizerEnabled = !IsEqualizerEnabled;
        _equalizer.IsEnabled = IsEqualizerEnabled;
    }

    [RelayCommand]
    private void SelectEqualizerPreset(string presetName)
    {
        _equalizer.ApplyPreset(presetName);
        if (!_equalizer.IsEnabled)
        {
            _equalizer.IsEnabled = true;
            IsEqualizerEnabled = true;
        }
        CurrentEqualizerPresetName = _equalizer.CurrentPresetName;
    }

    [RelayCommand]
    private void ResetEqualizer()
    {
        _equalizer.ResetToFlat();
        CurrentEqualizerPresetName = _equalizer.CurrentPresetName;
    }

    partial void OnIsEqualizerEnabledChanged(bool value)
    {
        if (_equalizer.IsEnabled != value)
        {
            _equalizer.IsEnabled = value;
        }
    }

    private double _lastSoughtSeconds = -1;

    public void StartSeeking()
    {
        _isUserSeeking = true;
        _lastSoughtSeconds = -1;
    }

    public void FinishSeeking(double seconds)
    {
        _isUserSeeking = false;
        if (Math.Abs(_lastSoughtSeconds - seconds) < 0.25)
        {
            return;
        }

        _lastSoughtSeconds = seconds;
        var target = TimeSpan.FromSeconds(seconds);
        _audioPlayer.Seek(target);
        Position = target;
        PositionSeconds = seconds;
    }

    [RelayCommand]
    private async Task ToggleLikeAsync()
    {
        if (CurrentTrack == null) return;

        string trackId = CurrentTrack.Id;
        var isEn = Loc?.CurrentLanguage == "en";

        if (IsCurrentTrackDisliked)
        {
            IsCurrentTrackDisliked = false;
            bool ok = await _musicService.UndislikeTrackAsync(trackId).ConfigureAwait(false);
            if (!ok)
            {
                RunOnUi(() => IsCurrentTrackDisliked = true);
            }
            else
            {
                ShowToast(GetLocString("TrackToast_Undisliked", isEn ? "Removed from disliked" : "Удалено из «Не нравится»"));
            }
            return;
        }

        bool currentlyLiked = IsCurrentTrackLiked;
        IsCurrentTrackLiked = !currentlyLiked;

        try
        {
            if (!currentlyLiked)
            {
                bool ok = await _musicService.LikeTrackAsync(trackId).ConfigureAwait(false);
                if (!ok)
                {
                    RunOnUi(() => IsCurrentTrackLiked = false);
                }
                else
                {
                    ShowToast(GetLocString("TrackToast_Liked", isEn ? "Added to favorites" : "Добавлено в избранное"));
                }
            }
            else
            {
                bool ok = await _musicService.UnlikeTrackAsync(trackId).ConfigureAwait(false);
                if (!ok)
                {
                    RunOnUi(() => IsCurrentTrackLiked = true);
                }
                else
                {
                    ShowToast(GetLocString("TrackToast_Unliked", isEn ? "Removed from favorites" : "Удалено из избранного"));
                }
            }
        }
        catch
        {
            RunOnUi(() => IsCurrentTrackLiked = currentlyLiked);
        }
    }

    [RelayCommand]
    public async Task OpenCurrentAlbumAsync()
    {
        if (CurrentTrack == null) return;
        IsNowPlayingDrawerOpen = false;

        Album? targetAlbum = CurrentTrack.Albums?.FirstOrDefault();
        if (targetAlbum != null && targetAlbum.Id > 0)
        {
            await _navigationService.NavigateToAlbumAsync(targetAlbum);
            return;
        }

        if (!string.IsNullOrEmpty(CurrentTrack.Id) && CurrentTrack.Id.Contains(':'))
        {
            string[] parts = CurrentTrack.Id.Split(':');
            if (parts.Length > 1 && long.TryParse(parts[1], out long albumId) && albumId > 0)
            {
                await _navigationService.NavigateToAlbumByIdAsync(albumId, CurrentTrack.AlbumTitle);
                return;
            }
        }

        if (!string.IsNullOrEmpty(CurrentTrack.AlbumTitle) && targetAlbum != null)
        {
            await _navigationService.NavigateToAlbumAsync(targetAlbum);
        }
    }

    [RelayCommand]
    public async Task OpenCurrentArtistAsync(object? parameter = null)
    {
        if (CurrentTrack == null) return;
        var anchor = parameter as FrameworkElement;
        await ArtistNavigationHelper.NavigateToTrackArtistAsync(anchor, CurrentTrack, () => IsNowPlayingDrawerOpen = false);
    }

    [RelayCommand]
    private void ToggleNowPlayingDrawer()
    {
        IsNowPlayingDrawerOpen = !IsNowPlayingDrawerOpen;
    }

    [RelayCommand]
    private void OpenNowPlayingDrawer()
    {
        IsNowPlayingDrawerOpen = true;
    }

    [RelayCommand]
    private void CloseNowPlayingDrawer()
    {
        IsNowPlayingDrawerOpen = false;
    }

    [RelayCommand]
    private void ResumeAutoScroll()
    {
        IsUserScrolledAway = false;
        if (ActiveLyricLine != null)
        {
            ActiveLyricChanged?.Invoke(this, ActiveLyricLine);
        }
    }

    private void UpdateFckCensorState()
    {
        RunOnUi(() =>
        {
            if (CurrentTrack == null)
            {
                IsCurrentTrackReplaced = false;
                CanReportCurrentTrack = false;
                return;
            }

            IsCurrentTrackReplaced = _fckCensorService.IsReplaced(CurrentTrack.Id);
            CanReportCurrentTrack = !IsCurrentTrackReplaced && !_fckCensorService.IsReported(CurrentTrack.Id);
        });
    }

    [RelayCommand]
    public async Task ReplaceCurrentTrackAsync()
    {
        if (CurrentTrack == null) return;

        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.MusicLibrary
            };
            picker.FileTypeFilter.Add(".mp3");
            picker.FileTypeFilter.Add(".wav");
            picker.FileTypeFilter.Add(".flac");
            picker.FileTypeFilter.Add(".ogg");
            picker.FileTypeFilter.Add(".m4a");
            picker.FileTypeFilter.Add(".aac");

            if (App.MainWindow != null)
            {
                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                bool ok = await _fckCensorService.ReplaceTrackWithFileAsync(CurrentTrack.Id, file.Path);
                if (ok)
                {
                    UpdateFckCensorState();
                    ShowToast("Трек успешно подменён (FckCensor)");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ReplaceCurrentTrackAsync] {ex.Message}");
            ShowToast("Ошибка при выборе аудиофайла");
        }
    }

    [RelayCommand]
    public async Task RemoveCurrentTrackReplacementAsync()
    {
        if (CurrentTrack == null) return;

        bool ok = await _fckCensorService.RemoveReplacementAsync(CurrentTrack.Id);
        if (ok)
        {
            UpdateFckCensorState();
            ShowToast("Трек восстановлен к оригиналу");
        }
    }

    [RelayCommand]
    public async Task ReportCurrentTrackCensorshipAsync()
    {
        if (CurrentTrack == null) return;

        bool ok = await _fckCensorService.ReportCensorshipAsync(CurrentTrack.Id, replaced: false);
        if (ok)
        {
            UpdateFckCensorState();
            ShowToast("Спасибо! Трек скоро будет добавлен в базу.");
        }
    }

    [RelayCommand]
    public async Task OpenFckCensorRepoAsync()
    {
        try
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/Hazzz895/FckCensor"));
        }
        catch { }
    }

    [RelayCommand]
    public async Task StartTrackWaveAsync()
    {
        if (CurrentTrack == null) return;

        try
        {
            var waveVm = App.GetService<WaveViewModel>();
            await waveVm.StartWaveForTrackAsync(CurrentTrack).ConfigureAwait(false);

            var isEn = Loc?.CurrentLanguage == "en";
            ShowToast(isEn
                ? $"My Wave tuned to \"{CurrentTrack.Title}\""
                : $"Моя волна настроена по треку «{CurrentTrack.Title}»");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StartTrackWave Error] {ex.Message}");
        }
    }

    private string GetLocString(string key, string fallback) =>
        Loc?[key] is { } v && !string.IsNullOrEmpty(v) && v != key ? v : fallback;

    [RelayCommand]
    public void PlayNext()
    {
        if (CurrentTrack == null) return;
        _audioPlayer.PlayNext(CurrentTrack);
        var isEn = Loc?.CurrentLanguage == "en";
        ShowToast(GetLocString("TrackToast_PlayNext", isEn ? "Will play next" : "Трек будет воспроизведён следующим"));
    }

    [RelayCommand]
    public void AddToQueue()
    {
        if (CurrentTrack == null) return;
        _audioPlayer.AppendToQueue(new[] { CurrentTrack });
        var isEn = Loc?.CurrentLanguage == "en";
        ShowToast(GetLocString("TrackToast_AddedToQueue", isEn ? "Added to queue" : "Трек добавлен в конец очереди"));
    }

    [RelayCommand]
    public async Task DislikeAndSkipAsync()
    {
        if (CurrentTrack == null) return;
        var track = CurrentTrack;
        var isEn = Loc?.CurrentLanguage == "en";

        if (IsCurrentTrackDisliked)
        {
            IsCurrentTrackDisliked = false;
            bool ok = await _musicService.UndislikeTrackAsync(track.Id).ConfigureAwait(false);
            if (!ok)
            {
                RunOnUi(() => IsCurrentTrackDisliked = true);
            }
            else
            {
                ShowToast(GetLocString("TrackToast_Undisliked", isEn ? "Removed from disliked" : "Удалено из «Не нравится»"));
            }
            return;
        }

        try
        {
            var waveVm = App.GetService<WaveViewModel>();
            _ = _musicService.SendRotorFeedbackAsync(
                "dislike",
                waveVm.SelectedPreset?.StationType ?? "user",
                waveVm.SelectedPreset?.StationTag ?? "onyourwave",
                track.Id);

            IsCurrentTrackDisliked = true;
            IsCurrentTrackLiked = false;
            await _musicService.DislikeTrackAsync(track.Id).ConfigureAwait(false);

            ShowToast(GetLocString("TrackToast_Disliked", isEn ? "Track disliked & skipped" : "Не нравится (трек скрыт и пропущен)"));

            await _audioPlayer.NextAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Dislike Error] {ex.Message}");
        }
    }

    [RelayCommand]
    public void ShareTrack()
    {
        if (CurrentTrack == null) return;

        string url;
        if (CurrentTrack.Albums != null && CurrentTrack.Albums.Count > 0 && CurrentTrack.Albums[0].Id > 0)
        {
            url = $"https://music.yandex.ru/album/{CurrentTrack.Albums[0].Id}/track/{CurrentTrack.Id}";
        }
        else
        {
            url = $"https://music.yandex.ru/track/{CurrentTrack.Id}";
        }

        try
        {
            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(url);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);

            var isEn = Loc?.CurrentLanguage == "en";
            ShowToast(GetLocString("TrackToast_LinkCopied", isEn ? "Track link copied to clipboard" : "Ссылка на трек скопирована"));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Share Error] {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task DownloadTrackFileAsync()
    {
        if (CurrentTrack == null) return;
        var track = CurrentTrack;

        var isEn = Loc?.CurrentLanguage == "en";
        ShowToast(GetLocString("TrackToast_Downloading", isEn ? "Downloading track..." : "Скачивание трека началось..."));

        try
        {
            var (streamUrl, quality) = await _musicService.GetTrackStreamInfoAsync(track.Id).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(streamUrl))
            {
                streamUrl = await _musicService.GetTrackDownloadUrlAsync(track.Id).ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(streamUrl))
            {
                ShowToast(GetLocString("TrackToast_DownloadFailed", isEn ? "Failed to get download URL" : "Не удалось получить ссылку на трек"));
                return;
            }

            string ext = string.Equals(quality?.Codec, "aac", StringComparison.OrdinalIgnoreCase) ? "m4a" : "mp3";
            string rawName = $"{track.ArtistNames} - {track.FullTitle}.{ext}";
            string cleanName = string.Join("_", rawName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
            if (string.IsNullOrWhiteSpace(cleanName)) cleanName = $"Track_{track.Id}.{ext}";

            string downloadsFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string destDir = Path.Combine(downloadsFolder, "Downloads");
            if (!Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }
            string destPath = Path.Combine(destDir, cleanName);

            using var httpClient = YandexBypassProxy.CreateHttpClient(TimeSpan.FromMinutes(2));
            using var response = await httpClient.GetAsync(streamUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using (var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await response.Content.CopyToAsync(fileStream).ConfigureAwait(false);
            }

            string downloadTemplate = GetLocString("TrackToast_Downloaded", isEn ? "Downloaded to Downloads: {0}" : "Сохранено в Загрузки: {0}");
            ShowToast(string.Format(downloadTemplate, cleanName));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DownloadTrackFile Error] {ex.Message}");
            ShowToast(GetLocString("TrackToast_DownloadError", isEn ? "Error downloading track" : "Ошибка при скачивании трека"));
        }
    }

    public async Task<IReadOnlyList<Playlist>> GetUserPlaylistsAsync()
    {
        try
        {
            var status = await _musicService.GetAccountStatusAsync().ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(status?.UserId))
            {
                var playlists = await _musicService.GetUserPlaylistsAsync(status.UserId).ConfigureAwait(false);
                return playlists ?? Array.Empty<Playlist>();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GetUserPlaylists Error] {ex.Message}");
        }
        return Array.Empty<Playlist>();
    }

    public async Task AddCurrentTrackToPlaylistAsync(Playlist playlist)
    {
        if (CurrentTrack == null) return;
        var track = CurrentTrack;

        var isEn = Loc?.CurrentLanguage == "en";
        try
        {
            long ownerUid = playlist.Uid > 0 ? playlist.Uid : (playlist.Owner?.Uid ?? 0);
            string? albumId = track.Albums?.FirstOrDefault()?.Id.ToString();
            bool success = await _musicService.AddTrackToPlaylistAsync(
                ownerUid,
                playlist.Kind,
                track.Id,
                albumId).ConfigureAwait(false);

            if (success)
            {
                string template = GetLocString("TrackToast_AddedToPlaylist", isEn ? "Added to \"{0}\"" : "Добавлено в плейлист «{0}»");
                ShowToast(string.Format(template, playlist.Title));
            }
            else
            {
                ShowToast(GetLocString("TrackToast_AddToPlaylistFailed", isEn ? "Failed to add to playlist" : "Не удалось добавить в плейлист"));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AddToPlaylist Error] {ex.Message}");
            ShowToast(GetLocString("TrackToast_AddToPlaylistError", isEn ? "Error adding to playlist" : "Ошибка при добавлении в плейлист"));
        }
    }

    public async Task PopulatePlaylistSubItemAsync(MenuFlyoutSubItem? subItem)
    {
        if (subItem == null) return;
        subItem.Items.Clear();

        var isEn = Loc?.CurrentLanguage == "en";
        var loadingItem = new MenuFlyoutItem
        {
            Text = GetLocString("TrackMenu_LoadingPlaylists", isEn ? "Loading playlists..." : "Загрузка плейлистов..."),
            IsEnabled = false
        };
        subItem.Items.Add(loadingItem);

        try
        {
            var playlists = await GetUserPlaylistsAsync();
            subItem.Items.Clear();

            if (playlists == null || playlists.Count == 0)
            {
                subItem.Items.Add(new MenuFlyoutItem
                {
                    Text = GetLocString("TrackMenu_NoPlaylists", isEn ? "No playlists found" : "Плейлисты не найдены"),
                    IsEnabled = false
                });
                return;
            }

            foreach (var playlist in playlists)
            {
                var item = new MenuFlyoutItem
                {
                    Text = playlist.Title,
                    Icon = new FontIcon { Glyph = "\uE8F4" }
                };
                item.Click += async (s, args) =>
                {
                    await AddCurrentTrackToPlaylistAsync(playlist);
                };
                subItem.Items.Add(item);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PopulatePlaylistSubItemAsync Error] {ex.Message}");
            subItem.Items.Clear();
            subItem.Items.Add(new MenuFlyoutItem
            {
                Text = GetLocString("TrackMenu_FailedPlaylists", isEn ? "Failed to load playlists" : "Ошибка загрузки плейлистов"),
                IsEnabled = false
            });
        }
    }

    [RelayCommand]
    public async Task ShowTrackDetailsAsync()
    {
        if (CurrentTrack == null) return;
        var track = CurrentTrack;
        var isEn = Loc?.CurrentLanguage == "en";

        if (App.MainWindow?.Content?.XamlRoot == null) return;

        string qualityInfo = CurrentQuality != null
            ? $"{CurrentQuality.Codec?.ToUpperInvariant() ?? "MP3"} • {CurrentQuality.BitrateInKbps} kbps"
            : (SelectedQualityPreference == AudioQualityPreference.AacPreferred ? "AAC (High Quality)" : "MP3 (320 kbps)");

        string albumInfo = track.Albums != null && track.Albums.Count > 0
            ? $"{track.Albums[0].Title}{(track.Albums[0].Year.HasValue ? $" ({track.Albums[0].Year})" : "")}"
            : "—";

        var stack = new StackPanel { Spacing = 10, Margin = new Thickness(0, 10, 0, 0) };

        void AddDetailRow(string label, string value)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var lbl = new TextBlock
            {
                Text = label,
                FontSize = 13,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            };
            var val = new TextBlock
            {
                Text = value,
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(lbl, 0);
            Grid.SetColumn(val, 1);
            row.Children.Add(lbl);
            row.Children.Add(val);
            stack.Children.Add(row);
        }

        AddDetailRow(GetLocString("TrackDetails_TrackTitle", isEn ? "Title:" : "Название:"), track.FullTitle);
        AddDetailRow(GetLocString("TrackDetails_Artist", isEn ? "Artist:" : "Исполнитель:"), track.ArtistNames);
        AddDetailRow(GetLocString("TrackDetails_Album", isEn ? "Album:" : "Альбом:"), albumInfo);
        AddDetailRow(GetLocString("TrackDetails_Duration", isEn ? "Duration:" : "Длительность:"), track.FormattedDuration);
        AddDetailRow(GetLocString("TrackDetails_Quality", isEn ? "Audio Stream:" : "Качество аудио:"), qualityInfo);
        AddDetailRow(GetLocString("TrackDetails_TrackId", isEn ? "Track ID:" : "ID трека:"), track.Id);

        var dialog = new ContentDialog
        {
            XamlRoot = App.MainWindow.Content.XamlRoot,
            Title = GetLocString("TrackDetails_Title", isEn ? "Track Details" : "Информация о треке"),
            Content = stack,
            CloseButtonText = GetLocString("TrackDetails_Close", isEn ? "Close" : "Закрыть"),
            DefaultButton = ContentDialogButton.Close
        };

        try
        {
            await dialog.ShowAsync();
        }
        catch { }
    }

    public void ShowToast(string message)
    {
        RunOnUi(() =>
        {
            ToastMessage = message;
            IsToastOpen = true;
        });

        _ = Task.Delay(3000).ContinueWith(_ =>
        {
            RunOnUi(() => IsToastOpen = false);
        });
    }
}
