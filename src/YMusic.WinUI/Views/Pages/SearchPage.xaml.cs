#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using YMusic.WinUI.Models;
using YMusic.WinUI.ViewModels;

namespace YMusic.WinUI.Views.Pages;

public sealed partial class SearchPage : Page
{
    public SearchViewModel ViewModel => App.GetService<SearchViewModel>();

    public SearchPage()
    {
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        var focused = XamlRoot != null ? Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot) : null;
        if (focused is AutoSuggestBox) return;

        if (string.IsNullOrWhiteSpace(ViewModel.Query))
        {
            SearchInputBox.Focus(FocusState.Programmatic);
        }
    }

    public CornerRadius BestResultCoverRadius(string? type)
    {
        return string.Equals(type, "artist", StringComparison.OrdinalIgnoreCase)
            ? new CornerRadius(55)
            : new CornerRadius(10);
    }

    private void SearchInputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            _ = ViewModel.SearchAsync();
            e.Handled = true;
        }
    }

    private void TabButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out int index))
        {
            ViewModel.SelectTab(index);
        }
    }

    private void RecentQueryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string query)
        {
            ViewModel.UseRecentQuery(query);
        }
    }

    private void DeleteRecentQueryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string query)
        {
            ViewModel.RemoveRecentQuery(query);
        }
    }

    private void SuggestionChipButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string chip)
        {
            ViewModel.UseSuggestionChip(chip);
        }
    }

    private void SeeAllTracks_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectTab(1);
    }

    private void SeeAllArtists_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectTab(2);
    }

    private void SeeAllAlbums_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectTab(3);
    }

    private void SeeAllPlaylists_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectTab(4);
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

    private void ArtistCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Artist artist)
        {
            _ = ViewModel.OpenArtistAsync(artist);
        }
    }

    private void AlbumCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Album album)
        {
            _ = ViewModel.OpenAlbumAsync(album);
        }
    }

    private void PlaylistCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Playlist playlist)
        {
            _ = ViewModel.OpenPlaylistAsync(playlist);
        }
    }

    private void BestResultCard_Click(object sender, RoutedEventArgs e)
    {
        _ = ViewModel.PlayBestResultAsync();
    }

    private void BestResultPreviewRow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.ActualWidth > 0)
        {
            int maxIcons = Math.Max(1, (int)((elem.ActualWidth + 8) / 48));
            ViewModel.UpdateVisiblePreviewCovers(maxIcons);
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
