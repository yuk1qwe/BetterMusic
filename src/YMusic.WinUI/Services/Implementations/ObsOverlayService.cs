#nullable enable

using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.Services.Implementations;

public sealed class ObsOverlayService : IObsOverlayService
{
    private readonly IAudioPlayerService _audioPlayer;
    private readonly ISettingsService _settingsService;
    private readonly object _syncLock = new();

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly List<HttpListenerResponse> _sseClients = new();
    private readonly object _sseLock = new();

    private bool _isInitialized;
    private bool _disposed;
    private string? _lastTrackId;
    private bool _lastIsPlaying;

    public bool IsRunning => _listener != null && _listener.IsListening;
    public int Port => _settingsService.ObsOverlayPort > 0 ? _settingsService.ObsOverlayPort : 21337;
    public string WidgetUrl => $"http://localhost:{Port}/widget";

    public ObsOverlayService(IAudioPlayerService audioPlayer, ISettingsService settingsService)
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
            Log("Initializing ObsOverlayService...");
            _audioPlayer.CurrentTrackChanged += OnPlaybackChanged;
            _audioPlayer.IsPlayingChanged += OnIsPlayingChanged;
            _audioPlayer.PositionChanged += OnPositionChanged;
            _settingsService.SettingChanged += OnSettingChanged;

