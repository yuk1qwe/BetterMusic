#nullable enable

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YMusic.WinUI.Helpers;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.ViewModels;

public sealed partial class ExploreViewModel : BaseViewModel
{
    private readonly IYandexMusicService _musicService;
    private readonly IAudioPlayerService _audioPlayer;
    private readonly ITrackPlaybackCoordinator _playbackCoordinator;
    private readonly ICredentialStorageService _credentialStorage;
    private readonly IPinnedItemsService _pinnedItemsService;
    private readonly ISettingsService _settingsService;

    public ObservableCollection<Playlist> PersonalPlaylists { get; } = new();
    public ObservableCollection<Playlist> EditorialPlaylists { get; } = new();
    public ObservableCollection<Album> NewReleases { get; } = new();
    public ObservableCollection<Track> ChartTracks { get; } = new();
    public ObservableCollection<Track> DetailTracks { get; } = new();
    private readonly List<Track> _allDetailTracks = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetailSearchFilter))]
    private string _detailSearchFilter = string.Empty;

    public bool HasDetailSearchFilter => !string.IsNullOrWhiteSpace(DetailSearchFilter);
    public bool HasTracksToFilter => _allDetailTracks.Count > 0;
    public string FilterPlaceholder => Loc?.CurrentLanguage == "en" ? "Search in playlist..." : "Поиск по трекам...";
    public string ColumnHeaderTitle => Loc?.CurrentLanguage == "en" ? "TITLE" : "НАЗВАНИЕ";
    public string ColumnHeaderAlbum => Loc?.CurrentLanguage == "en" ? "ALBUM" : "АЛЬБОМ";

    partial void OnDetailSearchFilterChanged(string value)
    {
        ApplyDetailFilter();
    }

    private void ApplyDetailFilter()
    {
        string query = DetailSearchFilter?.Trim() ?? string.Empty;
        DetailTracks.Clear();

        if (string.IsNullOrWhiteSpace(query))
        {
            foreach (var t in _allDetailTracks)
            {
                DetailTracks.Add(t);
            }
        }
        else
        {
            var matched = _allDetailTracks.Where(t =>
                (t.Title != null && t.Title.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                (t.ArtistNames != null && t.ArtistNames.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                (t.AlbumTitle != null && t.AlbumTitle.Contains(query, StringComparison.OrdinalIgnoreCase)));

            foreach (var t in matched)
            {
                DetailTracks.Add(t);
            }
        }

        _playbackCoordinator.Sync(DetailTracks);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBrowseVisible))]
    private bool _isDetailOpen;

    public bool IsBrowseVisible => !IsDetailOpen;

    private string GetLoc(string key, string fallback)
    {
        var val = Loc?[key];
        return (!string.IsNullOrEmpty(val) && val != key) ? val : fallback;
    }

    public string HeaderTitle => GetLoc("Nav_Explore", "Главное");
    public string HeaderSubtitle => GetLoc("Explore_Subtitle", Loc?.CurrentLanguage == "en" ? "Personal mixes, curated playlists, fresh albums & charts from Yandex" : "Персональные миксы, плейлисты редакции, свежие релизы и чарты Яндекса");
    public string PersonalPlaylistsTitle => GetLoc("Explore_PickedForYou", Loc?.CurrentLanguage == "en" ? "Made For You" : "Собрано для вас");
    public string NewReleasesTitle => GetLoc("Explore_NewReleases", Loc?.CurrentLanguage == "en" ? "New Releases & Albums" : "Новые релизы и альбомы");
    public string EditorialPlaylistsTitle => GetLoc("Explore_PopularPlaylists", Loc?.CurrentLanguage == "en" ? "Popular Playlists" : "Популярные плейлисты");
    public string ChartTitle => GetLoc("Explore_TopChart", Loc?.CurrentLanguage == "en" ? "Top Chart" : "Чарт");
    public string ErrorTitle => Loc?.CurrentLanguage == "en" ? "Explore Error" : "Ошибка";

    protected override void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(HeaderSubtitle));
        OnPropertyChanged(nameof(PersonalPlaylistsTitle));
        OnPropertyChanged(nameof(NewReleasesTitle));
        OnPropertyChanged(nameof(EditorialPlaylistsTitle));
        OnPropertyChanged(nameof(ChartTitle));
        OnPropertyChanged(nameof(ErrorTitle));
        OnPropertyChanged(nameof(BackToExploreText));
        OnPropertyChanged(nameof(PlayAllText));
        OnPropertyChanged(nameof(ShuffleText));
        OnPropertyChanged(nameof(FilterPlaceholder));
        OnPropertyChanged(nameof(ColumnHeaderTitle));
        OnPropertyChanged(nameof(ColumnHeaderAlbum));
        if (ActivePlaylist != null)
        {
            DetailTypeBadge = Loc?.CurrentLanguage == "en"
                ? (ActivePlaylist.Owner?.Login?.Contains("daily") == true ? "DAILY MIX" : "PLAYLIST")
                : (ActivePlaylist.Owner?.Login?.Contains("daily") == true ? "МОЙ МИКС" : "ПЛЕЙЛИСТ");
        }
        else if (ActiveAlbum != null)
        {
            DetailTypeBadge = Loc?.CurrentLanguage == "en" ? "ALBUM" : "АЛЬБОМ";
        }
    }

    [ObservableProperty]
    private string _detailTitle = string.Empty;

    [ObservableProperty]
    private string _detailSubtitle = string.Empty;

    [ObservableProperty]
    private string _detailDescription = string.Empty;

    [ObservableProperty]
    private string? _detailCoverUrl;

    [ObservableProperty]
    private string _detailMeta = string.Empty;

    [ObservableProperty]
    private string _detailTypeBadge = "PLAYLIST";

    [ObservableProperty]
    private Playlist? _activePlaylist;

    [ObservableProperty]
    private Album? _activeAlbum;

    [ObservableProperty]
    private bool _isLoadingDetail;

    [ObservableProperty]
    private bool _isDetailPinned;

    [ObservableProperty]
    private bool _isDetailLiked;

    public string BackToExploreText => Loc?.CurrentLanguage == "en" ? "Back to Explore" : "Назад на Главную";
    public string PlayAllText => Loc?.CurrentLanguage == "en" ? "Play All" : "Слушать всё";
    public string ShuffleText => Loc?.CurrentLanguage == "en" ? "Shuffle" : "Перемешать";

    public bool HasLoaded { get; private set; }

    public bool ShowPersonalPlaylists => _settingsService.ShowPersonalPlaylistsOnHome;
    public bool ShowNewReleases => _settingsService.ShowNewReleasesOnHome;
    public bool ShowEditorialPlaylists => _settingsService.ShowEditorialPlaylistsOnHome;
    public bool ShowChart => _settingsService.ShowChartOnHome;

    public ExploreViewModel(
        IYandexMusicService musicService,
        IAudioPlayerService audioPlayer,
        ITrackPlaybackCoordinator playbackCoordinator,
        ICredentialStorageService credentialStorage,
        IPinnedItemsService pinnedItemsService,
        ISettingsService settingsService)
    {
        _musicService = musicService;
        _audioPlayer = audioPlayer;
        _playbackCoordinator = playbackCoordinator;
        _credentialStorage = credentialStorage;
        _pinnedItemsService = pinnedItemsService;
        _settingsService = settingsService;

        _settingsService.SettingChanged += OnSettingChanged;
        _pinnedItemsService.PinStateChanged += OnPinStateChanged;
        _musicService.AlbumLikeChanged += OnAlbumLikeChanged;
        _musicService.PlaylistLikeChanged += OnPlaylistLikeChanged;
        _playbackCoordinator.Register(DetailTracks);
        _playbackCoordinator.Register(ChartTracks);
    }

    private void OnSettingChanged(object? sender, string propName)
    {
        if (propName is nameof(ISettingsService.ShowPersonalPlaylistsOnHome) or
                       nameof(ISettingsService.ShowNewReleasesOnHome) or
                       nameof(ISettingsService.ShowEditorialPlaylistsOnHome) or
                       nameof(ISettingsService.ShowChartOnHome))
        {
            OnPropertyChanged(nameof(ShowPersonalPlaylists));
            OnPropertyChanged(nameof(ShowNewReleases));
            OnPropertyChanged(nameof(ShowEditorialPlaylists));
            OnPropertyChanged(nameof(ShowChart));
        }
    }

    [RelayCommand]
    public async Task LoadExploreDataAsync()
    {
        SetBusy(true);
        ClearError();

        try
        {
            if (string.IsNullOrWhiteSpace(_musicService.CurrentToken))
            {
                var token = _credentialStorage.GetToken();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    _musicService.SetOAuthToken(token);
                }
            }

            var personalTask = _musicService.GetPersonalPlaylistsAsync();
            var editorialTask = _musicService.GetEditorialPlaylistsAsync();
            var albumsTask = _musicService.GetNewReleasesAsync();
            var chartTask = _musicService.GetChartTracksAsync();
            var ensurePlaylistsTask = _musicService.EnsureLikedPlaylistsLoadedAsync();
            var ensureAlbumsTask = _musicService.EnsureLikedAlbumsLoadedAsync();

            await Task.WhenAll(personalTask, editorialTask, albumsTask, chartTask, ensurePlaylistsTask, ensureAlbumsTask).ConfigureAwait(false);

            var personal = await personalTask.ConfigureAwait(false);
            var editorial = await editorialTask.ConfigureAwait(false);
            var albums = await albumsTask.ConfigureAwait(false);
            var chart = await chartTask.ConfigureAwait(false);

            RunOnUi(() =>
            {
                PersonalPlaylists.Clear();
                foreach (var p in personal)
                {
                    p.IsLiked = _musicService.IsPlaylistLiked(p.Uid, p.Kind);
                    PersonalPlaylists.Add(p);
                }

                EditorialPlaylists.Clear();
                foreach (var p in editorial)
                {
                    p.IsLiked = _musicService.IsPlaylistLiked(p.Uid, p.Kind);
                    EditorialPlaylists.Add(p);
                }

                NewReleases.Clear();
                foreach (var a in albums)
                {
                    a.IsLiked = _musicService.IsAlbumLiked(a.Id);
                    NewReleases.Add(a);
                }

                ChartTracks.Clear();
                foreach (var t in chart)
                {
                    ChartTracks.Add(t);
                }

                _playbackCoordinator.Sync(ChartTracks);
                HasLoaded = true;
                System.Diagnostics.Debug.WriteLine($"[Explore] Personal: {PersonalPlaylists.Count}, Editorial: {EditorialPlaylists.Count}, Albums: {NewReleases.Count}, Chart: {ChartTracks.Count}");
            });
        }
        catch (Exception ex)
        {
            SetError($"Failed to load Explore content: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[Explore Error] {ex}");
        }

        finally
        {
            SetBusy(false);
        }
    }

    [RelayCommand]
    public async Task OpenPlaylistAsync(Playlist? playlist)
    {
        if (playlist == null) return;
        if (IsDetailOpen && ActivePlaylist != null && ActivePlaylist.Uid == playlist.Uid && ActivePlaylist.Kind == playlist.Kind) return;

        ActivePlaylist = playlist;
        ActiveAlbum = null;
        DetailTitle = playlist.Title;
        DetailSubtitle = playlist.OwnerName;
        DetailDescription = playlist.Description ?? string.Empty;
        DetailCoverUrl = playlist.CoverUrl;
        DetailTypeBadge = Loc?.CurrentLanguage == "en"
            ? (playlist.Owner?.Login?.Contains("daily") == true ? "DAILY MIX" : "PLAYLIST")
            : (playlist.Owner?.Login?.Contains("daily") == true ? "МОЙ МИКС" : "ПЛЕЙЛИСТ");
        DetailMeta = playlist.FormattedTrackCount;
        IsDetailPinned = _pinnedItemsService.IsPlaylistPinned(playlist.Uid, playlist.Kind);
        playlist.IsPinned = IsDetailPinned;
        playlist.IsLiked = _musicService.IsPlaylistLiked(playlist.Uid, playlist.Kind);
        IsDetailLiked = playlist.IsLiked;
        IsDetailOpen = true;
        IsLoadingDetail = true;

        _allDetailTracks.Clear();
        DetailTracks.Clear();
        DetailSearchFilter = string.Empty;

        var existingTracks = playlist.ResolvedTracks ?? playlist.Tracks?.Select(ti => ti.Track).OfType<Track>().ToList();
        if (existingTracks != null && existingTracks.Count > 0)
        {
            _allDetailTracks.AddRange(existingTracks);
            ApplyDetailFilter();
        }

        try
        {
            await _musicService.EnsureLikedTracksLoadedAsync().ConfigureAwait(false);
            await _musicService.EnsureLikedPlaylistsLoadedAsync().ConfigureAwait(false);
            var fullPlaylist = await _musicService.GetPlaylistWithTracksAsync(playlist.Uid, playlist.Kind).ConfigureAwait(false);
            if (fullPlaylist != null)
            {
                fullPlaylist.IsPinned = _pinnedItemsService.IsPlaylistPinned(fullPlaylist.Uid, fullPlaylist.Kind);
                fullPlaylist.IsLiked = _musicService.IsPlaylistLiked(fullPlaylist.Uid, fullPlaylist.Kind);
            }
            var tracks = fullPlaylist?.ResolvedTracks ?? new List<Track>();

            if (tracks.Count == 0 && fullPlaylist?.Tracks != null && fullPlaylist.Tracks.Count > 0)
            {
                var trackIds = fullPlaylist.Tracks.Select(t => t.Id).Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
                if (trackIds.Count > 0)
                {
                    var fetched = await _musicService.GetTracksAsync(trackIds).ConfigureAwait(false);
                    tracks = fetched.ToList();
                }
            }

            RunOnUi(() =>
            {
                if (ActivePlaylist?.Uid != playlist.Uid || ActivePlaylist?.Kind != playlist.Kind) return;

                if (fullPlaylist != null)
                {
                    ActivePlaylist = fullPlaylist;
                    IsDetailLiked = fullPlaylist.IsLiked;
                }
                _allDetailTracks.Clear();
                _allDetailTracks.AddRange(tracks);

                long totalMs = 0;
                foreach (var t in tracks)
                {
                    totalMs += t.DurationMs;
                }

                var totalSpan = TimeSpan.FromMilliseconds(totalMs);
                DetailMeta = $"{PluralizationHelper.FormatTrackCount(_allDetailTracks.Count)} • {PluralizationHelper.FormatDuration(totalSpan)}";

                DetailSearchFilter = string.Empty;
                ApplyDetailFilter();
                OnPropertyChanged(nameof(HasTracksToFilter));
            });
        }
        catch (Exception ex)
        {
            SetError($"Failed to load playlist: {ex.Message}");
        }
        finally
        {
            RunOnUi(() => IsLoadingDetail = false);
        }
    }

    [RelayCommand]
    public async Task OpenAlbumAsync(Album? album)
    {
        if (album == null) return;
        if (IsDetailOpen && ActiveAlbum != null && ActiveAlbum.Id == album.Id) return;

        ActiveAlbum = album;
        ActivePlaylist = null;
        DetailTitle = album.Title;
        DetailSubtitle = album.ArtistNames;
        DetailDescription = !string.IsNullOrWhiteSpace(album.Description) ? album.Description : (!string.IsNullOrWhiteSpace(album.Genre) ? $"Genre: {album.Genre}" : string.Empty);
        DetailCoverUrl = album.CoverUrl;
        DetailTypeBadge = Loc?.CurrentLanguage == "en" ? "ALBUM" : "АЛЬБОМ";
        DetailMeta = album.FormattedYearAndTracks;
        IsDetailPinned = _pinnedItemsService.IsAlbumPinned(album.Id);
        album.IsPinned = IsDetailPinned;
        album.IsLiked = _musicService.IsAlbumLiked(album.Id);
        IsDetailLiked = album.IsLiked;
        IsDetailOpen = true;
        IsLoadingDetail = true;

        _allDetailTracks.Clear();
        DetailTracks.Clear();
        DetailSearchFilter = string.Empty;

        if (album.AllTracks != null && album.AllTracks.Count > 0)
        {
            _allDetailTracks.AddRange(album.AllTracks);
            ApplyDetailFilter();
        }

        try
        {
            await _musicService.EnsureLikedTracksLoadedAsync().ConfigureAwait(false);
            await _musicService.EnsureLikedAlbumsLoadedAsync().ConfigureAwait(false);
            var fullAlbum = await _musicService.GetAlbumWithTracksAsync(album.Id).ConfigureAwait(false);
            if (fullAlbum != null)
            {
                fullAlbum.IsPinned = _pinnedItemsService.IsAlbumPinned(fullAlbum.Id);
                fullAlbum.IsLiked = _musicService.IsAlbumLiked(fullAlbum.Id);
            }
            var tracks = fullAlbum?.AllTracks ?? new List<Track>();

            RunOnUi(() =>
            {
                if (ActiveAlbum?.Id != album.Id) return;

                if (fullAlbum != null)
                {
                    ActiveAlbum = fullAlbum;
                    IsDetailLiked = fullAlbum.IsLiked;
                }
                _allDetailTracks.Clear();
                _allDetailTracks.AddRange(tracks);

                long totalMs = 0;
                foreach (var t in tracks)
                {
                    totalMs += t.DurationMs;
                }

                var totalSpan = TimeSpan.FromMilliseconds(totalMs);
                string yearStr = album.Year.HasValue ? $"{album.Year.Value} • " : string.Empty;
                DetailMeta = $"{yearStr}{PluralizationHelper.FormatTrackCount(_allDetailTracks.Count)} • {PluralizationHelper.FormatDuration(totalSpan)}";

                DetailSearchFilter = string.Empty;
                ApplyDetailFilter();
                OnPropertyChanged(nameof(HasTracksToFilter));
            });
        }
        catch (Exception ex)
        {
            SetError($"Failed to load album: {ex.Message}");
        }
        finally
        {
            RunOnUi(() => IsLoadingDetail = false);
        }
    }

    [RelayCommand]
    public void CloseDetail()
    {
        IsDetailOpen = false;
        ActivePlaylist = null;
        ActiveAlbum = null;
        DetailTracks.Clear();
        _allDetailTracks.Clear();
        DetailSearchFilter = string.Empty;
        OnPropertyChanged(nameof(HasTracksToFilter));
    }

    [RelayCommand]
    public async Task PlayPlaylistDirectAsync(Playlist? playlist)
    {
        if (playlist == null) return;

        try
        {
            var full = await _musicService.GetPlaylistWithTracksAsync(playlist.Uid, playlist.Kind).ConfigureAwait(false);
            var tracks = full?.ResolvedTracks ?? new List<Track>();
            if (tracks.Count == 0 && full?.Tracks != null && full.Tracks.Count > 0)
            {
                var trackIds = full.Tracks.Select(t => t.Id).Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
                if (trackIds.Count > 0)
                {
                    var fetched = await _musicService.GetTracksAsync(trackIds).ConfigureAwait(false);
                    tracks = fetched.ToList();
                }
            }

            if (tracks.Count > 0)
            {
                _audioPlayer.CurrentPlaybackMode = PlaybackMode.Playlist;
                await _audioPlayer.PlayQueueAsync(tracks, 0).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            SetError($"Failed to play playlist: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task PlayAlbumDirectAsync(Album? album)
    {
        if (album == null) return;

        try
        {
            var full = await _musicService.GetAlbumWithTracksAsync(album.Id).ConfigureAwait(false);
            var tracks = full?.AllTracks ?? new List<Track>();
            if (tracks.Count > 0)
            {
                _audioPlayer.CurrentPlaybackMode = PlaybackMode.Album;
                await _audioPlayer.PlayQueueAsync(tracks, 0).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            SetError($"Failed to play album: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task PlayDetailAllAsync()
    {
        if (DetailTracks.Count > 0)
        {
            _audioPlayer.CurrentPlaybackMode = ActiveAlbum != null ? PlaybackMode.Album : PlaybackMode.Playlist;
            await _audioPlayer.PlayQueueAsync(DetailTracks.ToList(), 0).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    public async Task ShufflePlayDetailAsync()
    {
        if (DetailTracks.Count > 0)
        {
            _audioPlayer.CurrentPlaybackMode = ActiveAlbum != null ? PlaybackMode.Album : PlaybackMode.Playlist;
            _audioPlayer.IsShuffle = true;
            await _audioPlayer.PlayQueueAsync(DetailTracks.ToList(), 0).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    public async Task PlayTrackAsync(Track? track)
    {
        if (track == null) return;

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

        var sourceList = DetailTracks.Contains(track) ? DetailTracks.ToList() : (ChartTracks.Contains(track) ? ChartTracks.ToList() : new List<Track> { track });
        _audioPlayer.CurrentPlaybackMode = ActiveAlbum != null ? PlaybackMode.Album : PlaybackMode.Playlist;
        await _audioPlayer.PlayTrackAsync(track, sourceList).ConfigureAwait(false);
    }

    [RelayCommand]
    public async Task ToggleLikeTrackAsync(Track? track)
    {
        if (track == null) return;

        bool targetLiked = !(track.IsLiked || _musicService.IsTrackLiked(track.Id));
        track.IsLiked = targetLiked;

        if (targetLiked)
        {
            await _musicService.LikeTrackAsync(track.Id).ConfigureAwait(false);
        }
        else
        {
            await _musicService.UnlikeTrackAsync(track.Id).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    public void ToggleDetailPin()
    {
        if (ActivePlaylist != null)
        {
            IsDetailPinned = _pinnedItemsService.TogglePlaylistPin(ActivePlaylist.Uid, ActivePlaylist.Kind, ActivePlaylist.Title, ActivePlaylist.CoverUrl);
            ActivePlaylist.IsPinned = IsDetailPinned;
        }
        else if (ActiveAlbum != null)
        {
            IsDetailPinned = _pinnedItemsService.ToggleAlbumPin(ActiveAlbum.Id, ActiveAlbum.Title, ActiveAlbum.CoverUrl);
            ActiveAlbum.IsPinned = IsDetailPinned;
        }
    }

    [RelayCommand]
    public async Task TogglePlaylistLikeAsync(Playlist? playlist)
    {
        if (playlist == null) return;
        bool willLike = !playlist.IsLiked;
        playlist.IsLiked = willLike;
        if (ActivePlaylist != null && ActivePlaylist.Uid == playlist.Uid && ActivePlaylist.Kind == playlist.Kind)
        {
            IsDetailLiked = willLike;
        }

        try
        {
            if (willLike)
            {
                await _musicService.LikePlaylistAsync(playlist.Uid, playlist.Kind).ConfigureAwait(false);
            }
            else
            {
                await _musicService.UnlikePlaylistAsync(playlist.Uid, playlist.Kind).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Explore TogglePlaylistLike Error] {ex.Message}");
            playlist.IsLiked = !willLike;
            if (ActivePlaylist != null && ActivePlaylist.Uid == playlist.Uid && ActivePlaylist.Kind == playlist.Kind)
            {
                IsDetailLiked = !willLike;
            }
        }
    }

    [RelayCommand]
    public async Task ToggleAlbumLikeAsync(Album? album)
    {
        if (album == null) return;
        bool willLike = !album.IsLiked;
        album.IsLiked = willLike;
        if (ActiveAlbum?.Id == album.Id)
        {
            IsDetailLiked = willLike;
        }

        try
        {
            if (willLike)
            {
                await _musicService.LikeAlbumAsync(album.Id).ConfigureAwait(false);
            }
            else
            {
                await _musicService.UnlikeAlbumAsync(album.Id).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Explore ToggleAlbumLike Error] {ex.Message}");
            album.IsLiked = !willLike;
            if (ActiveAlbum?.Id == album.Id)
            {
                IsDetailLiked = !willLike;
            }
        }
    }

    [RelayCommand]
    public async Task ToggleDetailLikeAsync()
    {
        if (ActivePlaylist != null)
        {
            await TogglePlaylistLikeAsync(ActivePlaylist).ConfigureAwait(false);
        }
        else if (ActiveAlbum != null)
        {
            await ToggleAlbumLikeAsync(ActiveAlbum).ConfigureAwait(false);
        }
    }

    private void OnAlbumLikeChanged(object? sender, (long AlbumId, bool IsLiked) e)
    {
        RunOnUi(() =>
        {
            if (ActiveAlbum?.Id == e.AlbumId)
            {
                ActiveAlbum.IsLiked = e.IsLiked;
                IsDetailLiked = e.IsLiked;
            }
            var inNew = NewReleases.FirstOrDefault(a => a.Id == e.AlbumId);
            if (inNew != null) inNew.IsLiked = e.IsLiked;
        });
    }

    private void OnPlaylistLikeChanged(object? sender, (long OwnerUid, int Kind, bool IsLiked) e)
    {
        RunOnUi(() =>
        {
            if (ActivePlaylist != null && ActivePlaylist.Uid == e.OwnerUid && ActivePlaylist.Kind == e.Kind)
            {
                ActivePlaylist.IsLiked = e.IsLiked;
                IsDetailLiked = e.IsLiked;
            }
            var inPers = PersonalPlaylists.FirstOrDefault(p => p.Uid == e.OwnerUid && p.Kind == e.Kind);
            if (inPers != null) inPers.IsLiked = e.IsLiked;
            var inEdit = EditorialPlaylists.FirstOrDefault(p => p.Uid == e.OwnerUid && p.Kind == e.Kind);
            if (inEdit != null) inEdit.IsLiked = e.IsLiked;
        });
    }

    private void OnPinStateChanged(object? sender, string key)
    {
        RunOnUi(() =>
        {
            if (ActivePlaylist != null && string.Equals(ActivePlaylist.PinKey, key, StringComparison.OrdinalIgnoreCase))
            {
                IsDetailPinned = _pinnedItemsService.IsPinned(key);
                ActivePlaylist.IsPinned = IsDetailPinned;
            }
            else if (ActiveAlbum != null && string.Equals(ActiveAlbum.PinKey, key, StringComparison.OrdinalIgnoreCase))
            {
                IsDetailPinned = _pinnedItemsService.IsPinned(key);
                ActiveAlbum.IsPinned = IsDetailPinned;
            }
        });
    }
}
