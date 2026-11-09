using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using YMusic.WinUI.Models;
using YMusic.WinUI.ViewModels;

namespace YMusic.WinUI.Views.Pages;

public sealed partial class PlaylistsPage : Page
{
    public PlaylistsViewModel ViewModel { get; }

    private Storyboard? _currentTransition;
    private Storyboard? _tabTransition;
    private bool _isAnimating;
    private int _currentTabIndex = 0;
    private string? _currentDisplayedDetailKey;
    private DateTime _lastDetailOpenTime = DateTime.MinValue;
    private DateTime _lastPageNavigatedTime = DateTime.MinValue;

    private string? GetCurrentDetailKey()
    {
        if (ViewModel.SelectedAlbum != null)
        {
            return $"album:{ViewModel.SelectedAlbum.Id}";
        }
        if (ViewModel.SelectedPlaylist != null)
        {
            long effectiveUid = ViewModel.SelectedPlaylist.Uid > 0 ? ViewModel.SelectedPlaylist.Uid : (ViewModel.SelectedPlaylist.Owner?.Uid ?? 0);
            return $"playlist:{effectiveUid}:{ViewModel.SelectedPlaylist.Kind}:{ViewModel.SelectedPlaylist.Title}";
        }
        return null;
    }

    public PlaylistsPage()
    {
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        ViewModel = App.GetService<PlaylistsViewModel>();
        InitializeComponent();

        ApplyInitialViewState();

        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(PlaylistsViewModel.IsDetailOpen))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (ViewModel.IsDetailOpen)
                    {
                        ResetScrollAndHeader();
                        _lastDetailOpenTime = DateTime.UtcNow;
                        _currentDisplayedDetailKey = GetCurrentDetailKey();
                        PlaylistHeroGrid.Opacity = 1.0;
                        if (DetailViewRoot.Visibility != Visibility.Visible || DetailViewRoot.Opacity < 0.9)
                        {
                            AnimateToDetail();
                        }
                    }
                    else
                    {
                        _currentDisplayedDetailKey = null;
                        if (!string.IsNullOrEmpty(ViewModel.PreviousPageTag))
                        {
                            _currentTransition?.Stop();
                            _isAnimating = false;
                            DetailViewRoot.Visibility = Visibility.Collapsed;
                            DetailViewRoot.Opacity = 0;
                            BrowseViewRoot.Visibility = Visibility.Collapsed;
                            BrowseViewRoot.Opacity = 0;
                        }
                        else if (!_isAnimating && DetailViewRoot.Visibility == Visibility.Visible)
                        {
                            AnimateToBrowse();
                        }
                    }
                });
            }
            else if (e.PropertyName == nameof(PlaylistsViewModel.SelectedAlbum) ||
                     e.PropertyName == nameof(PlaylistsViewModel.SelectedPlaylist))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (!ViewModel.IsDetailOpen) return;

                    string? newKey = GetCurrentDetailKey();
                    if (string.IsNullOrEmpty(newKey)) return;

                    // If it's the exact same item (e.g. duplicate property change), do not re-animate
                    if (string.Equals(_currentDisplayedDetailKey, newKey, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    // If the page itself just entered via Frame transition (within 500ms),
                    // or detail view was opened recently from browse (within 500ms), do not play double animation!
                    if ((DateTime.UtcNow - _lastPageNavigatedTime) < TimeSpan.FromMilliseconds(500) ||
                        (DateTime.UtcNow - _lastDetailOpenTime) < TimeSpan.FromMilliseconds(500))
                    {
                        _currentDisplayedDetailKey = newKey;
                        PlaylistHeroGrid.Opacity = 1.0;
                        return;
                    }

                    _currentDisplayedDetailKey = newKey;
                    ResetScrollAndHeader();

                    if (DetailViewRoot.Visibility == Visibility.Visible && DetailViewRoot.Opacity >= 0.95 && !_isAnimating)
                    {
                        AnimateHeaderFadeIn();
                    }
                    else
                    {
                        PlaylistHeroGrid.Opacity = 1.0;
                    }
                });
            }
            else if (e.PropertyName == nameof(PlaylistsViewModel.SelectedCategoryIndex))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (!ViewModel.IsDetailOpen)
                    {
                        AnimateTabTransition(ViewModel.SelectedCategoryIndex);
                    }
                    else
                    {
                        _currentTabIndex = ViewModel.SelectedCategoryIndex;
                    }
                });
            }
        };
    }

    private void ApplyInitialViewState()
    {
        BrowseTranslateTransform.Y = 0.0;
        DetailTranslateTransform.Y = 0.0;
        PlaylistsTabTranslateTransform.X = 0.0;
        AlbumsTabTranslateTransform.X = 0.0;

        if (ViewModel.IsDetailOpen)
        {
            _currentDisplayedDetailKey = GetCurrentDetailKey();
            _lastDetailOpenTime = DateTime.UtcNow;
            BrowseViewRoot.Visibility = Visibility.Collapsed;
            BrowseViewRoot.Opacity = 0.0;

            DetailViewRoot.Visibility = Visibility.Visible;
            DetailViewRoot.Opacity = 1.0;
        }
        else
        {
            BrowseViewRoot.Visibility = Visibility.Visible;
            BrowseViewRoot.Opacity = 1.0;

            DetailViewRoot.Visibility = Visibility.Collapsed;
            DetailViewRoot.Opacity = 0.0;
        }

        if (ViewModel.SelectedCategoryIndex == 0)
        {
            PlaylistsTabContent.Visibility = Visibility.Visible;
            PlaylistsTabContent.Opacity = 1.0;

            AlbumsTabContent.Visibility = Visibility.Collapsed;
            AlbumsTabContent.Opacity = 0.0;
            _currentTabIndex = 0;
        }
        else
        {
            PlaylistsTabContent.Visibility = Visibility.Collapsed;
            PlaylistsTabContent.Opacity = 0.0;

            AlbumsTabContent.Visibility = Visibility.Visible;
            AlbumsTabContent.Opacity = 1.0;
            _currentTabIndex = 1;
        }
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _lastPageNavigatedTime = DateTime.UtcNow;
        PlaylistHeroGrid.Opacity = 1.0;

        if (ViewModel.IsDetailOpen)
        {
            DetailViewRoot.Visibility = Visibility.Visible;
            DetailViewRoot.Opacity = 1.0;
            BrowseViewRoot.Visibility = Visibility.Collapsed;
            BrowseViewRoot.Opacity = 0.0;
        }
        else
        {
            BrowseViewRoot.Visibility = Visibility.Visible;
            BrowseViewRoot.Opacity = 1.0;
            DetailViewRoot.Visibility = Visibility.Collapsed;
            DetailViewRoot.Opacity = 0.0;
        }
    }

    public void AnimateToDetail()
    {
        _currentTransition?.Stop();
        _isAnimating = true;

        DetailViewRoot.Visibility = Visibility.Visible;
        BrowseViewRoot.Visibility = Visibility.Visible;

        DetailTranslateTransform.Y = 0.0;
        BrowseTranslateTransform.Y = 0.0;

        PlaylistHeroGrid.Opacity = 1.0;

        var sb = new Storyboard();

        // 1. Detail Fade In (0 -> 1)
        var detailFade = new DoubleAnimation
        {
            From = DetailViewRoot.Opacity,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(detailFade, DetailViewRoot);
        Storyboard.SetTargetProperty(detailFade, "Opacity");
        sb.Children.Add(detailFade);

        // 2. Browse Fade Out (1 -> 0)
        var browseFade = new DoubleAnimation
        {
            From = BrowseViewRoot.Opacity,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(150),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(browseFade, BrowseViewRoot);
        Storyboard.SetTargetProperty(browseFade, "Opacity");
        sb.Children.Add(browseFade);

        sb.Completed += (s, e) =>
        {
            BrowseViewRoot.Visibility = Visibility.Collapsed;
            _isAnimating = false;
            _currentTransition = null;
        };

        _currentTransition = sb;
        sb.Begin();
    }

    public void AnimateHeaderFadeIn()
    {
        _currentTransition?.Stop();
        PlaylistHeroGrid.Opacity = 0.2;

        var sb = new Storyboard();
        var heroFade = new DoubleAnimation
        {
            From = 0.2,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(heroFade, PlaylistHeroGrid);
        Storyboard.SetTargetProperty(heroFade, "Opacity");
        sb.Children.Add(heroFade);

        sb.Completed += (s, e) =>
        {
            PlaylistHeroGrid.Opacity = 1.0;
            _currentTransition = null;
        };

        _currentTransition = sb;
        sb.Begin();
    }

    public void AnimateToBrowse(Action? onCompleted = null)
    {
        _currentTransition?.Stop();
        _isAnimating = true;

        BrowseViewRoot.Visibility = Visibility.Visible;
        DetailViewRoot.Visibility = Visibility.Visible;

        BrowseTranslateTransform.Y = 0.0;
        DetailTranslateTransform.Y = 0.0;

        var sb = new Storyboard();

        // 1. Detail Fade Out (1 -> 0)
        var detailFade = new DoubleAnimation
        {
            From = DetailViewRoot.Opacity,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(150),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(detailFade, DetailViewRoot);
        Storyboard.SetTargetProperty(detailFade, "Opacity");
        sb.Children.Add(detailFade);

        // 2. Browse Fade In (0 -> 1)
        var browseFade = new DoubleAnimation
        {
            From = BrowseViewRoot.Opacity,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(browseFade, BrowseViewRoot);
        Storyboard.SetTargetProperty(browseFade, "Opacity");
        sb.Children.Add(browseFade);

        sb.Completed += (s, e) =>
        {
            DetailViewRoot.Visibility = Visibility.Collapsed;
            _isAnimating = false;
            _currentTransition = null;
            onCompleted?.Invoke();
        };

        _currentTransition = sb;
        sb.Begin();
    }

    private void DetailBackButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(ViewModel.PreviousPageTag))
        {
            _currentTransition?.Stop();
            _isAnimating = false;
            DetailViewRoot.Visibility = Visibility.Collapsed;
            DetailViewRoot.Opacity = 0;
            BrowseViewRoot.Visibility = Visibility.Collapsed;
            BrowseViewRoot.Opacity = 0;

            ViewModel.CloseDetailCommand.Execute(null);
            return;
        }

        AnimateToBrowse(() =>
        {
            ViewModel.CloseDetailCommand.Execute(null);
        });
    }

    public void AnimateTabTransition(int targetIndex)
    {
        if (_currentTabIndex == targetIndex) return;

        int fromIndex = _currentTabIndex;
        _currentTabIndex = targetIndex;

        _tabTransition?.Stop();

        var outgoing = fromIndex == 0 ? PlaylistsTabContent : AlbumsTabContent;
        var outgoingTransform = fromIndex == 0 ? PlaylistsTabTranslateTransform : AlbumsTabTranslateTransform;

        var incoming = targetIndex == 0 ? PlaylistsTabContent : AlbumsTabContent;
        var incomingTransform = targetIndex == 0 ? PlaylistsTabTranslateTransform : AlbumsTabTranslateTransform;

        incoming.Visibility = Visibility.Visible;
        incoming.Opacity = 0.0;

        outgoingTransform.X = 0;
        incomingTransform.X = 0;

        var sb = new Storyboard();

        // 1. Outgoing Fade Out
        var outFade = new DoubleAnimation
        {
            From = outgoing.Opacity,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(140),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(outFade, outgoing);
        Storyboard.SetTargetProperty(outFade, "Opacity");
        sb.Children.Add(outFade);

        // 2. Incoming Fade In
        var inFade = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(190),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(inFade, incoming);
        Storyboard.SetTargetProperty(inFade, "Opacity");
        sb.Children.Add(inFade);

        sb.Completed += (s, e) =>
        {
            outgoing.Visibility = Visibility.Collapsed;
            outgoing.Opacity = 0.0;
            incoming.Opacity = 1.0;
            _tabTransition = null;
        };

        _tabTransition = sb;
        sb.Begin();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Playlists.Count == 0 && ViewModel.LikedAlbums.Count == 0)
        {
            await ViewModel.LoadPlaylistsAsync();
        }
        else if (!ViewModel.IsDetailOpen)
        {
            // Background refresh to guarantee the user never needs to press any reload button
            _ = ViewModel.LoadPlaylistsAsync();
        }
    }

    private void CategoryPlaylists_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectCategory("playlists");
    }

    private void CategoryAlbums_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectCategory("albums");
    }

    private async void PlaylistCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Playlist playlist)
        {
            ViewModel.IsBackButtonVisible = true;
            TracksListView.ScrollToTop();
            await ViewModel.OpenPlaylistCommand.ExecuteAsync(playlist);
        }
    }

    private async void DirectPlayPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Playlist playlist)
        {
            await ViewModel.PlayPlaylistDirectCommand.ExecuteAsync(playlist);
        }
    }

    private async void AlbumCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Album album)
        {
            ViewModel.IsBackButtonVisible = true;
            TracksListView.ScrollToTop();
            await ViewModel.OpenAlbumCommand.ExecuteAsync(album);
        }
    }

    private async void DirectPlayAlbum_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is Album album)
        {
            await ViewModel.PlayAlbumDirectCommand.ExecuteAsync(album);
        }
    }

    private async void TrackListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Track track)
        {
            if (!track.Available)
            {
                var isEn = App.Loc?.CurrentLanguage == "en";
                App.GetService<PlayerBarViewModel>()?.ShowToast(isEn
                    ? "This track will be available on the album's release day"
                    : "Этот трек станет доступен в день выхода альбома");
                return;
            }
            await ViewModel.PlayTrackCommand.ExecuteAsync(track);
        }
    }

    private void TrackLikeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Track track)
        {
            ViewModel.ToggleLikeTrackCommand.Execute(track);
        }
    }

    private void PinPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is Playlist playlist)
        {
            ViewModel.TogglePlaylistPinCommand.Execute(playlist);
        }
    }

    private void TogglePlaylistPinContext_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is Playlist playlist)
        {
            ViewModel.TogglePlaylistPinCommand.Execute(playlist);
        }
    }

    private void TogglePlaylistLikeContext_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is Playlist playlist)
        {
            _ = ViewModel.TogglePlaylistLikeCommand.ExecuteAsync(playlist);
        }
    }

    private void EditPlaylistContext_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is Playlist playlist)
        {
            ViewModel.OpenEditPlaylistDialogCommand.Execute(playlist);
        }
    }

    private void DeletePlaylistContext_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is Playlist playlist)
        {
            ViewModel.OpenDeletePlaylistDialogCommand.Execute(playlist);
        }
    }

    private void PinAlbum_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is Album album)
        {
            ViewModel.ToggleAlbumPinCommand.Execute(album);
        }
    }

    private void ToggleAlbumPinContext_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is Album album)
        {
            ViewModel.ToggleAlbumPinCommand.Execute(album);
        }
    }

    private void ToggleAlbumLikeContext_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is Album album)
        {
            _ = ViewModel.ToggleAlbumLikeCommand.ExecuteAsync(album);
        }
    }

    public void ResetScrollAndHeader()
    {
        TracksListView?.ScrollToTop();
    }

    private async void TrackAlbum_Click(object sender, RoutedEventArgs e)
    {
        if (sender is HyperlinkButton button && button.Tag is Track track)
        {
            var album = track.Albums?.FirstOrDefault();
            if (album != null && album.Id > 0)
            {
                await App.GetService<YMusic.WinUI.Services.Interfaces.INavigationService>().NavigateToAlbumAsync(album);
            }
            else if (!string.IsNullOrEmpty(track.Id) && track.Id.Contains(':'))
            {
                var parts = track.Id.Split(':');
                if (parts.Length > 1 && long.TryParse(parts[1], out long albumId) && albumId > 0)
                {
                    await App.GetService<YMusic.WinUI.Services.Interfaces.INavigationService>().NavigateToAlbumByIdAsync(albumId, track.AlbumTitle);
                }
            }
        }
    }

    private async void TrackArtist_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement anchor && anchor.Tag is Track track)
        {
            await YMusic.WinUI.Helpers.ArtistNavigationHelper.NavigateToTrackArtistAsync(anchor, track);
        }
    }
}
