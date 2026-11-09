#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.Helpers;

public static class ArtistNavigationHelper
{
    public static async Task NavigateToTrackArtistAsync(FrameworkElement? anchor, Track? track, Action? onBeforeNavigate = null)
    {
        if (track == null) return;
        await NavigateToArtistsAsync(anchor, track.Artists, track.ArtistNames, onBeforeNavigate);
    }

    public static async Task NavigateToAlbumArtistAsync(FrameworkElement? anchor, Album? album, Action? onBeforeNavigate = null)
    {
        if (album == null) return;
        await NavigateToArtistsAsync(anchor, album.Artists, album.ArtistNames, onBeforeNavigate);
    }

    public static async Task NavigateToArtistsAsync(
        FrameworkElement? anchor,
        IReadOnlyList<Artist>? artists,
        string? fallbackName = null,
        Action? onBeforeNavigate = null)
    {
        var navService = App.GetService<INavigationService>();
        if (navService == null) return;

        var validArtists = artists?
            .Where(a => !string.IsNullOrWhiteSpace(a.Name) || a.Id > 0)
            .GroupBy(a => a.Id > 0 ? a.Id.ToString() : a.Name.Trim())
            .Select(g => g.First())
            .ToList() ?? new List<Artist>();

        if (validArtists.Count == 1)
        {
            var single = validArtists[0];
            onBeforeNavigate?.Invoke();
            if (single.Id > 0)
            {
                await navService.NavigateToArtistAsync(single);
            }
            else if (!string.IsNullOrWhiteSpace(single.Name))
            {
                navService.NavigateToSearch(single.Name);
            }
            return;
        }

        if (validArtists.Count > 1)
        {
            if (anchor != null)
            {
                var flyout = new MenuFlyout();
                foreach (var artist in validArtists)
                {
                    var item = new MenuFlyoutItem
                    {
                        Text = !string.IsNullOrWhiteSpace(artist.Name) ? artist.Name : "Artist",
                        Icon = new FontIcon { Glyph = "\uE77B" }
                    };
                    var target = artist;
                    item.Click += async (_, _) =>
                    {
                        onBeforeNavigate?.Invoke();
                        if (target.Id > 0)
                        {
                            await navService.NavigateToArtistAsync(target);
                        }
                        else if (!string.IsNullOrWhiteSpace(target.Name))
                        {
                            navService.NavigateToSearch(target.Name);
                        }
                    };
                    flyout.Items.Add(item);
                }

                flyout.ShowAt(anchor);
                return;
            }

            // Fallback when anchor is null
            onBeforeNavigate?.Invoke();
            if (validArtists[0].Id > 0)
            {
                await navService.NavigateToArtistAsync(validArtists[0]);
            }
            return;
        }

        // 0 artists
        if (!string.IsNullOrWhiteSpace(fallbackName) && fallbackName != "Unknown Artist")
        {
            onBeforeNavigate?.Invoke();
            navService.NavigateToSearch(fallbackName);
        }
    }
}
