#nullable enable

using CommunityToolkit.Mvvm.ComponentModel;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;

using CommunityToolkit.Mvvm.Input;
using System;
using System.Threading.Tasks;

namespace YMusic.WinUI.ViewModels;

public sealed partial class ShellViewModel : BaseViewModel
{
    private readonly ICredentialStorageService _credentialStorage;
    private readonly IYandexMusicService _musicService;
    private readonly IUpdateService _updateService;
    private readonly INavigationService _navigationService;
    private readonly ISettingsService _settingsService;
    private UpdateInfo? _detectedUpdate;

    [ObservableProperty]
    private PlayerBarViewModel _playerBar;

    [ObservableProperty]
    private UserProfile? _currentUser;

    [ObservableProperty]
    private bool _isAuthenticated;

    [ObservableProperty]
    private bool _isUpdateBannerOpen;

    [ObservableProperty]
    private string _updateBannerMessage = string.Empty;

    [ObservableProperty]
    private string _updateBannerVersion = string.Empty;

    private bool IsRussian => Loc?.CurrentLanguage != "en";

    public string UpdateBannerTitle => IsRussian ? "Доступно обновление" : "Update Available";
    public string UpdateBannerButtonText => IsRussian ? "Обновить" : "Update";

    public string AccountNavItemTitle => IsAuthenticated
        ? (!string.IsNullOrWhiteSpace(CurrentUser?.DisplayName) ? CurrentUser.DisplayName : (IsRussian ? "Профиль и настройки" : "Profile & Settings"))
        : (IsRussian ? "Войти в аккаунт" : "Sign In");

    public string NavExploreText => IsRussian ? "Главное" : "Home";
    public string NavSearchText => IsRussian ? "Поиск" : "Search";
    public string NavWaveText => IsRussian ? "Моя волна" : "My Wave";
    public string NavLikesText => IsRussian ? "Любимые треки" : "Liked Tracks";
    public string NavPlaylistsText => IsRussian ? "Плейлисты и альбомы" : "Playlists & Albums";
    public string SearchPlaceholderText => IsRussian ? "Поиск треков, артистов, альбомов..." : "Search tracks, artists, albums...";

    public bool ShowWaveInNav => _settingsService.ShowWaveInNav;
    public bool ShowLikesInNav => _settingsService.ShowLikesInNav;
    public bool ShowPlaylistsInNav => _settingsService.ShowPlaylistsInNav;
    public bool ShowSearchInNav => _settingsService.ShowSearchInNav;

    public Microsoft.UI.Xaml.Visibility WaveNavVisibility => _settingsService.ShowWaveInNav ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility LikesNavVisibility => _settingsService.ShowLikesInNav ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility PlaylistsNavVisibility => _settingsService.ShowPlaylistsInNav ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility SearchNavVisibility => _settingsService.ShowSearchInNav ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    partial void OnCurrentUserChanged(UserProfile? value) => OnPropertyChanged(nameof(AccountNavItemTitle));
    partial void OnIsAuthenticatedChanged(bool value) => OnPropertyChanged(nameof(AccountNavItemTitle));

    protected override void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(AccountNavItemTitle));
        OnPropertyChanged(nameof(NavExploreText));
        OnPropertyChanged(nameof(NavSearchText));
        OnPropertyChanged(nameof(NavWaveText));
        OnPropertyChanged(nameof(NavLikesText));
        OnPropertyChanged(nameof(NavPlaylistsText));
        OnPropertyChanged(nameof(SearchPlaceholderText));
        OnPropertyChanged(nameof(UpdateBannerTitle));
        OnPropertyChanged(nameof(UpdateBannerButtonText));
    }

    public ShellViewModel(
        PlayerBarViewModel playerBar,
        ICredentialStorageService credentialStorage,
        IYandexMusicService musicService,
        IUpdateService updateService,
        INavigationService navigationService,
        ISettingsService settingsService)
    {
        _playerBar = playerBar;
        _credentialStorage = credentialStorage;
        _musicService = musicService;
        _updateService = updateService;
        _navigationService = navigationService;
        _settingsService = settingsService;
        _isAuthenticated = _credentialStorage.HasToken();

        _settingsService.SettingChanged += (s, prop) =>
        {
            if (prop == nameof(ISettingsService.ShowWaveInNav))
            {
                OnPropertyChanged(nameof(ShowWaveInNav));
                OnPropertyChanged(nameof(WaveNavVisibility));
            }
            else if (prop == nameof(ISettingsService.ShowLikesInNav))
            {
                OnPropertyChanged(nameof(ShowLikesInNav));
                OnPropertyChanged(nameof(LikesNavVisibility));
            }
            else if (prop == nameof(ISettingsService.ShowPlaylistsInNav))
            {
                OnPropertyChanged(nameof(ShowPlaylistsInNav));
                OnPropertyChanged(nameof(PlaylistsNavVisibility));
            }
            else if (prop == nameof(ISettingsService.ShowSearchInNav))
            {
                OnPropertyChanged(nameof(ShowSearchInNav));
                OnPropertyChanged(nameof(SearchNavVisibility));
            }
        };
    }

    [RelayCommand]
    public void OpenUpdates()
    {
        IsUpdateBannerOpen = false;
        var profileVm = App.GetService<ProfileViewModel>();
        if (profileVm != null)
        {
            profileVm.ShouldScrollToUpdates = true;
            if (_detectedUpdate != null && _detectedUpdate.IsUpdateAvailable)
            {
                profileVm.SetAvailableUpdate(_detectedUpdate);
            }
            else
            {
                _ = profileVm.CheckForUpdatesAsync();
            }
        }
        _navigationService.NavigateToTag("Account:Updates");
    }

    [RelayCommand]
    public void DismissUpdateBanner()
    {
        IsUpdateBannerOpen = false;
    }

    public async Task InitializeAsync()
    {
        if (_credentialStorage.HasToken())
        {
            try
            {
                string? storedToken = _credentialStorage.GetToken();
                if (!string.IsNullOrWhiteSpace(storedToken))
                {
                    _musicService.SetOAuthToken(storedToken);
                }

                var profile = await _musicService.GetAccountStatusAsync();
                RunOnUi(() =>
                {
                    if (profile != null)
                    {
                        CurrentUser = profile;
                        IsAuthenticated = true;
                    }
                    else
                    {
                        // Maintain authentication if valid token is stored
                        IsAuthenticated = !string.IsNullOrWhiteSpace(_credentialStorage.GetToken());
                    }
                });

                if (profile != null)
                {
                    _ = _musicService.EnsureLikedTracksLoadedAsync();

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var profileVm = App.GetService<ProfileViewModel>();
                            await profileVm.InitializeAsync();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ShellViewModel] Profile preloading error: {ex.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ShellViewModel Initialize warning] {ex.Message}");
                RunOnUi(() => IsAuthenticated = !string.IsNullOrWhiteSpace(_credentialStorage.GetToken()));
            }
        }
        else
        {
            RunOnUi(() => IsAuthenticated = false);
        }

        // Check for updates in background (non-blocking)
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(2500);
                var update = await _updateService.CheckForUpdatesAsync();
                if (update.IsUpdateAvailable)
                {
                    _detectedUpdate = update;
                    RunOnUi(() =>
                    {
                        UpdateBannerVersion = $"v{update.LatestVersion}";
                        UpdateBannerMessage = IsRussian
                            ? $"Доступна новая версия WinYandex {UpdateBannerVersion}!"
                            : $"New version WinYandex {UpdateBannerVersion} is available!";
                        IsUpdateBannerOpen = true;
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ShellViewModel Update check error] {ex.Message}");
            }
        });
    }
}
