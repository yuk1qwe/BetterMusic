#nullable enable

using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;
using YMusic.WinUI.ViewModels;
using YMusic.WinUI.Views;
using YMusic.WinUI.Views.Pages;

namespace YMusic.WinUI.Services.Implementations;

public sealed class NavigationService : INavigationService
{
    private ShellWindow? _shellWindow;

    public void Initialize(ShellWindow shellWindow)
    {
        _shellWindow = shellWindow;
    }

    public void NavigateToTag(string tag)
    {
        void Execute()
        {
            _shellWindow?.NavigateToTag(tag);
        }

        var dispatcher = App.MainDispatcherQueue;
        if (dispatcher != null && !dispatcher.HasThreadAccess)
        {
            dispatcher.TryEnqueue(Execute);
        }
        else
        {
            Execute();
        }
    }

    public async Task NavigateToAlbumAsync(Album album, bool isFromPinned = false)
    {
        if (album == null || _shellWindow == null) return;

        var playlistsVm = App.GetService<PlaylistsViewModel>();

        // If this album is already open and visible on PlaylistsPage, do nothing
        if (playlistsVm.IsDetailOpen && playlistsVm.SelectedAlbum?.Id == album.Id &&
            _shellWindow.IsPlaylistsPageActive)
        {
            if (isFromPinned)
            {
                playlistsVm.IsBackButtonVisible = false;
            }
            return;
        }

        var tcs = new TaskCompletionSource();

        void Execute()
        {
            try
            {
                playlistsVm.SelectedCategoryIndex = 1; // Albums tab

                async System.Threading.Tasks.Task LoadAsync()
                {
                    try
                    {
                        await playlistsVm.OpenAlbumAsync(album);
                        _shellWindow.CurrentPlaylistsPage?.ResetScrollAndHeader();
                        tcs.TrySetResult();
                    }
                    catch (Exception loadEx)
                    {
                        tcs.TrySetException(loadEx);
                    }
                }

                _ = LoadAsync();

                if (isFromPinned)
                {
                    playlistsVm.IsBackButtonVisible = false;
                    playlistsVm.PreviousPageTag = null;
                    _shellWindow.EnsurePlaylistsPageVisible();
                    _shellWindow.SelectPinnedItem($"album:{album.Id}");
                }
                else
                {
                    playlistsVm.IsBackButtonVisible = true;
                    string? currentTag = _shellWindow.CurrentPageTag;
                    if (!string.IsNullOrEmpty(currentTag) && currentTag != "Playlists")
                    {
                        playlistsVm.PreviousPageTag = currentTag;
                        _shellWindow.ClearNavSelection();
                        _shellWindow.EnsurePlaylistsPageVisible();
                    }
                    else
                    {
                        playlistsVm.PreviousPageTag = null;
                        _shellWindow.SelectPlaylistsTab();
                    }
                }
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }

        var dispatcher = App.MainDispatcherQueue;
        if (dispatcher != null && !dispatcher.HasThreadAccess)
        {
            dispatcher.TryEnqueue(Execute);
        }
        else
        {
            Execute();
        }

        await tcs.Task;
    }

    public async Task NavigateToAlbumByIdAsync(long albumId, string? albumTitle = null)
    {
        var album = new Album
        {
            Id = albumId,
            Title = albumTitle ?? string.Empty
        };
        await NavigateToAlbumAsync(album);
    }

    public async Task NavigateToPlaylistAsync(Playlist playlist, bool isFromPinned = false)
    {
        if (playlist == null || _shellWindow == null) return;

        var playlistsVm = App.GetService<PlaylistsViewModel>();

        // If this playlist is already open and visible on PlaylistsPage, do nothing
        if (playlistsVm.IsDetailOpen &&
            playlistsVm.SelectedPlaylist?.Uid == playlist.Uid &&
            playlistsVm.SelectedPlaylist?.Kind == playlist.Kind &&
            _shellWindow.IsPlaylistsPageActive)
        {
            if (isFromPinned)
            {
                playlistsVm.IsBackButtonVisible = false;
            }
            return;
        }

        var tcs = new TaskCompletionSource();

        void Execute()
        {
            try
            {
                playlistsVm.SelectedCategoryIndex = 0; // Playlists tab

                async System.Threading.Tasks.Task LoadAsync()
                {
                    try
                    {
                        await playlistsVm.OpenPlaylistAsync(playlist);
                        _shellWindow.CurrentPlaylistsPage?.ResetScrollAndHeader();
                        tcs.TrySetResult();
                    }
                    catch (Exception loadEx)
                    {
                        tcs.TrySetException(loadEx);
                    }
                }

                _ = LoadAsync();

                if (isFromPinned)
                {
                    playlistsVm.IsBackButtonVisible = false;
                    playlistsVm.PreviousPageTag = null;
                    _shellWindow.EnsurePlaylistsPageVisible();
                    _shellWindow.SelectPinnedItem($"playlist:{playlist.Uid}:{playlist.Kind}");
                }
                else
                {
                    playlistsVm.IsBackButtonVisible = true;
                    string? currentTag = _shellWindow.CurrentPageTag;
                    if (!string.IsNullOrEmpty(currentTag) && currentTag != "Playlists")
                    {
                        playlistsVm.PreviousPageTag = currentTag;
                        _shellWindow.ClearNavSelection();
                        _shellWindow.EnsurePlaylistsPageVisible();
                    }
                    else
                    {
                        playlistsVm.PreviousPageTag = null;
                        _shellWindow.SelectPlaylistsTab();
                    }
                }
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }

        var dispatcher = App.MainDispatcherQueue;
        if (dispatcher != null && !dispatcher.HasThreadAccess)
        {
            dispatcher.TryEnqueue(Execute);
        }
        else
        {
            Execute();
        }

        await tcs.Task;
    }

    public async Task NavigateToArtistAsync(Artist artist)
    {
        if (artist == null || _shellWindow == null) return;

        var tcs = new TaskCompletionSource();

        void Execute()
        {
            try
            {
                _ = _shellWindow.NavigateToArtistAsync(artist).ContinueWith(_ =>
                {
                    tcs.TrySetResult();
                });
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }

        var dispatcher = App.MainDispatcherQueue;
        if (dispatcher != null && !dispatcher.HasThreadAccess)
        {
            dispatcher.TryEnqueue(Execute);
        }
        else
        {
            Execute();
        }

        await tcs.Task;
    }

    public async Task NavigateToArtistByIdAsync(long artistId, string? artistName = null)
    {
        if (_shellWindow == null) return;

        var tcs = new TaskCompletionSource();

        void Execute()
        {
            try
            {
                _ = _shellWindow.NavigateToArtistByIdAsync(artistId, artistName).ContinueWith(_ =>
                {
                    tcs.TrySetResult();
                });
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }

        var dispatcher = App.MainDispatcherQueue;
        if (dispatcher != null && !dispatcher.HasThreadAccess)
        {
            dispatcher.TryEnqueue(Execute);
        }
        else
        {
            Execute();
        }

        await tcs.Task;
    }

    public void NavigateToSearch(string? query = null)
    {
        void Execute()
        {
            if (_shellWindow == null) return;
            _shellWindow.NavigateToTag("Search");
            if (!string.IsNullOrWhiteSpace(query))
            {
                var searchVm = App.GetService<SearchViewModel>();
                searchVm.SetQueryAndSearch(query);
            }
        }

        var dispatcher = App.MainDispatcherQueue;
        if (dispatcher != null && !dispatcher.HasThreadAccess)
        {
            dispatcher.TryEnqueue(Execute);
        }
        else
        {
            Execute();
        }
    }
}