            if (_settingsService.ObsOverlayEnabled)
            {
                Start();
            }
        }
        catch (Exception ex)
        {
            Log($"Initialization failed: {ex.Message}");
        }
    }

    public void Start()
    {
        lock (_syncLock)
        {
            if (IsRunning) return;

            try
            {
                _cts = new CancellationTokenSource();
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{Port}/");
                _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
                _listener.Start();

                Log($"OBS Overlay server started on http://localhost:{Port}/");
                _ = Task.Run(() => ListenLoopAsync(_listener, _cts.Token));
            }
            catch (Exception ex)
            {
                Log($"Failed to start HTTP listener on port {Port}: {ex.Message}");
                _listener = null;
            }
        }
    }

    public void Stop()
    {
        lock (_syncLock)
        {
            if (!IsRunning) return;

            try
            {
                Log("Stopping OBS Overlay server...");
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;

                lock (_sseLock)
                {
                    foreach (var client in _sseClients)
                    {
                        try { client.Close(); } catch { }
                    }
                    _sseClients.Clear();
                }

                _listener?.Stop();
                _listener?.Close();
                _listener = null;
            }
            catch (Exception ex)
            {
                Log($"Error stopping listener: {ex.Message}");
            }
        }
    }

    public void Restart()
    {
        Stop();
        Start();
    }

    private void OnSettingChanged(object? sender, string settingName)
    {
        if (settingName == nameof(ISettingsService.ObsOverlayEnabled))
        {
            if (_settingsService.ObsOverlayEnabled)
            {
                Start();
            }
            else
            {
                Stop();
            }
        }
        else if (settingName == nameof(ISettingsService.ObsOverlayPort))
        {
            if (_settingsService.ObsOverlayEnabled)
            {
                Restart();
            }
        }
    }

    private void OnPlaybackChanged(object? sender, Track? track)
    {
        string? currentId = track?.Id;
        if (currentId != _lastTrackId)
        {
            _lastTrackId = currentId;
            BroadcastState();
        }
    }

    private void OnIsPlayingChanged(object? sender, bool isPlaying)
    {
        if (isPlaying != _lastIsPlaying)
        {
            _lastIsPlaying = isPlaying;
            BroadcastState();
        }
    }

    private DateTime _lastBroadcastTime = DateTime.MinValue;
    private void OnPositionChanged(object? sender, TimeSpan position)
    {
        if (!IsRunning) return;

        lock (_sseLock)
        {
            if (_sseClients.Count == 0) return;
        }

        // Throttle position updates to SSE to twice per second to reduce traffic
        if ((DateTime.UtcNow - _lastBroadcastTime).TotalMilliseconds >= 500)
        {
            _lastBroadcastTime = DateTime.UtcNow;
            BroadcastState();
        }
    }

    private string BuildCurrentStateJson()
    {
        var track = _audioPlayer.CurrentTrack;
        bool isPlaying = _audioPlayer.IsPlaying;
        var position = _audioPlayer.Position;
        var duration = _audioPlayer.Duration;

        var state = new
        {
            hasTrack = track != null,
            trackId = track?.Id ?? string.Empty,
            title = track?.FullTitle ?? string.Empty,
            rawTitle = track?.Title ?? string.Empty,
            artists = track?.ArtistNames ?? string.Empty,
            album = track?.AlbumTitle ?? string.Empty,
            coverUrl = track?.GetCoverUrl(400) ?? track?.GetCoverUrl(200) ?? string.Empty,
            isPlaying = isPlaying,
            positionMs = (long)position.TotalMilliseconds,
            durationMs = track?.DurationMs > 0 ? track.DurationMs : (long)duration.TotalMilliseconds,
            formattedPosition = position.ToString(@"m\:ss"),
            formattedDuration = duration.ToString(@"m\:ss"),
            isExplicit = track?.IsDimmed == false && track.Title.Contains("explicit", StringComparison.OrdinalIgnoreCase),
            isFckCensor = track?.IsReplaced == true
        };

        return JsonSerializer.Serialize(state);
    }

    private void BroadcastState()
    {
        lock (_sseLock)
        {
            if (_sseClients.Count == 0) return;
        }

        string json = BuildCurrentStateJson();
        string sseData = $"data: {json}\n\n";
        byte[] bytes = Encoding.UTF8.GetBytes(sseData);

        lock (_sseLock)
        {
            for (int i = _sseClients.Count - 1; i >= 0; i--)
            {
                var response = _sseClients[i];
                try
                {
                    response.OutputStream.Write(bytes, 0, bytes.Length);
                    response.OutputStream.Flush();
                }
                catch
                {
                    try { response.Close(); } catch { }
                    _sseClients.RemoveAt(i);
                }
            }
        }
    }

    private async Task ListenLoopAsync(HttpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && listener.IsListening)
        {
            try
            {
                var context = await listener.GetContextAsync().ConfigureAwait(false);
                _ = Task.Run(() => HandleRequestAsync(context));
            }
            catch (HttpListenerException) when (!listener.IsListening || ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log($"Listener error: {ex.Message}");
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        // Browser sources on this server use same-origin requests. Do not expose
        // playback data to arbitrary websites through permissive CORS.
        response.Headers.Add("X-Content-Type-Options", "nosniff");
        response.Headers.Add("Referrer-Policy", "no-referrer");

        if (request.HttpMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            response.StatusCode = 204;
            response.Close();
            return;
        }

        string path = request.Url?.AbsolutePath.TrimEnd('/').ToLowerInvariant() ?? "";
        if (string.IsNullOrEmpty(path)) path = "/";

        try
        {
            switch (path)
            {
                case "":
                case "/":
                    // Interactive preview & settings page
                    await ServeHtmlAsync(response, GetDashboardHtml());
                    break;

                case "/widget":
                    // Standalone transparent widget for OBS Studio
                    await ServeHtmlAsync(response, GetWidgetHtml());
                    break;

                case "/api/track":
                    // JSON state
                    string json = BuildCurrentStateJson();
                    await ServeJsonAsync(response, json);
                    break;

                case "/events":
                    // Realtime Server-Sent Events (SSE)
                    HandleSseConnection(response);
                    break;

                case "/track.txt":
                    // Plain text "Artist - Title" for classic OBS Text sources
                    var t = _audioPlayer.CurrentTrack;
                    string plainText = t != null ? $"{t.ArtistNames} - {t.FullTitle}" : "";
                    await ServeTextAsync(response, plainText);
                    break;

                case "/cover":
                    // Direct redirect to current cover image
                    string? coverUrl = _audioPlayer.CurrentTrack?.GetCoverUrl(400);
                    if (!string.IsNullOrWhiteSpace(coverUrl))
                    {
                        response.Redirect(coverUrl);
                        response.Close();
                    }
                    else
                    {
                        response.StatusCode = 404;
                        response.Close();
                    }
                    break;

                default:
                    response.StatusCode = 404;
                    response.Close();
                    break;
            }
        }
        catch (Exception ex)
        {
            Log($"Request handle error ({path}): {ex.Message}");
            try
            {
                response.StatusCode = 500;
                response.Close();
            }
            catch { }
        }
    }

    private void HandleSseConnection(HttpListenerResponse response)
    {
        response.ContentType = "text/event-stream";
        response.Headers.Add("Cache-Control", "no-cache");
        response.Headers.Add("Connection", "keep-alive");

        lock (_sseLock)
        {
            if (_sseClients.Count >= 8)
            {
                response.StatusCode = 503;
                response.Close();
                return;
            }
            _sseClients.Add(response);
        }

        // Send initial state immediately
        string initialData = $"data: {BuildCurrentStateJson()}\n\n";
        byte[] bytes = Encoding.UTF8.GetBytes(initialData);
        try
        {
            response.OutputStream.Write(bytes, 0, bytes.Length);
            response.OutputStream.Flush();
        }
        catch
        {
            lock (_sseLock) { _sseClients.Remove(response); }
            try { response.Close(); } catch { }
            throw;
        }
    }

    private static async Task ServeHtmlAsync(HttpListenerResponse response, string html)
    {
        response.ContentType = "text/html; charset=utf-8";
        byte[] buffer = Encoding.UTF8.GetBytes(html);
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer).ConfigureAwait(false);
        response.Close();
    }

    private static async Task ServeJsonAsync(HttpListenerResponse response, string json)
    {
        response.ContentType = "application/json; charset=utf-8";
        byte[] buffer = Encoding.UTF8.GetBytes(json);
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer).ConfigureAwait(false);
        response.Close();
    }

    private static async Task ServeTextAsync(HttpListenerResponse response, string text)
    {
        response.ContentType = "text/plain; charset=utf-8";
        byte[] buffer = Encoding.UTF8.GetBytes(text);
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer).ConfigureAwait(false);
        response.Close();
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                Log("Disposing ObsOverlayService...");
                _audioPlayer.CurrentTrackChanged -= OnPlaybackChanged;
                _audioPlayer.IsPlayingChanged -= OnIsPlayingChanged;
                _audioPlayer.PositionChanged -= OnPositionChanged;
                _settingsService.SettingChanged -= OnSettingChanged;
                Stop();
            }
            catch (Exception ex)
            {
                Log($"Dispose error: {ex.Message}");
            }
        }
    }

    private static void Log(string message)
    {
        string formatted = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [ObsOverlay] {message}";
        Debug.WriteLine(formatted);

        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string dir = Path.Combine(appData, "YMusic");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "obs_overlay.log"), formatted + Environment.NewLine);
        }
        catch { }
    }

    #region Embedded HTML/CSS/JS Widget & Dashboard

    private string GetWidgetHtml()
    {
        return """
<!DOCTYPE html>
<html lang="ru">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>WinYandex OBS Stream Widget</title>
    <link rel="preconnect" href="https://fonts.googleapis.com">
    <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
    <link href="https://fonts.googleapis.com/css2?family=Montserrat:wght@400;500;600;700;800&family=Inter:wght@400;500;600;700&display=swap" rel="stylesheet">
    <style>
        * {
            box-sizing: border-box;
            margin: 0;
            padding: 0;
            user-select: none;
        }

        body {
            background-color: transparent;
            overflow: hidden;
            font-family: 'Montserrat', 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            display: flex;
            align-items: center;
            justify-content: center;
            width: 100vw;
            height: 100vh;
            padding: 12px;
        }

        /* Root Container with smooth transitions */
        #widget-root {
            position: relative;
            transition: opacity 0.45s cubic-bezier(0.16, 1, 0.3, 1), transform 0.45s cubic-bezier(0.16, 1, 0.3, 1);
        }

        #widget-root.hidden-pause {
            opacity: 0 !important;
            transform: translateY(20px) scale(0.95) !important;
            pointer-events: none;
        }

        /* Ambient Glow (only active in glass theme) */
        .ambient-bg {
            display: none;
        }

        /* ================= THEME 1: GLASSMORPHISM CARD ================= */
        .theme-glass {
            display: flex;
            align-items: center;
            width: 500px;
            height: 136px;
            padding: 16px;
            border-radius: 20px;
            background: rgba(18, 18, 24, 0.8);
            backdrop-filter: blur(28px) saturate(180%);
            -webkit-backdrop-filter: blur(28px) saturate(180%);
            border: 1px solid rgba(255, 255, 255, 0.12);
            box-shadow: 0 16px 45px rgba(0, 0, 0, 0.55), 0 0 40px var(--ambient-glow, rgba(255, 204, 0, 0.15));
            color: #ffffff;
            position: relative;
            overflow: hidden;
        }

        .theme-glass .ambient-bg {
            display: block;
            position: absolute;
            top: -40px;
            left: -40px;
            width: 220px;
            height: 220px;
            border-radius: 50%;
            background: var(--ambient-glow, rgba(255, 204, 0, 0.35));
            filter: blur(65px);
            opacity: 0.45;
            pointer-events: none;
            transition: background 0.8s ease;
        }

        .theme-glass .cover-container {
            position: relative;
            width: 104px;
            height: 104px;
            min-width: 104px;
            border-radius: 14px;
            overflow: hidden;
            box-shadow: 0 8px 24px rgba(0, 0, 0, 0.45);
            background: #24242a;
            flex-shrink: 0;
        }

        .theme-glass .cover-img {
            width: 100%;
            height: 100%;
            object-fit: cover;
            display: block;
            transition: transform 0.6s cubic-bezier(0.16, 1, 0.3, 1);
        }

        .theme-glass .info-container {
            margin-left: 18px;
            flex: 1;
            min-width: 0;
            display: flex;
            flex-direction: column;
            justify-content: center;
            height: 100%;
            position: relative;
            z-index: 2;
        }

        /* ================= THEME 2: COMPACT PILL ================= */
        .theme-pill {
            display: flex;
            align-items: center;
            height: 60px;
            width: 420px;
            padding: 6px 16px 6px 6px;
            border-radius: 9999px;
            background: rgba(16, 16, 22, 0.88);
            backdrop-filter: blur(24px) saturate(180%);
            -webkit-backdrop-filter: blur(24px) saturate(180%);
            border: 1px solid rgba(255, 255, 255, 0.12);
            box-shadow: 0 10px 30px rgba(0, 0, 0, 0.5), 0 0 24px var(--ambient-glow, rgba(255, 204, 0, 0.2));
            color: #ffffff;
            position: relative;
            overflow: hidden;
        }

        .theme-pill .cover-container {
            width: 48px;
            height: 48px;
            min-width: 48px;
            border-radius: 50%;
            overflow: hidden;
            box-shadow: 0 4px 14px rgba(0, 0, 0, 0.4);
            position: relative;
            flex-shrink: 0;
        }

        .theme-pill .cover-img {
            width: 100%;
            height: 100%;
            object-fit: cover;
            border-radius: 50%;
            animation: spin 12s linear infinite;
            animation-play-state: paused;
        }

        .theme-pill.playing .cover-img {
            animation-play-state: running;
        }

        .theme-pill .info-container {
            margin-left: 12px;
            flex: 1;
            min-width: 0;
            display: flex;
            flex-direction: column;
            justify-content: center;
        }

        .theme-pill .top-meta-row {
            display: none;
        }

        .theme-pill .title {
            font-size: 14px;
            font-weight: 700;
            color: #ffffff;
            text-shadow: none;
            margin-bottom: 0;
        }

        .theme-pill .artist {
            font-size: 12px;
            font-weight: 500;
            color: rgba(255, 255, 255, 0.65);
            margin-bottom: 0;
            margin-top: 1px;
        }

        .theme-pill .progress-wrapper {
            display: none;
        }

        .theme-pill .progress-bar-edge {
            position: absolute;
            bottom: 0;
            left: 0;
            height: 3px;
            background: linear-gradient(90deg, #ffcc00, #ff3b30);
            width: 0%;
            transition: width 0.3s linear;
        }

        /* ================= THEME 3: VINYL RECORD ================= */
        .theme-vinyl {
            display: flex;
            align-items: center;
            width: 580px;
            height: 136px;
            padding: 16px;
            border-radius: 20px;
            background: rgba(18, 18, 24, 0.82);
            backdrop-filter: blur(28px) saturate(180%);
            -webkit-backdrop-filter: blur(28px) saturate(180%);
            border: 1px solid rgba(255, 255, 255, 0.12);
            box-shadow: 0 16px 45px rgba(0, 0, 0, 0.55), 0 0 40px var(--ambient-glow, rgba(255, 204, 0, 0.15));
            color: #ffffff;
            position: relative;
            overflow: hidden;
        }

        .theme-vinyl .ambient-bg {
            display: block;
            position: absolute;
            top: -40px;
            left: -40px;
            width: 220px;
            height: 220px;
            border-radius: 50%;
            background: var(--ambient-glow, rgba(255, 204, 0, 0.3));
            filter: blur(65px);
            opacity: 0.45;
            pointer-events: none;
            transition: background 0.8s ease;
        }

        .theme-vinyl .cover-container {
            position: relative;
            width: 104px;
            height: 104px;
            min-width: 104px;
            border-radius: 12px;
            overflow: hidden;
            box-shadow: 4px 0 20px rgba(0, 0, 0, 0.65), 0 8px 24px rgba(0, 0, 0, 0.5);
            border: 1px solid rgba(255, 255, 255, 0.12);
            z-index: 4;
            background: #1c1c24;
            flex-shrink: 0;
        }

        .theme-vinyl .cover-img {
            width: 100%;
            height: 100%;
            object-fit: cover;
            display: block;
        }

        .theme-vinyl .vinyl-disk {
            position: absolute;
            top: 16px;
            left: 74px;
            width: 104px;
            height: 104px;
            border-radius: 50%;
            background: radial-gradient(circle, #2a2a2a 0%, #161616 35%, #0a0a0a 60%, #1a1a1a 85%, #0d0d0d 100%);
            box-shadow: 0 8px 25px rgba(0, 0, 0, 0.7);
            border: 2px solid #242424;
            z-index: 2;
            display: flex;
            align-items: center;
            justify-content: center;
            animation: spin 8s linear infinite;
            animation-play-state: paused;
            transition: transform 0.5s ease;
        }

        .theme-vinyl.playing .vinyl-disk {
            animation-play-state: running;
        }

        .theme-vinyl .vinyl-grooves {
            position: absolute;
            top: 6px;
            left: 6px;
            right: 6px;
            bottom: 6px;
            border-radius: 50%;
            border: 1px dashed rgba(255, 255, 255, 0.12);
            box-shadow: inset 0 0 10px rgba(255, 255, 255, 0.05);
            pointer-events: none;
        }

        .theme-vinyl .vinyl-center {
            width: 40px;
            height: 40px;
            border-radius: 50%;
            overflow: hidden;
            border: 2px solid #333;
            position: relative;
            z-index: 3;
            flex-shrink: 0;
        }

        .theme-vinyl .vinyl-center::after {
            content: '';
            position: absolute;
            top: 50%;
            left: 50%;
            width: 7px;
            height: 7px;
            background: #0d0d12;
            border-radius: 50%;
            transform: translate(-50%, -50%);
            box-shadow: 0 0 2px rgba(0, 0, 0, 0.8);
            z-index: 4;
        }

        .theme-vinyl .vinyl-center img {
            width: 100%;
            height: 100%;
            object-fit: cover;
            display: block;
        }

        .theme-vinyl .info-container {
            margin-left: 78px;
            flex: 1;
            min-width: 0;
            display: flex;
            flex-direction: column;
            justify-content: center;
            height: 100%;
            position: relative;
            z-index: 5;
        }

        /* ================= SHARED TYPOGRAPHY & PROGRESS ================= */
        .top-meta-row {
            display: flex;
            align-items: center;
            gap: 8px;
            margin-bottom: 4px;
        }

        .title-container {
            overflow: hidden;
            white-space: nowrap;
            position: relative;
            width: 100%;
            margin-bottom: 2px;
        }

        .title {
            font-size: 18px;
            font-weight: 700;
            color: #ffffff;
            display: block;
            letter-spacing: -0.2px;
            text-shadow: 0 2px 8px rgba(0, 0, 0, 0.5);
            overflow: hidden;
            text-overflow: ellipsis;
            white-space: nowrap;
        }

        .artist {
            font-size: 13px;
            font-weight: 500;
            color: rgba(255, 255, 255, 0.72);
            overflow: hidden;
            text-overflow: ellipsis;
            white-space: nowrap;
            margin-bottom: 10px;
        }

        .progress-wrapper {
            display: flex;
            align-items: center;
            gap: 8px;
            width: 100%;
        }

        .progress-track {
            flex: 1;
            height: 4px;
            background: rgba(255, 255, 255, 0.15);
            border-radius: 999px;
            overflow: hidden;
            position: relative;
        }

        .progress-fill {
            height: 100%;
            background: linear-gradient(90deg, #ffcc00, #ff5500);
            border-radius: 999px;
            width: 0%;
            box-shadow: 0 0 10px rgba(255, 120, 0, 0.6);
            transition: width 0.3s linear;
        }

        .time-text {
            font-size: 11px;
            font-weight: 600;
            color: rgba(255, 255, 255, 0.6);
            font-variant-numeric: tabular-nums;
            min-width: 28px;
        }

        /* ================= SHARED ELEMENTS & BADGES ================= */
        .badge {
            font-size: 9px;
            font-weight: 800;
            padding: 2px 6px;
            border-radius: 4px;
            text-transform: uppercase;
            letter-spacing: 0.5px;
            display: inline-flex;
            align-items: center;
            line-height: 1;
        }

        .badge-explicit {
            background: rgba(255, 255, 255, 0.18);
            color: #ddd;
        }

        /* Animated Audio Wave Equalizer */
        .equalizer {
            display: inline-flex;
            align-items: flex-end;
            gap: 2.5px;
            height: 12px;
            margin-right: 2px;
        }

        .eq-bar {
            width: 2.5px;
            background: #ffcc00;
            border-radius: 2px;
            height: 3px;
            transition: height 0.2s ease;
        }

        .eq-bar:nth-child(1) { animation: bounce 0.9s ease-in-out infinite alternate; animation-play-state: paused; }
        .eq-bar:nth-child(2) { animation: bounce 1.1s ease-in-out infinite 0.2s alternate; animation-play-state: paused; }
        .eq-bar:nth-child(3) { animation: bounce 0.8s ease-in-out infinite 0.4s alternate; animation-play-state: paused; }
        .eq-bar:nth-child(4) { animation: bounce 1.2s ease-in-out infinite 0.1s alternate; animation-play-state: paused; }

        .playing .eq-bar {
            animation-play-state: running;
        }

        @keyframes bounce {
            0% { height: 2px; }
            100% { height: 12px; }
        }

        @keyframes spin {
            from { transform: rotate(0deg); }
            to { transform: rotate(360deg); }
        }

        /* ================= SMOOTH TRACK TRANSITION ANIMATIONS ================= */
        .anim-exit {
            opacity: 0 !important;
            transform: translateY(-16px) scale(0.97) !important;
            transition: opacity 0.28s ease, transform 0.28s cubic-bezier(0.4, 0, 1, 1) !important;
        }

        .anim-enter {
            opacity: 0;
            transform: translateY(16px) scale(0.97);
            animation: slideInTrack 0.42s cubic-bezier(0.16, 1, 0.3, 1) forwards;
        }

        @keyframes slideInTrack {
            0% {
                opacity: 0;
                transform: translateY(16px) scale(0.97);
            }
            100% {
                opacity: 1;
                transform: translateY(0) scale(1);
            }
        }

        /* Marquee horizontal text scrolling when title is long */
        .marquee-active {
            display: inline-block;
            animation: marqueeScroll 9s linear infinite;
        }

        @keyframes marqueeScroll {
            0% { transform: translateX(0); }
            20% { transform: translateX(0); }
            80% { transform: translateX(calc(-100% + 280px)); }
            100% { transform: translateX(0); }
        }
    </style>
</head>
<body>

    <div id="widget-root" class="theme-glass">
        <!-- Ambient Color Glow -->
        <div class="ambient-bg" id="ambient-glow"></div>

        <!-- Cover Artwork -->
        <div class="cover-container" id="cover-wrap">
            <img id="cover-art" class="cover-img" src="" alt="Album Art">
        </div>

        <!-- Vinyl Disc (Only rendered in vinyl theme) -->
        <div id="vinyl-disk" class="vinyl-disk" style="display: none;">
            <div class="vinyl-grooves"></div>
            <div class="vinyl-center">
                <img id="vinyl-cover" src="" alt="Label">
            </div>
        </div>

        <!-- Details Info -->
        <div class="info-container" id="info-box">
            <div class="top-meta-row" id="top-meta">
                <div class="equalizer">
                    <div class="eq-bar"></div>
                    <div class="eq-bar"></div>
                    <div class="eq-bar"></div>
                    <div class="eq-bar"></div>
                </div>
                <span id="badge-explicit" class="badge badge-explicit" style="display: none;">E</span>
            </div>

            <div class="title-container" id="title-box">
                <span id="track-title" class="title">WinYandex</span>
            </div>

            <div id="track-artist" class="artist">Ожидание трека...</div>

            <div class="progress-wrapper" id="progress-box">
                <span id="time-pos" class="time-text">0:00</span>
                <div class="progress-track">
                    <div id="progress-fill" class="progress-fill"></div>
                </div>
                <span id="time-dur" class="time-text">0:00</span>
            </div>
        </div>

        <!-- Edge progress bar for pill theme -->
        <div id="progress-edge" class="progress-bar-edge" style="display: none;"></div>
    </div>

    <script>
        // Parse URL params for theme customization: ?theme=glass|pill|vinyl, ?hideOnPause=true, ?scale=1.2
        const urlParams = new URLSearchParams(window.location.search);
        const selectedTheme = urlParams.get('theme') || 'glass';
        const hideOnPause = urlParams.get('hideOnPause') === 'true' || urlParams.get('hideOnPause') === '1';
        const scale = parseFloat(urlParams.get('scale') || '1.0');

        const root = document.getElementById('widget-root');
        const coverArt = document.getElementById('cover-art');
        const vinylCover = document.getElementById('vinyl-cover');
        const vinylDisk = document.getElementById('vinyl-disk');
        const titleEl = document.getElementById('track-title');
        const artistEl = document.getElementById('track-artist');
        const badgeExplicit = document.getElementById('badge-explicit');
        const progressFill = document.getElementById('progress-fill');
        const progressEdge = document.getElementById('progress-edge');
        const timePos = document.getElementById('time-pos');
        const timeDur = document.getElementById('time-dur');
        const ambientGlow = document.getElementById('ambient-glow');

        // Apply scale
        if (scale && scale !== 1.0) {
            root.style.transformOrigin = 'top left';
            root.style.transform = `scale(${scale})`;
        }

        // Apply theme classes
        root.className = '';
        if (selectedTheme === 'pill') {
            root.classList.add('theme-pill');
            if (progressEdge) progressEdge.style.display = 'block';
            const progressBox = document.getElementById('progress-box');
            if (progressBox) progressBox.style.display = 'none';
        } else if (selectedTheme === 'vinyl') {
            root.classList.add('theme-vinyl');
            if (vinylDisk) vinylDisk.style.display = 'flex';
        } else {
            root.classList.add('theme-glass');
        }

        let currentTrackId = null;
        let isTransitioning = false;
        let clientPositionMs = 0;
        let clientDurationMs = 0;
        let isPlaying = false;
        let lastServerUpdate = Date.now();

        // 60 FPS smooth progress interpolator
        setInterval(() => {
            if (isPlaying && clientDurationMs > 0) {
                const elapsed = Date.now() - lastServerUpdate;
                const estimatedPos = Math.min(clientPositionMs + elapsed, clientDurationMs);
                const percent = Math.min((estimatedPos / clientDurationMs) * 100, 100);

                if (progressFill) progressFill.style.width = percent + '%';
                if (progressEdge) progressEdge.style.width = percent + '%';
                if (timePos) timePos.textContent = formatTime(estimatedPos);
            }
        }, 100);

        function formatTime(ms) {
            const totalSec = Math.floor(ms / 1000);
            const m = Math.floor(totalSec / 60);
            const s = totalSec % 60;
            return `${m}:${s < 10 ? '0' : ''}${s}`;
        }

        function updateState(data) {
            if (!data) return;

            isPlaying = !!data.isPlaying;
            clientPositionMs = data.positionMs || 0;
            clientDurationMs = data.durationMs || 0;
            lastServerUpdate = Date.now();

            if (isPlaying) {
                root.classList.add('playing');
                root.classList.remove('hidden-pause');
            } else {
                root.classList.remove('playing');
                if (hideOnPause) {
                    root.classList.add('hidden-pause');
                }
            }

            // Check if track changed (trigger smooth broadcast-grade animation)
            if (data.trackId && data.trackId !== currentTrackId) {
                triggerTrackTransition(data);
            } else {
                // Same track: just update progress and duration
                if (timeDur) timeDur.textContent = data.formattedDuration || formatTime(data.durationMs);
            }
        }

        function triggerTrackTransition(data) {
            if (isTransitioning) return;
            isTransitioning = true;
            currentTrackId = data.trackId;

            // Step 1: Smooth Exit Animation
            root.classList.add('anim-exit');

            // Step 2: Preload new album cover image in background to avoid any visual flicker
            const tempImg = new Image();
            tempImg.crossOrigin = 'anonymous';
            tempImg.onload = tempImg.onerror = () => {
                setTimeout(() => {
                    // Update content while hidden
                    applyTrackData(data);

                    // Step 3: Enter Animation
                    root.classList.remove('anim-exit');
                    root.classList.add('anim-enter');

                    setTimeout(() => {
                        root.classList.remove('anim-enter');
                        isTransitioning = false;
                    }, 450);
                }, 220);
            };
            tempImg.src = data.coverUrl || '';
        }

        function applyTrackData(data) {
            titleEl.textContent = data.title || 'Без названия';
            artistEl.textContent = data.artists || 'Неизвестный исполнитель';

            if (data.coverUrl) {
                coverArt.src = data.coverUrl;
                if (vinylCover) vinylCover.src = data.coverUrl;
            } else {
                coverArt.src = 'data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg" width="100" height="100" fill="%23333"><rect width="100" height="100"/></svg>';
            }

            if (badgeExplicit) badgeExplicit.style.display = data.isExplicit ? 'inline-flex' : 'none';

            if (timeDur) timeDur.textContent = data.formattedDuration || formatTime(data.durationMs);

            // Auto-detect marquee if title overflows
            const titleBox = document.getElementById('title-box');
            if (titleBox && titleEl.scrollWidth > titleBox.clientWidth + 10) {
                titleEl.classList.add('marquee-active');
            } else {
                titleEl.classList.remove('marquee-active');
            }
        }

        // Realtime EventSource (SSE) connection
        function connectSse() {
            const evtSource = new EventSource('/events');

            evtSource.onmessage = (e) => {
                try {
                    const data = JSON.parse(e.data);
                    updateState(data);
                } catch (err) { }
            };

            evtSource.onerror = () => {
                evtSource.close();
                // Reconnect with backoff
                setTimeout(connectSse, 2000);
            };
        }

        // Initial fetch + fallback poll
        function fetchTrack() {
            fetch('/api/track')
                .then(res => res.json())
                .then(data => updateState(data))
                .catch(() => {});
        }

        fetchTrack();
        connectSse();
        setInterval(fetchTrack, 2500);
    </script>
</body>
</html>
""";
    }

    private string GetDashboardHtml()
    {
        return $$"""
<!DOCTYPE html>
<html lang="ru">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>WinYandex Overlay Studio</title>
    <link rel="preconnect" href="https://fonts.googleapis.com">
    <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
    <link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&family=JetBrains+Mono:wght@400;500&display=swap" rel="stylesheet">
    <style>
        *, *::before, *::after {
            box-sizing: border-box;
            margin: 0;
            padding: 0;
        }

        :root {
            --bg-page: #0a0a0c;
            --bg-card: #121216;
            --bg-subtle: #181820;
            --border: rgba(255, 255, 255, 0.08);
            --border-hover: rgba(255, 255, 255, 0.16);
            --text-main: #f4f4f5;
            --text-sub: #a1a1aa;
            --accent: #ffcc00;
            --accent-hover: #ffdb33;
        }

        body {
            background-color: var(--bg-page);
            color: var(--text-main);
            font-family: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            min-height: 100vh;
            display: flex;
            flex-direction: column;
            line-height: 1.5;
            -webkit-font-smoothing: antialiased;
        }

        /* Top Minimal Header */
        header {
            height: 52px;
            border-bottom: 1px solid var(--border);
            display: flex;
            align-items: center;
            justify-content: space-between;
            padding: 0 24px;
            background: rgba(10, 10, 12, 0.85);
            backdrop-filter: blur(16px);
            position: sticky;
            top: 0;
            z-index: 100;
        }

        .brand {
            display: flex;
            align-items: center;
            gap: 10px;
            font-size: 14px;
            font-weight: 600;
            color: var(--text-main);
        }

        .brand .badge-tag {
            font-size: 11px;
            font-weight: 500;
            background: var(--bg-subtle);
            border: 1px solid var(--border);
            padding: 2px 7px;
            border-radius: 6px;
            color: var(--text-sub);
        }

        .status-pill {
            display: flex;
            align-items: center;
            gap: 6px;
            font-size: 12px;
            font-weight: 500;
            color: #34d399;
            background: rgba(52, 211, 153, 0.08);
            border: 1px solid rgba(52, 211, 153, 0.2);
            padding: 3px 10px;
            border-radius: 9999px;
            font-family: 'JetBrains Mono', monospace;
        }

        .status-dot {
            width: 6px;
            height: 6px;
            background: #34d399;
            border-radius: 50%;
            box-shadow: 0 0 8px #34d399;
        }

        /* Main Workspace */
        main {
            flex: 1;
            max-width: 860px;
            width: 100%;
            margin: 0 auto;
            padding: 28px 20px 48px;
            display: flex;
            flex-direction: column;
            gap: 16px;
        }

        /* Toolbar */
        .stage-header {
            display: flex;
            align-items: center;
            justify-content: space-between;
            flex-wrap: wrap;
            gap: 12px;
        }

        .segmented-group {
            display: inline-flex;
            background: var(--bg-card);
            padding: 3px;
            border-radius: 10px;
            border: 1px solid var(--border);
        }

        .seg-btn {
            background: transparent;
            color: var(--text-sub);
            border: none;
            padding: 6px 14px;
            border-radius: 7px;
            font-size: 13px;
            font-weight: 500;
            cursor: pointer;
            transition: all 0.15s ease;
            font-family: inherit;
        }

        .seg-btn:hover {
            color: var(--text-main);
        }

        .seg-btn.active {
            background: var(--bg-subtle);
            color: var(--text-main);
            box-shadow: 0 1px 3px rgba(0, 0, 0, 0.3);
            border: 1px solid var(--border);
        }

        .stage-options {
            display: flex;
            align-items: center;
            gap: 8px;
        }

        .toggle-btn {
            background: var(--bg-card);
            border: 1px solid var(--border);
            color: var(--text-sub);
            padding: 6px 12px;
            border-radius: 8px;
            font-size: 12px;
            font-weight: 500;
            cursor: pointer;
            transition: all 0.15s ease;
            display: flex;
            align-items: center;
            gap: 6px;
            font-family: inherit;
        }

        .toggle-btn:hover {
            border-color: var(--border-hover);
            color: var(--text-main);
        }

        .toggle-btn.active {
            background: var(--bg-subtle);
            border-color: rgba(255, 204, 0, 0.35);
            color: var(--accent);
        }

        /* Preview Canvas */
        .preview-canvas {
            width: 100%;
            height: 240px;
            border-radius: 14px;
            border: 1px solid var(--border);
            position: relative;
            overflow: hidden;
            display: flex;
            align-items: center;
            justify-content: center;
            transition: background 0.2s ease;
        }

        .preview-canvas.bg-checker {
            background: repeating-conic-gradient(#111116 0% 25%, #16161c 0% 50%) 50% / 20px 20px;
        }

        .preview-canvas.bg-dark {
            background: #09090b;
        }

        .preview-canvas.bg-chroma {
            background: #00b140;
        }

        iframe {
            width: 100%;
            height: 100%;
            border: none;
        }

        .viewport-badge {
            position: absolute;
            bottom: 10px;
            right: 12px;
            font-family: 'JetBrains Mono', monospace;
            font-size: 11px;
            color: var(--text-sub);
            background: rgba(0, 0, 0, 0.65);
            backdrop-filter: blur(8px);
            padding: 2px 8px;
            border-radius: 6px;
            border: 1px solid var(--border);
            pointer-events: none;
        }

        /* URL & Action Bar */
        .url-panel {
            background: var(--bg-card);
            border: 1px solid var(--border);
            border-radius: 14px;
            padding: 16px;
            display: flex;
            flex-direction: column;
            gap: 12px;
        }

        .url-row {
            display: flex;
            align-items: center;
            gap: 10px;
        }

        .url-input-wrap {
            flex: 1;
            display: flex;
            align-items: center;
            background: #09090b;
            border: 1px solid var(--border);
            border-radius: 8px;
            padding: 0 12px;
            height: 40px;
            transition: border-color 0.15s ease;
        }

        .url-input-wrap:focus-within {
            border-color: rgba(255, 204, 0, 0.4);
        }

        .url-input {
            width: 100%;
            background: transparent;
            border: none;
            outline: none;
            color: #e4e4e7;
            font-family: 'JetBrains Mono', monospace;
            font-size: 13px;
        }

        .action-btn {
            height: 40px;
            padding: 0 18px;
            border-radius: 8px;
            border: none;
            font-size: 13px;
            font-weight: 600;
            cursor: pointer;
            display: flex;
            align-items: center;
            gap: 6px;
            transition: all 0.15s ease;
            font-family: inherit;
            white-space: nowrap;
        }

        .action-btn.primary {
            background: var(--accent);
            color: #000;
        }

        .action-btn.primary:hover {
            background: var(--accent-hover);
        }

        .action-btn.primary.copied {
            background: #34d399;
            color: #000;
        }

        /* Specs Bar */
        .specs-row {
            display: flex;
            align-items: center;
            justify-content: space-between;
            flex-wrap: wrap;
            gap: 8px;
            padding-top: 10px;
            border-top: 1px solid var(--border);
            font-size: 12px;
            color: var(--text-sub);
        }

        .specs-tags {
            display: flex;
            align-items: center;
            gap: 8px;
        }

        .spec-item {
            display: inline-flex;
            align-items: center;
            gap: 4px;
            background: var(--bg-subtle);
            border: 1px solid var(--border);
            padding: 2px 8px;
            border-radius: 6px;
            font-family: 'JetBrains Mono', monospace;
            font-size: 11px;
            color: #e4e4e7;
        }

        /* Clean Setup Steps */
        .guide-grid {
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(240px, 1fr));
            gap: 10px;
        }

        .guide-step {
            background: var(--bg-card);
            border: 1px solid var(--border);
            border-radius: 12px;
            padding: 12px 14px;
            display: flex;
            gap: 10px;
            align-items: flex-start;
        }

        .step-num {
            width: 20px;
            height: 20px;
            border-radius: 5px;
            background: var(--bg-subtle);
            border: 1px solid var(--border);
            font-size: 11px;
            font-weight: 600;
            display: flex;
            align-items: center;
            justify-content: center;
            color: var(--accent);
            flex-shrink: 0;
            margin-top: 1px;
        }

        .step-content h4 {
            font-size: 12px;
            font-weight: 600;
            color: var(--text-main);
            margin-bottom: 2px;
        }

        .step-content p {
            font-size: 11px;
            color: var(--text-sub);
            line-height: 1.4;
        }
    </style>
</head>
<body>

    <header>
        <div class="brand">
            <span>WinYandex</span>
            <span class="badge-tag">OBS Overlay</span>
        </div>
        <div class="status-pill">
            <span class="status-dot"></span>
            <span>:{{Port}}</span>
        </div>
    </header>

    <main>
        <div class="stage-header">
            <div class="segmented-group">
                <button class="seg-btn active" onclick="setTheme('glass')">Glass</button>
                <button class="seg-btn" onclick="setTheme('vinyl')">Vinyl</button>
                <button class="seg-btn" onclick="setTheme('pill')">Pill</button>
            </div>

            <div class="stage-options">
                <button id="btn-pause-toggle" class="toggle-btn" onclick="toggleHideOnPause()">
                    <span>Скрывать при паузе</span>
                </button>
                <button id="btn-bg-toggle" class="toggle-btn" onclick="cycleBg()">
                    <span id="bg-label">Фон: Шахматка</span>
                </button>
            </div>
        </div>

        <div class="preview-canvas bg-checker" id="canvas-wrap">
            <iframe id="preview-frame" src="/widget?theme=glass"></iframe>
            <div class="viewport-badge" id="viewport-res">500 × 136 px</div>
        </div>

        <div class="url-panel">
            <div class="url-row">
                <div class="url-input-wrap">
                    <input type="text" id="widget-url-input" class="url-input" readonly value="http://localhost:{{Port}}/widget?theme=glass">
                </div>
                <button class="action-btn primary" id="copy-btn" onclick="copyWidgetUrl()">
                    <span id="copy-btn-text">Скопировать</span>
                </button>
            </div>

            <div class="specs-row">
                <span id="theme-desc">Стеклянная карточка с размытием и цветным акцентом обложки</span>
                <div class="specs-tags">
                    <span class="spec-item" id="spec-obs-size">520 × 160</span>
                    <span class="spec-item">60 FPS</span>
                    <span class="spec-item">Alpha</span>
                </div>
            </div>
        </div>

        <div class="guide-grid">
            <div class="guide-step">
                <div class="step-num">1</div>
                <div class="step-content">
                    <h4>Источник в OBS</h4>
                    <p>Нажмите «+» в источниках сцены и добавьте «Браузер» (Browser).</p>
                </div>
            </div>
            <div class="guide-step">
                <div class="step-num">2</div>
                <div class="step-content">
                    <h4>Параметры ссылки</h4>
                    <p>Вставьте скопированный URL и укажите рекомендованные размеры.</p>
                </div>
            </div>
            <div class="guide-step">
                <div class="step-num">3</div>
                <div class="step-content">
                    <h4>Готово</h4>
                    <p>Фон прозрачный. Виджет автоматически синхронизируется в реальном времени.</p>
                </div>
            </div>
        </div>
    </main>

    <script>
        const port = {{Port}};
        let currentTheme = 'glass';
        let hideOnPause = false;
        let bgMode = 0; // 0 = checker, 1 = dark, 2 = chroma

        const themeMeta = {
            glass: { res: '500 × 136 px', obsSize: '520 × 160', desc: 'Стеклянная карточка с эффектом размытия и динамической подсветкой' },
            vinyl: { res: '580 × 136 px', obsSize: '600 × 160', desc: 'Единая виниловая дека с выдвигающейся пластинкой и плавным вращением' },
            pill:  { res: '420 × 60 px',  obsSize: '440 × 80',  desc: 'Минималистичная капсула для размещения под веб-камерой' }
        };

        function buildUrl() {
            let u = 'http://localhost:' + port + '/widget?theme=' + currentTheme;
            if (hideOnPause) u += '&hideOnPause=true';
            return u;
        }

        function updateUi() {
            const url = buildUrl();
            document.getElementById('widget-url-input').value = url;
            document.getElementById('preview-frame').src = '/widget?theme=' + currentTheme + (hideOnPause ? '&hideOnPause=true' : '');

            const meta = themeMeta[currentTheme] || themeMeta.glass;
            document.getElementById('viewport-res').textContent = meta.res;
            document.getElementById('spec-obs-size').textContent = meta.obsSize;
            document.getElementById('theme-desc').textContent = meta.desc;
        }

        function setTheme(theme) {
            currentTheme = theme;
            document.querySelectorAll('.seg-btn').forEach(b => {
                b.classList.toggle('active', b.textContent.toLowerCase().includes(theme));
            });
            updateUi();
        }

        function toggleHideOnPause() {
            hideOnPause = !hideOnPause;
            const btn = document.getElementById('btn-pause-toggle');
            btn.classList.toggle('active', hideOnPause);
            updateUi();
        }

        function cycleBg() {
            bgMode = (bgMode + 1) % 3;
            const canvas = document.getElementById('canvas-wrap');
            const label = document.getElementById('bg-label');
            canvas.className = 'preview-canvas';

            if (bgMode === 0) {
                canvas.classList.add('bg-checker');
                label.textContent = 'Фон: Шахматка';
            } else if (bgMode === 1) {
                canvas.classList.add('bg-dark');
                label.textContent = 'Фон: Тёмный';
            } else {
                canvas.classList.add('bg-chroma');
                label.textContent = 'Фон: Хромакей';
            }
        }

        function copyWidgetUrl() {
            const url = document.getElementById('widget-url-input').value;
            navigator.clipboard.writeText(url).then(() => {
                const btn = document.getElementById('copy-btn');
                const text = document.getElementById('copy-btn-text');
                btn.classList.add('copied');
                text.textContent = '✓ Скопировано';
                setTimeout(() => {
                    btn.classList.remove('copied');
                    text.textContent = 'Скопировать';
                }, 2000);
            });
        }
    </script>
</body>
</html>
""";
    }

    #endregion
}

