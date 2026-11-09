#nullable enable

using System;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using YMusic.WinUI.Models;
using YMusic.WinUI.ViewModels;

namespace YMusic.WinUI.Views.Pages;

public sealed partial class ArtistPage : Page
{
    public ArtistViewModel ViewModel => App.GetService<ArtistViewModel>();

    public ArtistPage()
    {
        InitializeComponent();
    }

    public void ResetScroll()
    {
        MainScrollViewer?.ChangeView(null, 0, null, true);
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.GoBack();
    }

    private void FilterChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out int index))
        {
            ViewModel.SelectAlbumFilter(index);
        }
    }

    private void TrackRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Track track)
        {
            _ = ViewModel.PlayTrackAsync(track);
        }
    }

    private void TrackLikeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Track track)
        {
            _ = ViewModel.ToggleLikeTrackAsync(track);
        }
    }

    private void AlbumCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Album album)
        {
            _ = ViewModel.OpenAlbumAsync(album);
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
