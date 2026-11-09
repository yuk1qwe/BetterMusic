#nullable enable

using YMusic.WinUI.Models;
using YMusic.WinUI.Views;

namespace YMusic.WinUI.Services.Interfaces;

public interface INavigationService
{
    void Initialize(ShellWindow shellWindow);
    void NavigateToTag(string tag);
    Task NavigateToAlbumAsync(Album album, bool isFromPinned = false);
    Task NavigateToAlbumByIdAsync(long albumId, string? albumTitle = null);
    Task NavigateToPlaylistAsync(Playlist playlist, bool isFromPinned = false);
    Task NavigateToArtistAsync(Artist artist);
    Task NavigateToArtistByIdAsync(long artistId, string? artistName = null);
    void NavigateToSearch(string? query = null);
}
