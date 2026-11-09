#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.ViewModels;

public sealed partial class ArtistViewModel : BaseViewModel
{
    private readonly IYandexMusicService _musicService;
    private readonly IAudioPlayerService _audioPlayer;
    private readonly INavigationService _navigationService;
    private readonly ITrackPlaybackCoordinator _playbackCoordinator;

    public ObservableCollection<Track> PopularTracks { get; } = new();
    public ObservableCollection<Album> AllAlbums { get; } = new();
    public ObservableCollection<Album> AlsoAlbums { get; } = new();
    public ObservableCollection<Album> DisplayAlbums { get; } = new();

    [ObservableProperty]
    private Artist? _artist;

    [ObservableProperty]
    private long _artistId;

    [ObservableProperty]
    private string _artistName = string.Empty;

    [ObservableProperty]
    private string? _avatarUrl;

    [ObservableProperty]
    private string _formattedGenres = string.Empty;

    [ObservableProperty]
    private string _formattedListeners = string.Empty;

    [ObservableProperty]
    private string _descriptionText = string.Empty;

    [ObservableProperty]
    private bool _hasDescription;

    [ObservableProperty]
    private bool _isLiked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackButtonText))]
    private string? _previousPageTag;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAllReleasesFilter))]
    [NotifyPropertyChangedFor(nameof(IsAlbumsFilter))]
    [NotifyPropertyChangedFor(nameof(IsSinglesFilter))]
    [NotifyPropertyChangedFor(nameof(IsCompilationsFilter))]
    private int _selectedAlbumFilterIndex; // 0 = All, 1 = Albums, 2 = Singles & EPs, 3 = Compilations

    public bool IsAllReleasesFilter => SelectedAlbumFilterIndex == 0;
    public bool IsAlbumsFilter => SelectedAlbumFilterIndex == 1;
    public bool IsSinglesFilter => SelectedAlbumFilterIndex == 2;
    public bool IsCompilationsFilter => SelectedAlbumFilterIndex == 3;

    [ObservableProperty]
    private int _tracksCount;

    [ObservableProperty]
    private int _albumsCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool _hasPopularTracks;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool _hasAlbums;

    [ObservableProperty]
    private bool _hasAlsoAlbums;

    [ObservableProperty]
    private bool _hasDisplayAlbums;

    private long? _lastMonthListeners;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortOrderText))]
    [NotifyPropertyChangedFor(nameof(SortOrderGlyph))]
    private bool _isSortDescending = true; // Default: true (newest first)

    public string BadgeText => Loc?.CurrentLanguage == "en" ? "ARTIST" : "ИСПОЛНИТЕЛЬ";
    public string PlayPopularTracksText => Loc?.CurrentLanguage == "en" ? "Play" : "Слушать";
    public string LikeArtistTooltip => IsLiked
        ? (Loc?.CurrentLanguage == "en" ? "Remove from favorite artists" : "Удалить из любимых исполнителей")
        : (Loc?.CurrentLanguage == "en" ? "Add to favorite artists" : "Добавить в любимые исполнители");
    public string PopularTracksTitle => Loc?.CurrentLanguage == "en" ? "Popular Tracks" : "Популярные треки";
    public string DiscographyTitle => Loc?.CurrentLanguage == "en" ? "Discography" : "Дискография";
    public string AllReleasesFilterText => Loc?.CurrentLanguage == "en" ? "All Releases" : "Все релизы";
    public string AlbumsFilterText => Loc?.CurrentLanguage == "en" ? "Albums" : "Альбомы";
    public string SinglesFilterText => Loc?.CurrentLanguage == "en" ? "Singles & EPs" : "Синглы и EP";
    public string CompilationsFilterText => Loc?.CurrentLanguage == "en" ? "Compilations" : "Сборники";
    public string SortOrderText => IsSortDescending
        ? (Loc?.CurrentLanguage == "en" ? "Newest first" : "Сначала новые")
        : (Loc?.CurrentLanguage == "en" ? "Oldest first" : "Сначала старые");
    public string SortOrderTooltip => Loc?.CurrentLanguage == "en" ? "Change sort order (by year)" : "Изменить порядок сортировки (по году)";
    public string EmptyCategoryText => Loc?.CurrentLanguage == "en" ? "No releases in this category yet" : "В этой категории пока нет релизов";
    public string AboutArtistTitle => Loc?.CurrentLanguage == "en" ? "About the Artist" : "Об исполнителе";
    public string EmptyStateText => Loc?.CurrentLanguage == "en" ? "This artist has no available tracks or albums yet" : "У этого исполнителя пока нет доступных треков или альбомов";
    public string FckCensorTooltip => Loc?.CurrentLanguage == "en" ? "Track replaced by FckCensor (Uncensored)" : "Трек подменен FckCensor (Uncensored)";

    public string SortOrderGlyph => IsSortDescending ? "\uE74B" : "\uE74A";

    public bool ShowEmptyState => !IsBusy && !HasPopularTracks && !HasAlbums;

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(IsBusy))
        {
            OnPropertyChanged(nameof(ShowEmptyState));
        }
        else if (e.PropertyName == nameof(IsLiked))
        {
            OnPropertyChanged(nameof(LikeArtistTooltip));
        }
    }

    public string BackButtonText
    {
        get
        {
            bool isEn = Loc?.CurrentLanguage == "en";
            return PreviousPageTag switch
            {
                "Search" => isEn ? "Back to Search" : "Назад к поиску",
                "Explore" => isEn ? "Back to Explore" : "Назад на Главную",
                "Likes" => isEn ? "Back to Liked Tracks" : "Назад к Любимым трекам",
                "Wave" => isEn ? "Back to My Wave" : "Назад в Мою волну",
                "Playlists" => isEn ? "Back to Library" : "Назад к Коллекции",
                "Account" => isEn ? "Back" : "Назад",
                _ => isEn ? "Back" : "Назад"
            };
        }
    }

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        OnPropertyChanged(nameof(BackButtonText));
        OnPropertyChanged(nameof(BadgeText));
        OnPropertyChanged(nameof(PlayPopularTracksText));
        OnPropertyChanged(nameof(LikeArtistTooltip));
        OnPropertyChanged(nameof(PopularTracksTitle));
        OnPropertyChanged(nameof(DiscographyTitle));
        OnPropertyChanged(nameof(AllReleasesFilterText));
        OnPropertyChanged(nameof(AlbumsFilterText));
        OnPropertyChanged(nameof(SinglesFilterText));
        OnPropertyChanged(nameof(CompilationsFilterText));
        OnPropertyChanged(nameof(SortOrderText));
        OnPropertyChanged(nameof(SortOrderTooltip));
        OnPropertyChanged(nameof(EmptyCategoryText));
        OnPropertyChanged(nameof(AboutArtistTitle));
        OnPropertyChanged(nameof(EmptyStateText));
        OnPropertyChanged(nameof(FckCensorTooltip));
        if (_lastMonthListeners != null)
        {
            FormattedListeners = ArtistStats.FormatListeners(_lastMonthListeners, Loc?.CurrentLanguage == "en");
        }
    }

    public ArtistViewModel(
        IYandexMusicService musicService,
        IAudioPlayerService audioPlayer,
        INavigationService navigationService,
        ITrackPlaybackCoordinator playbackCoordinator)
    {
        _musicService = musicService;
        _audioPlayer = audioPlayer;
        _navigationService = navigationService;
        _playbackCoordinator = playbackCoordinator;

        _playbackCoordinator.Register(PopularTracks);

        _musicService.ArtistLikeChanged += (s, args) =>
        {
            if (args.ArtistId == ArtistId)
            {
                RunOnUi(() => IsLiked = args.IsLiked);
            }
        };
    }

    public async Task LoadArtistAsync(Artist artist)
    {
        if (artist == null) return;

        RunOnUi(() =>
        {
            Artist = artist;
            ArtistId = artist.Id;
            ArtistName = !string.IsNullOrWhiteSpace(artist.Name) ? artist.Name : "Artist";
            AvatarUrl = artist.AvatarUrl;
            FormattedGenres = artist.GenresString;
            IsLiked = _musicService.IsArtistLiked(artist.Id);
            DescriptionText = artist.Description?.Text ?? string.Empty;
            HasDescription = !string.IsNullOrWhiteSpace(DescriptionText);
            PopularTracks.Clear();
            AllAlbums.Clear();
            AlsoAlbums.Clear();
            DisplayAlbums.Clear();
            HasPopularTracks = false;
            HasAlbums = false;
            HasAlsoAlbums = false;
            HasDisplayAlbums = false;
            IsSortDescending = true;
        });

        await LoadArtistDetailsAsync(artist.Id).ConfigureAwait(false);
    }

    public async Task LoadArtistByIdAsync(long artistId, string? artistName = null)
    {
        ArtistId = artistId;
        if (!string.IsNullOrWhiteSpace(artistName))
        {
            RunOnUi(() => ArtistName = artistName);
        }

        RunOnUi(() =>
        {
            IsLiked = _musicService.IsArtistLiked(artistId);
            PopularTracks.Clear();
            AllAlbums.Clear();
            AlsoAlbums.Clear();
            DisplayAlbums.Clear();
            HasPopularTracks = false;
            HasAlbums = false;
            HasAlsoAlbums = false;
            HasDisplayAlbums = false;
            IsSortDescending = true;
        });

        await LoadArtistDetailsAsync(artistId).ConfigureAwait(false);
    }

    private async Task LoadArtistDetailsAsync(long artistId)
    {
        SetBusy(true);
        ClearError();

        try
        {
            await _musicService.EnsureLikedTracksLoadedAsync().ConfigureAwait(false);
            await _musicService.EnsureLikedArtistsLoadedAsync().ConfigureAwait(false);

            // Fetch artist brief-info, full direct discography, and compilations in parallel
            var briefInfoTask = _musicService.GetArtistBriefInfoAsync(artistId);
            var directAlbumsTask = _musicService.GetArtistDirectAlbumsAsync(artistId);
            var alsoAlbumsTask = _musicService.GetArtistAlsoAlbumsAsync(artistId);

            await Task.WhenAll(briefInfoTask, directAlbumsTask, alsoAlbumsTask).ConfigureAwait(false);

            var briefInfo = await briefInfoTask;
            var directAlbums = await directAlbumsTask;
            var alsoAlbums = await alsoAlbumsTask;

            // FckCensor: Fetch inserted / re-uploaded tracks for censored artist profiles
            var fckCensor = App.GetService<YMusic.WinUI.Services.Interfaces.IFckCensorService>();
            var insertions = fckCensor?.GetArtistInsertions(artistId);
            List<Track>? insertedTracks = null;
            if (insertions != null && insertions.Tracks.Count > 0)
            {
                try
                {
                    var ids = insertions.Tracks.Select(t => t.ReleaseId).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
                    if (ids.Count > 0)
                    {
                        var fetched = await _musicService.GetTracksAsync(ids).ConfigureAwait(false);
                        insertedTracks = fetched.ToList();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[FckCensor Artist Insertions Warning] {ex.Message}");
                }
            }

            RunOnUi(() =>
            {
                PopularTracks.Clear();
                AllAlbums.Clear();
                AlsoAlbums.Clear();
                DisplayAlbums.Clear();

                if (briefInfo?.Artist != null)
                {
                    Artist = briefInfo.Artist;
                    ArtistName = briefInfo.Artist.Name;
                    AvatarUrl = briefInfo.Artist.AvatarUrl;
                    FormattedGenres = briefInfo.Artist.GenresString;
                    IsLiked = _musicService.IsArtistLiked(artistId);

                    if (briefInfo.Artist.Description != null && !string.IsNullOrWhiteSpace(briefInfo.Artist.Description.Text))
                    {
                        DescriptionText = briefInfo.Artist.Description.Text;
                        HasDescription = true;
                    }
                    else
                    {
                        DescriptionText = string.Empty;
                        HasDescription = false;
                    }

                    if (briefInfo.Artist.Counts != null)
                    {
                        TracksCount = briefInfo.Artist.Counts.Tracks;
                        AlbumsCount = briefInfo.Artist.Counts.DirectAlbums;
                    }
                }

                if (briefInfo?.Stats != null)
                {
                    _lastMonthListeners = briefInfo.Stats.LastMonthListeners;
                    FormattedListeners = ArtistStats.FormatListeners(_lastMonthListeners, Loc?.CurrentLanguage == "en");
                }
                else
                {
                    _lastMonthListeners = null;
                    FormattedListeners = string.Empty;
                }

                if (briefInfo?.PopularTracks != null)
                {
                    foreach (var track in briefInfo.PopularTracks)
                    {
                        PopularTracks.Add(track);
                    }

                    // Apply FckCensor artist insertions (banned/re-uploaded tracks)
                    if (insertions != null && insertedTracks != null && insertedTracks.Count > 0)
                    {
                        var trackMap = insertedTracks.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
                        foreach (var ins in insertions.Tracks)
                        {
                            if (trackMap.TryGetValue(ins.ReleaseId, out var trackToInsert) && !PopularTracks.Any(t => t.Id == trackToInsert.Id))
                            {
                                int idx = ins.Index >= 0
                                    ? Math.Clamp(ins.Index, 0, PopularTracks.Count)
                                    : Math.Clamp(PopularTracks.Count + 1 + ins.Index, 0, PopularTracks.Count);
                                PopularTracks.Insert(idx, trackToInsert);
                            }
                        }
                    }

                    _playbackCoordinator.Sync(PopularTracks);
                }

                // Load upcoming / preorder releases if provided in brief-info
                var upcomingList = new List<Album>();
                if (briefInfo?.UpcomingAlbum != null)
                {
                    briefInfo.UpcomingAlbum.IsPreorder = true;
                    upcomingList.Add(briefInfo.UpcomingAlbum);
                }
                if (briefInfo?.UpcomingAlbums != null)
                {
                    foreach (var ua in briefInfo.UpcomingAlbums)
                    {
                        ua.IsPreorder = true;
                        if (upcomingList.All(x => x.Id != ua.Id))
                        {
                            upcomingList.Add(ua);
                        }
                    }
                }

                // Load all direct releases (albums, singles, EPs). Fallback to briefInfo.Albums if directAlbums is empty.
                var baseAlbums = directAlbums != null && directAlbums.Count > 0
                    ? directAlbums
                    : (briefInfo?.Albums ?? new List<Album>());

                var combinedAlbums = new List<Album>();
                foreach (var up in upcomingList)
                {
                    combinedAlbums.Add(up);
                }
                foreach (var alb in baseAlbums)
                {
                    if (combinedAlbums.All(a => a.Id != alb.Id))
                    {
                        combinedAlbums.Add(alb);
                    }
                }

                foreach (var album in combinedAlbums)
                {
                    AllAlbums.Add(album);
                }

                if (alsoAlbums != null)
                {
                    foreach (var album in alsoAlbums)
                    {
                        AlsoAlbums.Add(album);
                    }
                }

                HasPopularTracks = PopularTracks.Count > 0;
                HasAlbums = AllAlbums.Count > 0;
                HasAlsoAlbums = AlsoAlbums.Count > 0;

                FilterAlbums(0);
            });
        }
        catch (Exception ex)
        {
            SetError($"Failed to load artist details: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[ArtistViewModel Error] {ex}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    [RelayCommand]
    public void SelectAlbumFilter(int index)
    {
        SelectedAlbumFilterIndex = index;
        FilterAlbums(index);
    }

    [RelayCommand]
    public void ToggleSortOrder()
    {
        IsSortDescending = !IsSortDescending;
        FilterAlbums(SelectedAlbumFilterIndex);
    }

    public void FilterAlbums(int filterIndex)
    {
        DisplayAlbums.Clear();

        IEnumerable<Album> filtered = filterIndex switch
        {
            // 1: Albums only (exclude singles & EPs)
            1 => AllAlbums.Where(a => !IsSingleOrEp(a)),
            // 2: Singles & EPs
            2 => AllAlbums.Where(a => IsSingleOrEp(a)),
            // 3: Compilations / Сборники
            3 => AlsoAlbums,
            // 0: All releases
            _ => AllAlbums
        };

        var sorted = IsSortDescending
            ? filtered
                .OrderByDescending(a => a.IsPreSave ? 1 : 0)
                .ThenByDescending(a => a.Year ?? 0)
                .ThenByDescending(a => a.ParsedReleaseDate ?? DateTime.MinValue)
                .ThenByDescending(a => a.Id)
            : filtered
                .OrderBy(a => a.IsPreSave ? 1 : 0)
                .ThenBy(a => a.Year ?? 9999)
                .ThenBy(a => a.ParsedReleaseDate ?? DateTime.MaxValue)
                .ThenBy(a => a.Id);

        foreach (var album in sorted)
        {
            DisplayAlbums.Add(album);
        }

        HasDisplayAlbums = DisplayAlbums.Count > 0;
    }

    private static bool IsSingleOrEp(Album album)
    {
        if (string.Equals(album.Type, "single", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(album.MetaType, "single", StringComparison.OrdinalIgnoreCase)) return true;
        if (album.TrackCount <= 3) return true;
        if (!string.IsNullOrWhiteSpace(album.Genre) &&
            (album.Genre.Contains("single", StringComparison.OrdinalIgnoreCase) ||
             album.Genre.Contains("ep", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }
        if (!string.IsNullOrWhiteSpace(album.Description) &&
            (album.Description.Contains("single", StringComparison.OrdinalIgnoreCase) ||
             album.Description.Contains("сингл", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }
        return false;
    }

    [RelayCommand]
    public async Task PlayPopularTracksAsync()
    {
        if (PopularTracks.Count == 0) return;

        var playable = PopularTracks.Where(t => t.Available).ToList();
        if (playable.Count == 0)
        {
            var isEn = Loc?.CurrentLanguage == "en";
            App.GetService<PlayerBarViewModel>()?.ShowToast(isEn ? "No playable tracks available yet" : "Пока нет доступных треков для воспроизведения");
            return;
        }

        _audioPlayer.CurrentPlaybackMode = PlaybackMode.Custom;
        await _audioPlayer.PlayQueueAsync(playable, 0).ConfigureAwait(false);
    }

    [RelayCommand]
    public async Task PlayTrackAsync(Track? track)
    {
        if (track == null || PopularTracks.Count == 0) return;

        if (!track.Available)
        {
            var isEn = Loc?.CurrentLanguage == "en";
            App.GetService<PlayerBarViewModel>()?.ShowToast(isEn
                ? "This track will be available on the album's release day"
                : "Этот трек станет доступен в день выхода альбома");
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

        int index = PopularTracks.IndexOf(track);
        if (index < 0) index = 0;

        _audioPlayer.CurrentPlaybackMode = PlaybackMode.Custom;
        await _audioPlayer.PlayQueueAsync(PopularTracks.ToList(), index).ConfigureAwait(false);
    }

    [RelayCommand]
    public async Task ToggleLikeArtistAsync()
    {
        if (ArtistId <= 0) return;

        bool targetLiked = !IsLiked;
        IsLiked = targetLiked;

        try
        {
            if (targetLiked)
            {
                await _musicService.LikeArtistAsync(ArtistId).ConfigureAwait(false);
            }
            else
            {
                await _musicService.UnlikeArtistAsync(ArtistId).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ToggleLikeArtist Error] {ex.Message}");
            IsLiked = !targetLiked;
        }
    }

    [RelayCommand]
    public async Task ToggleLikeTrackAsync(Track? track)
    {
        if (track == null) return;

        bool newLiked = !track.IsLiked;
        track.IsLiked = newLiked;

        try
        {
            if (newLiked)
            {
                await _musicService.LikeTrackAsync(track.Id).ConfigureAwait(false);
            }
            else
            {
                await _musicService.UnlikeTrackAsync(track.Id).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ToggleLikeTrack Error] {ex.Message}");
            track.IsLiked = !newLiked;
        }
    }

    [RelayCommand]
    public async Task OpenAlbumAsync(Album? album)
    {
        if (album == null) return;
        await _navigationService.NavigateToAlbumAsync(album).ConfigureAwait(false);
    }

    [RelayCommand]
    public void GoBack()
    {
        string? targetTag = PreviousPageTag;
        PreviousPageTag = null;

        if (!string.IsNullOrEmpty(targetTag))
        {
            _navigationService.NavigateToTag(targetTag);
        }
        else
        {
            _navigationService.NavigateToTag("Search");
        }
    }
}
