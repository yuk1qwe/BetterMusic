#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Implementations;
using YMusic.WinUI.Services.Interfaces;
using YMusic.WinUI.ViewModels;
using YMusic.WinUI.Views;

namespace YMusic.WinUI;

public partial class App : Application
{
    public static ShellWindow? MainWindow { get; private set; }
    private Window? _window;

    public static Microsoft.UI.Dispatching.DispatcherQueue? MainDispatcherQueue { get; set; }

    public static IServiceProvider Services { get; private set; } = null!;

    public static ILocalizationService Loc => Services.GetRequiredService<ILocalizationService>();

    public static T GetService<T>() where T : class
    {
        return Services.GetRequiredService<T>();
    }

    public App()
    {
        InitializeComponent();
        MainDispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        Services = ConfigureServices();

        UnhandledException += (sender, e) =>
        {
            string msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [CRITICAL UnhandledException]: {e.Message}\n{e.Exception}\nStackTrace:\n{e.Exception?.StackTrace}\n";
            System.Diagnostics.Debug.WriteLine(msg);
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string dir = System.IO.Path.Combine(appData, "YMusic");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "app_debug.log"), msg);
            }
            catch { }
            e.Handled = true;
        };
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Core services
        services.AddSingleton<ICredentialStorageService, CredentialStorageService>();
        services.AddSingleton<IPlaybackStateStorageService, PlaybackStateStorageService>();
        services.AddSingleton<IYandexMusicService, YandexMusicService>();
        services.AddSingleton<ILyricsOffsetService, LyricsOffsetService>();
        services.AddSingleton<IFckCensorService, FckCensorService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ISloplessService, SloplessService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<IAudioCacheService, AudioCacheService>();
        services.AddSingleton<IAudioPlayerService, AudioPlayerService>();
        services.AddSingleton<IEqualizerService, EqualizerService>();
        services.AddSingleton<ITrackPlaybackCoordinator, TrackPlaybackCoordinator>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IPinnedItemsService, PinnedItemsService>();
        services.AddSingleton<ITrayNotificationService, TrayNotificationService>();
        services.AddSingleton<IDiscordRpcService, DiscordRpcService>();
        services.AddSingleton<IObsOverlayService, ObsOverlayService>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<ISpotifyImportService, SpotifyImportService>();
        services.AddSingleton<IGameOverlayService, GameOverlayService>();
        services.AddSingleton<IThemeService, ThemeService>();

        // ViewModels
        services.AddSingleton<PlayerBarViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<LoginViewModel>();
        services.AddSingleton<WaveViewModel>();
        services.AddSingleton<ExploreViewModel>();
        services.AddSingleton<LikedTracksViewModel>();
        services.AddSingleton<PlaylistsViewModel>();
        services.AddSingleton<SearchViewModel>();
        services.AddSingleton<ArtistViewModel>();
        services.AddSingleton<ProfileViewModel>();

        var provider = services.BuildServiceProvider();
        var fckCensor = provider.GetRequiredService<IFckCensorService>();
        var audioCache = provider.GetRequiredService<IAudioCacheService>();
        var loc = provider.GetRequiredService<ILocalizationService>();
        var settings = provider.GetRequiredService<ISettingsService>();

        // Apply saved language setting
        if (!string.IsNullOrWhiteSpace(settings.Language))
        {
            loc.CurrentLanguage = settings.Language;
        }

        settings.SettingChanged += (s, propName) =>
        {
            if (propName == nameof(ISettingsService.Language) && loc.CurrentLanguage != settings.Language)
            {
                loc.CurrentLanguage = settings.Language;
            }
        };

        loc.LanguageChanged += (s, newLang) =>
        {
            if (settings.Language != newLang)
            {
                settings.Language = newLang;
            }
        };

        Track.IsTrackReplacedChecker = id => fckCensor.IsReplaced(id);
        Track.TrackSpoofGetter = id => fckCensor.GetTrackSpoof(id);
        Track.IsTrackCachedChecker = id => audioCache.IsTrackCached(id);
        Album.AlbumSpoofGetter = id => fckCensor.GetAlbumSpoof(id);

        AppDomain.CurrentDomain.ProcessExit += (s, e) =>
        {
            try
            {
                provider.GetService<IDiscordRpcService>()?.Dispose();
                provider.GetService<IObsOverlayService>()?.Dispose();
                provider.GetService<IGameOverlayService>()?.Dispose();
            }
            catch { }
        };

        return provider;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var themeService = GetService<IThemeService>();
        themeService?.PreInitializeResources();

        MainWindow = new ShellWindow();
        _window = MainWindow;
        MainDispatcherQueue = _window.DispatcherQueue;
        themeService?.Initialize();
        _window.Activate();
        GetService<ITrayNotificationService>()?.InitializeThumbnailButtons();
        GetService<IDiscordRpcService>()?.Initialize();
        GetService<IObsOverlayService>()?.Initialize();
        GetService<IGameOverlayService>()?.Initialize();
        UpdateService.CleanOldUpdates();
    }
}
