#nullable enable

using System.Text.Json;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.Services.Implementations;

public sealed class PlaybackStateStorageService : IPlaybackStateStorageService
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public PlaybackStateStorageService()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string dir = Path.Combine(appData, "YMusic");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "playback_state.json");
    }

    public PlaybackSessionState? LoadPlaybackState()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    return null;
                }

                string json = File.ReadAllText(_filePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return null;
                }

                return JsonSerializer.Deserialize<PlaybackSessionState>(json, JsonOptions);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PlaybackStateStorage] Failed to load state: {ex.Message}");
                return null;
            }
        }
    }

    public void SavePlaybackState(PlaybackSessionState state)
    {
        lock (_lock)
        {
            try
            {
                string json = JsonSerializer.Serialize(state, JsonOptions);
                string tempPath = _filePath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, _filePath, overwrite: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PlaybackStateStorage] Failed to save state: {ex.Message}");
            }
        }
    }

    public void ClearPlaybackState()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    File.Delete(_filePath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PlaybackStateStorage] Failed to clear state: {ex.Message}");
            }
        }
    }
}
