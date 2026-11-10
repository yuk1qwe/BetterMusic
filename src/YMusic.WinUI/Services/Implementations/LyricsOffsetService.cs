#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.Services.Implementations;

public sealed class LyricsOffsetService : ILyricsOffsetService
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private readonly ConcurrentDictionary<string, double> _userOffsets = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<string>? OffsetChanged;

    /// <summary>
    /// Built-in offset corrections for tracks that have album skits / dialogue intros
    /// not present in community LRCLIB timings.
    /// </summary>
    private static readonly Dictionary<string, double> BuiltInOffsets = new(StringComparer.OrdinalIgnoreCase)
    {
        // GONE.Fludd - В ЧЕМ ДЕЛО, ДОК (album version has a 19.5s dialogue intro from Looney Tunes before beat drop)
        ["40498605"] = 19.5,
        ["в чем дело, док:gone.fludd"] = 19.5,
        ["gone.fludd:в чем дело, док"] = 19.5,
        ["в чем дело док:gone.fludd"] = 19.5,
        ["gone.fludd:в чем дело док"] = 19.5,
    };

    public LyricsOffsetService()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string dir = Path.Combine(appData, "YMusic");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "lyrics_offsets.json");

        Load();
    }

    private static string NormalizeKey(string? trackId, string? title, string? artist)
    {
        if (!string.IsNullOrWhiteSpace(trackId))
        {
            string cleanId = trackId.Contains(':') ? trackId.Split(':')[0] : trackId;
            return cleanId.Trim();
        }

        string cleanTitle = (title ?? string.Empty).Trim().ToLowerInvariant();
        string cleanArtist = (artist ?? string.Empty).Trim().ToLowerInvariant();
        return $"{cleanTitle}:{cleanArtist}";
    }

    private void Load()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    string json = File.ReadAllText(_filePath);
                    var dict = JsonSerializer.Deserialize<Dictionary<string, double>>(json);
                    if (dict != null)
                    {
                        foreach (var kvp in dict)
                        {
                            _userOffsets[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LyricsOffsetService] Load error: {ex.Message}");
            }
        }
    }

    private void Save()
    {
        Dictionary<string, double> snapshot;
        lock (_lock)
        {
            snapshot = new Dictionary<string, double>(_userOffsets, StringComparer.OrdinalIgnoreCase);
        }

        _ = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LyricsOffsetService] Save error: {ex.Message}");
            }
        });
    }

    public double GetOffset(string? trackId, string? title = null, string? artist = null)
    {
        string key = NormalizeKey(trackId, title, artist);

        // 1. User custom offset takes precedence
        if (_userOffsets.TryGetValue(key, out double userVal))
        {
            return userVal;
        }

        if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(artist))
        {
            string titleArtistKey = $"{title.Trim().ToLowerInvariant()}:{artist.Trim().ToLowerInvariant()}";
            if (_userOffsets.TryGetValue(titleArtistKey, out double byTitleVal))
            {
                return byTitleVal;
            }
        }

        // 2. Built-in known track corrections
        if (BuiltInOffsets.TryGetValue(key, out double builtInVal))
        {
            return builtInVal;
        }

        if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(artist))
        {
            string titleArtistKey = $"{title.Trim().ToLowerInvariant()}:{artist.Trim().ToLowerInvariant()}";
            if (BuiltInOffsets.TryGetValue(titleArtistKey, out double byTitleBuiltIn))
            {
                return byTitleBuiltIn;
            }

            string reverseKey = $"{artist.Trim().ToLowerInvariant()}:{title.Trim().ToLowerInvariant()}";
            if (BuiltInOffsets.TryGetValue(reverseKey, out double reverseBuiltIn))
            {
                return reverseBuiltIn;
            }
        }

        return 0.0;
    }

    public void SetOffset(string? trackId, string? title, string? artist, double offsetSeconds)
    {
        string key = NormalizeKey(trackId, title, artist);
        if (string.IsNullOrWhiteSpace(key)) return;

        // Round to 1 decimal place (0.1s resolution is ideal for lyrics sync)
        double rounded = Math.Round(offsetSeconds, 1);
        _userOffsets[key] = rounded;

        if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(artist))
        {
            string titleKey = $"{title.Trim().ToLowerInvariant()}:{artist.Trim().ToLowerInvariant()}";
            _userOffsets[titleKey] = rounded;
        }

        Save();
        OffsetChanged?.Invoke(this, key);
    }

    public void ResetOffset(string? trackId, string? title, string? artist)
    {
        string key = NormalizeKey(trackId, title, artist);
        if (string.IsNullOrWhiteSpace(key)) return;

        _userOffsets.TryRemove(key, out _);

        if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(artist))
        {
            string titleKey = $"{title.Trim().ToLowerInvariant()}:{artist.Trim().ToLowerInvariant()}";
            _userOffsets.TryRemove(titleKey, out _);
        }

        Save();
        OffsetChanged?.Invoke(this, key);
    }
}
