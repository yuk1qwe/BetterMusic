using System.Reflection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.ApplicationModel.DataTransfer;
using YMusic.WinUI.Helpers;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;
using YMusic.WinUI.ViewModels;
using YMusic.WinUI.Views.Navigation;
using YMusic.WinUI.Views.Pages;

namespace YMusic.WinUI.Views;

public sealed partial class ShellWindow : Window
{
    public ShellViewModel ViewModel { get; }
    public Grid WindowRootGrid => RootGrid;
    private readonly PinnedNavManager _pinnedNavManager;

    public ShellWindow()
    {
        InitializeComponent();

        ViewModel = App.GetService<ShellViewModel>();

        _pinnedNavManager = new PinnedNavManager(
            NavView,
            App.GetService<IPinnedItemsService>(),
            () => ViewModel.IsAuthenticated,
            NavigateToTag,
            DispatcherQueue);

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        string iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (System.IO.File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
        }
        else
        {
            AppWindow.SetIcon("Assets/AppIcon.ico");
        }

        AppWindow.Resize(new Windows.Graphics.SizeInt32(1180, 780));

        if (Content is FrameworkElement rootElement)
        {
            rootElement.KeyDown += (s, e) =>
            {
                if (e.Key == Windows.System.VirtualKey.Escape)
                {
                    if (_pinnedNavManager.IsDragging)
                    {
                        _pinnedNavManager.CancelDrag();
                        e.Handled = true;
                        return;
                    }
                    if (ViewModel.PlayerBar.IsNowPlayingDrawerOpen)
                    {
                        ViewModel.PlayerBar.IsNowPlayingDrawerOpen = false;
                        e.Handled = true;
                    }
                }
                else if (e.Key == Windows.System.VirtualKey.F11)
                {
                    ToggleFullScreen();
                    e.Handled = true;
                }
                else if (e.Key == Windows.System.VirtualKey.Space && rootElement.XamlRoot != null)
                {
                    var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(rootElement.XamlRoot);
                    if (focused is not TextBox && focused is not PasswordBox && focused is not AutoSuggestBox && focused is not RichEditBox)
                    {
                        if (ViewModel.PlayerBar.HasTrack)
                        {
                            ViewModel.PlayerBar.TogglePlayPauseCommand.Execute(null);
                            e.Handled = true;
                        }
                    }
                }
                else if (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
                {
                    if (e.Key == Windows.System.VirtualKey.Right)
                    {
                        if (ViewModel.PlayerBar.HasTrack)
                        {
                            ViewModel.PlayerBar.NextCommand.Execute(null);
                            e.Handled = true;
                        }
                    }
                    else if (e.Key == Windows.System.VirtualKey.Left)
                    {
                        if (ViewModel.PlayerBar.HasTrack)
                        {
                            ViewModel.PlayerBar.PreviousCommand.Execute(null);
                            e.Handled = true;
                        }
                    }
                    else if (e.Key == Windows.System.VirtualKey.L)
                    {
                        if (ViewModel.PlayerBar.HasTrack)
                        {
                            ViewModel.PlayerBar.ToggleLikeCommand.Execute(null);
                            e.Handled = true;
                        }
                    }
                    else if (e.Key == Windows.System.VirtualKey.F || e.Key == Windows.System.VirtualKey.K)
                    {
                        NavigateToTag("Search");
                        NavAutoSuggestBox?.Focus(FocusState.Programmatic);
                        e.Handled = true;
                    }
                }
                else if (e.Key == Windows.System.VirtualKey.L && rootElement.XamlRoot != null)
                {
                    var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(rootElement.XamlRoot);
                    if (focused is not TextBox && focused is not PasswordBox && focused is not AutoSuggestBox && focused is not RichEditBox)
                    {
                        if (ViewModel.PlayerBar.HasTrack)
                        {
                            ViewModel.PlayerBar.ToggleNowPlayingDrawerCommand.Execute(null);
                            e.Handled = true;
                        }
                    }
                }
            };
        }

        UpdateTitleBarButtonColors(false);
        UpdatePlayerBarVisibility();

        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.IsAuthenticated))
            {
                DispatcherQueue.TryEnqueue(UpdatePlayerBarVisibility);
            }
        };

        var searchVm = App.GetService<SearchViewModel>();
        if (searchVm != null)
        {
            searchVm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SearchViewModel.Query))
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (NavAutoSuggestBox != null && NavAutoSuggestBox.Text != searchVm.Query)
                        {
                            NavAutoSuggestBox.Text = searchVm.Query ?? string.Empty;
                        }
                    });
                }
            };
        }

        ViewModel.PlayerBar.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(PlayerBarViewModel.IsNowPlayingDrawerOpen))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    bool isOpen = ViewModel.PlayerBar.IsNowPlayingDrawerOpen;
                    if (isOpen)
                    {
                        UpdatePlayerBarVisibility();
                        AppTitleBar.Opacity = 0.0;
                        AppTitleBar.IsHitTestVisible = false;
                        NowPlayingDrawer.UpdateTitleBarInsets();
                        NowPlayingDrawer.AnimateOpen();
                        SetTitleBar(NowPlayingDrawer.TitleBarDragRegion);
                        UpdateTitleBarButtonColors(true);
                    }
                    else
                    {
                        // Smoothly animate drawer closed, keeping DWM title bar stable without compositor lag
                        NowPlayingDrawer.AnimateClose(() =>
                        {
                            UpdatePlayerBarVisibility();
                            ResetTitleBar();
                        });
                    }
                });
            }
        };

        var loginVm = App.GetService<LoginViewModel>();
        loginVm.LoginSucceeded += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ViewModel.IsAuthenticated = true;
                ViewModel.CurrentUser = loginVm.CurrentUser;
                UpdatePlayerBarVisibility();
                _pinnedNavManager.RefreshPinnedMenuItems();
                NavView.SelectedItem = NavItemExplore;
                NavigateToTag("Explore");
            });
        };
        loginVm.LoggedOut += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ViewModel.IsAuthenticated = false;
                ViewModel.CurrentUser = null;
                UpdatePlayerBarVisibility();
                _pinnedNavManager.RefreshPinnedMenuItems();
                NavView.SelectedItem = NavItemAccount;
                NavigateToTag("Account");
            });
        };

        var trayService = App.GetService<ITrayNotificationService>();
        var settingsService = App.GetService<ISettingsService>();
        trayService.Initialize(this);
        Activated += (_, _) => trayService.InitializeThumbnailButtons();

        AppWindow.Closing += (sender, args) =>
        {
            if (!trayService.IsExplicitExit && settingsService.MinimizeToTrayOnClose)
            {
                args.Cancel = true;
                trayService.MinimizeToTray();
                App.GetService<IAudioPlayerService>()?.RequestSaveState();
                return;
            }

            try
            {
                SetTitleBar(null);
            }
            catch { }

            try
            {
                App.GetService<IAudioPlayerService>()?.SaveCurrentState();
            }
            catch { }
        };

        Closed += (_, _) =>
        {
            try
            {
                SetTitleBar(null);
            }
            catch { }

            try
            {
                App.GetService<IAudioPlayerService>()?.SaveCurrentState();
            }
            catch { }

            try
            {
                trayService.Dispose();
            }
            catch { }

            // Dispose audio graph, OBS server, Discord RPC and game overlay asynchronously
            // so that teardown never deadlocks or blocks the UI thread on exit
            _ = System.Threading.Tasks.Task.Run(() =>
            {
                try { App.GetService<IDiscordRpcService>()?.Dispose(); } catch { }
                try { App.GetService<IObsOverlayService>()?.Dispose(); } catch { }
                try { App.GetService<IGameOverlayService>()?.Dispose(); } catch { }
                try { App.GetService<IAudioPlayerService>()?.Dispose(); } catch { }
            });

            try
            {
                _pinnedNavManager.Dispose();
            }
            catch { }

            try
            {
                Application.Current.Exit();
            }
            catch { }

            Environment.Exit(0);
        };

        App.GetService<INavigationService>().Initialize(this);

        _pinnedNavManager.Initialize();
        _pinnedNavManager.RefreshPinnedMenuItems();

        InitializeNavigation();
    }

    private async void InitializeNavigation()
    {
        var settingsService = App.GetService<ISettingsService>();
        ApplyTheme(settingsService.AppTheme);
        ApplyFont(settingsService.AppFont);

        await ViewModel.InitializeAsync();

        if (ViewModel.IsAuthenticated)
        {
            NavView.SelectedItem = NavItemExplore;
            NavigateToTag("Explore");
        }
        else
        {
            NavView.SelectedItem = NavItemAccount;
            NavigateToTag("Account");
        }
    }

    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (_pinnedNavManager.IsRebuilding || _pinnedNavManager.IsDragging || (DateTime.UtcNow - _pinnedNavManager.LastDragEndTime) < TimeSpan.FromMilliseconds(700))
        {
            return;
        }

        string? tag = (args.InvokedItemContainer as NavigationViewItem)?.Tag?.ToString();
        if (string.IsNullOrEmpty(tag) && args.InvokedItem != null)
        {
            string itemStr = args.InvokedItem.ToString() ?? string.Empty;
            tag = itemStr switch
            {
                "Explore" or "Главное" => "Explore",
                "Search" or "Поиск" => "Search",
                "My Wave" or "Моя волна" => "Wave",
                "Liked Tracks" => "Likes",
                "Playlists" or "Playlists & Albums" => "Playlists",
                "Account / Sign In" => "Account",
                _ => null
            };
        }

        if (string.IsNullOrEmpty(tag))
        {
            return;
        }

        if (tag == "Explore" && ContentFrame.Content is ExplorePage explorePage && explorePage.ViewModel.IsDetailOpen)
        {
            explorePage.ViewModel.CloseDetail();
            return;
        }

        if (tag == "Playlists" && ContentFrame.Content is PlaylistsPage playlistsPage && playlistsPage.ViewModel.IsDetailOpen)
        {
            playlistsPage.ViewModel.CloseDetail();
            return;
        }

        NavigateToTag(tag);
    }

    private void NavAutoSuggestBox_GotFocus(object sender, RoutedEventArgs e)
    {
        UpdateNavAutoSuggestBoxSuggestions(sender as AutoSuggestBox);
    }

    private void NavAutoSuggestBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            string query = sender.Text ?? string.Empty;

            UpdateNavAutoSuggestBoxSuggestions(sender);

            if (!string.IsNullOrEmpty(query))
            {
                if (CurrentPageTag != "Search")
                {
                    NavigateToTag("Search");
                    sender.Focus(FocusState.Programmatic);
                }
            }

            var searchVm = App.GetService<SearchViewModel>();
            if (searchVm != null && searchVm.Query != query)
            {
                searchVm.Query = query;
            }
        }
    }

    private void UpdateNavAutoSuggestBoxSuggestions(AutoSuggestBox? box)
    {
        if (box == null) return;
        var searchVm = App.GetService<SearchViewModel>();
        if (searchVm == null) return;

        string query = box.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(query))
        {
            box.ItemsSource = searchVm.RecentQueries.ToList();
        }
        else
        {
            box.ItemsSource = searchVm.RecentQueries
                .Where(q => q.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    private void NavAutoSuggestBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is string chosen)
        {
            sender.Text = chosen;
            if (CurrentPageTag != "Search")
            {
                NavigateToTag("Search");
            }
            var searchVm = App.GetService<SearchViewModel>();
            searchVm?.SetQueryAndSearch(chosen);
        }
    }

    private void NavAutoSuggestBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        string query = args.QueryText?.Trim() ?? string.Empty;
        if (CurrentPageTag != "Search")
        {
            NavigateToTag("Search");
        }
        if (!string.IsNullOrWhiteSpace(query))
        {
            var searchVm = App.GetService<SearchViewModel>();
            searchVm?.SetQueryAndSearch(query);
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_pinnedNavManager.IsRebuilding || _pinnedNavManager.IsDragging || (DateTime.UtcNow - _pinnedNavManager.LastDragEndTime) < TimeSpan.FromMilliseconds(250))
        {
            return;
        }

        if (args.SelectedItem is NavigationViewItem item)
        {
            string? tag = item.Tag?.ToString();
            NavigateToTag(tag);
        }
    }

    public void NavigateToTag(string? tag)
    {
        if (string.IsNullOrEmpty(tag))
        {
            return;
        }

        if (tag.StartsWith("playlist:"))
        {
            var parts = tag.Split(':');
            if (parts.Length == 3 && long.TryParse(parts[1], out long uid) && int.TryParse(parts[2], out int kind))
            {
                var pinned = App.GetService<IPinnedItemsService>()?.GetPinnedItems().FirstOrDefault(p => p.Key == tag);
                var playlist = new Playlist
                {
                    Uid = uid,
                    Kind = kind,
                    Title = pinned?.Title ?? "Playlist",
                    Cover = pinned?.CoverUrl != null ? new Cover { Uri = pinned.CoverUrl } : null,
                    IsPinned = true
                };
                _ = App.GetService<INavigationService>().NavigateToPlaylistAsync(playlist, isFromPinned: true);
                return;
            }
        }
        else if (tag.StartsWith("album:"))
        {
            var parts = tag.Split(':');
            if (parts.Length == 2 && long.TryParse(parts[1], out long albumId))
            {
                var pinned = App.GetService<IPinnedItemsService>()?.GetPinnedItems().FirstOrDefault(p => p.Key == tag);
                var album = new Album
                {
                    Id = albumId,
                    Title = pinned?.Title ?? string.Empty,
                    CoverUri = pinned?.CoverUrl,
                    IsPinned = true
                };
                _ = App.GetService<INavigationService>().NavigateToAlbumAsync(album, isFromPinned: true);
                return;
            }
        }

        bool isUpdatesTarget = tag.Equals("Account:Updates", StringComparison.OrdinalIgnoreCase) ||
                               tag.Equals("Settings:Updates", StringComparison.OrdinalIgnoreCase);

        Type? targetPage = tag switch
        {
            "Explore" => typeof(ExplorePage),
            "Search" => typeof(SearchPage),
            "Wave" => typeof(WavePage),
            "Likes" => typeof(LikedTracksPage),
            "Playlists" => typeof(PlaylistsPage),
            "Account" or "Account:Updates" => ViewModel.IsAuthenticated ? typeof(ProfilePage) : typeof(LoginPage),
            "Profile" => typeof(ProfilePage),
            "Settings" or "Settings:Updates" => typeof(ProfilePage),
            "Artist" => typeof(ArtistPage),
            _ => null
        };

        if (targetPage != null)
        {
            try
            {
                NavigationViewItem? navItem = tag switch
                {
                    "Explore" => NavItemExplore,
                    "Search" => NavItemSearch,
                    "Wave" => NavItemWave,
                    "Likes" => NavItemLikes,
                    "Playlists" => NavItemPlaylists,
                    "Account" or "Profile" or "Settings" or "Account:Updates" or "Settings:Updates" => NavItemAccount,
                    _ => null
                };
                if (navItem != null && !ReferenceEquals(NavView.SelectedItem, navItem))
                {
                    NavView.SelectedItem = navItem;
                }
                else if (navItem == null)
                {
                    NavView.SelectedItem = null;
                }

                if (ContentFrame.CurrentSourcePageType != targetPage)
                {
                    if (tag == "Playlists")
                    {
                        var playlistsVm = App.GetService<PlaylistsViewModel>();
                        if (playlistsVm != null && playlistsVm.IsDetailOpen)
                        {
                            playlistsVm.CloseDetail();
                        }
                    }
                    else if (tag == "Explore")
                    {
                        var exploreVm = App.GetService<ExploreViewModel>();
                        if (exploreVm != null && exploreVm.IsDetailOpen)
                        {
                            exploreVm.CloseDetail();
                        }
                    }

                    ContentFrame.Navigate(targetPage, isUpdatesTarget ? "Updates" : null, new EntranceNavigationTransitionInfo());
                }
                else if (isUpdatesTarget && ContentFrame.Content is ProfilePage profilePage)
                {
                    profilePage.ScrollToUpdates();
                }
                else if (tag == "Playlists" && ContentFrame.Content is PlaylistsPage playlistsPage)
                {
                    var playlistsVm = App.GetService<PlaylistsViewModel>();
                    if (playlistsVm != null && playlistsVm.IsDetailOpen)
                    {
                        playlistsVm.CloseDetail();
                        playlistsPage.AnimateToBrowse();
                    }
                }
                else if (tag == "Explore" && ContentFrame.Content is ExplorePage explorePage)
                {
                    var exploreVm = App.GetService<ExploreViewModel>();
                    if (exploreVm != null && exploreVm.IsDetailOpen)
                    {
                        exploreVm.CloseDetail();
                        explorePage.AnimateToBrowse();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Navigation Error] {ex}");
                try
                {
                    string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    string dir = Path.Combine(appData, "YMusic");
                    Directory.CreateDirectory(dir);
                    File.AppendAllText(Path.Combine(dir, "app_debug.log"), $"[NavigateToTag Exception]: {ex}\n");
                }
                catch { }
            }
        }
    }

    public string? CurrentPageTag => ContentFrame.CurrentSourcePageType switch
    {
        Type t when t == typeof(ExplorePage) => "Explore",
        Type t when t == typeof(SearchPage) => "Search",
        Type t when t == typeof(WavePage) => "Wave",
        Type t when t == typeof(LikedTracksPage) => "Likes",
        Type t when t == typeof(PlaylistsPage) => "Playlists",
        Type t when t == typeof(LoginPage) || t == typeof(ProfilePage) => "Account",
        Type t when t == typeof(ArtistPage) => "Artist",
        _ => null
    };

    public void ApplyTheme(string theme)
    {
        var themeService = App.GetService<IThemeService>();
        if (themeService != null)
        {
            themeService.ApplyTheme(theme);
        }
        else
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                RootGrid.RequestedTheme = theme switch
                {
                    "Light" => ElementTheme.Light,
                    "Default" => ElementTheme.Default,
                    _ => ElementTheme.Dark
                };
            });
        }
    }

    public void ApplyFont(string font)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            FontFamily newFont = font switch
            {
                "System" => new FontFamily("Segoe UI Variable Display, Segoe UI"),
                "YandexSans" => new FontFamily("ms-appx:///Assets/Fonts/YSText-Regular.ttf#YS Text"),
                _ => new FontFamily("ms-appx:///Assets/Fonts/Inter.ttf#Inter")
            };

            NavView.FontFamily = newFont;
            ContentFrame.FontFamily = newFont;
            BottomPlayerBar.FontFamily = newFont;
            NowPlayingDrawer.FontFamily = newFont;

            try
            {
                App.Current.Resources["ContentControlThemeFontFamily"] = newFont;
            }
            catch { }
        });
    }

    public async Task NavigateToArtistAsync(Artist artist)
    {
        App.GetService<PlayerBarViewModel>().IsNowPlayingDrawerOpen = false;

        var artistVm = App.GetService<ArtistViewModel>();
        string? currentTag = CurrentPageTag;
        if (!string.IsNullOrEmpty(currentTag))
        {
            artistVm.PreviousPageTag = currentTag;
        }

        NavView.SelectedItem = null;
        if (ContentFrame.CurrentSourcePageType != typeof(ArtistPage))
        {
            ContentFrame.Navigate(typeof(ArtistPage), null, new EntranceNavigationTransitionInfo());
        }
        (ContentFrame.Content as ArtistPage)?.ResetScroll();

        await artistVm.LoadArtistAsync(artist);
    }

    public async Task NavigateToArtistByIdAsync(long artistId, string? artistName = null)
    {
        App.GetService<PlayerBarViewModel>().IsNowPlayingDrawerOpen = false;

        var artistVm = App.GetService<ArtistViewModel>();
        string? currentTag = CurrentPageTag;
        if (!string.IsNullOrEmpty(currentTag))
        {
            artistVm.PreviousPageTag = currentTag;
        }

        NavView.SelectedItem = null;
        if (ContentFrame.CurrentSourcePageType != typeof(ArtistPage))
        {
            ContentFrame.Navigate(typeof(ArtistPage), null, new EntranceNavigationTransitionInfo());
        }
        (ContentFrame.Content as ArtistPage)?.ResetScroll();

        await artistVm.LoadArtistByIdAsync(artistId, artistName);
    }

    public void SelectPlaylistsTab()
    {
        NavView.SelectedItem = NavItemPlaylists;
        NavigateToTag("Playlists");
    }

    public void ClearNavSelection()
    {
        NavView.SelectedItem = null;
    }

    public void SelectPinnedItem(string tag)
    {
        var item = NavView.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => i.Tag?.ToString() == tag);
        if (item != null)
        {
            NavView.SelectedItem = item;
        }
        else
        {
            NavView.SelectedItem = null;
        }
    }

    public bool IsPlaylistsPageActive => ContentFrame.CurrentSourcePageType == typeof(PlaylistsPage);

    public void EnsurePlaylistsPageVisible()
    {
        if (ContentFrame.CurrentSourcePageType != typeof(PlaylistsPage))
        {
            ContentFrame.Navigate(typeof(PlaylistsPage), null, new EntranceNavigationTransitionInfo());
        }
    }

    public PlaylistsPage? CurrentPlaylistsPage => ContentFrame.Content as PlaylistsPage;

    private void UpdatePlayerBarVisibility()
    {
        bool show = ViewModel.IsAuthenticated;
        BottomPlayerBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    public bool IsFullScreen => AppWindow.Presenter.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen;

    public void ToggleFullScreen()
    {
        if (AppWindow.Presenter.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen)
        {
            AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Default);
        }
        else
        {
            AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
        }
    }

    private void UpdateTitleBarButtonColors(bool isLyricsMode)
    {
        if (Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
        {
            var titleBar = AppWindow.TitleBar;
            if (isLyricsMode)
            {
                titleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
                titleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
                titleBar.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 235, 235, 235);
                titleBar.ButtonHoverForegroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
                titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(35, 255, 255, 255);
                titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(55, 255, 255, 255);
                titleBar.ButtonPressedForegroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
            }
            else
            {
                titleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
                titleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
                titleBar.ButtonForegroundColor = null;
                titleBar.ButtonHoverForegroundColor = null;
                titleBar.ButtonHoverBackgroundColor = null;
                titleBar.ButtonPressedBackgroundColor = null;
                titleBar.ButtonPressedForegroundColor = null;
            }
        }
    }

    private void UpdateInfoBar_CloseButtonClick(InfoBar sender, object args)
    {
        ViewModel.DismissUpdateBannerCommand.Execute(null);
    }

    public void ResetTitleBar()
    {
        SetTitleBar(AppTitleBar);
        AppTitleBar.Opacity = 1.0;
        AppTitleBar.IsHitTestVisible = true;
        UpdateTitleBarButtonColors(false);
    }
}
