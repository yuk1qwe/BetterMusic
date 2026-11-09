#nullable enable

namespace YMusic.WinUI.Services;

public static class EnvironmentConfig
{
    private static readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private static bool _isLoaded;

    public static string YandexClientId => Get("YANDEX_CLIENT_ID", "23cabbbdc6cd418abb4b39c32c41195d");

    public static string Get(string key, string defaultValue = "")
    {
        EnsureLoaded();

        if (_values.TryGetValue(key, out var val) && !string.IsNullOrWhiteSpace(val))
        {
            return val;
        }

        string? envVal = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrWhiteSpace(envVal))
        {
            return envVal;
        }

        return defaultValue;
    }

    public static void EnsureLoaded()
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;

        var searchDirectories = new List<string>
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory()
        };

        try
        {
            string? current = Directory.GetCurrentDirectory();
            while (!string.IsNullOrEmpty(current))
            {
                if (!searchDirectories.Contains(current))
                {
                    searchDirectories.Add(current);
                }
                var parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
            }
        }
        catch
        {
            // Ignore filesystem walk errors
        }

        try
        {
            var installedLocation = Windows.ApplicationModel.Package.Current?.InstalledLocation?.Path;
            if (!string.IsNullOrWhiteSpace(installedLocation) && !searchDirectories.Contains(installedLocation))
            {
                searchDirectories.Add(installedLocation);
            }
        }
        catch
        {
            // Packaged identity might not be present in unpackaged mode
        }

        foreach (var dir in searchDirectories)
        {
            try
            {
                string envPath = Path.Combine(dir, ".env");
                if (File.Exists(envPath))
                {
                    LoadFromFile(envPath);
                    break;
                }
            }
            catch
            {
                // Continue searching
            }
        }
    }

    private static void LoadFromFile(string filePath)
    {
        foreach (var rawLine in File.ReadAllLines(filePath))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#') || line.StartsWith(';'))
            {
                continue;
            }

            int eqIndex = line.IndexOf('=');
            if (eqIndex <= 0)
            {
                continue;
            }

            string key = line.Substring(0, eqIndex).Trim();
            string val = line.Substring(eqIndex + 1).Trim();

            // Strip optional quotes
            if (val.Length >= 2 && ((val.StartsWith('"') && val.EndsWith('"')) || (val.StartsWith('\'') && val.EndsWith('\''))))
            {
                val = val.Substring(1, val.Length - 2);
            }

            _values[key] = val;
            Environment.SetEnvironmentVariable(key, val);
        }
    }
}
