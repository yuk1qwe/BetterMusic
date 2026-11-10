#nullable enable

using System.Diagnostics;
using System.IO;
using System.Text;
using DiscordRPC;
using DiscordRPC.Logging;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.Services.Implementations;

public sealed class DiscordRpcService : IDiscordRpcService
{
    // Application ID registered on Discord for Yandex Music Rich Presence ("Слушает Яндекс Музыку")
    private const string DefaultDiscordClientId = "1269826362399522849";
    private const string FallbackCoverUrl = "https://cdn.discordapp.com/app-icons/1269826362399522849/56e8639a627935aee43e96fd587ea829.png";

    private readonly IAudioPlayerService _audioPlayer;
    private readonly ISettingsService _settingsService;
    private readonly object _syncLock = new();

    private DiscordRpcClient? _client;
    private bool _isInitialized;
    private bool _disposed;
    private string? _lastTrackId;
    private bool _lastIsPlaying;
    private DateTime _lastUpdateTimeUtc = DateTime.MinValue;
    private TimeSpan _lastReportedPosition = TimeSpan.Zero;
    private System.Threading.Timer? _debounceTimer;

    public bool IsConnected => _client != null && _client.IsInitialized && !_client.IsDisposed && _client.CurrentUser != null;

    public DiscordRpcService(IAudioPlayerService audioPlayer, ISettingsService settingsService)
    {
        _audioPlayer = audioPlayer;
        _settingsService = settingsService;
    }

