#nullable enable

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YMusic.WinUI.Helpers;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.ViewModels;

public sealed partial class PlaylistsViewModel : BaseViewModel
{
    private readonly IYandexMusicService _musicService;
    private readonly IAudioPlayerService _audioPlayer;
    private readonly ICredentialStorageService _credentialStorage;
    private readonly ITrackPlaybackCoordinator _playbackCoordinator;
    private readonly IPinnedItemsService _pinnedItemsService;
    private readonly ISpotifyImportService _spotifyImportService;

    public ObservableCollection<Playlist> Playlists { get; } = new();
    public ObservableCollection<Album> LikedAlbums { get; } = new();
    public ObservableCollection<Track> SelectedPlaylistTracks { get; } = new();
    private readonly List<Track> _allSelectedTracks = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSearchFilter))]
    private string _searchFilter = string.Empty;

    public bool HasSearchFilter => !string.IsNullOrWhiteSpace(SearchFilter);

    public string FilterPlaceholder => Loc?.CurrentLanguage == "en" ? "Filter tracks..." : "Поиск по трекам...";

    public string PreSaveBannerTitle => Loc?.CurrentLanguage == "en" ? "Album Coming Soon" : "Альбом ещё не вышел";
    public string PreSaveBannerDescription => Loc?.CurrentLanguage == "en"
        ? "Pre-save this album to have it automatically added to your library the moment it drops."
        : "Оформите пресейв, чтобы не пропустить релиз. Альбом автоматически добавится в вашу медиатеку в день выхода.";

    public bool HasFormattedDuration => !string.IsNullOrEmpty(FormattedDuration) && SelectedPlaylistTracks.Any(t => t.DurationMs > 0);
    public bool HasTrackCountOrDuration => !string.IsNullOrWhiteSpace(FormattedTrackCount) || HasFormattedDuration;
    public bool IsPreSaveEmptyState => IsDetailPreSave && SelectedPlaylistTracks.Count == 0;

    partial void OnSearchFilterChanged(string value)
    {
        RunOnUi(ApplyTrackFilter);
    }

    private void ApplyTrackFilter()
    {
        SelectedPlaylistTracks.Clear();
        var query = SearchFilter?.Trim();

        var filtered = string.IsNullOrWhiteSpace(query)
            ? _allSelectedTracks
            : _allSelectedTracks.Where(t =>
                t.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.ArtistNames.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.AlbumTitle.Contains(query, StringComparison.OrdinalIgnoreCase));

        foreach (var track in filtered)
        {
            SelectedPlaylistTracks.Add(track);
        }

        _playbackCoordinator.Sync(SelectedPlaylistTracks);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailTitle))]
    [NotifyPropertyChangedFor(nameof(DetailSubtitle))]
    [NotifyPropertyChangedFor(nameof(DetailCoverUrl))]
    [NotifyPropertyChangedFor(nameof(DetailDescription))]
    [NotifyPropertyChangedFor(nameof(DetailTypeBadge))]
    [NotifyPropertyChangedFor(nameof(DetailPlayButtonText))]
    [NotifyPropertyChangedFor(nameof(IsDetailPinned))]
    [NotifyPropertyChangedFor(nameof(IsDetailLiked))]
    [NotifyPropertyChangedFor(nameof(IsDetailPlaylist))]
    [NotifyPropertyChangedFor(nameof(IsSelectedPlaylistOwned))]
    private Playlist? _selectedPlaylist;

    public bool IsDetailPlaylist => SelectedPlaylist != null;

    public bool IsSelectedPlaylistOwned =>
        SelectedPlaylist != null &&
        (SelectedPlaylist.Uid.ToString() == _credentialStorage.GetUserId() ||
         SelectedPlaylist.Owner?.Uid.ToString() == _credentialStorage.GetUserId());

    public bool IsPlaylistOwned(Playlist? pl) =>
        pl != null &&
        (pl.Uid.ToString() == _credentialStorage.GetUserId() ||
         pl.Owner?.Uid.ToString() == _credentialStorage.GetUserId());

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailTitle))]
    [NotifyPropertyChangedFor(nameof(DetailSubtitle))]
    [NotifyPropertyChangedFor(nameof(DetailCoverUrl))]
    [NotifyPropertyChangedFor(nameof(DetailDescription))]
    [NotifyPropertyChangedFor(nameof(DetailTypeBadge))]
    [NotifyPropertyChangedFor(nameof(DetailPlayButtonText))]
    [NotifyPropertyChangedFor(nameof(DetailPlayButtonGlyph))]
    [NotifyPropertyChangedFor(nameof(IsDetailPreSave))]
    [NotifyPropertyChangedFor(nameof(IsDetailPinned))]
    [NotifyPropertyChangedFor(nameof(IsDetailLiked))]
    private Album? _selectedAlbum;

    public bool IsDetailPreSave => SelectedAlbum?.IsPreSave == true;

    public bool IsDetailPinned =>
        SelectedPlaylist != null
            ? _pinnedItemsService.IsPlaylistPinned(SelectedPlaylist.Uid, SelectedPlaylist.Kind)
            : SelectedAlbum != null
                ? _pinnedItemsService.IsAlbumPinned(SelectedAlbum.Id)
                : false;

    public bool IsDetailLiked =>
        SelectedPlaylist != null
            ? SelectedPlaylist.IsLiked
            : SelectedAlbum != null
                ? SelectedAlbum.IsLiked
                : false;

    partial void OnSelectedPlaylistChanged(Playlist? value)
    {
        if (value != null)
        {
            value.IsPinned = _pinnedItemsService.IsPlaylistPinned(value.Uid, value.Kind);
            value.IsLiked = _musicService.IsPlaylistLiked(value.Uid, value.Kind);
        }
        OnPropertyChanged(nameof(IsDetailPinned));
        OnPropertyChanged(nameof(IsDetailLiked));
        OnPropertyChanged(nameof(DetailPlayButtonText));
        OnPropertyChanged(nameof(DetailPlayButtonGlyph));
    }

    partial void OnSelectedAlbumChanged(Album? value)
    {
        if (value != null)
        {
            value.IsPinned = _pinnedItemsService.IsAlbumPinned(value.Id);
            value.IsLiked = _musicService.IsAlbumLiked(value.Id);
        }
        OnPropertyChanged(nameof(IsDetailPinned));
        OnPropertyChanged(nameof(IsDetailLiked));
        OnPropertyChanged(nameof(IsDetailPreSave));
        OnPropertyChanged(nameof(DetailPlayButtonText));
        OnPropertyChanged(nameof(DetailPlayButtonGlyph));
        OnPropertyChanged(nameof(IsDetailLikeButtonVisible));
    }

    public bool IsDetailLikeButtonVisible => !(IsDetailPreSave && SelectedAlbum?.HasPlayableTracks == false);
    public bool HasTracksToFilter => _allSelectedTracks.Count > 1;

    public string DetailTitle => SelectedPlaylist?.Title ?? SelectedAlbum?.Title ?? string.Empty;
    public string DetailSubtitle
    {
        get
        {
            if (SelectedPlaylist != null) return SelectedPlaylist.OwnerName;
            if (SelectedAlbum != null)
            {
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(SelectedAlbum.ArtistNames))
                    parts.Add(SelectedAlbum.ArtistNames);

                if (SelectedAlbum.IsPreSave)
                {
                    parts.Add(SelectedAlbum.FormattedReleaseDate);
                }
                else if (SelectedAlbum.Year.HasValue)
                {
                    parts.Add(SelectedAlbum.Year.Value.ToString());
                }

                if (!string.IsNullOrWhiteSpace(SelectedAlbum.Genre))
                    parts.Add(SelectedAlbum.Genre);

                return string.Join(" • ", parts);
            }
            return string.Empty;
        }
    }
    public string? DetailCoverUrl => SelectedPlaylist?.CoverUrl ?? SelectedAlbum?.CoverUrl;
    public string? DetailDescription => SelectedPlaylist?.Description ?? SelectedAlbum?.Description;
    public string DetailTypeBadge
    {
        get
        {
            if (SelectedPlaylist != null)
                return Loc?.CurrentLanguage == "en" ? "PLAYLIST" : "ПЛЕЙЛИСТ";

            if (SelectedAlbum != null)
            {
                if (SelectedAlbum.IsPreSave)
                    return Loc?.CurrentLanguage == "en" ? "PRE-SAVE" : "ПРЕСЕЙВ";

                return Loc?.CurrentLanguage == "en" ? "ALBUM" : "АЛЬБОМ";
            }

            return string.Empty;
        }
    }
    public string DetailPlayButtonText
    {
        get
        {
            bool isEn = Loc?.CurrentLanguage == "en";
            if (SelectedPlaylist != null)
            {
                return isEn ? "Play Playlist" : "Слушать плейлист";
            }
            if (SelectedAlbum != null)
            {
                if (SelectedAlbum.IsPreSave)
                {
                    if (SelectedAlbum.HasPlayableTracks)
                    {
                        return isEn ? "Play Available Singles" : "Слушать доступные синглы";
                    }
                    return IsDetailLiked
                        ? (isEn ? "Pre-save Added ✓" : "Пресейв добавлен ✓")
                        : (isEn ? "Pre-save Album" : "Предсохранить альбом");
                }
                return isEn ? "Play Album" : "Слушать альбом";
            }
            return isEn ? "Play" : "Слушать";
        }
    }

    public string DetailPlayButtonGlyph =>
        (SelectedAlbum?.IsPreSave == true && !SelectedAlbum.HasPlayableTracks)
            ? (IsDetailLiked ? "\uE73E" : "\uEA8F")
            : "\uE768";

    public string PageTitle => Loc?.CurrentLanguage == "en" ? "Library" : (Loc?.GetString("Nav_Playlists") ?? "Плейлисты и альбомы");
    public string PlaylistsTabText => Loc?.CurrentLanguage == "en" ? "Playlists" : "Плейлисты";
    public string LikedAlbumsTabText => Loc?.CurrentLanguage == "en" ? "Liked Albums" : "Любимые альбомы";
    public string RefreshTooltip => Loc?.CurrentLanguage == "en" ? "Refresh library" : "Обновить библиотеку";
    public string NoPlaylistsText => Loc?.CurrentLanguage == "en" ? "No playlists found" : "Плейлисты не найдены";
    public string NoLikedAlbumsText => Loc?.CurrentLanguage == "en" ? "No liked albums yet" : "Любимых альбомов пока нет";
    public string NoLikedAlbumsSubtext => Loc?.CurrentLanguage == "en" ? "Albums you like on Yandex Music will appear here." : "Альбомы, которые вы лайкнете в Яндекс Музыке, появятся здесь.";
    public string PinnedBadgeText => Loc?.CurrentLanguage == "en" ? "Pinned" : "Закреплено";
    public string PlayMenuText => Loc?.CurrentLanguage == "en" ? "Play" : "Слушать";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackButtonText))]
    private string? _previousPageTag;

    public string BackButtonText
    {
        get
        {
            bool isEn = Loc?.CurrentLanguage == "en";
            return PreviousPageTag switch
            {
                "Search" => isEn ? "Back to Search" : "Назад к поиску",
                "Artist" => isEn ? "Back to Artist" : "Назад к исполнителю",
                "Likes" => isEn ? "Back to Liked Tracks" : "Назад к Любимым трекам",
                "Explore" => isEn ? "Back to Explore" : "Назад на Главную",
                "Wave" => isEn ? "Back to My Wave" : "Назад в Мою волну",
                "Account" => isEn ? "Back" : "Назад",
                _ => (SelectedCategoryIndex == 1
                    ? (isEn ? "Back to Liked Albums" : "Назад к Альбомам")
                    : (isEn ? "Back to Playlists" : "Назад к Плейлистам"))
            };
        }
    }

    public string CreatePlaylistButtonText => Loc?.CurrentLanguage == "en" ? "Create" : "Создать";
    public string CreatePlaylistTooltip => Loc?.CurrentLanguage == "en" ? "Create a new playlist" : "Создать новый плейлист";
    public string EditPlaylistButtonText => Loc?.CurrentLanguage == "en" ? "Edit" : "Редактировать";
    public string DeletePlaylistButtonText => Loc?.CurrentLanguage == "en" ? "Delete Playlist" : "Удалить плейлист";
    public string EditPlaylistMenuText => Loc?.CurrentLanguage == "en" ? "Edit playlist" : "Редактировать плейлист";
    public string DeletePlaylistMenuText => Loc?.CurrentLanguage == "en" ? "Delete playlist" : "Удалить плейлист";
    public string CreatePlaylistHeader => Loc?.CurrentLanguage == "en" ? "New Playlist" : "Новый плейлист";
    public string EditPlaylistHeader => Loc?.CurrentLanguage == "en" ? "Edit Playlist" : "Редактировать плейлист";
    public string DeletePlaylistHeader => Loc?.CurrentLanguage == "en" ? "Delete Playlist" : "Удалить плейлист";
    public string PlaylistTitlePlaceholder => Loc?.CurrentLanguage == "en" ? "Playlist name (required)" : "Название плейлиста (обязательно)";
    public string PlaylistDescPlaceholder => Loc?.CurrentLanguage == "en" ? "Description (optional)" : "Описание (необязательно)";
    public string PlaylistPublicToggleText => Loc?.CurrentLanguage == "en" ? "Public playlist" : "Публичный плейлист";
    public string SaveButtonText => Loc?.CurrentLanguage == "en" ? "Save" : "Сохранить";
    public string CancelButtonText => Loc?.CurrentLanguage == "en" ? "Cancel" : "Отмена";
    public string DeleteButtonText => Loc?.CurrentLanguage == "en" ? "Delete" : "Удалить";
    public string CreateButtonText => Loc?.CurrentLanguage == "en" ? "Create" : "Создать";
    public string RemoveTrackFromPlaylistText => Loc?.CurrentLanguage == "en" ? "Remove from this playlist" : "Удалить из этого плейлиста";

    public string ImportButtonText => Loc?.CurrentLanguage == "en" ? "Import" : "Импорт";
    public string ImportTooltip => Loc?.CurrentLanguage == "en" ? "Import playlist or album from Spotify" : "Импортировать плейлист или альбом из Spotify";
    public string ImportDialogTitle => Loc?.CurrentLanguage == "en" ? "Import Playlist from Spotify" : "Импорт плейлиста из Spotify";
    public string ImportUrlPlaceholder => Loc?.CurrentLanguage == "en" ? "Paste Spotify playlist, album, or track URL..." : "Вставьте ссылку на плейлист, альбом или трек из Spotify...";
    public string ImportCustomTitlePlaceholder => Loc?.CurrentLanguage == "en" ? "Custom playlist name in Yandex (optional)" : "Название плейлиста в Яндекс Музыке (необязательно)";
    public string StartImportButtonText => Loc?.CurrentLanguage == "en" ? "Start Import" : "Начать импорт";
    public string OpenImportedPlaylistButtonText => Loc?.CurrentLanguage == "en" ? "Open Playlist" : "Открыть плейлист";
    public string CloseButtonText => Loc?.CurrentLanguage == "en" ? "Close" : "Закрыть";

    protected override void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PlaylistsTabText));
        OnPropertyChanged(nameof(LikedAlbumsTabText));
        OnPropertyChanged(nameof(RefreshTooltip));
        OnPropertyChanged(nameof(NoPlaylistsText));
        OnPropertyChanged(nameof(NoLikedAlbumsText));
        OnPropertyChanged(nameof(NoLikedAlbumsSubtext));
        OnPropertyChanged(nameof(PinnedBadgeText));
        OnPropertyChanged(nameof(PlayMenuText));
        OnPropertyChanged(nameof(DetailTypeBadge));
        OnPropertyChanged(nameof(DetailPlayButtonText));
        OnPropertyChanged(nameof(DetailPlayButtonGlyph));
        OnPropertyChanged(nameof(BackButtonText));
        OnPropertyChanged(nameof(FilterPlaceholder));
        OnPropertyChanged(nameof(PreSaveBannerTitle));
        OnPropertyChanged(nameof(PreSaveBannerDescription));
        OnPropertyChanged(nameof(ImportButtonText));
        OnPropertyChanged(nameof(ImportTooltip));
        OnPropertyChanged(nameof(ImportDialogTitle));
        OnPropertyChanged(nameof(ImportUrlPlaceholder));
        OnPropertyChanged(nameof(ImportCustomTitlePlaceholder));
        OnPropertyChanged(nameof(StartImportButtonText));
        OnPropertyChanged(nameof(OpenImportedPlaylistButtonText));
        OnPropertyChanged(nameof(CloseButtonText));
        OnPropertyChanged(nameof(CreatePlaylistButtonText));
        OnPropertyChanged(nameof(EditPlaylistButtonText));
        OnPropertyChanged(nameof(DeletePlaylistButtonText));
        OnPropertyChanged(nameof(EditPlaylistMenuText));
        OnPropertyChanged(nameof(DeletePlaylistMenuText));
        OnPropertyChanged(nameof(CreatePlaylistHeader));
        OnPropertyChanged(nameof(EditPlaylistHeader));
        OnPropertyChanged(nameof(DeletePlaylistHeader));
        OnPropertyChanged(nameof(PlaylistTitlePlaceholder));
        OnPropertyChanged(nameof(PlaylistDescPlaceholder));
        OnPropertyChanged(nameof(PlaylistPublicToggleText));
        OnPropertyChanged(nameof(SaveButtonText));
        OnPropertyChanged(nameof(CancelButtonText));
        OnPropertyChanged(nameof(DeleteButtonText));
        OnPropertyChanged(nameof(CreateButtonText));
        OnPropertyChanged(nameof(RemoveTrackFromPlaylistText));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBrowseVisible))]
    private bool _isDetailOpen;

    public bool IsBrowseVisible => !IsDetailOpen;

    [ObservableProperty]
    private bool _isBackButtonVisible = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlaylistsTab))]
    [NotifyPropertyChangedFor(nameof(IsAlbumsTab))]
    [NotifyPropertyChangedFor(nameof(BackButtonText))]
    private int _selectedCategoryIndex; // 0 = Playlists, 1 = Liked Albums

    public bool IsPlaylistsTab => SelectedCategoryIndex == 0;
    public bool IsAlbumsTab => SelectedCategoryIndex == 1;

    [ObservableProperty]
    private string _formattedTrackCount = string.Empty;

    [ObservableProperty]
    private string _formattedDuration = string.Empty;

    [ObservableProperty]
    private bool _isImportDialogOpen;

    [ObservableProperty]
    private string _importUrl = string.Empty;

    [ObservableProperty]
    private string _importCustomTitle = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotImporting))]
    private bool _isImporting;

    public bool IsNotImporting => !IsImporting;

    [ObservableProperty]
    private double _importProgressValue;

    [ObservableProperty]
    private string _importStatusText = string.Empty;

    [ObservableProperty]
    private string _importResultSummary = string.Empty;

    [ObservableProperty]
    private bool _isImportFinished;

    [ObservableProperty]
    private Playlist? _lastImportedPlaylist;

    // Create Playlist dialog
    [ObservableProperty]
    private bool _isCreatePlaylistDialogOpen;

    [ObservableProperty]
    private string _newPlaylistTitle = string.Empty;

    [ObservableProperty]
    private string _newPlaylistDescription = string.Empty;

    [ObservableProperty]
    private bool _isNewPlaylistPublic = true;

    [ObservableProperty]
    private string? _createPlaylistErrorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotCreatingPlaylist))]
    private bool _isCreatingPlaylist;

    public bool IsNotCreatingPlaylist => !IsCreatingPlaylist;

    // Edit Playlist dialog
    [ObservableProperty]
    private bool _isEditPlaylistDialogOpen;

    [ObservableProperty]
    private string _editPlaylistTitle = string.Empty;

    [ObservableProperty]
    private string _editPlaylistDescription = string.Empty;

    [ObservableProperty]
    private string? _editPlaylistErrorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotEditingPlaylist))]
    private bool _isEditingPlaylist;

    public bool IsNotEditingPlaylist => !IsEditingPlaylist;

    [ObservableProperty]
    private Playlist? _playlistToEdit;

    // Delete Playlist dialog
    [ObservableProperty]
    private bool _isDeletePlaylistDialogOpen;

    [ObservableProperty]
    private Playlist? _playlistToDelete;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotDeletingPlaylist))]
    private bool _isDeletingPlaylist;

    public bool IsNotDeletingPlaylist => !IsDeletingPlaylist;

    public string DeletePlaylistPromptText => PlaylistToDelete != null
        ? (Loc?.CurrentLanguage == "en"
            ? $"Are you sure you want to delete \"{PlaylistToDelete.Title}\"? This cannot be undone."
            : $"Вы действительно хотите удалить плейлист «{PlaylistToDelete.Title}»? Это действие нельзя отменить.")
        : string.Empty;

    public PlaylistsViewModel(
        IYandexMusicService musicService,
        IAudioPlayerService audioPlayer,
        ICredentialStorageService credentialStorage,
        ITrackPlaybackCoordinator playbackCoordinator,
        IPinnedItemsService pinnedItemsService,
        ISpotifyImportService spotifyImportService)
    {
        _musicService = musicService;
        _audioPlayer = audioPlayer;
        _credentialStorage = credentialStorage;
        _playbackCoordinator = playbackCoordinator;
        _pinnedItemsService = pinnedItemsService;
        _spotifyImportService = spotifyImportService;

        _pinnedItemsService.PinStateChanged += OnPinStateChanged;
        _musicService.AlbumLikeChanged += OnAlbumLikeChanged;
        _musicService.PlaylistLikeChanged += OnPlaylistLikeChanged;
        _playbackCoordinator.Register(SelectedPlaylistTracks);
    }

    [RelayCommand]
    public void SelectCategory(string? category)
    {
        PreviousPageTag = null;
        SelectedCategoryIndex = category == "albums" ? 1 : 0;
    }

    [RelayCommand]
    public void OpenImportDialog()
    {
        ImportUrl = string.Empty;
        ImportCustomTitle = string.Empty;
        IsImporting = false;
        IsImportFinished = false;
        ImportProgressValue = 0;
        ImportStatusText = string.Empty;
        ImportResultSummary = string.Empty;
        LastImportedPlaylist = null;
        IsImportDialogOpen = true;
    }

    [RelayCommand]
    public void CloseImportDialog()
    {
        IsImportDialogOpen = false;
    }

    [RelayCommand]
    public async Task StartImportAsync()
    {
        if (string.IsNullOrWhiteSpace(ImportUrl)) return;
        if (!_spotifyImportService.IsSupportedUrl(ImportUrl))
        {
            ImportStatusText = Loc?.CurrentLanguage == "en"
                ? "Invalid Spotify URL (playlist, album or track required)"
                : "Некорректная ссылка на Spotify (нужен плейлист, альбом или трек)";
            return;
        }

        IsImporting = true;
        IsImportFinished = false;
        ImportProgressValue = 0;
        ImportStatusText = Loc?.CurrentLanguage == "en"
            ? "Fetching track list from Spotify..."
            : "Получение списка треков из Spotify...";

        var spotifyData = await _spotifyImportService.FetchSpotifyPlaylistAsync(ImportUrl);
        if (spotifyData == null || spotifyData.Tracks.Count == 0)
        {
            IsImporting = false;
            ImportStatusText = Loc?.CurrentLanguage == "en"
                ? "Failed to load tracks from Spotify. Please check the URL or your network."
                : "Не удалось загрузить треки из Spotify. Проверьте ссылку или сетевое подключение.";
            return;
        }

        string targetTitle = !string.IsNullOrWhiteSpace(ImportCustomTitle)
            ? ImportCustomTitle
            : spotifyData.Title;

        var progress = new Progress<ImportProgressReport>(report =>
        {
            RunOnUi(() =>
            {
                ImportProgressValue = (double)report.Current / report.Total * 100.0;
                ImportStatusText = Loc?.CurrentLanguage == "en"
                    ? $"Searching Yandex Music: {report.Current} / {report.Total} (found: {report.FoundCount})...\n{report.CurrentTrackName}"
                    : $"Поиск треков в Яндекс Музыке: {report.Current} / {report.Total} (найдено: {report.FoundCount})...\n{report.CurrentTrackName}";
            });
        });

        var result = await _spotifyImportService.ImportToYandexAsync(
            spotifyData,
            targetTitle,
            progress);

        RunOnUi(async () =>
        {
            IsImporting = false;
            IsImportFinished = true;
            LastImportedPlaylist = result.CreatedPlaylist;

            ImportResultSummary = Loc?.CurrentLanguage == "en"
                ? $"Successfully imported {result.FoundTracks} of {result.TotalTracks} tracks into \"{result.CreatedPlaylist?.Title ?? targetTitle}\"!"
                : $"Успешно импортировано {result.FoundTracks} из {result.TotalTracks} треков в плейлист «{result.CreatedPlaylist?.Title ?? targetTitle}»!";

            // Refresh user playlists
            await LoadPlaylistsAsync();
        });
    }

    [RelayCommand]
    public async Task OpenImportedPlaylistAsync()
    {
        if (LastImportedPlaylist != null)
        {
            IsImportDialogOpen = false;
            await OpenPlaylistAsync(LastImportedPlaylist);
        }
    }

    [RelayCommand]
    public void OpenCreatePlaylistDialog()
    {
        NewPlaylistTitle = string.Empty;
        NewPlaylistDescription = string.Empty;
        IsNewPlaylistPublic = true;
        CreatePlaylistErrorMessage = null;
        IsCreatingPlaylist = false;
        IsCreatePlaylistDialogOpen = true;
    }

    [RelayCommand]
    public void CloseCreatePlaylistDialog()
    {
        IsCreatePlaylistDialogOpen = false;
    }

    [RelayCommand]
    public async Task CreatePlaylistAsync()
    {
        string title = NewPlaylistTitle?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title))
        {
            CreatePlaylistErrorMessage = Loc?.CurrentLanguage == "en"
                ? "Please enter a playlist name"
                : "Введите название плейлиста";
            return;
        }

        IsCreatingPlaylist = true;
        CreatePlaylistErrorMessage = null;

        try
        {
            var playlist = await _musicService.CreatePlaylistAsync(
                title,
                string.IsNullOrWhiteSpace(NewPlaylistDescription) ? null : NewPlaylistDescription.Trim(),
                IsNewPlaylistPublic);

            if (playlist != null)
            {
                playlist.IsPinned = false;
                playlist.IsLiked = false;
                Playlists.Insert(0, playlist);
                IsCreatePlaylistDialogOpen = false;

                ShowToast(string.Format(
                    Loc?.CurrentLanguage == "en" ? "Playlist \"{0}\" created" : "Плейлист «{0}» создан",
                    playlist.Title));

                await OpenPlaylistAsync(playlist);
            }
            else
            {
                CreatePlaylistErrorMessage = Loc?.CurrentLanguage == "en"
                    ? "Failed to create playlist. Please try again."
                    : "Не удалось создать плейлист. Попробуйте снова.";
            }
        }
        catch (Exception ex)
        {
            CreatePlaylistErrorMessage = ex.Message;
        }
        finally
        {
            IsCreatingPlaylist = false;
        }
    }

    [RelayCommand]
    public void OpenEditPlaylistDialog(Playlist? playlist = null)
    {
        var target = playlist ?? SelectedPlaylist;
        if (target == null) return;

        PlaylistToEdit = target;
        EditPlaylistTitle = target.Title;
        EditPlaylistDescription = target.Description ?? string.Empty;
        EditPlaylistErrorMessage = null;
        IsEditingPlaylist = false;
        IsEditPlaylistDialogOpen = true;
    }

    [RelayCommand]
    public void CloseEditPlaylistDialog()
    {
        IsEditPlaylistDialogOpen = false;
        PlaylistToEdit = null;
    }

    [RelayCommand]
    public async Task SaveEditPlaylistAsync()
    {
        var target = PlaylistToEdit ?? SelectedPlaylist;
        if (target == null) return;

        string title = EditPlaylistTitle?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title))
        {
            EditPlaylistErrorMessage = Loc?.CurrentLanguage == "en"
                ? "Playlist name cannot be empty"
                : "Название не может быть пустым";
            return;
        }

        IsEditingPlaylist = true;
        EditPlaylistErrorMessage = null;

        try
        {
            string? desc = string.IsNullOrWhiteSpace(EditPlaylistDescription) ? null : EditPlaylistDescription.Trim();
            bool ok = await _musicService.RenamePlaylistAsync(target.Uid, target.Kind, title, desc);
            if (ok)
            {
                target.Title = title;
                target.Description = desc;

                // Refresh in Playlists collection if present
                var inList = Playlists.FirstOrDefault(p => p.Uid == target.Uid && p.Kind == target.Kind);
                if (inList != null)
                {
                    inList.Title = title;
                    inList.Description = desc;
                }

                if (SelectedPlaylist?.Uid == target.Uid && SelectedPlaylist?.Kind == target.Kind)
                {
                    OnPropertyChanged(nameof(DetailTitle));
                    OnPropertyChanged(nameof(DetailDescription));
                }

                IsEditPlaylistDialogOpen = false;
                PlaylistToEdit = null;

                ShowToast(Loc?.CurrentLanguage == "en" ? "Playlist updated" : "Плейлист обновлён");
            }
            else
            {
                EditPlaylistErrorMessage = Loc?.CurrentLanguage == "en"
                    ? "Failed to update playlist. Please try again."
                    : "Не удалось обновить плейлист. Попробуйте снова.";
            }
        }
        catch (Exception ex)
        {
            EditPlaylistErrorMessage = ex.Message;
        }
        finally
        {
            IsEditingPlaylist = false;
        }
    }

    [RelayCommand]
    public void OpenDeletePlaylistDialog(Playlist? playlist = null)
    {
        PlaylistToDelete = playlist ?? SelectedPlaylist;
        if (PlaylistToDelete == null) return;

        OnPropertyChanged(nameof(DeletePlaylistPromptText));
        IsDeletingPlaylist = false;
        IsDeletePlaylistDialogOpen = true;
    }

    [RelayCommand]
    public void CloseDeletePlaylistDialog()
    {
        IsDeletePlaylistDialogOpen = false;
        PlaylistToDelete = null;
    }

    [RelayCommand]
    public async Task ConfirmDeletePlaylistAsync()
    {
        var target = PlaylistToDelete;
        if (target == null) return;

        IsDeletingPlaylist = true;

        try
        {
            bool isOwned = IsPlaylistOwned(target);
            bool ok;
            if (isOwned)
            {
                ok = await _musicService.DeletePlaylistAsync(target.Uid, target.Kind);
            }
            else
            {
                ok = await _musicService.UnlikePlaylistAsync(target.Uid, target.Kind);
            }

            if (ok)
            {
                var matching = Playlists.Where(p => (p.Uid == target.Uid && p.Kind == target.Kind) || (p.Kind == target.Kind && p.Kind != 0)).ToList();
                foreach (var p in matching)
                {
                    Playlists.Remove(p);
                }
                Playlists.Remove(target);

                if (_pinnedItemsService.IsPlaylistPinned(target.Uid, target.Kind))
                {
                    _pinnedItemsService.UnpinPlaylist(target.Uid, target.Kind);
                }

                if (SelectedPlaylist?.Uid == target.Uid && SelectedPlaylist?.Kind == target.Kind)
                {
                    CloseDetail();
                }

                IsDeletePlaylistDialogOpen = false;
                PlaylistToDelete = null;

                ShowToast(isOwned
                    ? (Loc?.CurrentLanguage == "en" ? "Playlist deleted" : "Плейлист удалён")
                    : (Loc?.CurrentLanguage == "en" ? "Playlist removed from library" : "Плейлист удалён из медиатеки"));

                // Refresh playlists from server immediately so counts and lists stay fully synchronized
                _ = LoadPlaylistsAsync();
            }
            else
            {
                ShowToast(Loc?.CurrentLanguage == "en" ? "Failed to delete playlist" : "Не удалось удалить плейлист");
            }
        }
        catch (Exception ex)
        {
            ShowToast(ex.Message);
        }
        finally
        {
            IsDeletingPlaylist = false;
        }
    }

    [RelayCommand]
    public async Task RemoveTrackFromCurrentPlaylistAsync(Track? track)
    {
        if (track == null || SelectedPlaylist == null || !IsSelectedPlaylistOwned) return;

        int index = SelectedPlaylistTracks.IndexOf(track);
        if (index < 0) return;

        try
        {
            bool ok = await _musicService.RemoveTrackFromPlaylistAsync(SelectedPlaylist.Uid, SelectedPlaylist.Kind, index);
            if (ok)
            {
                SelectedPlaylistTracks.Remove(track);
                _allSelectedTracks.Remove(track);
                _playbackCoordinator.Sync(SelectedPlaylistTracks);
                UpdateFormattedTrackCountAndDuration();
                ShowToast(Loc?.CurrentLanguage == "en" ? "Track removed from playlist" : "Трек удалён из плейлиста");
            }
            else
            {
                ShowToast(Loc?.CurrentLanguage == "en" ? "Failed to remove track" : "Не удалось удалить трек");
            }
        }
        catch (Exception ex)
        {
            ShowToast(ex.Message);
        }
    }

    private void UpdateFormattedTrackCountAndDuration()
    {
        FormattedTrackCount = PluralizationHelper.FormatTrackCount(SelectedPlaylistTracks.Count);
        long totalMs = SelectedPlaylistTracks.Sum(t => t.DurationMs);
        var totalSpan = TimeSpan.FromMilliseconds(totalMs);
        FormattedDuration = totalMs > 0 ? PluralizationHelper.FormatDuration(totalSpan) : string.Empty;
        OnPropertyChanged(nameof(HasFormattedDuration));
        OnPropertyChanged(nameof(HasTrackCountOrDuration));
        OnPropertyChanged(nameof(HasTracksToFilter));
    }

    private void ShowToast(string message)
    {
        App.GetService<PlayerBarViewModel>()?.ShowToast(message);
    }

    [RelayCommand]
    public async Task LoadPlaylistsAsync()
    {
        if (string.IsNullOrWhiteSpace(_musicService.CurrentToken))
        {
            var token = _credentialStorage.GetToken();
            if (!string.IsNullOrWhiteSpace(token))
            {
                _musicService.SetOAuthToken(token);
            }
        }

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
            var playlistsTask = _musicService.GetUserPlaylistsAsync(userId);
            var albumsTask = _musicService.GetLikedAlbumsAsync(userId);

            await Task.WhenAll(playlistsTask, albumsTask).ConfigureAwait(false);

            var playlists = await playlistsTask.ConfigureAwait(false);
            var albums = await albumsTask.ConfigureAwait(false);

            RunOnUi(() =>
            {
                foreach (var playlist in playlists)
                {
                    playlist.IsPinned = _pinnedItemsService.IsPlaylistPinned(playlist.Uid, playlist.Kind);
                    playlist.IsLiked = _musicService.IsPlaylistLiked(playlist.Uid, playlist.Kind);
                    if (playlist.IsPinned)
                    {
                        _pinnedItemsService.PinPlaylist(playlist.Uid, playlist.Kind, playlist.Title, playlist.CoverUrl);
                    }
                }
                var sortedPlaylists = playlists
                    .OrderByDescending(p => p.IsPinned)
                    .ThenBy(p => _pinnedItemsService.GetPinOrder(p.PinKey))
                    .ToList();

                Playlists.Clear();
                foreach (var playlist in sortedPlaylists)
                {
                    Playlists.Add(playlist);
                }

                foreach (var album in albums)
                {
                    album.IsPinned = _pinnedItemsService.IsAlbumPinned(album.Id);
                    album.IsLiked = true;
                    if (album.IsPinned)
                    {
                        _pinnedItemsService.PinAlbum(album.Id, album.Title, album.CoverUrl);
                    }
                }
                var sortedAlbums = albums
                    .OrderByDescending(a => a.IsPinned)
                    .ThenBy(a => _pinnedItemsService.GetPinOrder(a.PinKey))
                    .ThenByDescending(a => a.IsPreSave)
                    .ToList();

                LikedAlbums.Clear();
                foreach (var album in sortedAlbums)
                {
                    LikedAlbums.Add(album);
                }
            });
        }
        catch (Exception ex)
        {
            SetError($"Failed to load library: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    [RelayCommand]
    public async Task SelectPlaylistAsync(Playlist? playlist)
    {
        if (playlist != null)
        {
            playlist.IsPinned = _pinnedItemsService.IsPlaylistPinned(playlist.Uid, playlist.Kind);
        }

        RunOnUi(() =>
        {
            SelectedPlaylist = playlist;
            OnPropertyChanged(nameof(IsDetailPinned));

            _allSelectedTracks.Clear();
            SelectedPlaylistTracks.Clear();
            FormattedDuration = string.Empty;
            SearchFilter = string.Empty;

            var existingTracks = playlist?.ResolvedTracks ?? playlist?.Tracks?.Select(ti => ti.Track).OfType<Track>().ToList();
            if (existingTracks != null && existingTracks.Count > 0)
            {
                foreach (var t in existingTracks)
                {
                    _allSelectedTracks.Add(t);
                }
                ApplyTrackFilter();
            }
        });

        if (playlist == null)
        {
            return;
        }

        SetBusy(true);
        ClearError();

        try
        {
            await _musicService.EnsureLikedTracksLoadedAsync().ConfigureAwait(false);
            await _musicService.EnsureLikedPlaylistsLoadedAsync().ConfigureAwait(false);

            long effectiveUid = playlist.Uid > 0 ? playlist.Uid : (playlist.Owner?.Uid ?? 0);
            if (effectiveUid <= 0)
            {
                var myUserIdStr = _credentialStorage.GetUserId();
                if (long.TryParse(myUserIdStr, out var parsed))
                {
                    effectiveUid = parsed;
                }
            }
            if (playlist.Uid <= 0 && effectiveUid > 0)
            {
                playlist.Uid = effectiveUid;
            }

            var fullPlaylist = await _musicService.GetPlaylistWithTracksAsync(effectiveUid, playlist.Kind).ConfigureAwait(false);
            if (fullPlaylist != null)
            {
                if (fullPlaylist.Uid <= 0 && effectiveUid > 0)
                {
                    fullPlaylist.Uid = effectiveUid;
                }
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
                if (SelectedPlaylist?.Uid != playlist.Uid || SelectedPlaylist?.Kind != playlist.Kind) return;

                if (fullPlaylist != null)
                {
                    SelectedPlaylist = fullPlaylist;
                    FormattedTrackCount = fullPlaylist.FormattedTrackCount;
                    OnPropertyChanged(nameof(IsDetailPinned));
                    OnPropertyChanged(nameof(IsDetailLiked));
                }
                _allSelectedTracks.Clear();
                foreach (var t in tracks)
                {
                    _allSelectedTracks.Add(t);
                }
                SearchFilter = string.Empty;
                ApplyTrackFilter();
            });
        }
        catch (Exception ex)
        {
            SetError($"Failed to load playlist tracks: {ex.Message}");
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

        if (IsDetailOpen && SelectedPlaylist != null && SelectedPlaylist.Uid == playlist.Uid && SelectedPlaylist.Kind == playlist.Kind)
        {
            return;
        }

        playlist.IsPinned = _pinnedItemsService.IsPlaylistPinned(playlist.Uid, playlist.Kind);
        playlist.IsLiked = _musicService.IsPlaylistLiked(playlist.Uid, playlist.Kind);
        SelectedPlaylist = playlist;
        SelectedAlbum = null;
        IsDetailOpen = true;
        FormattedTrackCount = playlist.FormattedTrackCount;
        OnPropertyChanged(nameof(IsDetailPinned));
        OnPropertyChanged(nameof(IsDetailLiked));
        OnPropertyChanged(nameof(DetailTitle));
        OnPropertyChanged(nameof(DetailSubtitle));
        OnPropertyChanged(nameof(DetailCoverUrl));
        OnPropertyChanged(nameof(DetailDescription));
        OnPropertyChanged(nameof(DetailTypeBadge));
        OnPropertyChanged(nameof(DetailPlayButtonText));
        OnPropertyChanged(nameof(DetailPlayButtonGlyph));

        await SelectPlaylistAsync(playlist);

        long totalMs = SelectedPlaylistTracks.Sum(t => t.DurationMs);
        var totalSpan = TimeSpan.FromMilliseconds(totalMs);
        FormattedDuration = PluralizationHelper.FormatDuration(totalSpan);
        OnPropertyChanged(nameof(FormattedTrackCount));
        OnPropertyChanged(nameof(FormattedDuration));
        OnPropertyChanged(nameof(HasFormattedDuration));
    }

    [RelayCommand]
    public async Task OpenAlbumAsync(Album? album)
    {
        if (album == null) return;

        if (IsDetailOpen && SelectedAlbum != null && SelectedAlbum.Id == album.Id)
        {
            return;
        }

        album.IsPinned = _pinnedItemsService.IsAlbumPinned(album.Id);
        album.IsLiked = _musicService.IsAlbumLiked(album.Id) || _musicService.IsAlbumPresaved(album.Id);
        SelectedAlbum = album;
        SelectedPlaylist = null;
        IsDetailOpen = true;
        FormattedTrackCount = album.FormattedTrackCount;
        OnPropertyChanged(nameof(IsDetailPinned));
        OnPropertyChanged(nameof(IsDetailLiked));
        OnPropertyChanged(nameof(DetailTitle));
        OnPropertyChanged(nameof(DetailSubtitle));
        OnPropertyChanged(nameof(DetailCoverUrl));
        OnPropertyChanged(nameof(DetailDescription));
        OnPropertyChanged(nameof(DetailTypeBadge));
        OnPropertyChanged(nameof(DetailPlayButtonText));
        OnPropertyChanged(nameof(DetailPlayButtonGlyph));
        OnPropertyChanged(nameof(IsDetailPreSave));
        OnPropertyChanged(nameof(IsPreSaveEmptyState));

        _allSelectedTracks.Clear();
        SelectedPlaylistTracks.Clear();
        FormattedDuration = string.Empty;
        SearchFilter = string.Empty;

        if (album.AllTracks != null && album.AllTracks.Count > 0)
        {
            foreach (var t in album.AllTracks)
            {
                _allSelectedTracks.Add(t);
            }
            ApplyTrackFilter();
        }

        SetBusy(true);
        ClearError();

        try
        {
            await _musicService.EnsureLikedTracksLoadedAsync().ConfigureAwait(false);
            await _musicService.EnsureLikedAlbumsLoadedAsync().ConfigureAwait(false);
            var fullAlbum = await _musicService.GetAlbumWithTracksAsync(album.Id).ConfigureAwait(false);
            if (fullAlbum == null || fullAlbum.IsNotFound || string.IsNullOrWhiteSpace(fullAlbum.Title))
            {
                fullAlbum = await _musicService.GetAlbumAsync(album.Id).ConfigureAwait(false);
            }

            if (fullAlbum != null && !fullAlbum.IsNotFound && !string.IsNullOrWhiteSpace(fullAlbum.Title))
            {
                if (album.IsPreorder == true) fullAlbum.IsPreorder = true;
                if (album.Preorder == true) fullAlbum.Preorder = true;
                if (album.AvailablePartially == true) fullAlbum.AvailablePartially = true;

                if (string.IsNullOrWhiteSpace(fullAlbum.CoverUri) && !string.IsNullOrWhiteSpace(album.CoverUri))
                    fullAlbum.CoverUri = album.CoverUri;
                if ((fullAlbum.Artists == null || fullAlbum.Artists.Count == 0) && album.Artists != null && album.Artists.Count > 0)
                    fullAlbum.Artists = new List<Artist>(album.Artists);
                if (string.IsNullOrWhiteSpace(fullAlbum.ReleaseDate) && !string.IsNullOrWhiteSpace(album.ReleaseDate))
                    fullAlbum.ReleaseDate = album.ReleaseDate;
            }
            else
            {
                fullAlbum = album;
            }

            // Fallback: If title or cover is still missing, try presaves cache/API
            if (string.IsNullOrWhiteSpace(fullAlbum.Title) || string.IsNullOrWhiteSpace(fullAlbum.CoverUri) || (fullAlbum.Artists == null || fullAlbum.Artists.Count == 0))
            {
                try
                {
                    var presaves = await _musicService.GetPresavesAsync().ConfigureAwait(false);
                    var presavedAlbum = presaves?.UpcomingAlbums?.FirstOrDefault(a => a.Id == album.Id);
                    if (presavedAlbum != null)
                    {
                        if (string.IsNullOrWhiteSpace(fullAlbum.Title)) fullAlbum.Title = presavedAlbum.Title;
                        if (string.IsNullOrWhiteSpace(fullAlbum.CoverUri)) fullAlbum.CoverUri = presavedAlbum.CoverUri;
                        if ((fullAlbum.Artists == null || fullAlbum.Artists.Count == 0) && presavedAlbum.Artists != null && presavedAlbum.Artists.Count > 0)
                            fullAlbum.Artists = new List<Artist>(presavedAlbum.Artists);
                        if (string.IsNullOrWhiteSpace(fullAlbum.ReleaseDate)) fullAlbum.ReleaseDate = presavedAlbum.ReleaseDate;
                        fullAlbum.IsPreorder = true;
                    }
                }
                catch { }
            }

            var tracks = fullAlbum?.AllTracks != null ? new List<Track>(fullAlbum.AllTracks) : new List<Track>();

            // Generate placeholders if tracks are not yet published for this upcoming album
            if (fullAlbum != null && fullAlbum.IsPreSave && tracks.Count == 0 && fullAlbum.TrackCount > 0)
            {
                bool isEn = Loc?.CurrentLanguage == "en";
                for (int i = 1; i <= fullAlbum.TrackCount; i++)
                {
                    tracks.Add(new Track
                    {
                        Id = $"placeholder:{fullAlbum.Id}:{i}",
                        Title = isEn ? $"Track {i}" : $"Трек {i}",
                        Available = false,
                        DurationMs = 0,
                        Albums = new List<Album> { fullAlbum },
                        Artists = fullAlbum.Artists != null ? new List<Artist>(fullAlbum.Artists) : new List<Artist>()
                    });
                }
            }

            // FckCensor: Restore censored/deleted album tracks if present in album spoofs
            var fckCensor = App.GetService<YMusic.WinUI.Services.Interfaces.IFckCensorService>();
            var albumSpoof = fckCensor?.GetAlbumSpoof(album.Id);
            if (albumSpoof != null && albumSpoof.Volumes.Count > 0)
            {
                var existingTrackIds = new HashSet<string>(tracks.Select(t => t.Id), StringComparer.OrdinalIgnoreCase);
                var missingIds = new List<string>();
                foreach (var vol in albumSpoof.Volumes)
                {
                    foreach (var tid in vol)
                    {
                        string sid = tid.ToString();
                        if (!existingTrackIds.Contains(sid))
                        {
                            missingIds.Add(sid);
                        }
                    }
                }

                if (missingIds.Count > 0)
                {
                    try
                    {
                        var fetchedTracks = await _musicService.GetTracksAsync(missingIds).ConfigureAwait(false);
                        var fetchedDict = fetchedTracks.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);

                        var restoredTracks = new List<Track>();
                        foreach (var vol in albumSpoof.Volumes)
                        {
                            foreach (var tid in vol)
                            {
                                string sid = tid.ToString();
                                var found = tracks.FirstOrDefault(t => t.Id == sid) ?? (fetchedDict.TryGetValue(sid, out var ft) ? ft : null);
                                if (found != null)
                                {
                                    restoredTracks.Add(found);
                                }
                            }
                        }
                        if (restoredTracks.Count > 0)
                        {
                            tracks = restoredTracks;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[FckCensor Album Restore Warning] {ex.Message}");
                    }
                }
            }

            string? albumCoverUri = fullAlbum?.CoverUri ?? album.CoverUri ?? albumSpoof?.CoverUri;
            var albumRef = fullAlbum ?? album;

            foreach (var t in tracks)
            {
                if (t.Albums == null || t.Albums.Count == 0)
                {
                    t.Albums = new List<Album> { albumRef };
                }
                else if (string.IsNullOrEmpty(t.Albums[0].CoverUri) && !string.IsNullOrEmpty(albumCoverUri))
                {
                    t.Albums[0].CoverUri = albumCoverUri;
                }

                if (string.IsNullOrEmpty(t.CoverUri) && !string.IsNullOrEmpty(albumCoverUri))
                {
                    t.CoverUri = albumCoverUri;
                }
            }

            RunOnUi(() =>
            {
                if (SelectedAlbum?.Id != album.Id) return;

                if (fullAlbum != null)
                {
                    fullAlbum.IsLiked = _musicService.IsAlbumLiked(fullAlbum.Id) || _musicService.IsAlbumPresaved(fullAlbum.Id);
                    fullAlbum.IsPinned = _pinnedItemsService.IsAlbumPinned(fullAlbum.Id);
                    SelectedAlbum = fullAlbum;
                    FormattedTrackCount = fullAlbum.FormattedTrackCount;
                    OnPropertyChanged(nameof(IsDetailPinned));
                    OnPropertyChanged(nameof(IsDetailLiked));
                }

                _allSelectedTracks.Clear();
                long totalMs = 0;
                foreach (var t in tracks)
                {
                    _allSelectedTracks.Add(t);
                    totalMs += t.DurationMs;
                }

                var totalSpan = TimeSpan.FromMilliseconds(totalMs);
                FormattedDuration = totalMs > 0 ? PluralizationHelper.FormatDuration(totalSpan) : string.Empty;

                SearchFilter = string.Empty;
                ApplyTrackFilter();

                OnPropertyChanged(nameof(DetailTitle));
                OnPropertyChanged(nameof(DetailSubtitle));
                OnPropertyChanged(nameof(DetailCoverUrl));
                OnPropertyChanged(nameof(DetailDescription));
                OnPropertyChanged(nameof(DetailTypeBadge));
                OnPropertyChanged(nameof(DetailPlayButtonText));
                OnPropertyChanged(nameof(DetailPlayButtonGlyph));
                OnPropertyChanged(nameof(IsDetailPreSave));
                OnPropertyChanged(nameof(IsDetailLikeButtonVisible));
                OnPropertyChanged(nameof(HasTracksToFilter));
                OnPropertyChanged(nameof(HasFormattedDuration));
                OnPropertyChanged(nameof(HasTrackCountOrDuration));
                OnPropertyChanged(nameof(IsPreSaveEmptyState));
            });
        }
        catch (Exception ex)
        {
            SetError($"Failed to load album tracks: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    [RelayCommand]
    public void CloseDetail()
    {
        string? returnTag = PreviousPageTag;

        IsDetailOpen = false;
        PreviousPageTag = null;
        IsBackButtonVisible = true;
        SelectedPlaylist = null;
        SelectedAlbum = null;
        SearchFilter = string.Empty;
        _allSelectedTracks.Clear();
        SelectedPlaylistTracks.Clear();

        if (!string.IsNullOrEmpty(returnTag))
        {
            App.GetService<YMusic.WinUI.Services.Interfaces.INavigationService>().NavigateToTag(returnTag);
        }
    }

    [RelayCommand]
    public async Task PlayPlaylistDirectAsync(Playlist? playlist)
    {
        if (playlist == null) return;
        await OpenPlaylistAsync(playlist);
        if (SelectedPlaylistTracks.Count > 0)
        {
            _audioPlayer.CurrentPlaybackMode = PlaybackMode.Playlist;
            await _audioPlayer.PlayQueueAsync(SelectedPlaylistTracks.ToList(), 0);
        }
    }

    [RelayCommand]
    public async Task PlayAlbumDirectAsync(Album? album)
    {
        if (album == null) return;
        if (album.IsPreSave && !album.HasPlayableTracks)
        {
            await ToggleAlbumLikeAsync(album).ConfigureAwait(false);
            return;
        }

        await OpenAlbumAsync(album);
        var playable = SelectedPlaylistTracks.Where(t => t.Available).ToList();
        if (playable.Count > 0)
        {
            _audioPlayer.CurrentPlaybackMode = PlaybackMode.Album;
            await _audioPlayer.PlayQueueAsync(playable, 0);
        }
    }

    [RelayCommand]
    public async Task PlayCurrentCollectionAsync()
    {
        if (SelectedPlaylist != null)
        {
            if (SelectedPlaylistTracks.Count == 0)
            {
                await SelectPlaylistAsync(SelectedPlaylist);
            }
            if (SelectedPlaylistTracks.Count > 0)
            {
                _audioPlayer.CurrentPlaybackMode = PlaybackMode.Playlist;
                await _audioPlayer.PlayQueueAsync(SelectedPlaylistTracks.ToList(), 0);
            }
        }
        else if (SelectedAlbum != null)
        {
            if (SelectedAlbum.IsPreSave && !SelectedAlbum.HasPlayableTracks)
            {
                await ToggleCurrentDetailLikeAsync().ConfigureAwait(false);
                return;
            }

            var playableTracks = SelectedPlaylistTracks.Where(t => t.Available).ToList();
            if (playableTracks.Count > 0)
            {
                _audioPlayer.CurrentPlaybackMode = PlaybackMode.Album;
                await _audioPlayer.PlayQueueAsync(playableTracks, 0);
            }
            else
            {
                var isEn = Loc?.CurrentLanguage == "en";
                App.GetService<PlayerBarViewModel>()?.ShowToast(isEn
                    ? "This album will be available on release day"
                    : "Этот альбом станет доступен в день выхода");
            }
        }
    }

    [RelayCommand]
    private async Task PlayPlaylistAsync(Playlist? playlist)
    {
        playlist ??= SelectedPlaylist;
        if (playlist == null) return;

        if (SelectedPlaylistTracks.Count == 0)
        {
            await SelectPlaylistAsync(playlist);
        }

        if (SelectedPlaylistTracks.Count > 0)
        {
            _audioPlayer.CurrentPlaybackMode = PlaybackMode.Playlist;
            await _audioPlayer.PlayQueueAsync(SelectedPlaylistTracks.ToList(), 0);
        }
    }

    [RelayCommand]
    private async Task PlayTrackAsync(Track? track)
    {
        if (track == null || SelectedPlaylistTracks.Count == 0)
        {
            return;
        }

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

        _audioPlayer.CurrentPlaybackMode = SelectedAlbum != null ? PlaybackMode.Album : PlaybackMode.Playlist;
        await _audioPlayer.PlayTrackAsync(track, SelectedPlaylistTracks.ToList());
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
    public void TogglePlaylistPin(Playlist? playlist)
    {
        if (playlist == null) return;
        bool isPinned = _pinnedItemsService.TogglePlaylistPin(playlist.Uid, playlist.Kind, playlist.Title, playlist.CoverUrl);
        playlist.IsPinned = isPinned;
        if (SelectedPlaylist != null && string.Equals(SelectedPlaylist.PinKey, playlist.PinKey, StringComparison.OrdinalIgnoreCase))
        {
            SelectedPlaylist.IsPinned = isPinned;
        }
        ReorderPlaylists();
        OnPropertyChanged(nameof(IsDetailPinned));
    }

    [RelayCommand]
    public void ToggleAlbumPin(Album? album)
    {
        if (album == null) return;
        bool isPinned = _pinnedItemsService.ToggleAlbumPin(album.Id, album.Title, album.CoverUrl);
        album.IsPinned = isPinned;
        if (SelectedAlbum != null && string.Equals(SelectedAlbum.PinKey, album.PinKey, StringComparison.OrdinalIgnoreCase))
        {
            SelectedAlbum.IsPinned = isPinned;
        }
        ReorderAlbums();
        OnPropertyChanged(nameof(IsDetailPinned));
    }

    [RelayCommand]
    public void ToggleCurrentDetailPin()
    {
        if (SelectedPlaylist != null)
        {
            TogglePlaylistPin(SelectedPlaylist);
        }
        else if (SelectedAlbum != null)
        {
            ToggleAlbumPin(SelectedAlbum);
        }
    }

    [RelayCommand]
    public async Task TogglePlaylistLikeAsync(Playlist? playlist)
    {
        if (playlist == null) return;
        bool willLike = !playlist.IsLiked;
        playlist.IsLiked = willLike;
        OnPropertyChanged(nameof(IsDetailLiked));

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
            System.Diagnostics.Debug.WriteLine($"[TogglePlaylistLike Error] {ex.Message}");
            playlist.IsLiked = !willLike;
            OnPropertyChanged(nameof(IsDetailLiked));
        }
    }

    [RelayCommand]
    public async Task ToggleAlbumLikeAsync(Album? album)
    {
        if (album == null) return;
        bool willLike = !album.IsLiked;
        album.IsLiked = willLike;
        OnPropertyChanged(nameof(IsDetailLiked));
        OnPropertyChanged(nameof(DetailPlayButtonText));
        OnPropertyChanged(nameof(DetailPlayButtonGlyph));

        try
        {
            var isEn = Loc?.CurrentLanguage == "en";
            if (album.IsPreSave)
            {
                if (willLike)
                {
                    await _musicService.AddAlbumPresaveAsync(album.Id).ConfigureAwait(false);
                    App.GetService<PlayerBarViewModel>()?.ShowToast(isEn
                        ? "Album pre-saved! It will be added to your library on release."
                        : "Альбом предсохранён! Он появится в вашей медиатеке в день релиза.");
                }
                else
                {
                    await _musicService.RemoveAlbumPresaveAsync(album.Id).ConfigureAwait(false);
                    App.GetService<PlayerBarViewModel>()?.ShowToast(isEn
                        ? "Pre-save removed"
                        : "Пресейв отменён");
                }
            }
            else
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
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ToggleAlbumLike Error] {ex.Message}");
            album.IsLiked = !willLike;
            OnPropertyChanged(nameof(IsDetailLiked));
            OnPropertyChanged(nameof(DetailPlayButtonText));
            OnPropertyChanged(nameof(DetailPlayButtonGlyph));
        }
    }

    [RelayCommand]
    public async Task ToggleCurrentDetailLikeAsync()
    {
        if (SelectedPlaylist != null)
        {
            await TogglePlaylistLikeAsync(SelectedPlaylist).ConfigureAwait(false);
        }
        else if (SelectedAlbum != null)
        {
            await ToggleAlbumLikeAsync(SelectedAlbum).ConfigureAwait(false);
        }
    }

    private void OnAlbumLikeChanged(object? sender, (long AlbumId, bool IsLiked) e)
    {
        RunOnUi(() =>
        {
            if (SelectedAlbum?.Id == e.AlbumId)
            {
                SelectedAlbum.IsLiked = e.IsLiked;
                OnPropertyChanged(nameof(IsDetailLiked));
                OnPropertyChanged(nameof(DetailPlayButtonText));
                OnPropertyChanged(nameof(DetailPlayButtonGlyph));
            }
            var inList = LikedAlbums.FirstOrDefault(a => a.Id == e.AlbumId);
            if (inList != null)
            {
                inList.IsLiked = e.IsLiked;
                if (!e.IsLiked)
                {
                    LikedAlbums.Remove(inList);
                }
            }
            else if (e.IsLiked && SelectedAlbum?.Id == e.AlbumId)
            {
                LikedAlbums.Insert(0, SelectedAlbum);
            }
        });
    }

    private void OnPlaylistLikeChanged(object? sender, (long OwnerUid, int Kind, bool IsLiked) e)
    {
        RunOnUi(() =>
        {
            if (SelectedPlaylist != null && SelectedPlaylist.Uid == e.OwnerUid && SelectedPlaylist.Kind == e.Kind)
            {
                SelectedPlaylist.IsLiked = e.IsLiked;
                OnPropertyChanged(nameof(IsDetailLiked));
            }
            var inList = Playlists.FirstOrDefault(p => p.Uid == e.OwnerUid && p.Kind == e.Kind);
            if (inList != null)
            {
                inList.IsLiked = e.IsLiked;
            }
            else if (e.IsLiked && SelectedPlaylist != null && SelectedPlaylist.Uid == e.OwnerUid && SelectedPlaylist.Kind == e.Kind)
            {
                Playlists.Insert(0, SelectedPlaylist);
            }
        });
    }

    private void OnPinStateChanged(object? sender, string key)
    {
        RunOnUi(() =>
        {
            if (SelectedPlaylist != null)
            {
                SelectedPlaylist.IsPinned = _pinnedItemsService.IsPlaylistPinned(SelectedPlaylist.Uid, SelectedPlaylist.Kind);
            }
            if (SelectedAlbum != null)
            {
                SelectedAlbum.IsPinned = _pinnedItemsService.IsAlbumPinned(SelectedAlbum.Id);
            }

            foreach (var p in Playlists)
            {
                if (string.Equals(p.PinKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    p.IsPinned = _pinnedItemsService.IsPinned(key);
                    break;
                }
            }
            foreach (var a in LikedAlbums)
            {
                if (string.Equals(a.PinKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    a.IsPinned = _pinnedItemsService.IsPinned(key);
                    break;
                }
            }
            ReorderPlaylists();
            ReorderAlbums();
            OnPropertyChanged(nameof(IsDetailPinned));
        });
    }

    private void ReorderPlaylists()
    {
        var ordered = Playlists
            .OrderByDescending(p => p.IsPinned)
            .ThenBy(p => _pinnedItemsService.GetPinOrder(p.PinKey))
            .ToList();

        for (int i = 0; i < ordered.Count; i++)
        {
            int oldIndex = Playlists.IndexOf(ordered[i]);
            if (oldIndex >= 0 && oldIndex != i)
            {
                Playlists.Move(oldIndex, i);
            }
        }
    }

    private void ReorderAlbums()
    {
        var ordered = LikedAlbums
            .OrderByDescending(a => a.IsPinned)
            .ThenBy(a => _pinnedItemsService.GetPinOrder(a.PinKey))
            .ToList();

        for (int i = 0; i < ordered.Count; i++)
        {
            int oldIndex = LikedAlbums.IndexOf(ordered[i]);
            if (oldIndex >= 0 && oldIndex != i)
            {
                LikedAlbums.Move(oldIndex, i);
            }
        }
    }
}
