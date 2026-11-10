using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using YMusic.WinUI.Models;
using YMusic.WinUI.ViewModels;

namespace YMusic.WinUI.Views.Pages;

public sealed partial class ExplorePage : Page
{
    public ExploreViewModel ViewModel { get; }

    private Storyboard? _currentTransition;
    private bool _isAnimating;
    private string? _currentDisplayedDetailKey;
    private DateTime _lastDetailOpenTime = DateTime.MinValue;
    private DateTime _lastPageNavigatedTime = DateTime.MinValue;

    private string? GetCurrentDetailKey()
    {
        if (ViewModel.ActiveAlbum != null)
        {
            return $"album:{ViewModel.ActiveAlbum.Id}";
        }
        if (ViewModel.ActivePlaylist != null)
        {
            long effectiveUid = ViewModel.ActivePlaylist.Uid > 0 ? ViewModel.ActivePlaylist.Uid : (ViewModel.ActivePlaylist.Owner?.Uid ?? 0);
            return $"playlist:{effectiveUid}:{ViewModel.ActivePlaylist.Kind}:{ViewModel.ActivePlaylist.Title}";
        }
        return null;
    }

    public ExplorePage()
    {
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        InitializeComponent();
        ViewModel = App.GetService<ExploreViewModel>();

        ApplyInitialViewState();

        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ExploreViewModel.IsDetailOpen))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (ViewModel.IsDetailOpen)
                    {
                        _lastDetailOpenTime = DateTime.UtcNow;
                        _currentDisplayedDetailKey = GetCurrentDetailKey();
                        ResetDetailScroll();
                        if (DetailViewRoot.Visibility != Visibility.Visible || DetailViewRoot.Opacity < 0.9)
                        {
                            AnimateToDetail();
                        }
                    }
                    else
                    {
                        _currentDisplayedDetailKey = null;
                        if (!_isAnimating && DetailViewRoot.Visibility == Visibility.Visible)
                        {
                            AnimateToBrowse();
                        }
                    }
                });
            }
            else if (e.PropertyName == nameof(ExploreViewModel.ActiveAlbum) ||
                     e.PropertyName == nameof(ExploreViewModel.ActivePlaylist))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (!ViewModel.IsDetailOpen) return;

                    string? newKey = GetCurrentDetailKey();
                    if (string.IsNullOrEmpty(newKey)) return;

                    // If it's the exact same item, don't replay transition
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
                        DetailHeroGrid.Opacity = 1.0;
                        return;
                    }

                    _currentDisplayedDetailKey = newKey;
                    ResetDetailScroll();

                    if (DetailViewRoot.Visibility == Visibility.Visible && DetailViewRoot.Opacity >= 0.95 && !_isAnimating)
                    {
                        AnimateHeaderFadeIn();
                    }
                    else
                    {
                        DetailHeroGrid.Opacity = 1.0;
                    }
                });
            }
        };
    }

    private void ApplyInitialViewState()
    {
        BrowseTranslateTransform.Y = 0.0;
        DetailTranslateTransform.Y = 0.0;

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
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _lastPageNavigatedTime = DateTime.UtcNow;
        DetailHeroGrid.Opacity = 1.0;

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

        DetailHeroGrid.Opacity = 1.0;

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
        DetailHeroGrid.Opacity = 0.2;

        var sb = new Storyboard();
        var heroFade = new DoubleAnimation
        {
            From = 0.2,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(heroFade, DetailHeroGrid);
        Storyboard.SetTargetProperty(heroFade, "Opacity");
        sb.Children.Add(heroFade);

        sb.Completed += (s, e) =>
        {
            DetailHeroGrid.Opacity = 1.0;
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
        AnimateToBrowse(() =>
        {
            ViewModel.CloseDetailCommand.Execute(null);
        });
    }

    public void ResetDetailScroll()
    {
        DetailTracksListView.ScrollToTop();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.HasLoaded)
        {
            await ViewModel.LoadExploreDataCommand.ExecuteAsync(null);
        }
    }

    private void PersonalPlaylistCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Playlist playlist)
        {
            ViewModel.OpenPlaylistCommand.Execute(playlist);
        }
    }

    private void AlbumCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Album album)
        {
            ViewModel.OpenAlbumCommand.Execute(album);
        }
    }

    private void EditorialPlaylistCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Playlist playlist)
        {
            ViewModel.OpenPlaylistCommand.Execute(playlist);
        }
    }

    private void DirectPlayPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Playlist playlist)
        {
            ViewModel.PlayPlaylistDirectCommand.Execute(playlist);
        }
    }

    private void DirectPlayAlbum_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Album album)
        {
            ViewModel.PlayAlbumDirectCommand.Execute(album);
        }
    }

    private void TogglePlaylistLikeContext_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is Playlist playlist)
        {
            _ = ViewModel.TogglePlaylistLikeCommand.ExecuteAsync(playlist);
        }
    }

    private void ToggleAlbumLikeContext_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is Album album)
        {
            _ = ViewModel.ToggleAlbumLikeCommand.ExecuteAsync(album);
        }
    }

    private void ChartTrackRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Track track)
        {
            ViewModel.PlayTrackCommand.Execute(track);
        }
    }

    private void DetailTrack_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Track track)
        {
            ViewModel.PlayTrackCommand.Execute(track);
        }
    }

    private void DetailLikeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Track track)
        {
            ViewModel.ToggleLikeTrackCommand.Execute(track);
        }
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