    public void Initialize()
    {
        lock (_syncLock)
        {
            if (_isInitialized) return;
            _isInitialized = true;
        }

        try
        {
            Log("Initializing DiscordRpcService...");
            _audioPlayer.CurrentTrackChanged += OnCurrentTrackChanged;
            _audioPlayer.IsPlayingChanged += OnIsPlayingChanged;
            _audioPlayer.PositionChanged += OnPositionChanged;
            _settingsService.SettingChanged += OnSettingChanged;

            if (_settingsService.DiscordRpcEnabled)
            {
                EnsureClientInitialized();
                if (_audioPlayer.CurrentTrack != null)
                {
                    SchedulePresenceUpdate(500);
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Initialization failed: {ex.Message}");
        }
    }

    private void EnsureClientInitialized()
    {
        lock (_syncLock)
        {
            if (_client != null && _client.IsInitialized)
            {
                return;
            }

            try
            {
                Log($"Connecting DiscordRpcClient with AppId {DefaultDiscordClientId}...");
                _client = new DiscordRpcClient(DefaultDiscordClientId, autoEvents: true)
                {
                    Logger = new FileAndDebugLogger()
                };

                _client.OnReady += (sender, msg) =>
                {
                    Log($"Connected to Discord as {msg.User.Username} ({msg.User.ID})");
                    if (_settingsService.DiscordRpcEnabled && _audioPlayer.CurrentTrack != null)
                    {
                        SchedulePresenceUpdate(100);
                    }
                };

                _client.OnError += (sender, err) =>
                {
                    Log($"Discord RPC Error {err.Code}: {err.Message}");
                };

                _client.OnConnectionEstablished += (sender, args) =>
                {
                    Log("Pipe connection established to Discord.");
                };

                _client.OnConnectionFailed += (sender, args) =>
                {
                    Log("Pipe connection to Discord failed.");
                };

                bool ok = _client.Initialize();
                Log($"DiscordRpcClient.Initialize() returned {ok}");
            }
            catch (Exception ex)
            {
                Log($"Failed to create Discord client: {ex.Message}");
            }
        }
    }

    private void OnSettingChanged(object? sender, string settingName)
    {
        if (settingName == nameof(ISettingsService.DiscordRpcEnabled))
        {
            if (_settingsService.DiscordRpcEnabled)
            {
                Log("Discord RPC enabled in settings. Connecting...");
                EnsureClientInitialized();
                SchedulePresenceUpdate(100);
            }
            else
            {
                Log("Discord RPC disabled in settings. Clearing presence...");
                ClearPresence();
            }
        }
    }

    private void OnCurrentTrackChanged(object? sender, Track? track)
    {
        SchedulePresenceUpdate(200);
    }

    private void OnIsPlayingChanged(object? sender, bool isPlaying)
    {
        SchedulePresenceUpdate(150);
    }

    private void OnPositionChanged(object? sender, TimeSpan position)
    {
        if (!_settingsService.DiscordRpcEnabled || _audioPlayer.CurrentTrack == null || !_audioPlayer.IsPlaying)
        {
            return;
        }

        DateTime now = DateTime.UtcNow;
        TimeSpan expectedPosition = _lastReportedPosition + (now - _lastUpdateTimeUtc);
        double driftSeconds = Math.Abs((position - expectedPosition).TotalSeconds);

        // Discord client interpolates progress between Start and End automatically.
        // We only push a refresh when the track seeks significantly (> 3s) or after 25s.
        if (driftSeconds > 3.0 || (now - _lastUpdateTimeUtc).TotalSeconds >= 25.0)
        {
            SchedulePresenceUpdate(50);
        }
    }

    private void SchedulePresenceUpdate(int delayMs)
    {
        lock (_syncLock)
        {
            if (_debounceTimer == null)
            {
                _debounceTimer = new System.Threading.Timer(_ =>
                {
                    try
                    {
                        UpdatePresence(_audioPlayer.CurrentTrack, _audioPlayer.IsPlaying, _audioPlayer.Position, _audioPlayer.Duration);
                    }
                    catch (Exception ex)
                    {
                        Log($"Debounced UpdatePresence error: {ex.Message}");
                    }
                }, null, delayMs, System.Threading.Timeout.Infinite);
            }
            else
            {
                _debounceTimer.Change(delayMs, System.Threading.Timeout.Infinite);
            }
        }
    }

    public void UpdatePresence(Track? track, bool isPlaying, TimeSpan position, TimeSpan duration)
    {
        if (_disposed || !_settingsService.DiscordRpcEnabled)
        {
            ClearPresence();
            return;
        }

        if (track == null)
        {
            ClearPresence();
            return;
        }

        lock (_syncLock)
        {
            try
            {
                EnsureClientInitialized();
                if (_client == null || !_client.IsInitialized)
                {
                    Log("UpdatePresence skipped: client not initialized.");
                    return;
                }

                _lastTrackId = track.Id;
                _lastIsPlaying = isPlaying;
                _lastReportedPosition = position;
                _lastUpdateTimeUtc = DateTime.UtcNow;

                // Enforce Discord byte limits (max 128 UTF-8 bytes for text, min 2 bytes)
                string title = SafeByteString(track.FullTitle, "Трек", maxBytes: 128, minBytes: 2);
                string artists = SafeByteString(track.ArtistNames, "Неизвестный исполнитель", maxBytes: 128, minBytes: 2);

                string? coverUrl = track.GetCoverUrl(400);
                if (string.IsNullOrWhiteSpace(coverUrl))
                {
                    coverUrl = FallbackCoverUrl;
                }

                string largeImageText = !string.IsNullOrWhiteSpace(track.AlbumTitle)
                    ? SafeByteString(track.AlbumTitle, title, maxBytes: 128, minBytes: 2)
                    : title;

                // DiscordRichPresence requires SmallImageKey to be non-null to prevent NullReferenceException on response merge
                var assets = new Assets
                {
                    LargeImageKey = coverUrl,
                    LargeImageText = largeImageText,
                    SmallImageKey = FallbackCoverUrl,
                    SmallImageText = isPlaying ? "Воспроизводится" : "На паузе"
                };

                Timestamps? timestamps = null;
                if (isPlaying && duration > TimeSpan.Zero)
                {
                    DateTime now = DateTime.UtcNow;
                    DateTime startTime = now - position;
                    DateTime endTime = now + (duration - position);
                    if (endTime > startTime)
                    {
                        timestamps = new Timestamps
                        {
                            Start = startTime,
                            End = endTime
                        };
                    }
                }

                // Add up to 2 actionable buttons (Label max 31 UTF-8 bytes, min 1 byte)
                var buttons = new List<DiscordRPC.Button>();

                // Button 1: Track URL on Yandex Music
                string trackUrl;
                if (track.Albums.Count > 0 && track.Albums[0].Id > 0)
                {
                    trackUrl = $"https://music.yandex.ru/album/{track.Albums[0].Id}/track/{track.Id}";
                }
                else if (!string.IsNullOrWhiteSpace(track.Id))
                {
                    trackUrl = $"https://music.yandex.ru/track/{track.Id.Split(':')[0]}";
                }
                else
                {
                    trackUrl = "https://music.yandex.ru";
                }

                buttons.Add(new DiscordRPC.Button
                {
                    Label = "Слушать трек", // 23 UTF-8 bytes <= 31 bytes
                    Url = trackUrl
                });

                // Button 2: Artist URL (if known)
                if (track.Artists.Count > 0 && track.Artists[0].Id > 0)
                {
                    string artistName = SafeByteString(track.Artists[0].Name, "Артист", maxBytes: 31, minBytes: 1);
                    buttons.Add(new DiscordRPC.Button
                    {
                        Label = artistName,
                        Url = $"https://music.yandex.ru/artist/{track.Artists[0].Id}"
                    });
                }

                var presence = new RichPresence
                {
                    Type = ActivityType.Listening,
                    Details = title,
                    State = artists,
                    Assets = assets,
                    Timestamps = timestamps,
                    Buttons = buttons.Count > 0 ? buttons.ToArray() : null
                };

                _client.SetPresence(presence);
                Log($"SetPresence sent successfully: '{title}' by '{artists}' (Playing={isPlaying}, Position={position:mm\\:ss}/{duration:mm\\:ss})");
            }
            catch (Exception ex)
            {
                Log($"SetPresence failed: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }

    public void ClearPresence()
    {
        lock (_syncLock)
        {
            try
            {
                if (_client != null && _client.IsInitialized)
                {
                    _client.ClearPresence();
                    Log("ClearPresence sent.");
                }
            }
            catch (Exception ex)
            {
                Log($"ClearPresence failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Safely trims a string so that its UTF-8 byte count fits between minBytes and maxBytes.
    /// This prevents DiscordRPC StringOutOfRangeException on multi-byte Cyrillic characters.
    /// </summary>
    private static string SafeByteString(string? text, string fallback, int maxBytes = 128, int minBytes = 2)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            text = fallback;
        }

        text = text.Trim();

        // Ensure minimum UTF-8 bytes
        while (Encoding.UTF8.GetByteCount(text) < minBytes)
        {
            text += "\u200B"; // zero-width space
        }

        // Ensure maximum UTF-8 bytes without splitting characters
        if (Encoding.UTF8.GetByteCount(text) > maxBytes)
        {
            while (text.Length > 0 && Encoding.UTF8.GetByteCount(text + "…") > maxBytes)
            {
                text = text[..^1];
            }
            text += "…";
        }

        return text;
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                Log("Disposing DiscordRpcService...");
                _debounceTimer?.Dispose();
                _debounceTimer = null;

                _audioPlayer.CurrentTrackChanged -= OnCurrentTrackChanged;
                _audioPlayer.IsPlayingChanged -= OnIsPlayingChanged;
                _audioPlayer.PositionChanged -= OnPositionChanged;
                _settingsService.SettingChanged -= OnSettingChanged;

                if (_client != null)
                {
                    _client.ClearPresence();
                    _client.Deinitialize();
                    _client.Dispose();
                    _client = null;
                }
            }
            catch (Exception ex)
            {
                Log($"Dispose error: {ex.Message}");
            }
        }
    }

    private static void Log(string message)
    {
        string formatted = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [DiscordRpc] {message}";
        Debug.WriteLine(formatted);

        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string dir = Path.Combine(appData, "YMusic");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "discord_rpc.log"), formatted + Environment.NewLine);
        }
        catch { }
    }

    private sealed class FileAndDebugLogger : DiscordRPC.Logging.ILogger
    {
        public DiscordRPC.Logging.LogLevel Level { get; set; } = DiscordRPC.Logging.LogLevel.Info;

        public void Trace(string message, params object[] args)
        {
            if (Level <= DiscordRPC.Logging.LogLevel.Trace)
                Log($"[TRACE] {Format(message, args)}");
        }

        public void Info(string message, params object[] args)
        {
            if (Level <= DiscordRPC.Logging.LogLevel.Info)
                Log($"[INFO] {Format(message, args)}");
        }

        public void Warning(string message, params object[] args)
        {
            if (Level <= DiscordRPC.Logging.LogLevel.Warning)
                Log($"[WARN] {Format(message, args)}");
        }

        public void Error(string message, params object[] args)
        {
            if (Level <= DiscordRPC.Logging.LogLevel.Error)
                Log($"[ERROR] {Format(message, args)}");
        }

        private static string Format(string msg, object[] args)
        {
            return args.Length > 0 ? string.Format(msg, args) : msg;
        }
    }
}
