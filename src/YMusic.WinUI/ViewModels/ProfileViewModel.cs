#nullable enable

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;
using YMusic.WinUI.Views;

namespace YMusic.WinUI.ViewModels;

public sealed class ProfileCacheData
{
    public UserProfile? User { get; set; }
    public int LikedTracksCount { get; set; }
    public int PlaylistsCount { get; set; }
    public int PinnedItemsCount { get; set; }
}

public sealed partial class ProfileViewModel : BaseViewModel
{
    private readonly IYandexMusicService _musicService;
    private readonly ICredentialStorageService _credentialStorage;
    private readonly ISettingsService _settingsService;
    private readonly IAudioPlayerService _audioPlayer;
    private readonly IEqualizerService _equalizerService;
    private readonly IPinnedItemsService _pinnedItemsService;
    private readonly INavigationService _navigationService;
    private readonly LoginViewModel _loginViewModel;
    private readonly SearchViewModel _searchViewModel;
    private readonly IAudioCacheService _audioCacheService;
    private readonly IUpdateService _updateService;
    private readonly IGameOverlayService _gameOverlayService;
    private readonly IThemeService _themeService;

    private readonly string _appDataDir;
    private readonly string _profileCachePath;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private bool _hasCachedProfile;
    private bool _isInitializingSettings;

    [ObservableProperty]
    private bool _isCheckingUpdates;

    [ObservableProperty]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    private bool _isDownloadingUpdate;

    [ObservableProperty]
    private double _updateDownloadProgress;

    [ObservableProperty]
    private string _updateProgressText = string.Empty;

    [ObservableProperty]
    private string _updateStatusText = string.Empty;

    [ObservableProperty]
    private string _newVersionTag = string.Empty;

    [ObservableProperty]
    private string _updateReleaseNotes = string.Empty;

    [ObservableProperty]
    private UpdateInfo? _availableUpdate;

    public bool CanCheckUpdates => !IsCheckingUpdates && !IsDownloadingUpdate;
    public bool CanDownloadUpdate => !IsDownloadingUpdate;

    partial void OnIsCheckingUpdatesChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCheckUpdates));
    }

    partial void OnIsDownloadingUpdateChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCheckUpdates));
        OnPropertyChanged(nameof(CanDownloadUpdate));
    }

    [ObservableProperty]
    private UserProfile? _user;

    [ObservableProperty]
    private bool _isLoggedIn;

    [ObservableProperty]
    private int _likedTracksCount;

    [ObservableProperty]
    private int _playlistsCount;

    [ObservableProperty]
    private int _pinnedItemsCount;

    [ObservableProperty]
    private string _cacheSizeText = "0 KB";

    [ObservableProperty]
    private string _cachedTracksCountText = "0 tracks cached";

    [ObservableProperty]
    private bool _isAudioCacheEnabled = true;

    [ObservableProperty]
    private int _selectedCacheLimitIndex = 1;

    [ObservableProperty]
    private string _appDataPath = string.Empty;

    [ObservableProperty]
    private string _appVersionText = string.Empty;

    [ObservableProperty]
    private int _selectedQualityIndex;

    [ObservableProperty]
    private bool _isGaplessPreloadEnabled;

    [ObservableProperty]
    private bool _isEqualizerEnabled;

    [ObservableProperty]
    private ObservableCollection<string> _availablePresets = new();

    [ObservableProperty]
    private string _selectedEqualizerPreset = "Flat";

    [ObservableProperty]
    private bool _isDiscordRpcEnabled;

    [ObservableProperty]
    private bool _isObsOverlayEnabled;

    [ObservableProperty]
    private bool _isAutoplayWaveEnabled;

    [ObservableProperty]
    private bool _isAutoBypassProxyEnabled;

    [ObservableProperty]
    private bool _isCrossfadeEnabled;

    [ObservableProperty]
    private int _crossfadeDurationSeconds;

    [ObservableProperty]
    private bool _isGlobalHotkeysEnabled;

    [ObservableProperty]
    private bool _isGameOverlayEnabled;

    [ObservableProperty]
    private bool _isShowOverlayOnTrackChangeEnabled;

    [ObservableProperty]
    private int _gameOverlayDurationSeconds;

    [ObservableProperty]
    private int _selectedGameOverlayPositionIndex;

    [ObservableProperty]
    private bool _isMinimizeToTrayEnabled;

    [ObservableProperty]
    private bool _isTrackNotificationsEnabled;

    [ObservableProperty]
    private bool _isFckCensorEnabled;

    [ObservableProperty]
    private bool _isSloplessEnabled;

    [ObservableProperty]
    private int _sloplessBehaviorIndex;

    [ObservableProperty]
    private int _sloplessDetectionModeIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SloplessArtistThresholdText))]
    private double _sloplessArtistThreshold;

    public string SloplessArtistThresholdText => $"{Math.Round(SloplessArtistThreshold):0}%";

    [ObservableProperty]
    private bool _isSloplessShowTrackLabelsEnabled;

    [ObservableProperty]
    private bool _shouldScrollToUpdates;

    [ObservableProperty]
    private int _selectedThemeIndex;

    [ObservableProperty]
    private int _selectedBackdropIndex;

    [ObservableProperty]
    private string _currentAccentHex = string.Empty;

    [ObservableProperty]
    private string _customHexInput = string.Empty;

    [ObservableProperty]
    private int _selectedFontIndex;

    [ObservableProperty]
    private int _selectedLanguageIndex;

    [ObservableProperty]
    private int _selectedCategoryIndex = 1;

    partial void OnSelectedCategoryIndexChanged(int value)
    {
        OnPropertyChanged(nameof(ShowProfileSection));
        OnPropertyChanged(nameof(ShowAudioSection));
        OnPropertyChanged(nameof(ShowAppearanceSection));
        OnPropertyChanged(nameof(ShowIntegrationsSection));
        OnPropertyChanged(nameof(ShowSystemSection));
        OnPropertyChanged(nameof(ShowStorageSection));
        OnPropertyChanged(nameof(ShowAboutSection));
    }

    public bool ShowProfileSection => SelectedCategoryIndex == 0 || SelectedCategoryIndex == 1;
    public bool ShowAudioSection => SelectedCategoryIndex == 0 || SelectedCategoryIndex == 2;
    public bool ShowAppearanceSection => SelectedCategoryIndex == 0 || SelectedCategoryIndex == 3;
    public bool ShowIntegrationsSection => SelectedCategoryIndex == 0 || SelectedCategoryIndex == 4;
    public bool ShowSystemSection => SelectedCategoryIndex == 0 || SelectedCategoryIndex == 5;
    public bool ShowStorageSection => SelectedCategoryIndex == 0 || SelectedCategoryIndex == 6;
    public bool ShowAboutSection => SelectedCategoryIndex == 0 || SelectedCategoryIndex == 7;

    [ObservableProperty]
    private bool _isShowWaveInNavEnabled = true;

    [ObservableProperty]
    private bool _isShowLikesInNavEnabled = true;

    [ObservableProperty]
    private bool _isShowPlaylistsInNavEnabled = true;

    [ObservableProperty]
    private bool _isShowSearchInNavEnabled = true;

    [ObservableProperty]
    private bool _isShowLyricsButtonEnabled = true;

    [ObservableProperty]
    private bool _isShowEqualizerButtonEnabled = true;

    [ObservableProperty]
    private bool _isShowQualityBadgeEnabled = true;

    [ObservableProperty]
    private bool _isCompactPlayerVolumeEnabled = false;

    [ObservableProperty]
    private bool _isShowPersonalPlaylistsOnHomeEnabled = true;

    [ObservableProperty]
    private bool _isShowNewReleasesOnHomeEnabled = true;

    [ObservableProperty]
    private bool _isShowEditorialPlaylistsOnHomeEnabled = true;

    [ObservableProperty]
    private bool _isShowChartOnHomeEnabled = true;

    [ObservableProperty]
    private string? _notificationMessage;

    [ObservableProperty]
    private bool _hasNotification;

    public ObservableCollection<string> QualityOptions { get; } = new();
    public ObservableCollection<string> GameOverlayPositionOptions { get; } = new();
    public ObservableCollection<string> ThemeOptions { get; } = new();
    public ObservableCollection<string> BackdropOptions { get; } = new();
    public ObservableCollection<string> FontOptions { get; } = new();
    public ObservableCollection<string> CacheLimitOptions { get; } = new();
    public ObservableCollection<string> SloplessBehaviorOptions { get; } = new();
    public ObservableCollection<string> SloplessDetectionModeOptions { get; } = new();

    public IReadOnlyList<AccentPreset> AvailableAccents => _themeService.AvailableAccents;
    public IReadOnlyList<ThemePreset> AvailableThemes => _themeService.AvailableThemes;

    private bool IsRussian => Loc?.CurrentLanguage != "en";

    public string TitleText => IsRussian ? "Профиль и настройки" : "Profile & Settings";
    public string SubtitleText => IsRussian ? "Управление аккаунтом Яндекса, воспроизведением и настройками приложения" : "Manage your Yandex account, playback preferences, and application options";
    public string NotificationTitle => IsRussian ? "Настройки" : "Settings";
    public string CopyUserIdTooltip => IsRussian ? "Скопировать User ID" : "Copy User ID";
    public string RefreshButtonText => IsRussian ? "Обновить" : "Refresh";
    public string SignOutButtonText => IsRussian ? "Выйти" : "Sign Out";
    public string LikedTracksStatText => IsRussian ? "Любимые треки" : "Liked Tracks";
    public string PlaylistsStatText => IsRussian ? "Плейлисты" : "Playlists";
    public string PinnedItemsStatText => IsRussian ? "Закрепленные" : "Pinned Items";

    public string CategoryAllText => IsRussian ? "Все разделы" : "All Sections";
    public string CategoryProfileText => IsRussian ? "Профиль" : "Profile";
    public string CategoryAudioText => IsRussian ? "Звук" : "Audio";
    public string CategoryAppearanceText => IsRussian ? "Оформление" : "Appearance";
    public string CategoryIntegrationsText => IsRussian ? "Интеграции" : "Integrations";
    public string CategorySystemText => IsRussian ? "Система" : "System";
    public string CategoryStorageText => IsRussian ? "Хранилище" : "Storage";
    public string CategoryAboutText => IsRussian ? "О программе" : "About";

    public string IntegrationsSectionTitle => IsRussian ? "Интеграции" : "Integrations";
    public string ModularUiTitle => IsRussian ? "Модульный интерфейс" : "Modular Interface";
    public string ModularUiDesc => IsRussian ? "Настройка видимости элементов боковой панели и плеера" : "Customize visibility of navigation and player elements";
    public string NavSectionTitle => IsRussian ? "Боковая панель (Навигация)" : "Sidebar Navigation";
    public string ShowWaveTitle => IsRussian ? "Раздел «Моя волна»" : "«My Wave» Section";
    public string ShowWaveDesc => IsRussian ? "Отображать быстрый переход к Моей волне в боковом меню" : "Show My Wave in navigation sidebar";
    public string ShowLikesTitle => IsRussian ? "Раздел «Любимые треки»" : "«Liked Tracks» Section";
    public string ShowLikesDesc => IsRussian ? "Отображать любимые треки в боковом меню" : "Show Liked Tracks in navigation sidebar";
    public string ShowPlaylistsTitle => IsRussian ? "Раздел «Плейлисты и альбомы»" : "«Playlists & Albums» Section";
    public string ShowPlaylistsDesc => IsRussian ? "Отображать библиотеку плейлистов в боковом меню" : "Show Playlists & Albums in navigation sidebar";
    public string ShowSearchTitle => IsRussian ? "Поиск в боковой панели" : "Sidebar Search";
    public string ShowSearchDesc => IsRussian ? "Отображать строку поиска и пункт в боковом меню" : "Show search box and item in sidebar";

    public string PlayerModularTitle => IsRussian ? "Нижний плеер" : "Bottom Player Bar";
    public string ShowLyricsTitle => IsRussian ? "Кнопка текста песен и караоке" : "Lyrics & Karaoke Button";
    public string ShowLyricsDesc => IsRussian ? "Показывать кнопку открытия текста песни на панели плеера" : "Show button to open fullscreen lyrics";
    public string ShowEqualizerTitle => IsRussian ? "Кнопка эквалайзера" : "Equalizer Button";
    public string ShowEqualizerDesc => IsRussian ? "Быстрый доступ к 10-полосному эквалайзеру на панели плеера" : "Quick access to 10-band equalizer on player bar";
    public string ShowQualityBadgeTitle => IsRussian ? "Индикатор качества звука" : "Audio Quality Indicator";
    public string ShowQualityDesc => IsRussian ? "Показывать бейдж качества аудио (HQ / битрейт)" : "Show audio bitrate and format badge";
    public string CompactVolumeTitle => IsRussian ? "Компактный регулятор громкости" : "Compact Volume Slider";
    public string CompactVolumeDesc => IsRussian ? "Уменьшить ширину слайдера громкости для экономии места" : "Narrower volume slider to save space";

    public string HomeModularTitle => IsRussian ? "Главная страница («Главное»)" : "Home Page («Explore»)";
    public string ShowPersonalPlaylistsTitle => IsRussian ? "Раздел «Собрано для вас»" : "«Made For You» Section";
    public string ShowPersonalPlaylistsDesc => IsRussian ? "Персональные плейлисты дня и умные миксы" : "Daily personal playlists and smart mixes";
    public string ShowNewReleasesTitle => IsRussian ? "Раздел «Новые релизы»" : "«New Releases» Section";
    public string ShowNewReleasesDesc => IsRussian ? "Свежие альбомы и синглы артистов" : "Fresh albums and singles from artists";
    public string ShowEditorialPlaylistsTitle => IsRussian ? "Раздел «Популярные плейлисты»" : "«Curated Playlists» Section";
    public string ShowEditorialPlaylistsDesc => IsRussian ? "Тематические коллекции и жанровые подборки" : "Themed collections and genre selections";
    public string ShowChartTitle => IsRussian ? "Раздел «Чарт треков»" : "«Top Chart» Section";
    public string ShowChartDesc => IsRussian ? "Топ-100 самых прослушиваемых треков" : "Top 100 most played tracks";

    [RelayCommand]
    public void SelectCategory(string categoryIndexStr)
    {
        if (int.TryParse(categoryIndexStr, out int idx))
        {
            SelectedCategoryIndex = idx;
        }
    }

    public string SignedOutTitleText => IsRussian ? "Вы не авторизованы" : "You are currently signed out";
    public string SignedOutDescText => IsRussian ? "Войдите с Яндекс ID для синхронизации вашей коллекции, Моей волны, лайков и плейлистов." : "Sign in with your Yandex ID to sync your collection, My Wave recommendations, liked tracks and playlists.";
    public string SignInButtonText => IsRussian ? "Войти с Яндекс ID" : "Sign In with Yandex ID";

    public string AudioSectionTitle => IsRussian ? "Аудио и воспроизведение" : "Audio & Playback";
    public string StreamQualityTitle => IsRussian ? "Предпочитаемое качество" : "Default Streaming Quality";
    public string StreamQualityDesc => IsRussian ? "Кодек и битрейт для потокового воспроизведения треков" : "Preferred audio codec and bitrate for streaming tracks";

    public string GaplessTitle => IsRussian ? "Бесшовное воспроизведение и предзагрузка" : "Gapless Playback & Background Preloading";
    public string GaplessDesc => IsRussian ? "Предзагрузка следующего трека и текста на 80% для устранения пауз и буферизации" : "Preloads the next track stream and lyrics at 80% progress to eliminate buffering pauses";

    public string EqualizerTitle => IsRussian ? "Аудио эквалайзер" : "Audio Equalizer";
    public string EqualizerDesc => IsRussian ? "10-полосный эквалайзер частот звука и акустические профили" : "10-band audio frequency equalizer and acoustic profiles";
    public string EqualizerResetText => IsRussian ? "Сброс" : "Reset";

    public string SystemSectionTitle => IsRussian ? "Система и интеграции" : "System & Integrations";
    public string DiscordRpcTitle => "Discord Rich Presence";
    public string DiscordRpcDesc => IsRussian ? "Отображение названия трека, исполнителя и обложки в профиле Discord" : "Show current playing track title, artist name, and album artwork on your Discord profile";

    public string ObsOverlayTitle => IsRussian ? "OBS Studio Виджет" : "OBS Studio Overlay";
    public string ObsOverlayDesc => IsRussian ? "Встроенный локальный сервер для источника «Браузер» в OBS. Красивые карточки треков с анимациями переходов." : "Embedded local web server for OBS Browser Source with animated track transitions.";
    public string ObsWidgetUrlText => $"http://localhost:{_settingsService.ObsOverlayPort}/widget";
    public string CopyObsUrlButtonText => IsRussian ? "Скопировать ссылку" : "Copy URL";
    public string OpenObsDashboardButtonText => IsRussian ? "Панель управления" : "Open Dashboard";

    public string AutoplayWaveTitle => IsRussian ? "Моя волна после окончания альбома" : "Autoplay Wave on Album End";
    public string AutoplayWaveDesc => IsRussian ? "Автоматически запускать радио/волну по альбому после завершения последнего трека" : "Automatically transition to Wave based on the album when the last track finishes";

    public string AutoBypassProxyTitle => IsRussian ? "Обход системного VPN/прокси для Яндекса" : "Bypass System Proxy for Yandex";
    public string AutoBypassProxyDesc => IsRussian ? "Направлять запросы к сервисам Яндекса напрямую, предотвращая сбои воспроизведения при включенном VPN или прокси" : "Route Yandex API and audio stream requests directly, preventing playback issues when system VPN or proxy is active";

    public string CrossfadeTitle => IsRussian ? "Плавный кроссфейд между треками" : "Smart Crossfade Between Tracks";
    public string CrossfadeDesc => IsRussian ? "Плавное сведение треков: текущий плавно затухает, а следующий нарастает без паузы и тишины" : "Smoothly blends tracks: fades out the ending track while fading in the next track without silence";
    public string CrossfadeDurationText => IsRussian ? $"Длительность сведения: {CrossfadeDurationSeconds} сек" : $"Transition duration: {CrossfadeDurationSeconds} s";

    public string GlobalHotkeysTitle => IsRussian ? "Глобальные хоткеи (Ctrl+Alt+...)" : "Global System Hotkeys (Ctrl+Alt+...)";
    public string GlobalHotkeysDesc => IsRussian
        ? "Управление плеером из любых игр и окон: Пробел (Пауза/Плей), Вправо/Влево (Треки), L (Лайк), Вверх/Вниз (Громкость), O (HUD-оверлей)"
        : "Control player from any game or app: Space (Play/Pause), Right/Left (Tracks), L (Like), Up/Down (Volume), O (HUD Overlay)";

    public string GameOverlayTitle => IsRussian ? "Игровой HUD-оверлей поверх игр" : "In-Game HUD Overlay";
    public string GameOverlayDesc => IsRussian
        ? "Компактная карточка трека поверх игр: не перехватывает фокус мыши и клавиатуры, не сворачивает полноэкранные игры"
        : "Compact semi-transparent track HUD over games: never steals focus or mouse clicks from fullscreen games";

    public string ShowOverlayOnTrackChangeTitle => IsRussian ? "Показывать оверлей при автосмене трека" : "Show HUD on track change";
    public string ShowOverlayOnTrackChangeDesc => IsRussian
        ? "Кратковременно показывать карточку трека в углу экрана, когда начинается новая композиция"
        : "Briefly pop up the track card in the corner of your screen when a new song starts playing";

    public string GameOverlayDurationText => IsRussian
        ? $"Длительность показа: {GameOverlayDurationSeconds} сек"
        : $"Display duration: {GameOverlayDurationSeconds} s";

    public string GameOverlayPositionTitle => IsRussian ? "Позиция оверлея на экране" : "Overlay Screen Position";
    public string TestGameOverlayButtonText => IsRussian ? "Проверить оверлей" : "Test HUD Overlay";

    public string SelectedOverlayPositionName => SelectedGameOverlayPositionIndex switch
    {
        1 => IsRussian ? "Сверху слева" : "Top Left",
        4 => IsRussian ? "Сверху по центру" : "Top Center",
        0 => IsRussian ? "Сверху справа" : "Top Right",
        6 => IsRussian ? "Слева по центру" : "Middle Left",
        7 => IsRussian ? "Справа по центру" : "Middle Right",
        3 => IsRussian ? "Снизу слева" : "Bottom Left",
        5 => IsRussian ? "Снизу по центру" : "Bottom Center",
        2 => IsRussian ? "Снизу справа" : "Bottom Right",
        _ => IsRussian ? "Сверху справа" : "Top Right"
    };

    public Microsoft.UI.Xaml.Media.Brush PosTopLeftBrush => GetPositionBrush(1);
    public Microsoft.UI.Xaml.Media.Brush PosTopCenterBrush => GetPositionBrush(4);
    public Microsoft.UI.Xaml.Media.Brush PosTopRightBrush => GetPositionBrush(0);
    public Microsoft.UI.Xaml.Media.Brush PosMiddleLeftBrush => GetPositionBrush(6);
    public Microsoft.UI.Xaml.Media.Brush PosMiddleRightBrush => GetPositionBrush(7);
    public Microsoft.UI.Xaml.Media.Brush PosBottomLeftBrush => GetPositionBrush(3);
    public Microsoft.UI.Xaml.Media.Brush PosBottomCenterBrush => GetPositionBrush(5);
    public Microsoft.UI.Xaml.Media.Brush PosBottomRightBrush => GetPositionBrush(2);

    public Microsoft.UI.Xaml.Media.Brush PosTopLeftForeground => GetPositionForeground(1);
    public Microsoft.UI.Xaml.Media.Brush PosTopCenterForeground => GetPositionForeground(4);
    public Microsoft.UI.Xaml.Media.Brush PosTopRightForeground => GetPositionForeground(0);
    public Microsoft.UI.Xaml.Media.Brush PosMiddleLeftForeground => GetPositionForeground(6);
    public Microsoft.UI.Xaml.Media.Brush PosMiddleRightForeground => GetPositionForeground(7);
    public Microsoft.UI.Xaml.Media.Brush PosBottomLeftForeground => GetPositionForeground(3);
    public Microsoft.UI.Xaml.Media.Brush PosBottomCenterForeground => GetPositionForeground(5);
    public Microsoft.UI.Xaml.Media.Brush PosBottomRightForeground => GetPositionForeground(2);

    private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush ActivePosBrush = new(Windows.UI.Color.FromArgb(255, 255, 211, 0));
    private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush ActivePosFg = new(Windows.UI.Color.FromArgb(255, 18, 18, 20));
    private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush InactivePosBrush = new(Windows.UI.Color.FromArgb(32, 255, 255, 255));
    private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush InactivePosFg = new(Windows.UI.Color.FromArgb(210, 255, 255, 255));

    private Microsoft.UI.Xaml.Media.Brush GetPositionBrush(int pos) =>
        SelectedGameOverlayPositionIndex == pos ? ActivePosBrush : InactivePosBrush;

    private Microsoft.UI.Xaml.Media.Brush GetPositionForeground(int pos) =>
        SelectedGameOverlayPositionIndex == pos ? ActivePosFg : InactivePosFg;

    [RelayCommand]
    public void SetOverlayPosition(object? param)
    {
        if (param != null && int.TryParse(param.ToString(), out int pos))
        {
            SelectedGameOverlayPositionIndex = pos;
        }
    }

    public void NotifyPositionProperties()
    {
        OnPropertyChanged(nameof(SelectedOverlayPositionName));
        OnPropertyChanged(nameof(PosTopLeftBrush));
        OnPropertyChanged(nameof(PosTopCenterBrush));
        OnPropertyChanged(nameof(PosTopRightBrush));
        OnPropertyChanged(nameof(PosMiddleLeftBrush));
        OnPropertyChanged(nameof(PosMiddleRightBrush));
        OnPropertyChanged(nameof(PosBottomLeftBrush));
        OnPropertyChanged(nameof(PosBottomCenterBrush));
        OnPropertyChanged(nameof(PosBottomRightBrush));
        OnPropertyChanged(nameof(PosTopLeftForeground));
        OnPropertyChanged(nameof(PosTopCenterForeground));
        OnPropertyChanged(nameof(PosTopRightForeground));
        OnPropertyChanged(nameof(PosMiddleLeftForeground));
        OnPropertyChanged(nameof(PosMiddleRightForeground));
        OnPropertyChanged(nameof(PosBottomLeftForeground));
        OnPropertyChanged(nameof(PosBottomCenterForeground));
        OnPropertyChanged(nameof(PosBottomRightForeground));
    }

    [RelayCommand]
    public void TestGameOverlay()
    {
        _gameOverlayService.ShowOverlay(IsRussian ? "ТЕСТ ОВЕРЛЕЯ" : "HUD OVERLAY TEST");
    }

    [RelayCommand]
    public void CopyObsUrl()
    {
        try
        {
            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(ObsWidgetUrlText);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
            ShowNotification(IsRussian ? "Ссылка для OBS скопирована в буфер обмена!" : "OBS Browser Source URL copied to clipboard!");
        }
        catch { }
    }

    [RelayCommand]
    public async Task OpenObsDashboardAsync()
    {
        try
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri($"http://localhost:{_settingsService.ObsOverlayPort}/"));
        }
        catch { }
    }

    public string FckCensorTitle => "FckCensor (разблокировка цензуры)";
    public string FckCensorDesc => IsRussian ? "Автоматически предпочитать оригинальные треки без цензуры вместо заглушенных версий" : "Automatically prefer original explicit and uncensored versions of tracks over sanitized edits";
    public string FckCensorAuthorCreditText => IsRussian ? "Оригинальный проект: Hazzz895 / FckCensor (GitHub)" : "Original project: Hazzz895 / FckCensor (GitHub)";

    [RelayCommand]
    public async Task OpenFckCensorRepoAsync()
    {
        try
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/Hazzz895/FckCensor"));
        }
        catch { }
    }

    public string SloplessTitle => "Slopless (фильтрация ИИ-музыки)";
    public string SloplessDesc => IsRussian
        ? "Автоматически помечает сгенерированную нейросетями музыку и пропускает её в Моей волне и плейлистах"
        : "Automatically detects AI-generated music and filters/dislikes it in My Wave and playlists";
    public string SloplessAuthorCreditText => IsRussian
        ? "Оригинальный аддон: tslyf / slopless-pulsesync (GitHub)"
        : "Original addon: tslyf / slopless-pulsesync (GitHub)";
    public string SloplessBehaviorTitle => IsRussian ? "Действие при воспроизведении" : "Action on AI playback";
    public string SloplessBehaviorDesc => IsRussian ? "Что плеер делает, когда начинает играть трек от ИИ" : "Action taken when an AI-generated song begins playing";
    public string SloplessDetectionModeTitle => IsRussian ? "Критерий определения ИИ" : "Detection Criteria";
    public string SloplessDetectionModeDesc => IsRussian ? "По каким критериям плагин считает трек нейросетевым" : "Conditions used to classify a song as AI-generated";
    public string SloplessArtistThresholdTitle => IsRussian ? "Порог нейро-артиста" : "Artist AI Threshold";
    public string SloplessArtistThresholdDesc => IsRussian ? "Процент ИИ-треков у автора для признания его нейросетевым (рекомендуется 10%)" : "Percent of AI tracks for an artist to be classified as AI (10% recommended)";
    public string SloplessShowTrackLabelsTitle => IsRussian ? "Показывать бейдж «ИИ» на треках" : "Show AI badge on track rows";
    public string SloplessShowTrackLabelsDesc => IsRussian ? "Отображать метку «ИИ» рядом с названиями сгенерированных треков" : "Display an 'AI' badge next to titles of generated songs";

    [RelayCommand]
    public async Task OpenSloplessRepoAsync()
    {
        try
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/tslyf/slopless-pulsesync"));
        }
        catch { }
    }

    public string MinimizeToTrayTitle => IsRussian ? "Сворачивать в системный трей Windows" : "Minimize to Windows System Tray";
    public string MinimizeToTrayDesc => IsRussian ? "При нажатии крестика плеер продолжит играть в фоне и свернется в трей" : "Keep music playing in background and minimize to the taskbar notification tray when window is closed";

    public string TrackNotificationsTitle => IsRussian ? "Уведомления Windows Toast" : "Windows Toast Notifications";
    public string TrackNotificationsDesc => IsRussian ? "Показывать всплывающие уведомления на рабочем столе при переключении трека" : "Show native desktop banner notifications when track changes while playing in background";

    public string AppearanceSectionTitle => IsRussian ? "Внешний вид и кастомизация" : "Appearance & Customization";
    public string AppearanceSectionDesc => IsRussian ? "Темы, акцентные цвета, эффект размытия окна и типографика" : "Themes, accent colors, window backdrops, and typography";

    public string ThemeTitle => IsRussian ? "Тема оформления" : "Application Theme";
    public string ThemeDesc => IsRussian ? "Выберите базовую цветовую схему приложения" : "Select your visual appearance preference";

    public string BackdropTitle => IsRussian ? "Эффект фона окна (Backdrop)" : "Window Backdrop Material";
    public string BackdropDesc => IsRussian ? "Mica (Windows 11), глубокий Mica Alt, Desktop Acrylic размытие или сплошной цвет" : "Mica (Windows 11 standard), deep Mica Alt, acrylic blur, or solid theme background";

    public string AccentTitle => IsRussian ? "Акцентный цвет интерфейса" : "UI Accent Color";
    public string AccentDesc => IsRussian ? "Цвет кнопок, ползунков, прогресс-баров и интерактивных элементов" : "Highlight color for buttons, sliders, progress bars, and active elements";

    public string CustomHexPlaceholder => "#FFEAB308";
    public string ApplyHexButtonText => IsRussian ? "Применить" : "Apply";
    public string CustomColorTitle => IsRussian ? "Свой HEX-код цвета" : "Custom HEX Color Code";

    public string FontTitle => IsRussian ? "Шрифт интерфейса" : "UI Typography Font";
    public string FontDesc => IsRussian ? "Рекомендуемый Inter, системный Segoe UI или Yandex Sans" : "Recommended Inter, system Segoe UI, or signature Yandex Sans";

    public string LanguageTitle => "Language / Язык";
    public string LanguageDesc => IsRussian ? "Мгновенное переключение языка интерфейса без перезапуска" : "Switch interface language instantly / Смена языка интерфейса без перезапуска";

    public string StorageSectionTitle => IsRussian ? "Хранилище и данные" : "Storage & Data";
    public string AudioCacheTitle => IsRussian ? "Дисковый аудио-кэш (LRU)" : "Audio Disk Cache (LRU)";
    public string InstantReplayText => IsRussian ? "Мгновенный старт" : "Instant Replay";
    public string AudioCacheDesc => IsRussian ? "Сохранение треков на диске для мгновенного запуска (0 мс) без повторного скачивания" : "Save played tracks offline on disk for instant 0 ms replay without network re-downloading";

    public string CacheLocationTitle => IsRussian ? "Расположение кэша" : "Audio Cache Location";
    public string CacheLocationDesc => IsRussian ? "Директория на диске для хранения кэшированных треков" : "Disk folder for storing cached audio tracks";
    public string CurrentCachePath => _audioCacheService.CacheDirectory;
    public bool IsCustomCachePath => !string.IsNullOrEmpty(_settingsService.AudioCacheLocation);
    public string ChangeCacheFolderButtonText => IsRussian ? "Выбрать папку" : "Change Folder";
    public string ResetCacheFolderButtonText => IsRussian ? "По умолчанию" : "Reset";
    public string ResetCacheFolderTooltip => IsRussian ? "Вернуть стандартную папку на диске C:" : "Reset to default folder on drive C:";

    public string CacheLimitTitle => IsRussian ? "Лимит дискового кэша" : "Cache Storage Limit";
    public string CacheLimitDesc => IsRussian ? "Автоматически удаляет наименее прослушиваемые треки (LRU) при превышении квоты" : "Automatically evicts least recently played tracks (LRU) when local cache exceeds this quota";

    public string LocalStorageTitle => IsRussian ? "Локальный аудио и метаданные кэш" : "Local Audio & Metadata Cache";
    public string StorageOccupiedText => IsRussian ? "Занято места:" : "Storage occupied:";
    public string CachedItemsText => IsRussian ? "Кэшировано треков:" : "Cached items:";
    public string OpenCacheTooltip => IsRussian ? "Открыть папку кэша аудио в Проводнике" : "Open Cache/Audio folder in Explorer";
    public string OpenCacheButtonText => IsRussian ? "Открыть кэш" : "Open Cache";
    public string ClearCacheButtonText => IsRussian ? "Очистить кэш" : "Clear Cache";

    public string AppDataFolderTitle => IsRussian ? "Папка данных приложения" : "Application Data Folder";
    public string OpenInExplorerButtonText => IsRussian ? "Открыть в проводнике" : "Open in Explorer";

    public string SearchHistoryTitle => IsRussian ? "История поиска" : "Search History";
    public string SearchHistoryDesc => IsRussian ? "Очистить недавние поисковые запросы, сохранённые на этом устройстве" : "Clear recent search queries saved on this device";
    public string ClearHistoryButtonText => IsRussian ? "Очистить историю" : "Clear History";

    public string AboutDescText => IsRussian ? "Современный настольный клиент Яндекс Музыки на WinUI 3, Windows App SDK и .NET 8." : "Modern desktop client for Yandex Music built with WinUI 3, Windows App SDK & .NET 8.";
    public string GitHubRepoButtonText => IsRussian ? "Репозиторий GitHub" : "GitHub Repository";

    public string SwitchOnText => IsRussian ? "Вкл" : "On";
    public string SwitchOffText => IsRussian ? "Выкл" : "Off";
    public string SwitchEnabledText => IsRussian ? "Включено" : "Enabled";
    public string SwitchDisabledText => IsRussian ? "Отключено" : "Disabled";

    public string CheckUpdatesButtonText => IsRussian ? "Проверить обновления" : "Check for Updates";
    public string DownloadUpdateButtonText => IsRussian ? "Скачать и обновить" : "Download & Install";
    public string UpdateSectionTitle => IsRussian ? "Обновления" : "Updates";
    public string ReleaseNotesTitle => IsRussian ? "Что нового:" : "Release notes:";

    public ProfileViewModel(
        IYandexMusicService musicService,
        ICredentialStorageService credentialStorage,
        ISettingsService settingsService,
        IAudioPlayerService audioPlayer,
        IEqualizerService equalizerService,
        IPinnedItemsService pinnedItemsService,
        INavigationService navigationService,
        LoginViewModel loginViewModel,
        SearchViewModel searchViewModel,
        IAudioCacheService audioCacheService,
        IUpdateService updateService,
        IGameOverlayService gameOverlayService,
        IThemeService themeService)
    {
        _musicService = musicService;
        _credentialStorage = credentialStorage;
        _settingsService = settingsService;
        _audioPlayer = audioPlayer;
        _equalizerService = equalizerService;
        _pinnedItemsService = pinnedItemsService;
        _navigationService = navigationService;
        _loginViewModel = loginViewModel;
        _searchViewModel = searchViewModel;
        _audioCacheService = audioCacheService;
        _updateService = updateService;
        _gameOverlayService = gameOverlayService;
        _themeService = themeService;

        _appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YMusic");
        AppDataPath = _appDataDir;
        AppVersionText = $"v{_updateService.CurrentVersion}";
        _profileCachePath = Path.Combine(_appDataDir, "profile_cache.json");

        LoadCachedProfile();

        if (_user == null && _credentialStorage.HasToken())
        {
            _isLoggedIn = true;
            _user = _loginViewModel.CurrentUser;
        }
        _pinnedItemsCount = _pinnedItemsService.GetPinnedItems().Count;

        LoadSettingsState();
        UpdateOptionCollections();
        _settingsService.SettingChanged += OnSettingChanged;
        _equalizerService.EqualizerChanged += OnEqualizerServiceChanged;
        _audioCacheService.CacheUpdated += (_, _) => RunOnUi(CalculateCacheSize);
    }

    public async Task InitializeAsync()
    {
        bool shouldSetBusy = User == null && !_hasCachedProfile;
        if (shouldSetBusy)
        {
            SetBusy(true);
        }

        try
        {
            await RefreshProfileAsync();
            CalculateCacheSize();
        }
        finally
        {
            if (shouldSetBusy)
            {
                SetBusy(false);
            }
        }
    }

    private void LoadSettingsState()
    {
        _isInitializingSettings = true;
        try
        {
            SelectedQualityIndex = _settingsService.PreferredQuality switch
            {
                AudioQualityPreference.AacPreferred => 0,
                AudioQualityPreference.Mp3Preferred => 1,
                _ => 2
            };

            IsGaplessPreloadEnabled = _settingsService.GaplessPreloadEnabled;
            IsDiscordRpcEnabled = _settingsService.DiscordRpcEnabled;
            IsObsOverlayEnabled = _settingsService.ObsOverlayEnabled;
            IsAutoplayWaveEnabled = _settingsService.AutoplayWaveOnAlbumEnd;
            IsAutoBypassProxyEnabled = _settingsService.AutoBypassProxyForYandex;
            IsCrossfadeEnabled = _settingsService.CrossfadeEnabled;
            CrossfadeDurationSeconds = _settingsService.CrossfadeDurationSeconds;
            IsGlobalHotkeysEnabled = _settingsService.GlobalHotkeysEnabled;
            IsGameOverlayEnabled = _settingsService.GameOverlayEnabled;
            IsShowOverlayOnTrackChangeEnabled = _settingsService.ShowOverlayOnTrackChange;
            GameOverlayDurationSeconds = _settingsService.GameOverlayDurationSeconds;
            SelectedGameOverlayPositionIndex = _settingsService.GameOverlayPosition;
            IsMinimizeToTrayEnabled = _settingsService.MinimizeToTrayOnClose;
            IsTrackNotificationsEnabled = _settingsService.TrackNotificationsEnabled;
            IsFckCensorEnabled = _settingsService.FckCensorEnabled;
            IsSloplessEnabled = _settingsService.SloplessEnabled;
            SloplessBehaviorIndex = _settingsService.SloplessBehavior switch
            {
                "dislike" => 0,
                "dislike_if_not_liked" => 1,
                "skip" => 2,
                "skip_if_not_liked" => 3,
                "nothing" => 4,
                _ => 0
            };
            SloplessDetectionModeIndex = _settingsService.SloplessDetectionMode switch
            {
                "balance" => 0,
                "paranoid" => 1,
                "track_only" => 2,
                _ => 0
            };
            SloplessArtistThreshold = _settingsService.SloplessArtistThreshold;
            IsSloplessShowTrackLabelsEnabled = _settingsService.SloplessShowTrackLabels;
            IsAudioCacheEnabled = _settingsService.AudioCacheEnabled;

            SelectedCacheLimitIndex = _settingsService.CacheSizeLimitMb switch
            {
                512 => 0,
                1024 => 1,
                2048 => 2,
                4096 => 3,
                8192 => 4,
                16384 => 5,
                0 => 6,
                _ => 1
            };

            SelectedThemeIndex = _settingsService.AppTheme switch
            {
                "OLED" => 1,
                "Midnight" => 2,
                "Sepia" => 3,
                "Light" => 4,
                "Default" => 5,
                _ => 0 // Dark default
            };

            SelectedBackdropIndex = _settingsService.BackdropKind switch
            {
                "MicaAlt" => 1,
                "Acrylic" => 2,
                "Solid" => 3,
                _ => 0 // Mica
            };

            CurrentAccentHex = _settingsService.AccentColorHex;
            CustomHexInput = _settingsService.AccentColorHex;

            SelectedFontIndex = _settingsService.AppFont switch
            {
                "System" => 1,
                "YandexSans" => 2,
                _ => 0 // Inter default
            };

            SelectedLanguageIndex = _settingsService.Language switch
            {
                "en" => 1,
                _ => 0 // "ru" default
            };

            IsShowWaveInNavEnabled = _settingsService.ShowWaveInNav;
            IsShowLikesInNavEnabled = _settingsService.ShowLikesInNav;
            IsShowPlaylistsInNavEnabled = _settingsService.ShowPlaylistsInNav;
            IsShowSearchInNavEnabled = _settingsService.ShowSearchInNav;

            IsShowLyricsButtonEnabled = _settingsService.ShowLyricsButton;
            IsShowEqualizerButtonEnabled = _settingsService.ShowEqualizerButton;
            IsShowQualityBadgeEnabled = _settingsService.ShowQualityBadge;
            IsCompactPlayerVolumeEnabled = _settingsService.CompactPlayerVolume;

            IsShowPersonalPlaylistsOnHomeEnabled = _settingsService.ShowPersonalPlaylistsOnHome;
            IsShowNewReleasesOnHomeEnabled = _settingsService.ShowNewReleasesOnHome;
            IsShowEditorialPlaylistsOnHomeEnabled = _settingsService.ShowEditorialPlaylistsOnHome;
            IsShowChartOnHomeEnabled = _settingsService.ShowChartOnHome;

            IsEqualizerEnabled = _equalizerService.IsEnabled;

            AvailablePresets.Clear();
            foreach (var preset in _equalizerService.Presets)
            {
                AvailablePresets.Add(preset.Name);
            }
            SelectedEqualizerPreset = _equalizerService.CurrentPresetName;
        }
        finally
        {
            _isInitializingSettings = false;
        }
    }

    private void OnSettingChanged(object? sender, string propName)
    {
        if (_isInitializingSettings) return;

        RunOnUi(() =>
        {
            if (propName == nameof(ISettingsService.PreferredQuality))
            {
                int newIndex = _settingsService.PreferredQuality switch
                {
                    AudioQualityPreference.AacPreferred => 0,
                    AudioQualityPreference.Mp3Preferred => 1,
                    _ => 2
                };
                if (SelectedQualityIndex != newIndex) SelectedQualityIndex = newIndex;
            }
            else if (propName == nameof(ISettingsService.GaplessPreloadEnabled))
            {
                IsGaplessPreloadEnabled = _settingsService.GaplessPreloadEnabled;
            }
            else if (propName == nameof(ISettingsService.DiscordRpcEnabled))
            {
                IsDiscordRpcEnabled = _settingsService.DiscordRpcEnabled;
            }
            else if (propName == nameof(ISettingsService.ObsOverlayEnabled))
            {
                IsObsOverlayEnabled = _settingsService.ObsOverlayEnabled;
            }
            else if (propName == nameof(ISettingsService.AutoplayWaveOnAlbumEnd))
            {
                IsAutoplayWaveEnabled = _settingsService.AutoplayWaveOnAlbumEnd;
            }
            else if (propName == nameof(ISettingsService.AutoBypassProxyForYandex))
            {
                IsAutoBypassProxyEnabled = _settingsService.AutoBypassProxyForYandex;
            }
            else if (propName == nameof(ISettingsService.CrossfadeEnabled))
            {
                IsCrossfadeEnabled = _settingsService.CrossfadeEnabled;
            }
            else if (propName == nameof(ISettingsService.CrossfadeDurationSeconds))
            {
                CrossfadeDurationSeconds = _settingsService.CrossfadeDurationSeconds;
                OnPropertyChanged(nameof(CrossfadeDurationText));
            }
            else if (propName == nameof(ISettingsService.GlobalHotkeysEnabled))
            {
                IsGlobalHotkeysEnabled = _settingsService.GlobalHotkeysEnabled;
            }
            else if (propName == nameof(ISettingsService.GameOverlayEnabled))
            {
                IsGameOverlayEnabled = _settingsService.GameOverlayEnabled;
            }
            else if (propName == nameof(ISettingsService.ShowOverlayOnTrackChange))
            {
                IsShowOverlayOnTrackChangeEnabled = _settingsService.ShowOverlayOnTrackChange;
            }
            else if (propName == nameof(ISettingsService.GameOverlayDurationSeconds))
            {
                GameOverlayDurationSeconds = _settingsService.GameOverlayDurationSeconds;
                OnPropertyChanged(nameof(GameOverlayDurationText));
            }
            else if (propName == nameof(ISettingsService.GameOverlayPosition))
            {
                SelectedGameOverlayPositionIndex = _settingsService.GameOverlayPosition;
            }
            else if (propName == nameof(ISettingsService.MinimizeToTrayOnClose))
            {
                IsMinimizeToTrayEnabled = _settingsService.MinimizeToTrayOnClose;
            }
            else if (propName == nameof(ISettingsService.TrackNotificationsEnabled))
            {
                IsTrackNotificationsEnabled = _settingsService.TrackNotificationsEnabled;
            }
            else if (propName == nameof(ISettingsService.FckCensorEnabled))
            {
                IsFckCensorEnabled = _settingsService.FckCensorEnabled;
            }
            else if (propName == nameof(ISettingsService.SloplessEnabled))
            {
                if (IsSloplessEnabled != _settingsService.SloplessEnabled) IsSloplessEnabled = _settingsService.SloplessEnabled;
            }
            else if (propName == nameof(ISettingsService.SloplessBehavior))
            {
                int bIdx = _settingsService.SloplessBehavior switch
                {
                    "dislike" => 0,
                    "dislike_if_not_liked" => 1,
                    "skip" => 2,
                    "skip_if_not_liked" => 3,
                    "nothing" => 4,
                    _ => 0
                };
                if (SloplessBehaviorIndex != bIdx) SloplessBehaviorIndex = bIdx;
            }
            else if (propName == nameof(ISettingsService.SloplessDetectionMode))
            {
                int mIdx = _settingsService.SloplessDetectionMode switch
                {
                    "balance" => 0,
                    "paranoid" => 1,
                    "track_only" => 2,
                    _ => 0
                };
                if (SloplessDetectionModeIndex != mIdx) SloplessDetectionModeIndex = mIdx;
            }
            else if (propName == nameof(ISettingsService.SloplessArtistThreshold))
            {
                if (Math.Abs(SloplessArtistThreshold - _settingsService.SloplessArtistThreshold) > 0.01)
                    SloplessArtistThreshold = _settingsService.SloplessArtistThreshold;
            }
            else if (propName == nameof(ISettingsService.SloplessShowTrackLabels))
            {
                if (IsSloplessShowTrackLabelsEnabled != _settingsService.SloplessShowTrackLabels)
                    IsSloplessShowTrackLabelsEnabled = _settingsService.SloplessShowTrackLabels;
            }
            else if (propName == nameof(ISettingsService.AudioCacheEnabled))
            {
                IsAudioCacheEnabled = _settingsService.AudioCacheEnabled;
            }
            else if (propName == nameof(ISettingsService.CacheSizeLimitMb))
            {
                int newIdx = _settingsService.CacheSizeLimitMb switch
                {
                    512 => 0,
                    1024 => 1,
                    2048 => 2,
                    4096 => 3,
                    8192 => 4,
                    16384 => 5,
                    0 => 6,
                    _ => 1
                };
                if (SelectedCacheLimitIndex != newIdx) SelectedCacheLimitIndex = newIdx;
                CalculateCacheSize();
            }
            else if (propName == nameof(ISettingsService.Language))
            {
                int langIdx = _settingsService.Language == "en" ? 1 : 0;
                if (SelectedLanguageIndex != langIdx) SelectedLanguageIndex = langIdx;
            }
            else if (propName == nameof(ISettingsService.ShowWaveInNav))
            {
                if (IsShowWaveInNavEnabled != _settingsService.ShowWaveInNav) IsShowWaveInNavEnabled = _settingsService.ShowWaveInNav;
            }
            else if (propName == nameof(ISettingsService.ShowLikesInNav))
            {
                if (IsShowLikesInNavEnabled != _settingsService.ShowLikesInNav) IsShowLikesInNavEnabled = _settingsService.ShowLikesInNav;
            }
            else if (propName == nameof(ISettingsService.ShowPlaylistsInNav))
            {
                if (IsShowPlaylistsInNavEnabled != _settingsService.ShowPlaylistsInNav) IsShowPlaylistsInNavEnabled = _settingsService.ShowPlaylistsInNav;
            }
            else if (propName == nameof(ISettingsService.ShowSearchInNav))
            {
                if (IsShowSearchInNavEnabled != _settingsService.ShowSearchInNav) IsShowSearchInNavEnabled = _settingsService.ShowSearchInNav;
            }
            else if (propName == nameof(ISettingsService.ShowLyricsButton))
            {
                if (IsShowLyricsButtonEnabled != _settingsService.ShowLyricsButton) IsShowLyricsButtonEnabled = _settingsService.ShowLyricsButton;
            }
            else if (propName == nameof(ISettingsService.ShowEqualizerButton))
            {
                if (IsShowEqualizerButtonEnabled != _settingsService.ShowEqualizerButton) IsShowEqualizerButtonEnabled = _settingsService.ShowEqualizerButton;
            }
            else if (propName == nameof(ISettingsService.ShowQualityBadge))
            {
                if (IsShowQualityBadgeEnabled != _settingsService.ShowQualityBadge) IsShowQualityBadgeEnabled = _settingsService.ShowQualityBadge;
            }
            else if (propName == nameof(ISettingsService.CompactPlayerVolume))
            {
                if (IsCompactPlayerVolumeEnabled != _settingsService.CompactPlayerVolume) IsCompactPlayerVolumeEnabled = _settingsService.CompactPlayerVolume;
            }
            else if (propName == nameof(ISettingsService.ShowPersonalPlaylistsOnHome))
            {
                if (IsShowPersonalPlaylistsOnHomeEnabled != _settingsService.ShowPersonalPlaylistsOnHome) IsShowPersonalPlaylistsOnHomeEnabled = _settingsService.ShowPersonalPlaylistsOnHome;
            }
            else if (propName == nameof(ISettingsService.ShowNewReleasesOnHome))
            {
                if (IsShowNewReleasesOnHomeEnabled != _settingsService.ShowNewReleasesOnHome) IsShowNewReleasesOnHomeEnabled = _settingsService.ShowNewReleasesOnHome;
            }
            else if (propName == nameof(ISettingsService.ShowEditorialPlaylistsOnHome))
            {
                if (IsShowEditorialPlaylistsOnHomeEnabled != _settingsService.ShowEditorialPlaylistsOnHome) IsShowEditorialPlaylistsOnHomeEnabled = _settingsService.ShowEditorialPlaylistsOnHome;
            }
            else if (propName == nameof(ISettingsService.ShowChartOnHome))
            {
                if (IsShowChartOnHomeEnabled != _settingsService.ShowChartOnHome) IsShowChartOnHomeEnabled = _settingsService.ShowChartOnHome;
            }
        });
    }

    private void OnEqualizerServiceChanged(object? sender, EventArgs e)
    {
        RunOnUi(() =>
        {
            if (IsEqualizerEnabled != _equalizerService.IsEnabled)
            {
                IsEqualizerEnabled = _equalizerService.IsEnabled;
            }

            if (!string.Equals(SelectedEqualizerPreset, _equalizerService.CurrentPresetName, StringComparison.OrdinalIgnoreCase))
            {
                SelectedEqualizerPreset = _equalizerService.CurrentPresetName;
            }
        });
    }

    partial void OnSelectedQualityIndexChanged(int value)
    {
        if (_isInitializingSettings) return;

        var pref = value switch
        {
            0 => AudioQualityPreference.AacPreferred,
            1 => AudioQualityPreference.Mp3Preferred,
            _ => AudioQualityPreference.HighestBitrate
        };
        _settingsService.PreferredQuality = pref;
        string qualityName = value == 0
            ? (IsRussian ? "AAC (Рекомендуется)" : "AAC Preferred")
            : (value == 1 ? "MP3 320k" : (IsRussian ? "Наивысший битрейт" : "Highest Bitrate"));
        ShowNotification(IsRussian ? $"Качество аудио обновлено: {qualityName}" : $"Audio quality updated to: {qualityName}");
    }

    partial void OnIsGaplessPreloadEnabledChanged(bool value)
    {
        if (_isInitializingSettings) return;
        _settingsService.GaplessPreloadEnabled = value;
    }

    partial void OnIsDiscordRpcEnabledChanged(bool value)
    {
        if (_isInitializingSettings) return;
        _settingsService.DiscordRpcEnabled = value;
    }

    partial void OnIsObsOverlayEnabledChanged(bool value)
    {
        if (_isInitializingSettings) return;
        _settingsService.ObsOverlayEnabled = value;
        ShowNotification(value
            ? (IsRussian ? "OBS Overlay сервер запущен." : "OBS Overlay server started.")
            : (IsRussian ? "OBS Overlay сервер остановлен." : "OBS Overlay server stopped."));
    }

    partial void OnIsAutoplayWaveEnabledChanged(bool value)
    {
        if (_isInitializingSettings) return;
        _settingsService.AutoplayWaveOnAlbumEnd = value;
    }

    partial void OnIsAutoBypassProxyEnabledChanged(bool value)
    {
        if (_isInitializingSettings) return;
        _settingsService.AutoBypassProxyForYandex = value;
    }

    partial void OnIsCrossfadeEnabledChanged(bool value)
    {
        if (_isInitializingSettings) return;
        _settingsService.CrossfadeEnabled = value;
    }

    partial void OnCrossfadeDurationSecondsChanged(int value)
    {
        if (_isInitializingSettings) return;
        _settingsService.CrossfadeDurationSeconds = value;
        OnPropertyChanged(nameof(CrossfadeDurationText));
    }

    partial void OnIsGlobalHotkeysEnabledChanged(bool value)
    {
        if (_isInitializingSettings) return;
        _settingsService.GlobalHotkeysEnabled = value;
    }

    partial void OnIsGameOverlayEnabledChanged(bool value)
    {
        if (_isInitializingSettings) return;
        _settingsService.GameOverlayEnabled = value;
    }

    partial void OnIsShowOverlayOnTrackChangeEnabledChanged(bool value)
    {
        if (_isInitializingSettings) return;
        _settingsService.ShowOverlayOnTrackChange = value;
    }

    partial void OnGameOverlayDurationSecondsChanged(int value)
    {
        if (_isInitializingSettings) return;
        _settingsService.GameOverlayDurationSeconds = value;
        OnPropertyChanged(nameof(GameOverlayDurationText));
    }

    partial void OnSelectedGameOverlayPositionIndexChanged(int value)
    {
        if (_isInitializingSettings) return;
        _settingsService.GameOverlayPosition = value;
        NotifyPositionProperties();
    }

    partial void OnIsMinimizeToTrayEnabledChanged(bool value)
    {
        if (_isInitializingSettings) return;
        _settingsService.MinimizeToTrayOnClose = value;
    }

    partial void OnIsTrackNotificationsEnabledChanged(bool value)
    {
        if (_isInitializingSettings) return;
        _settingsService.TrackNotificationsEnabled = value;
        ShowNotification(value
            ? (IsRussian ? "Уведомления на рабочем столе о смене трека включены." : "Desktop notifications enabled for track changes.")
            : (IsRussian ? "Уведомления на рабочем столе отключены." : "Desktop notifications disabled."));
    }

    partial void OnIsFckCensorEnabledChanged(bool value)
    {
        if (_isInitializingSettings) return;
        _settingsService.FckCensorEnabled = value;
        _ = _audioPlayer.ReloadCurrentTrackQualityAsync();
        ShowNotification(value
            ? (IsRussian ? "FckCensor включен: воспроизводятся нецензурированные версии." : "FckCensor enabled: Uncensored audio active.")
            : (IsRussian ? "FckCensor выключен." : "FckCensor disabled."));
    }

    partial void OnIsSloplessEnabledChanged(bool value)
    {
        if (_isInitializingSettings) return;
        _settingsService.SloplessEnabled = value;
        ShowNotification(value
            ? (IsRussian ? "Slopless включен: ИИ-музыка фильтруется." : "Slopless enabled: AI music filtering active.")
            : (IsRussian ? "Slopless выключен." : "Slopless disabled."));
    }

    partial void OnSloplessBehaviorIndexChanged(int value)
    {
        if (_isInitializingSettings) return;
        string beh = value switch
        {
            0 => "dislike",
            1 => "dislike_if_not_liked",
            2 => "skip",
            3 => "skip_if_not_liked",
            4 => "nothing",
            _ => "dislike"
        };
        _settingsService.SloplessBehavior = beh;
    }

    partial void OnSloplessDetectionModeIndexChanged(int value)
    {
        if (_isInitializingSettings) return;
        string mode = value switch
        {
            0 => "balance",
            1 => "paranoid",
            2 => "track_only",
            _ => "balance"
        };
        _settingsService.SloplessDetectionMode = mode;
    }

    partial void OnSloplessArtistThresholdChanged(double value)
    {
        if (_isInitializingSettings) return;
        _settingsService.SloplessArtistThreshold = (int)Math.Round(value);
    }

    partial void OnIsSloplessShowTrackLabelsEnabledChanged(bool value)
    {
        if (_isInitializingSettings) return;
        _settingsService.SloplessShowTrackLabels = value;
    }

    partial void OnIsAudioCacheEnabledChanged(bool value)
    {
        if (_isInitializingSettings) return;
        _settingsService.AudioCacheEnabled = value;
        ShowNotification(value
            ? (IsRussian ? "Дисковый аудио-кэш включен." : "Audio Disk Cache enabled.")
            : (IsRussian ? "Дисковый аудио-кэш выключен." : "Audio Disk Cache disabled."));
    }

    partial void OnSelectedCacheLimitIndexChanged(int value)
    {
        if (_isInitializingSettings) return;

        int limit = value switch
        {
            0 => 512,
            1 => 1024,
            2 => 2048,
            3 => 4096,
            4 => 8192,
            5 => 16384,
            _ => 0 // Unlimited
        };
        _settingsService.CacheSizeLimitMb = limit;
        _audioCacheService.EnforceCacheLimit();
        CalculateCacheSize();
        ShowNotification(limit > 0
            ? (IsRussian ? $"Лимит дискового кэша установлен на {limit} МБ." : $"Audio cache limit set to {limit} MB.")
            : (IsRussian ? "Лимит дискового кэша отключен (без ограничений)." : "Audio cache limit set to Unlimited."));
    }

    partial void OnSelectedThemeIndexChanged(int value)
    {
        if (_isInitializingSettings) return;

        string theme = value switch
        {
            1 => "OLED",
            2 => "Midnight",
            3 => "Sepia",
            4 => "Light",
            5 => "Default",
            _ => "Dark"
        };
        _themeService.ApplyTheme(theme);
    }

    partial void OnSelectedBackdropIndexChanged(int value)
    {
        if (_isInitializingSettings) return;

        string backdrop = value switch
        {
            1 => "MicaAlt",
            2 => "Acrylic",
            3 => "Solid",
            _ => "Mica"
        };
        _themeService.ApplyBackdrop(backdrop);
    }

    [RelayCommand]
    public void SelectAccent(AccentPreset preset)
    {
        if (preset == null) return;
        _themeService.ApplyAccentColor(preset.Hex);
        CurrentAccentHex = preset.Hex;
        CustomHexInput = preset.Hex;
        ShowNotification(IsRussian ? $"Акцентный цвет: {preset.Name}" : $"Accent color: {preset.NameEn}");
    }

    [RelayCommand]
    public void ApplyCustomHex()
    {
        if (string.IsNullOrWhiteSpace(CustomHexInput)) return;
        string hex = CustomHexInput.Trim();
        if (!hex.StartsWith("#")) hex = "#" + hex;
        _themeService.ApplyAccentColor(hex);
        CurrentAccentHex = _settingsService.AccentColorHex;
        CustomHexInput = CurrentAccentHex;
        ShowNotification(IsRussian ? $"Пользовательский цвет применен: {CurrentAccentHex}" : $"Custom accent applied: {CurrentAccentHex}");
    }

    partial void OnSelectedFontIndexChanged(int value)
    {
        if (_isInitializingSettings) return;

        string font = value switch
        {
            1 => "System",
            2 => "YandexSans",
            _ => "Inter"
        };
        _settingsService.AppFont = font;

        if (App.MainWindow is ShellWindow shell)
        {
            shell.ApplyFont(font);
        }

        string fontName = value switch
        {
            1 => "Segoe UI",
            2 => "Yandex Sans",
            _ => "Inter"
        };
        ShowNotification(IsRussian ? $"Шрифт интерфейса изменён: {fontName}." : $"UI font changed to: {fontName}.");
    }

    partial void OnSelectedLanguageIndexChanged(int value)
    {
        if (_isInitializingSettings) return;

        string lang = value == 1 ? "en" : "ru";
        _settingsService.Language = lang;
        if (Loc != null && Loc.CurrentLanguage != lang)
        {
            Loc.CurrentLanguage = lang;
        }

        ShowNotification(IsRussian ? "Настройки сохранены." : "Settings saved.");
    }

    partial void OnIsShowWaveInNavEnabledChanged(bool value)
    {
        if (_isInitializingSettings) return;
        _settingsService.ShowWaveInNav = value;
    }

    partial void OnIsShowLikesInNavEnabledChanged(bool value)
    {
        if (_isInitializingSettings) return;
        _settingsService.ShowLikesInNav = value;
    }

    partial void OnIsShowPlaylistsInNavEnabledChanged(bool value)
    {
        if (_isInitializingSettings) return;
        _settingsService.ShowPlaylistsInNav = value;
    }

    partial void OnIsShowSearchInNavEnabledChanged(bool value)
    {
        if (_isInitializingSettings) return;
        _settingsService.ShowSearchInNav = value;
    }

    partial void OnIsShowLyricsButtonEnabledChanged(bool value)
    {
        if (_isInitializingSettings) return;
        _settingsService.ShowLyricsButton = value;
    }

    partial void OnIsShowEqualizerButtonEnabledChanged(bool value)
    {
        if (_isInitializingSettings) return;
        _settingsService.ShowEqualizerButton = value;
    }

    partial void OnIsShowQualityBadgeEnabledChanged(bool value)
    {
        if (_isInitializingSettings) return;
        _settingsService.ShowQualityBadge = value;
    }

    partial void OnIsCompactPlayerVolumeEnabledChanged(bool value)
    {
        if (_isInitializingSettings) return;
        _settingsService.CompactPlayerVolume = value;
    }

    partial void OnIsShowPersonalPlaylistsOnHomeEnabledChanged(bool value)
    {
        if (_isInitializingSettings) return;
        _settingsService.ShowPersonalPlaylistsOnHome = value;
    }

    partial void OnIsShowNewReleasesOnHomeEnabledChanged(bool value)
    {
        if (_isInitializingSettings) return;
        _settingsService.ShowNewReleasesOnHome = value;
    }

    partial void OnIsShowEditorialPlaylistsOnHomeEnabledChanged(bool value)
    {
        if (_isInitializingSettings) return;
        _settingsService.ShowEditorialPlaylistsOnHome = value;
    }

    partial void OnIsShowChartOnHomeEnabledChanged(bool value)
    {
        if (_isInitializingSettings) return;
        _settingsService.ShowChartOnHome = value;
    }

    partial void OnIsEqualizerEnabledChanged(bool value)
    {
        if (_isInitializingSettings) return;
        if (_equalizerService.IsEnabled != value)
        {
            _equalizerService.IsEnabled = value;
        }
    }

    partial void OnSelectedEqualizerPresetChanged(string value)
    {
        if (_isInitializingSettings || string.IsNullOrEmpty(value)) return;
        if (string.Equals(_equalizerService.CurrentPresetName, value, StringComparison.OrdinalIgnoreCase)) return;
        _equalizerService.ApplyPreset(value);
    }

    [RelayCommand]
    public async Task RefreshProfileAsync()
    {
        if (!await _refreshLock.WaitAsync(0))
        {
            // Already refreshing in background, avoid duplicate parallel calls
            return;
        }

        try
        {
            string? token = _credentialStorage.GetToken();
            if (string.IsNullOrWhiteSpace(token))
            {
                RunOnUi(() =>
                {
                    User = null;
                    IsLoggedIn = false;
                    LikedTracksCount = 0;
                    PlaylistsCount = 0;
                    PinnedItemsCount = _pinnedItemsService.GetPinnedItems().Count;
                });
                ClearCachedProfile();
                return;
            }

            _musicService.SetOAuthToken(token);
            var profile = await _musicService.GetAccountStatusAsync();
            if (profile == null && _loginViewModel.CurrentUser != null)
            {
                profile = _loginViewModel.CurrentUser;
            }
            if (profile == null && User != null)
            {
                profile = User;
            }

            int likedCount = LikedTracksCount;
            int playlistsCount = PlaylistsCount;

            if (profile != null && !string.IsNullOrWhiteSpace(profile.UserId))
            {
                try
                {
                    var likedIds = await _musicService.GetLikedTrackIdsAsync(profile.UserId);
                    if (likedIds != null)
                    {
                        likedCount = likedIds.Count;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ProfileViewModel] Liked tracks count error: {ex.Message}");
                }

                try
                {
                    var playlists = await _musicService.GetUserPlaylistsAsync(profile.UserId);
                    if (playlists != null)
                    {
                        playlistsCount = playlists.Count;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ProfileViewModel] Playlists count error: {ex.Message}");
                }
            }

            int pinnedCount = _pinnedItemsService.GetPinnedItems().Count;

            RunOnUi(() =>
            {
                User = profile;
                IsLoggedIn = true;
                LikedTracksCount = likedCount;
                PlaylistsCount = playlistsCount;
                PinnedItemsCount = pinnedCount;
            });

            SaveCachedProfile();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProfileViewModel] Failed to load profile stats: {ex.Message}");
            RunOnUi(() =>
            {
                IsLoggedIn = _credentialStorage.HasToken();
            });
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private void LoadCachedProfile()
    {
        try
        {
            if (File.Exists(_profileCachePath) && _credentialStorage.HasToken())
            {
                string json = File.ReadAllText(_profileCachePath);
                var cached = JsonSerializer.Deserialize<ProfileCacheData>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (cached != null && cached.User != null)
                {
                    User = cached.User;
                    IsLoggedIn = true;
                    LikedTracksCount = cached.LikedTracksCount;
                    PlaylistsCount = cached.PlaylistsCount;
                    PinnedItemsCount = cached.PinnedItemsCount > 0 ? cached.PinnedItemsCount : _pinnedItemsService.GetPinnedItems().Count;
                    _hasCachedProfile = true;
                    Debug.WriteLine($"[ProfileViewModel] Loaded cached profile for {User.DisplayName}: likes={LikedTracksCount}, playlists={PlaylistsCount}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProfileViewModel] Failed to load cached profile: {ex.Message}");
        }
    }

    private void SaveCachedProfile()
    {
        try
        {
            if (User != null && IsLoggedIn)
            {
                var data = new ProfileCacheData
                {
                    User = User,
                    LikedTracksCount = LikedTracksCount,
                    PlaylistsCount = PlaylistsCount,
                    PinnedItemsCount = PinnedItemsCount
                };
                string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                Directory.CreateDirectory(_appDataDir);
                File.WriteAllText(_profileCachePath, json);
                _hasCachedProfile = true;
                Debug.WriteLine("[ProfileViewModel] Successfully cached profile data to disk.");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProfileViewModel] Failed to save cached profile: {ex.Message}");
        }
    }

    private void ClearCachedProfile()
    {
        try
        {
            if (File.Exists(_profileCachePath))
            {
                File.Delete(_profileCachePath);
            }
            _hasCachedProfile = false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProfileViewModel] Failed to delete profile cache: {ex.Message}");
        }
    }

    [RelayCommand]
    public void ResetEqualizer()
    {
        _equalizerService.ResetToFlat();
        SelectedEqualizerPreset = "Flat";
        ShowNotification(IsRussian ? "Эквалайзер сброшен в Flat." : "Equalizer reset to Flat.");
    }

    [RelayCommand]
    public void CopyUserId()
    {
        if (User == null || string.IsNullOrWhiteSpace(User.UserId)) return;

        try
        {
            var dp = new DataPackage();
            dp.SetText(User.UserId);
            Clipboard.SetContent(dp);
            ShowNotification(IsRussian ? $"ID пользователя скопирован: {User.UserId}" : $"User ID copied: {User.UserId}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProfileViewModel] Clipboard copy failed: {ex.Message}");
        }
    }

    [RelayCommand]
    public void Logout()
    {
        ClearCachedProfile();
        User = null;
        IsLoggedIn = false;
        LikedTracksCount = 0;
        PlaylistsCount = 0;
        _loginViewModel.LogoutCommand.Execute(null);
    }

    [RelayCommand]
    public async Task ChangeCacheFolderAsync()
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder;
            picker.FileTypeFilter.Add("*");

            if (App.MainWindow != null)
            {
                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            var folder = await picker.PickSingleFolderAsync();
            if (folder == null) return;

            string selectedPath = folder.Path;
            if (string.IsNullOrWhiteSpace(selectedPath)) return;

            if (string.Equals(Path.GetFullPath(selectedPath).TrimEnd('\\'), Path.GetFullPath(_audioCacheService.CacheDirectory).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            bool shouldMigrate = false;
            int trackCount = _audioCacheService.GetCachedTrackCount();
            if (trackCount > 0 && App.MainWindow?.Content?.XamlRoot != null)
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = App.MainWindow.Content.XamlRoot,
                    Title = IsRussian ? "Перенос кэша треков" : "Migrate Audio Cache",
                    Content = IsRussian
                        ? $"В текущем кэше сохранено {trackCount} треков ({FormatBytes(_audioCacheService.GetTotalCacheSizeBytes())}).\n\nПеренести существующие файлы в новую папку ({selectedPath})?"
                        : $"Currently {trackCount} tracks ({FormatBytes(_audioCacheService.GetTotalCacheSizeBytes())}) are cached.\n\nDo you want to move existing files to the new folder ({selectedPath})?",
                    PrimaryButtonText = IsRussian ? "Перенести файлы" : "Move files",
                    SecondaryButtonText = IsRussian ? "Начать с чистого кэша" : "Start fresh",
                    CloseButtonText = IsRussian ? "Отмена" : "Cancel",
                    DefaultButton = ContentDialogButton.Primary
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.None)
                {
                    return;
                }

                shouldMigrate = (result == ContentDialogResult.Primary);
            }

            bool success = await _audioCacheService.ChangeCacheLocationAsync(selectedPath, shouldMigrate);
            if (success)
            {
                OnPropertyChanged(nameof(CurrentCachePath));
                OnPropertyChanged(nameof(IsCustomCachePath));
                CalculateCacheSize();
                ShowNotification(IsRussian
                    ? $"Папка кэша изменена: {selectedPath}"
                    : $"Cache folder changed: {selectedPath}");
            }
        }
        catch (Exception ex)
        {
            ShowNotification(IsRussian
                ? $"Ошибка при выборе папки: {ex.Message}"
                : $"Failed to change cache folder: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task ResetCacheFolderAsync()
    {
        try
        {
            string defaultDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YMusic", "Cache");
            if (string.Equals(Path.GetFullPath(defaultDir).TrimEnd('\\'), Path.GetFullPath(_audioCacheService.CacheDirectory).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            bool shouldMigrate = false;
            int trackCount = _audioCacheService.GetCachedTrackCount();
            if (trackCount > 0 && App.MainWindow?.Content?.XamlRoot != null)
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = App.MainWindow.Content.XamlRoot,
                    Title = IsRussian ? "Сброс расположения кэша" : "Reset Cache Location",
                    Content = IsRussian
                        ? $"Перенести сохраненные треки ({trackCount} шт.) обратно в стандартную папку на диске C:\n({defaultDir})?"
                        : $"Move saved tracks ({trackCount} items) back to default folder on drive C:\n({defaultDir})?",
                    PrimaryButtonText = IsRussian ? "Перенести файлы" : "Move files",
                    SecondaryButtonText = IsRussian ? "Сбросить без переноса" : "Reset without moving",
                    CloseButtonText = IsRussian ? "Отмена" : "Cancel",
                    DefaultButton = ContentDialogButton.Primary
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.None)
                {
                    return;
                }

                shouldMigrate = (result == ContentDialogResult.Primary);
            }

            bool success = await _audioCacheService.ChangeCacheLocationAsync(null, shouldMigrate);
            if (success)
            {
                OnPropertyChanged(nameof(CurrentCachePath));
                OnPropertyChanged(nameof(IsCustomCachePath));
                CalculateCacheSize();
                ShowNotification(IsRussian
                    ? "Папка кэша сброшена к стандартной на диске C:"
                    : "Cache folder reset to default location on drive C:");
            }
        }
        catch (Exception ex)
        {
            ShowNotification(IsRussian ? $"Ошибка сброса папки: {ex.Message}" : $"Reset failed: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task ClearCacheAsync()
    {
        try
        {
            await _audioCacheService.ClearCacheAsync();

            CalculateCacheSize();
            ShowNotification(IsRussian ? "Аудио и системный кэш успешно очищены." : "Audio and application cache successfully cleared.");
        }
        catch (Exception ex)
        {
            ShowNotification(IsRussian ? $"Не удалось очистить кэш: {ex.Message}" : $"Failed to clear cache: {ex.Message}");
        }
    }

    [RelayCommand]
    public void OpenCacheFolder()
    {
        try
        {
            string cacheFolder = _audioCacheService.AudioDirectory;
            Directory.CreateDirectory(cacheFolder);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{cacheFolder}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowNotification(IsRussian ? $"Не удалось открыть папку кэша: {ex.Message}" : $"Could not open cache folder: {ex.Message}");
        }
    }

    [RelayCommand]
    public void ClearSearchHistory()
    {
        _searchViewModel.ClearRecentQueries();
        ShowNotification(IsRussian ? "История поиска очищена." : "Search history cleared.");
    }

    [RelayCommand]
    public void OpenAppDataFolder()
    {
        try
        {
            Directory.CreateDirectory(_appDataDir);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{_appDataDir}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowNotification(IsRussian ? $"Не удалось открыть папку: {ex.Message}" : $"Could not open folder: {ex.Message}");
        }
    }

    public void SetAvailableUpdate(UpdateInfo update)
    {
        AvailableUpdate = update;
        IsUpdateAvailable = update.IsUpdateAvailable;

        if (update.IsUpdateAvailable)
        {
            NewVersionTag = $"v{update.LatestVersion}";
            UpdateReleaseNotes = update.GetReleaseNotes(IsRussian);
            UpdateStatusText = IsRussian
                ? $"Доступна новая версия: {NewVersionTag}!"
                : $"New version available: {NewVersionTag}!";
        }
    }

    [RelayCommand]
    public async Task CheckForUpdatesAsync()
    {
        if (IsCheckingUpdates || IsDownloadingUpdate) return;

        IsCheckingUpdates = true;
        UpdateStatusText = IsRussian ? "Проверка обновлений..." : "Checking for updates...";

        try
        {
            var update = await _updateService.CheckForUpdatesAsync();
            AvailableUpdate = update;
            IsUpdateAvailable = update.IsUpdateAvailable;

            if (update.IsUpdateAvailable)
            {
                NewVersionTag = $"v{update.LatestVersion}";
                UpdateReleaseNotes = update.GetReleaseNotes(IsRussian);
                UpdateStatusText = IsRussian
                    ? $"Доступна новая версия: {NewVersionTag}!"
                    : $"New version available: {NewVersionTag}!";
            }
            else
            {
                UpdateStatusText = IsRussian
                    ? $"У вас последняя версия ({update.CurrentVersion})."
                    : $"You are using the latest version ({update.CurrentVersion}).";
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText = IsRussian
                ? $"Ошибка проверки: {ex.Message}"
                : $"Check error: {ex.Message}";
        }
        finally
        {
            IsCheckingUpdates = false;
        }
    }

    [RelayCommand]
    public async Task DownloadAndInstallUpdateAsync()
    {
        if (AvailableUpdate == null || IsDownloadingUpdate) return;

        IsDownloadingUpdate = true;
        UpdateDownloadProgress = 0;
        UpdateProgressText = IsRussian ? "Подготовка к скачиванию..." : "Preparing download...";

        var progress = new Progress<(double Percentage, long DownloadedBytes, long TotalBytes)>(report =>
        {
            RunOnUi(() =>
            {
                UpdateDownloadProgress = report.Percentage;
                string downloadedMb = (report.DownloadedBytes / 1048576.0).ToString("F1");
                string totalMb = report.TotalBytes > 0 ? (report.TotalBytes / 1048576.0).ToString("F1") : "?";
                UpdateProgressText = IsRussian
                    ? $"Скачивание: {report.Percentage:F0}% ({downloadedMb} из {totalMb} МБ)"
                    : $"Downloading: {report.Percentage:F0}% ({downloadedMb} of {totalMb} MB)";
            });
        });

        try
        {
            string installerPath = await _updateService.DownloadUpdateAsync(AvailableUpdate, progress);
            UpdateProgressText = IsRussian ? "Запуск установщика..." : "Launching installer...";
            _updateService.InstallAndRestart(installerPath);
        }
        catch (Exception ex)
        {
            IsDownloadingUpdate = false;
            UpdateStatusText = IsRussian ? $"Не удалось скачать обновление: {ex.Message}" : $"Failed to download update: {ex.Message}";
            ShowNotification(UpdateStatusText);
        }
    }

    [RelayCommand]
    public void OpenGitHub()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/pixiemays/WinYandex",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProfileViewModel] Open GitHub failed: {ex.Message}");
        }
    }

    [RelayCommand]
    public void NavigateToLikes() => _navigationService.NavigateToTag("Likes");

    [RelayCommand]
    public void NavigateToPlaylists() => _navigationService.NavigateToTag("Playlists");

    [RelayCommand]
    public void NavigateToLogin() => _navigationService.NavigateToTag("Account");

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        UpdateOptionCollections();

        OnPropertyChanged(nameof(TitleText));
        OnPropertyChanged(nameof(SubtitleText));
        OnPropertyChanged(nameof(NotificationTitle));
        OnPropertyChanged(nameof(CopyUserIdTooltip));
        OnPropertyChanged(nameof(RefreshButtonText));
        OnPropertyChanged(nameof(SignOutButtonText));
        OnPropertyChanged(nameof(LikedTracksStatText));
        OnPropertyChanged(nameof(PlaylistsStatText));
        OnPropertyChanged(nameof(PinnedItemsStatText));
        OnPropertyChanged(nameof(SignedOutTitleText));
        OnPropertyChanged(nameof(SignedOutDescText));
        OnPropertyChanged(nameof(SignInButtonText));
        OnPropertyChanged(nameof(AudioSectionTitle));
        OnPropertyChanged(nameof(StreamQualityTitle));
        OnPropertyChanged(nameof(StreamQualityDesc));
        OnPropertyChanged(nameof(GaplessTitle));
        OnPropertyChanged(nameof(GaplessDesc));
        OnPropertyChanged(nameof(EqualizerTitle));
        OnPropertyChanged(nameof(EqualizerDesc));
        OnPropertyChanged(nameof(EqualizerResetText));
        OnPropertyChanged(nameof(SystemSectionTitle));
        OnPropertyChanged(nameof(DiscordRpcTitle));
        OnPropertyChanged(nameof(DiscordRpcDesc));
        OnPropertyChanged(nameof(ObsOverlayTitle));
        OnPropertyChanged(nameof(ObsOverlayDesc));
        OnPropertyChanged(nameof(ObsWidgetUrlText));
        OnPropertyChanged(nameof(CopyObsUrlButtonText));
        OnPropertyChanged(nameof(OpenObsDashboardButtonText));
        OnPropertyChanged(nameof(AutoplayWaveTitle));
        OnPropertyChanged(nameof(AutoplayWaveDesc));
        OnPropertyChanged(nameof(AutoBypassProxyTitle));
        OnPropertyChanged(nameof(AutoBypassProxyDesc));
        OnPropertyChanged(nameof(CrossfadeTitle));
        OnPropertyChanged(nameof(CrossfadeDesc));
        OnPropertyChanged(nameof(CrossfadeDurationText));
        OnPropertyChanged(nameof(GlobalHotkeysTitle));
        OnPropertyChanged(nameof(GlobalHotkeysDesc));
        OnPropertyChanged(nameof(GameOverlayTitle));
        OnPropertyChanged(nameof(GameOverlayDesc));
        OnPropertyChanged(nameof(ShowOverlayOnTrackChangeTitle));
        OnPropertyChanged(nameof(ShowOverlayOnTrackChangeDesc));
        OnPropertyChanged(nameof(GameOverlayDurationText));
        OnPropertyChanged(nameof(GameOverlayPositionTitle));
        OnPropertyChanged(nameof(TestGameOverlayButtonText));
        OnPropertyChanged(nameof(FckCensorTitle));
        OnPropertyChanged(nameof(FckCensorDesc));
        OnPropertyChanged(nameof(FckCensorAuthorCreditText));
        OnPropertyChanged(nameof(SloplessTitle));
        OnPropertyChanged(nameof(SloplessDesc));
        OnPropertyChanged(nameof(SloplessAuthorCreditText));
        OnPropertyChanged(nameof(SloplessBehaviorTitle));
        OnPropertyChanged(nameof(SloplessBehaviorDesc));
        OnPropertyChanged(nameof(SloplessDetectionModeTitle));
        OnPropertyChanged(nameof(SloplessDetectionModeDesc));
        OnPropertyChanged(nameof(SloplessArtistThresholdTitle));
        OnPropertyChanged(nameof(SloplessArtistThresholdDesc));
        OnPropertyChanged(nameof(SloplessShowTrackLabelsTitle));
        OnPropertyChanged(nameof(SloplessShowTrackLabelsDesc));
        OnPropertyChanged(nameof(MinimizeToTrayTitle));
        OnPropertyChanged(nameof(MinimizeToTrayDesc));
        OnPropertyChanged(nameof(TrackNotificationsTitle));
        OnPropertyChanged(nameof(TrackNotificationsDesc));
        OnPropertyChanged(nameof(AppearanceSectionTitle));
        OnPropertyChanged(nameof(AppearanceSectionDesc));
        OnPropertyChanged(nameof(ThemeTitle));
        OnPropertyChanged(nameof(ThemeDesc));
        OnPropertyChanged(nameof(BackdropTitle));
        OnPropertyChanged(nameof(BackdropDesc));
        OnPropertyChanged(nameof(AccentTitle));
        OnPropertyChanged(nameof(AccentDesc));
        OnPropertyChanged(nameof(ApplyHexButtonText));
        OnPropertyChanged(nameof(CustomColorTitle));
        OnPropertyChanged(nameof(FontTitle));
        OnPropertyChanged(nameof(FontDesc));
        OnPropertyChanged(nameof(LanguageTitle));
        OnPropertyChanged(nameof(LanguageDesc));
        OnPropertyChanged(nameof(StorageSectionTitle));
        OnPropertyChanged(nameof(AudioCacheTitle));
        OnPropertyChanged(nameof(InstantReplayText));
        OnPropertyChanged(nameof(AudioCacheDesc));
        OnPropertyChanged(nameof(CacheLimitTitle));
        OnPropertyChanged(nameof(CacheLimitDesc));
        OnPropertyChanged(nameof(LocalStorageTitle));
        OnPropertyChanged(nameof(StorageOccupiedText));
        OnPropertyChanged(nameof(CachedItemsText));
        OnPropertyChanged(nameof(CacheLocationTitle));
        OnPropertyChanged(nameof(CacheLocationDesc));
        OnPropertyChanged(nameof(CurrentCachePath));
        OnPropertyChanged(nameof(IsCustomCachePath));
        OnPropertyChanged(nameof(ChangeCacheFolderButtonText));
        OnPropertyChanged(nameof(ResetCacheFolderButtonText));
        OnPropertyChanged(nameof(ResetCacheFolderTooltip));
        OnPropertyChanged(nameof(OpenCacheTooltip));
        OnPropertyChanged(nameof(OpenCacheButtonText));
        OnPropertyChanged(nameof(ClearCacheButtonText));
        OnPropertyChanged(nameof(AppDataFolderTitle));
        OnPropertyChanged(nameof(OpenInExplorerButtonText));
        OnPropertyChanged(nameof(SearchHistoryTitle));
        OnPropertyChanged(nameof(SearchHistoryDesc));
        OnPropertyChanged(nameof(ClearHistoryButtonText));
        OnPropertyChanged(nameof(CategoryAllText));
        OnPropertyChanged(nameof(CategoryProfileText));
        OnPropertyChanged(nameof(CategoryAudioText));
        OnPropertyChanged(nameof(CategoryAppearanceText));
        OnPropertyChanged(nameof(CategoryIntegrationsText));
        OnPropertyChanged(nameof(CategorySystemText));
        OnPropertyChanged(nameof(CategoryStorageText));
        OnPropertyChanged(nameof(CategoryAboutText));
        OnPropertyChanged(nameof(IntegrationsSectionTitle));
        OnPropertyChanged(nameof(ModularUiTitle));
        OnPropertyChanged(nameof(ModularUiDesc));
        OnPropertyChanged(nameof(NavSectionTitle));
        OnPropertyChanged(nameof(ShowWaveTitle));
        OnPropertyChanged(nameof(ShowWaveDesc));
        OnPropertyChanged(nameof(ShowLikesTitle));
        OnPropertyChanged(nameof(ShowLikesDesc));
        OnPropertyChanged(nameof(ShowPlaylistsTitle));
        OnPropertyChanged(nameof(ShowPlaylistsDesc));
        OnPropertyChanged(nameof(ShowSearchTitle));
        OnPropertyChanged(nameof(ShowSearchDesc));
        OnPropertyChanged(nameof(PlayerModularTitle));
        OnPropertyChanged(nameof(ShowLyricsTitle));
        OnPropertyChanged(nameof(ShowLyricsDesc));
        OnPropertyChanged(nameof(ShowEqualizerTitle));
        OnPropertyChanged(nameof(ShowEqualizerDesc));
        OnPropertyChanged(nameof(ShowQualityBadgeTitle));
        OnPropertyChanged(nameof(ShowQualityDesc));
        OnPropertyChanged(nameof(CompactVolumeTitle));
        OnPropertyChanged(nameof(CompactVolumeDesc));
        OnPropertyChanged(nameof(AboutDescText));
        OnPropertyChanged(nameof(GitHubRepoButtonText));
        OnPropertyChanged(nameof(SwitchOnText));
        OnPropertyChanged(nameof(SwitchOffText));
        OnPropertyChanged(nameof(SwitchEnabledText));
        OnPropertyChanged(nameof(SwitchDisabledText));
        OnPropertyChanged(nameof(CheckUpdatesButtonText));
        OnPropertyChanged(nameof(DownloadUpdateButtonText));
        OnPropertyChanged(nameof(UpdateSectionTitle));
        OnPropertyChanged(nameof(ReleaseNotesTitle));

        if (AvailableUpdate != null)
        {
            UpdateReleaseNotes = AvailableUpdate.GetReleaseNotes(IsRussian);
            if (IsUpdateAvailable)
            {
                UpdateStatusText = IsRussian
                    ? $"Доступна новая версия: {NewVersionTag}!"
                    : $"New version available: {NewVersionTag}!";
            }
        }

        CalculateCacheSize();
    }

    private void UpdateOptionCollections()
    {
        bool wasInitializing = _isInitializingSettings;
        _isInitializingSettings = true;
        try
        {
            int qIdx = SelectedQualityIndex;
            QualityOptions.Clear();
            if (IsRussian)
            {
                QualityOptions.Add("AAC (192–256 kbps) • Рекомендуется");
                QualityOptions.Add("MP3 (320 kbps) • Постоянный битрейт");
                QualityOptions.Add("Наивысший доступный битрейт");
            }
            else
            {
                QualityOptions.Add("AAC (192–256 kbps) • Recommended");
                QualityOptions.Add("MP3 (320 kbps) • Constant Bitrate");
                QualityOptions.Add("Highest Bitrate Available");
            }
            SelectedQualityIndex = qIdx;

            int tIdx = SelectedThemeIndex;
            ThemeOptions.Clear();
            if (IsRussian)
            {
                ThemeOptions.Add("Тёмная классическая (Dark)");
                ThemeOptions.Add("OLED (Абсолютно чёрный)");
                ThemeOptions.Add("Глубокая полночь (Midnight)");
                ThemeOptions.Add("Тёплый графит (Sepia)");
                ThemeOptions.Add("Светлая тема (Light)");
                ThemeOptions.Add("Как в системе (Default)");
            }
            else
            {
                ThemeOptions.Add("Classic Dark");
                ThemeOptions.Add("OLED Pure Black");
                ThemeOptions.Add("Deep Midnight");
                ThemeOptions.Add("Warm Sepia");
                ThemeOptions.Add("Light Mode");
                ThemeOptions.Add("System Default");
            }
            SelectedThemeIndex = tIdx;

            int bIdx = SelectedBackdropIndex;
            BackdropOptions.Clear();
            if (IsRussian)
            {
                BackdropOptions.Add("Mica (Слюдяной стандарт)");
                BackdropOptions.Add("Mica Alt (Глубокий эффект)");
                BackdropOptions.Add("Desktop Acrylic (Размытое стекло)");
                BackdropOptions.Add("Сплошной цвет темы (Без размытия)");
            }
            else
            {
                BackdropOptions.Add("Mica (Standard translucency)");
                BackdropOptions.Add("Mica Alt (Deep backdrop)");
                BackdropOptions.Add("Desktop Acrylic (Blur glass)");
                BackdropOptions.Add("Solid Theme Background");
            }
            SelectedBackdropIndex = bIdx;

            int fIdx = SelectedFontIndex;
            FontOptions.Clear();
            if (IsRussian)
            {
                FontOptions.Add("Inter (Рекомендуемый, чёткий)");
                FontOptions.Add("Segoe UI (Системный)");
                FontOptions.Add("Yandex Sans (Фирменный)");
            }
            else
            {
                FontOptions.Add("Inter (Recommended, Crisp)");
                FontOptions.Add("Segoe UI (System Default)");
                FontOptions.Add("Yandex Sans (Signature)");
            }
            SelectedFontIndex = fIdx;

            int cIdx = SelectedCacheLimitIndex;
            CacheLimitOptions.Clear();
            if (IsRussian)
            {
                CacheLimitOptions.Add("512 МБ");
                CacheLimitOptions.Add("1 ГБ (Рекомендуется)");
                CacheLimitOptions.Add("2 ГБ");
                CacheLimitOptions.Add("4 ГБ");
                CacheLimitOptions.Add("8 ГБ");
                CacheLimitOptions.Add("16 ГБ");
                CacheLimitOptions.Add("Без ограничений");
            }
            else
            {
                CacheLimitOptions.Add("512 MB");
                CacheLimitOptions.Add("1 GB (Recommended)");
                CacheLimitOptions.Add("2 GB");
                CacheLimitOptions.Add("4 GB");
                CacheLimitOptions.Add("8 GB");
                CacheLimitOptions.Add("16 GB");
                CacheLimitOptions.Add("Unlimited");
            }
            SelectedCacheLimitIndex = cIdx;

            int pIdx = SelectedGameOverlayPositionIndex;
            GameOverlayPositionOptions.Clear();
            if (IsRussian)
            {
                GameOverlayPositionOptions.Add("Сверху справа");
                GameOverlayPositionOptions.Add("Сверху слева");
                GameOverlayPositionOptions.Add("Снизу справа");
                GameOverlayPositionOptions.Add("Снизу слева");
                GameOverlayPositionOptions.Add("Сверху по центру");
                GameOverlayPositionOptions.Add("Снизу по центру");
                GameOverlayPositionOptions.Add("Слева по центру");
                GameOverlayPositionOptions.Add("Справа по центру");
            }
            else
            {
                GameOverlayPositionOptions.Add("Top Right");
                GameOverlayPositionOptions.Add("Top Left");
                GameOverlayPositionOptions.Add("Bottom Right");
                GameOverlayPositionOptions.Add("Bottom Left");
                GameOverlayPositionOptions.Add("Top Center");
                GameOverlayPositionOptions.Add("Bottom Center");
                GameOverlayPositionOptions.Add("Middle Left");
                GameOverlayPositionOptions.Add("Middle Right");
            }
            SelectedGameOverlayPositionIndex = pIdx;
            NotifyPositionProperties();

            int slopBehIdx = SloplessBehaviorIndex;
            SloplessBehaviorOptions.Clear();
            if (IsRussian)
            {
                SloplessBehaviorOptions.Add("Ставить дизлайк и пропускать (Рекомендуется)");
                SloplessBehaviorOptions.Add("Ставить дизлайк, если нет лайка");
                SloplessBehaviorOptions.Add("Просто пропускать");
                SloplessBehaviorOptions.Add("Пропускать, если нет лайка");
                SloplessBehaviorOptions.Add("Ничего не делать (только бейджи)");
            }
            else
            {
                SloplessBehaviorOptions.Add("Dislike and skip (Recommended)");
                SloplessBehaviorOptions.Add("Dislike if not liked");
                SloplessBehaviorOptions.Add("Just skip");
                SloplessBehaviorOptions.Add("Skip if not liked");
                SloplessBehaviorOptions.Add("Do nothing (badges only)");
            }
            SloplessBehaviorIndex = slopBehIdx;

            int slopModeIdx = SloplessDetectionModeIndex;
            SloplessDetectionModeOptions.Clear();
            if (IsRussian)
            {
                SloplessDetectionModeOptions.Add("Баланс (Трек или артист > порога)");
                SloplessDetectionModeOptions.Add("Строгий (Любой след ИИ у артиста)");
                SloplessDetectionModeOptions.Add("Только по оценке трека");
            }
            else
            {
                SloplessDetectionModeOptions.Add("Balanced (Track score or artist ratio)");
                SloplessDetectionModeOptions.Add("Paranoid (Any artist AI track)");
                SloplessDetectionModeOptions.Add("Track AI score only");
            }
            SloplessDetectionModeIndex = slopModeIdx;
        }
        finally
        {
            _isInitializingSettings = wasInitializing;
        }
    }

    private void CalculateCacheSize()
    {
        try
        {
            long audioBytes = _audioCacheService.GetTotalCacheSizeBytes();
            int trackCount = _audioCacheService.GetCachedTrackCount();
            CachedTracksCountText = IsRussian
                ? $"{trackCount} трек(ов) в кэше"
                : (trackCount == 1 ? "1 track cached" : $"{trackCount} tracks cached");

            long totalBytes = 0;
            if (Directory.Exists(_appDataDir))
            {
                var dirInfo = new DirectoryInfo(_appDataDir);
                foreach (var file in dirInfo.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    totalBytes += file.Length;
                }
            }

            string currentCache = _audioCacheService.CacheDirectory;
            if (!string.IsNullOrEmpty(currentCache) &&
                Directory.Exists(currentCache) &&
                !currentCache.StartsWith(_appDataDir, StringComparison.OrdinalIgnoreCase))
            {
                var cacheDirInfo = new DirectoryInfo(currentCache);
                foreach (var file in cacheDirInfo.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    totalBytes += file.Length;
                }
            }

            string limitStr = _settingsService.CacheSizeLimitMb > 0
                ? (IsRussian ? $" / лимит {_settingsService.CacheSizeLimitMb} МБ" : $" / {_settingsService.CacheSizeLimitMb} MB limit")
                : (IsRussian ? " (без лимита)" : " (Unlimited)");

            CacheSizeText = $"{FormatBytes(totalBytes)} ({FormatBytes(audioBytes)} {(IsRussian ? "аудио" : "audio")}{limitStr})";
        }
        catch
        {
            CacheSizeText = "< 1 MB";
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }

    private void ShowNotification(string message)
    {
        NotificationMessage = message;
        HasNotification = true;
    }

    [RelayCommand]
    public void DismissNotification()
    {
        HasNotification = false;
        NotificationMessage = null;
    }
}
