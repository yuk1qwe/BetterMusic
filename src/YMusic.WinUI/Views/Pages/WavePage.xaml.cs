#nullable enable

using System;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using YMusic.WinUI.Models;
using YMusic.WinUI.ViewModels;

namespace YMusic.WinUI.Views.Pages;

public sealed partial class WavePage : Page
{
    public WaveViewModel ViewModel { get; }

    public WavePage()
    {
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        try
        {
            Log("WavePage ctor starting...");
            ViewModel = App.GetService<WaveViewModel>();
            Log("WavePage ViewModel resolved.");
            InitializeComponent();
            Log("WavePage InitializeComponent finished.");
        }
        catch (Exception ex)
        {
            Log($"[CRITICAL] WavePage ctor failed: {ex}");
            throw;
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        try
        {
            ViewModel.SyncPlayerState();
            Log("WavePage OnNavigatedTo completed.");
        }
        catch (Exception ex)
        {
            Log($"[ERROR] WavePage OnNavigatedTo: {ex}");
        }
    }

    private void PresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            var preset = ViewModel.Presets.FirstOrDefault(p => string.Equals(p.StationTag, tag, StringComparison.OrdinalIgnoreCase));
            if (preset != null)
            {
                _ = ViewModel.StartWaveCommand.ExecuteAsync(preset);
                return;
            }
        }

        if (sender is FrameworkElement fe && fe.DataContext is WavePreset pData)
        {
            _ = ViewModel.StartWaveCommand.ExecuteAsync(pData);
        }
    }

    private void UpcomingTracks_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Track track)
        {
            _ = ViewModel.PlayUpcomingTrackCommand.ExecuteAsync(track);
        }
    }

    private static void Log(string message)
    {
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string dir = Path.Combine(appData, "YMusic");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "app_debug.log"), $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
        }
        catch { }
    }
}
