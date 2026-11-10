#nullable enable

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YMusic.WinUI.Helpers;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.ViewModels;

public sealed partial class LikedTracksViewModel : BaseViewModel
{
    private readonly IYandexMusicService _musicService;
    private readonly IAudioPlayerService _audioPlayer;
    private readonly ICredentialStorageService _credentialStorage;
    private readonly ITrackPlaybackCoordinator _playbackCoordinator;

    private readonly List<Track> _allTracks = new();

    public ObservableCollection<Track> FilteredTracks { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSearchFilter))]
    private string _searchFilter = string.Empty;

    public bool HasSearchFilter => !string.IsNullOrWhiteSpace(SearchFilter);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedTotalTracks))]
    private int _totalTrackCount;

    public string FormattedTotalTracks => PluralizationHelper.FormatTrackCount(TotalTrackCount);
    public string PageTitle => App.Loc?.GetString("Nav_Likes") ?? (Loc?.CurrentLanguage == "en" ? "Liked Tracks" : "Любимые треки");

    public string PlayAllText => Loc?.CurrentLanguage == "en" ? "Play All" : "Слушать всё";
    public string ShuffleText => Loc?.CurrentLanguage == "en" ? "Shuffle" : "Перемешать";
    public string RefreshTooltip => Loc?.CurrentLanguage == "en" ? "Refresh tracks" : "Обновить треки";
    public string FilterPlaceholder => Loc?.CurrentLanguage == "en" ? "Filter tracks..." : "Поиск по трекам...";
    public string ErrorTitle => Loc?.CurrentLanguage == "en" ? "Error Loading Tracks" : "Ошибка при загрузке треков";

    protected override void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(FormattedTotalTracks));
        OnPropertyChanged(nameof(PlayAllText));
        OnPropertyChanged(nameof(ShuffleText));
        OnPropertyChanged(nameof(RefreshTooltip));
        OnPropertyChanged(nameof(FilterPlaceholder));
        OnPropertyChanged(nameof(ErrorTitle));
    }

    [ObservableProperty]
    private Track? _currentlyPlayingTrack;

    public bool HasLoaded { get; private set; }

    public LikedTracksViewModel(
        IYandexMusicService musicService,
        IAudioPlayerService audioPlayer,
        ICredentialStorageService credentialStorage,
        ITrackPlaybackCoordinator playbackCoordinator)
    {
        _musicService = musicService;
        _audioPlayer = audioPlayer;
        _credentialStorage = credentialStorage;
        _playbackCoordinator = playbackCoordinator;

        _playbackCoordinator.Register(FilteredTracks);

        _currentlyPlayingTrack = _audioPlayer.CurrentTrack;
        _audioPlayer.CurrentTrackChanged += (_, track) =>
        {
            RunOnUi(() => CurrentlyPlayingTrack = track);
        };

        _musicService.TrackLikeChanged += async (_, e) =>
        {
            if (e.IsLiked)
            {
                Track? trackToAdd = null;
                if (_audioPlayer.CurrentTrack != null && Track.IsSameTrackId(_audioPlayer.CurrentTrack.Id, e.TrackId))
                {
                    trackToAdd = _audioPlayer.CurrentTrack;
                }
                else
                {
                    try
                    {
                        var fetched = await _musicService.GetTracksAsync(new[] { e.TrackId.Split(':')[0] }).ConfigureAwait(false);
                        trackToAdd = fetched.FirstOrDefault();
                    }
                    catch
                    {
                    }
                }

                if (trackToAdd != null)
                {
                    RunOnUi(() =>
                    {
                        var existing = _allTracks.FirstOrDefault(t => Track.IsSameTrackId(t.Id, trackToAdd.Id));
                        if (existing != null)
                        {
                            _allTracks.Remove(existing);
                        }
                        _allTracks.Insert(0, trackToAdd);
                        TotalTrackCount = _allTracks.Count;

                        var filteredExisting = FilteredTracks.FirstOrDefault(t => Track.IsSameTrackId(t.Id, trackToAdd.Id));
                        if (filteredExisting != null)
                        {
                            FilteredTracks.Remove(filteredExisting);
                        }

                        var query = SearchFilter?.Trim();
                        bool matches = string.IsNullOrWhiteSpace(query) ||
                            trackToAdd.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                            trackToAdd.ArtistNames.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                            trackToAdd.AlbumTitle.Contains(query, StringComparison.OrdinalIgnoreCase);

                        if (matches)
                        {
                            FilteredTracks.Insert(0, trackToAdd);
                        }
                    });
                }
            }
            else
            {
                RunOnUi(() =>
                {
                    var existing = _allTracks.FirstOrDefault(t => Track.IsSameTrackId(t.Id, e.TrackId));
                    if (existing != null)
                    {
                        _allTracks.Remove(existing);
                        TotalTrackCount = _allTracks.Count;
                    }

                    var filteredItem = FilteredTracks.FirstOrDefault(t => Track.IsSameTrackId(t.Id, e.TrackId));
                    if (filteredItem != null)
                    {
                        FilteredTracks.Remove(filteredItem);
                    }
                });
            }
        };
    }

    [RelayCommand]
    public async Task LoadTracksAsync()
    {
        string? userId = _credentialStorage.GetUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            var profile = await _musicService.GetAccountStatusAsync();
            if (profile != null && !string.IsNullOrWhiteSpace(profile.UserId))
            {
                userId = profile.UserId;
                _credentialStorage.SaveUserId(userId);
            }
            else
            {
                SetError("User ID not available. Please log in.");
                return;
            }
        }

        SetBusy(true);
        ClearError();

        try
        {
            var likedIds = await _musicService.GetLikedTrackIdsAsync(userId);
            if (likedIds.Count == 0)
            {
                RunOnUi(() =>
                {
                    _allTracks.Clear();
                    FilteredTracks.Clear();
                    TotalTrackCount = 0;
                });
                return;
            }

            var tracks = await _musicService.GetTracksAsync(likedIds);

            RunOnUi(() =>
            {
                _allTracks.Clear();
                _allTracks.AddRange(tracks);
                TotalTrackCount = _allTracks.Count;
                ApplyFilter();
                HasLoaded = true;
            });
        }
        catch (Exception ex)
        {
            SetError($"Failed to load liked tracks: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    partial void OnSearchFilterChanged(string value)
    {
        RunOnUi(ApplyFilter);
    }

    private void ApplyFilter()
    {
        FilteredTracks.Clear();
        var query = SearchFilter?.Trim();

        var filtered = string.IsNullOrWhiteSpace(query)
            ? _allTracks
            : _allTracks.Where(t =>
                t.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.ArtistNames.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.AlbumTitle.Contains(query, StringComparison.OrdinalIgnoreCase));

        foreach (var track in filtered)
        {
            FilteredTracks.Add(track);
        }

        _playbackCoordinator.Sync(FilteredTracks);
    }

    [RelayCommand]
    private async Task PlayTrackAsync(Track? track)
    {
        if (track == null)
        {
            return;
        }

        if (Track.IsSameTrackId(track.Id, _audioPlayer.CurrentTrack?.Id))
        {
            if (_audioPlayer.IsPlaying)
            {
                _audioPlayer.Pause();
            }
            else
            {
                _audioPlayer.Play();
            }
            return;
        }

        _audioPlayer.CurrentPlaybackMode = PlaybackMode.LikedTracks;
        await _audioPlayer.PlayTrackAsync(track, _allTracks);
    }

    [RelayCommand]
    private async Task PlayAllAsync()
    {
        if (_allTracks.Count > 0)
        {
            _audioPlayer.CurrentPlaybackMode = PlaybackMode.LikedTracks;
            await _audioPlayer.PlayQueueAsync(_allTracks, 0);
        }
    }

    [RelayCommand]
    private async Task ShufflePlayAllAsync()
    {
        if (_allTracks.Count > 0)
        {
            _audioPlayer.CurrentPlaybackMode = PlaybackMode.LikedTracks;
            _audioPlayer.IsShuffle = true;
            await _audioPlayer.PlayQueueAsync(_allTracks, 0);
        }
    }

    [RelayCommand]
    private async Task UnlikeTrackAsync(Track? track)
    {
        if (track == null) return;
        await _musicService.UnlikeTrackAsync(track.Id).ConfigureAwait(false);
    }
}
