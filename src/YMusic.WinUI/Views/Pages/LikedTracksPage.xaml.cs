#nullable enable

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using YMusic.WinUI.Models;
using YMusic.WinUI.ViewModels;

namespace YMusic.WinUI.Views.Pages;

public sealed partial class LikedTracksPage : Page
{
    public LikedTracksViewModel ViewModel { get; }

    public LikedTracksPage()
    {
        ViewModel = App.GetService<LikedTracksViewModel>();
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.HasLoaded && !ViewModel.IsBusy)
        {
            await ViewModel.LoadTracksAsync();
        }
    }

    private async void TrackListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Track track)
        {
            await ViewModel.PlayTrackCommand.ExecuteAsync(track);
        }
    }

    private async void UnlikeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Track track)
        {
            await ViewModel.UnlikeTrackCommand.ExecuteAsync(track);
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

    private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SearchFilter = string.Empty;
    }
}
