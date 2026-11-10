#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Implementations;
using YMusic.WinUI.Services.Interfaces;
using YMusic.WinUI.ViewModels;

namespace YMusic.WinUI.Helpers;

public static class TrackMenuHelper
{
    public static readonly DependencyProperty TrackProperty =
        DependencyProperty.RegisterAttached(
            "Track",
            typeof(Track),
            typeof(TrackMenuHelper),
            new PropertyMetadata(null, OnTrackChanged));

    public static Track? GetTrack(DependencyObject obj) => (Track?)obj.GetValue(TrackProperty);
    public static void SetTrack(DependencyObject obj, Track? value) => obj.SetValue(TrackProperty, value);

    private static void OnTrackChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element)
        {
            element.ContextRequested -= Element_ContextRequested;
            if (e.NewValue is Track)
            {
                element.ContextRequested += Element_ContextRequested;
            }
        }
    }

    private static void Element_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (sender is not FrameworkElement element) return;
        var track = GetTrack(element);
        if (track == null) return;

        var flyout = CreateMenuFlyout(track);
        if (args.TryGetPosition(element, out Point point))
        {
            flyout.ShowAt(element, new FlyoutShowOptions
            {
                Position = point,
                ShowMode = FlyoutShowMode.Standard
            });
        }
        else
        {
            flyout.ShowAt(element);
        }
        args.Handled = true;
    }

    public static MenuFlyout CreateMenuFlyout(Track track)
    {
        var flyout = new MenuFlyout();
        var loc = App.Loc;
        bool isEn = loc?.CurrentLanguage == "en";

        string GetLoc(string key, string fallback) =>
            loc?[key] is { } v && !string.IsNullOrEmpty(v) && v != key ? v : fallback;

        var musicService = App.GetService<IYandexMusicService>();
        var audioPlayer = App.GetService<IAudioPlayerService>();
        var playerVm = App.GetService<PlayerBarViewModel>();
        var waveVm = App.GetService<WaveViewModel>();
        var fckService = App.GetService<IFckCensorService>();

        bool isDisliked = track.IsDisliked || musicService.IsTrackDisliked(track.Id);
        bool isLiked = track.IsLiked || musicService.IsTrackLiked(track.Id);
        bool isReplaced = track.IsReplaced || fckService.IsReplaced(track.Id);

        // 1. Start Wave by Track
        var startWaveItem = new MenuFlyoutItem
        {
            Text = GetLoc("Wave_ByTrack", isEn ? "My Wave by track" : "Моя волна по треку"),
            Icon = new FontIcon
            {
                Glyph = "\uE93E",
                Foreground = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
            }
        };
        startWaveItem.Click += async (s, e) =>
        {
            try
            {
                await waveVm.StartWaveForTrackAsync(track).ConfigureAwait(false);
                playerVm.ShowToast(isEn
                    ? $"My Wave tuned to \"{track.Title}\""
                    : $"Моя волна настроена по треку «{track.Title}»");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StartWave Error] {ex.Message}");
            }
        };
        flyout.Items.Add(startWaveItem);

        // 2. Play Next
        var playNextItem = new MenuFlyoutItem
        {
            Text = GetLoc("TrackMenu_PlayNext", isEn ? "Play next" : "Слушать следующим"),
            Icon = new FontIcon { Glyph = "\uE892" }
        };
        playNextItem.Click += (s, e) =>
        {
            audioPlayer.PlayNext(track);
            playerVm.ShowToast(GetLoc("TrackToast_PlayNext", isEn ? "Will play next" : "Трек будет воспроизведён следующим"));
        };
        flyout.Items.Add(playNextItem);

        // 3. Add to Queue
        var addToQueueItem = new MenuFlyoutItem
        {
            Text = GetLoc("TrackMenu_AddToQueue", isEn ? "Add to end of queue" : "В конец очереди"),
            Icon = new FontIcon { Glyph = "\uEA37" }
        };
        addToQueueItem.Click += (s, e) =>
        {
            audioPlayer.AppendToQueue(new[] { track });
            playerVm.ShowToast(GetLoc("TrackToast_AddedToQueue", isEn ? "Added to queue" : "Трек добавлен в конец очереди"));
        };
        flyout.Items.Add(addToQueueItem);

        flyout.Items.Add(new MenuFlyoutSeparator());

        // 4. Like / Unlike Toggle
        var likeItem = new MenuFlyoutItem
        {
            Text = isLiked
                ? (isEn ? "Unlike" : "Удалить из избранного")
                : (isEn ? "Add to favorites" : "В избранное"),
            Icon = new FontIcon
            {
                Glyph = isLiked ? "\uEB52" : "\uEB51",
                Foreground = isLiked
                    ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 51, 102))
                    : (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            }
        };
        likeItem.Click += async (s, e) =>
        {
            if (isLiked)
            {
                await musicService.UnlikeTrackAsync(track.Id);
                playerVm.ShowToast(GetLoc("TrackToast_Unliked", isEn ? "Removed from favorites" : "Удалено из избранного"));
            }
            else
            {
                await musicService.LikeTrackAsync(track.Id);
                playerVm.ShowToast(GetLoc("TrackToast_Liked", isEn ? "Added to favorites" : "Добавлено в избранное"));
            }
        };
        flyout.Items.Add(likeItem);

        // 5. Dislike / Undislike Toggle
        var dislikeItem = new MenuFlyoutItem
        {
            Text = isDisliked
                ? GetLoc("TrackMenu_Undislike", isEn ? "Remove from disliked" : "Убрать из «Не нравится»")
                : GetLoc("TrackMenu_Dislike", isEn ? "I don't like it" : "Не нравится"),
            Icon = new FontIcon
            {
                Glyph = isDisliked ? "\uE006" : "\uE1E0",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 85, 85))
            }
        };
        dislikeItem.Click += async (s, e) =>
        {
            if (isDisliked)
            {
                await musicService.UndislikeTrackAsync(track.Id);
                playerVm.ShowToast(GetLoc("TrackToast_Undisliked", isEn ? "Removed from disliked" : "Удалено из «Не нравится»"));
            }
            else
            {
                await musicService.DislikeTrackAsync(track.Id);
                if (Track.IsSameTrackId(audioPlayer.CurrentTrack?.Id, track.Id))
                {
                    playerVm.ShowToast(GetLoc("TrackToast_Disliked", isEn ? "Track disliked & skipped" : "Не нравится (трек скрыт и пропущен)"));
                    await audioPlayer.NextAsync().ConfigureAwait(false);
                }
                else
                {
                    playerVm.ShowToast(GetLoc("TrackToast_Disliked_NoSkip", isEn ? "Track disliked" : "Не нравится (трек скрыт)"));
                }
            }
        };
        flyout.Items.Add(dislikeItem);

        // 6. Add to Playlist (Submenu)
        var addToPlaylistItem = new MenuFlyoutSubItem
        {
            Text = GetLoc("TrackMenu_AddToPlaylist", isEn ? "Add to playlist" : "Добавить в плейлист"),
            Icon = new FontIcon { Glyph = "\uE8F4" }
        };
        addToPlaylistItem.Items.Add(new MenuFlyoutItem
        {
            Text = GetLoc("TrackMenu_LoadingPlaylists", isEn ? "Loading playlists..." : "Загрузка плейлистов..."),
            IsEnabled = false
        });

        // Populate playlist subitem on open
        flyout.Opening += async (s, e) =>
        {
            try
            {
                var playlists = await playerVm.GetUserPlaylistsAsync();
                addToPlaylistItem.Items.Clear();

                if (playlists == null || playlists.Count == 0)
                {
                    addToPlaylistItem.Items.Add(new MenuFlyoutItem
                    {
                        Text = GetLoc("TrackMenu_NoPlaylists", isEn ? "No playlists found" : "Плейлисты не найдены"),
                        IsEnabled = false
                    });
                    return;
                }

                foreach (var pl in playlists)
                {
                    var plItem = new MenuFlyoutItem
                    {
                        Text = pl.Title,
                        Icon = new FontIcon { Glyph = "\uE8F4" }
                    };
                    plItem.Click += async (senderPl, argsPl) =>
                    {
                        long ownerUid = pl.Uid > 0 ? pl.Uid : (pl.Owner?.Uid ?? 0);
                        string? albumId = track.Albums?.FirstOrDefault()?.Id.ToString();
                        bool ok = await musicService.AddTrackToPlaylistAsync(ownerUid, pl.Kind, track.Id, albumId);
                        if (ok)
                        {
                            string tpl = GetLoc("TrackToast_AddedToPlaylist", isEn ? "Added to \"{0}\"" : "Добавлено в плейлист «{0}»");
                            playerVm.ShowToast(string.Format(tpl, pl.Title));
                        }
                        else
                        {
                            playerVm.ShowToast(GetLoc("TrackToast_AddToPlaylistFailed", isEn ? "Failed to add to playlist" : "Не удалось добавить в плейлист"));
                        }
                    };
                    addToPlaylistItem.Items.Add(plItem);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PopulatePlaylists Error] {ex.Message}");
                addToPlaylistItem.Items.Clear();
                addToPlaylistItem.Items.Add(new MenuFlyoutItem
                {
                    Text = GetLoc("TrackMenu_FailedPlaylists", isEn ? "Failed to load playlists" : "Ошибка загрузки плейлистов"),
                    IsEnabled = false
                });
            }
        };
        flyout.Items.Add(addToPlaylistItem);

        // 6b. Remove from current playlist (if viewing an owned playlist)
        try
        {
            var playlistsVm = App.GetService<PlaylistsViewModel>();
            if (playlistsVm?.IsSelectedPlaylistOwned == true && playlistsVm.SelectedPlaylistTracks.Contains(track))
            {
                var removeFromPlaylistItem = new MenuFlyoutItem
                {
                    Text = GetLoc("TrackMenu_RemoveFromPlaylist", isEn ? "Remove from this playlist" : "Удалить из этого плейлиста"),
                    Icon = new FontIcon
                    {
                        Glyph = "\uE74D",
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 85, 85))
                    }
                };
                removeFromPlaylistItem.Click += async (s, e) =>
                {
                    await playlistsVm.RemoveTrackFromCurrentPlaylistAsync(track);
                };
                flyout.Items.Add(removeFromPlaylistItem);
            }
        }
        catch { }

        // 7. Share
        var shareItem = new MenuFlyoutItem
        {
            Text = GetLoc("TrackMenu_Share", isEn ? "Share" : "Поделиться"),
            Icon = new FontIcon { Glyph = "\uE72D" }
        };
        shareItem.Click += (s, e) =>
        {
            string url = (track.Albums != null && track.Albums.Count > 0 && track.Albums[0].Id > 0)
                ? $"https://music.yandex.ru/album/{track.Albums[0].Id}/track/{track.Id}"
                : $"https://music.yandex.ru/track/{track.Id}";

            try
            {
                var dp = new DataPackage();
                dp.SetText(url);
                Clipboard.SetContent(dp);
                playerVm.ShowToast(GetLoc("TrackToast_LinkCopied", isEn ? "Track link copied to clipboard" : "Ссылка на трек скопирована"));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Share Error] {ex.Message}");
            }
        };
        flyout.Items.Add(shareItem);

        // 8. Download to File
        var downloadItem = new MenuFlyoutItem
        {
            Text = GetLoc("TrackMenu_DownloadFile", isEn ? "Download to file" : "Скачать в файл"),
            Icon = new FontIcon { Glyph = "\uE896" }
        };
        downloadItem.Click += async (s, e) =>
        {
            playerVm.ShowToast(GetLoc("TrackToast_Downloading", isEn ? "Downloading track..." : "Скачивание трека началось..."));
            try
            {
                var (streamUrl, quality) = await musicService.GetTrackStreamInfoAsync(track.Id).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(streamUrl))
                {
                    streamUrl = await musicService.GetTrackDownloadUrlAsync(track.Id).ConfigureAwait(false);
                }

                if (string.IsNullOrWhiteSpace(streamUrl))
                {
                    playerVm.ShowToast(GetLoc("TrackToast_DownloadFailed", isEn ? "Failed to get download URL" : "Не удалось получить ссылку на трек"));
                    return;
                }

                string ext = string.Equals(quality?.Codec, "aac", StringComparison.OrdinalIgnoreCase) ? "m4a" : "mp3";
                string rawName = $"{track.ArtistNames} - {track.FullTitle}.{ext}";
                string cleanName = string.Join("_", rawName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
                if (string.IsNullOrWhiteSpace(cleanName)) cleanName = $"Track_{track.Id}.{ext}";

                string downloadsFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string destDir = Path.Combine(downloadsFolder, "Downloads");
                if (!Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }
                string destPath = Path.Combine(destDir, cleanName);

                using var httpClient = YandexBypassProxy.CreateHttpClient(TimeSpan.FromMinutes(2));
                using var response = await httpClient.GetAsync(streamUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                await using (var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await response.Content.CopyToAsync(fileStream).ConfigureAwait(false);
                }

                string downloadTemplate = GetLoc("TrackToast_Downloaded", isEn ? "Downloaded to Downloads: {0}" : "Сохранено в Загрузки: {0}");
                playerVm.ShowToast(string.Format(downloadTemplate, cleanName));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Download Error] {ex.Message}");
                playerVm.ShowToast(GetLoc("TrackToast_DownloadError", isEn ? "Error downloading track" : "Ошибка при скачивании трека"));
            }
        };
        flyout.Items.Add(downloadItem);

        // 9. Track Details
        var detailsItem = new MenuFlyoutItem
        {
            Text = GetLoc("TrackMenu_Details", isEn ? "Track details" : "Информация о треке"),
            Icon = new FontIcon { Glyph = "\uE946" }
        };
        detailsItem.Click += async (s, e) =>
        {
            if (App.MainWindow?.Content?.XamlRoot == null) return;

            string albumInfo = track.Albums != null && track.Albums.Count > 0
                ? $"{track.Albums[0].Title}{(track.Albums[0].Year.HasValue ? $" ({track.Albums[0].Year})" : "")}"
                : "—";

            var stack = new StackPanel { Spacing = 10, Margin = new Thickness(0, 10, 0, 0) };
            void AddDetailRow(string label, string value)
            {
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var lbl = new TextBlock
                {
                    Text = label,
                    FontSize = 13,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                };
                var val = new TextBlock
                {
                    Text = value,
                    FontSize = 13,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                };
                Grid.SetColumn(lbl, 0);
                Grid.SetColumn(val, 1);
                row.Children.Add(lbl);
                row.Children.Add(val);
                stack.Children.Add(row);
            }

            AddDetailRow(GetLoc("TrackDetails_TrackTitle", isEn ? "Title:" : "Название:"), track.FullTitle);
            AddDetailRow(GetLoc("TrackDetails_Artist", isEn ? "Artist:" : "Исполнитель:"), track.ArtistNames);
            AddDetailRow(GetLoc("TrackDetails_Album", isEn ? "Album:" : "Альбом:"), albumInfo);
            AddDetailRow(GetLoc("TrackDetails_Duration", isEn ? "Duration:" : "Длительность:"), track.FormattedDuration);
            AddDetailRow(GetLoc("TrackDetails_TrackId", isEn ? "Track ID:" : "ID трека:"), track.Id);

            var dialog = new ContentDialog
            {
                XamlRoot = App.MainWindow.Content.XamlRoot,
                Title = GetLoc("TrackDetails_Title", isEn ? "Track Details" : "Информация о треке"),
                Content = stack,
                CloseButtonText = GetLoc("TrackDetails_Close", isEn ? "Close" : "Закрыть"),
                DefaultButton = ContentDialogButton.Close
            };

            try
            {
                await dialog.ShowAsync();
            }
            catch { }
        };
        flyout.Items.Add(detailsItem);

        flyout.Items.Add(new MenuFlyoutSeparator());

        // 10. FckCensor Options
        if (isReplaced)
        {
            var removeReplacementItem = new MenuFlyoutItem
            {
                Text = isEn ? "Remove replacement (FckCensor)" : "Удалить замену (FckCensor)",
                Icon = new FontIcon { Glyph = "\uE74D", Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 85, 85)) }
            };
            removeReplacementItem.Click += async (s, e) =>
            {
                bool ok = await fckService.RemoveReplacementAsync(track.Id);
                if (ok)
                {
                    playerVm.ShowToast("Трек восстановлен к оригиналу");
                }
            };
            flyout.Items.Add(removeReplacementItem);

            var changeFileItem = new MenuFlyoutItem
            {
                Text = isEn ? "Choose another audio file..." : "Выбрать другой аудиофайл...",
                Icon = new FontIcon { Glyph = "\uE70F" }
            };
            changeFileItem.Click += async (s, e) =>
            {
                await ReplaceTrackWithPickerAsync(track.Id, fckService, playerVm);
            };
            flyout.Items.Add(changeFileItem);
        }
        else
        {
            var replaceTrackItem = new MenuFlyoutItem
            {
                Text = isEn ? "Replace track (FckCensor)..." : "Подменить трек (FckCensor)...",
                Icon = new FontIcon { Glyph = "\uE70F" }
            };
            replaceTrackItem.Click += async (s, e) =>
            {
                await ReplaceTrackWithPickerAsync(track.Id, fckService, playerVm);
            };
            flyout.Items.Add(replaceTrackItem);

            if (!fckService.IsReported(track.Id))
            {
                var reportItem = new MenuFlyoutItem
                {
                    Text = isEn ? "Report censorship" : "Сообщить о цензуре",
                    Icon = new FontIcon { Glyph = "\uE7BA", Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 185, 0)) }
                };
                reportItem.Click += async (s, e) =>
                {
                    bool ok = await fckService.ReportCensorshipAsync(track.Id, replaced: false);
                    if (ok)
                    {
                        playerVm.ShowToast("Спасибо! Трек скоро будет добавлен в базу.");
                    }
                };
                flyout.Items.Add(reportItem);
            }
        }

        return flyout;
    }

    private static async Task ReplaceTrackWithPickerAsync(string trackId, IFckCensorService fckService, PlayerBarViewModel playerVm)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.MusicLibrary
            };
            picker.FileTypeFilter.Add(".mp3");
            picker.FileTypeFilter.Add(".wav");
            picker.FileTypeFilter.Add(".flac");
            picker.FileTypeFilter.Add(".ogg");
            picker.FileTypeFilter.Add(".m4a");
            picker.FileTypeFilter.Add(".aac");

            if (App.MainWindow != null)
            {
                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                bool ok = await fckService.ReplaceTrackWithFileAsync(trackId, file.Path);
                if (ok)
                {
                    playerVm.ShowToast("Трек успешно подменён (FckCensor)");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ReplaceTrackWithPicker Error] {ex.Message}");
            playerVm.ShowToast("Ошибка при выборе аудиофайла");
        }
    }
}
