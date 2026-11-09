#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.Services.Implementations;

public sealed class UpdateService : IUpdateService
{
    private const string RepoOwner = "pixiemays";
    private const string RepoName = "WinYandex";
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    static UpdateService()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("WinYandex-App/1.0");
        HttpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public static string AppVersion
    {
        get
        {
            var assembly = Assembly.GetExecutingAssembly();
            var infoVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(infoVersion))
            {
                int plusIdx = infoVersion.IndexOf('+');
                return plusIdx > 0 ? infoVersion.Substring(0, plusIdx) : infoVersion;
            }
            var ver = assembly.GetName().Version;
            if (ver == null) return "1.0.0";
            return ver.Revision > 0
                ? $"{ver.Major}.{ver.Minor}.{ver.Build}.{ver.Revision}"
                : $"{ver.Major}.{ver.Minor}.{ver.Build}";
        }
    }

    public string CurrentVersion => AppVersion;

    public async Task<UpdateInfo> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        string currentVer = CurrentVersion;
        var info = new UpdateInfo
        {
            CurrentVersion = currentVer,
            IsUpdateAvailable = false
        };

        try
        {
            string url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
            using var response = await HttpClient.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return info;
            }

            string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string tagName = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? "" : "";
            string cleanTag = tagName.TrimStart('v', 'V');
            info.LatestVersion = cleanTag;

            if (root.TryGetProperty("name", out var nameProp))
            {
                info.ReleaseName = nameProp.GetString() ?? tagName;
            }

            if (root.TryGetProperty("body", out var bodyProp))
            {
                info.ReleaseNotes = bodyProp.GetString() ?? "";
            }

            bool isGenericChangelog = string.IsNullOrWhiteSpace(info.ReleaseNotes) ||
                                     info.ReleaseNotes.Contains("Full Changelog", StringComparison.OrdinalIgnoreCase) ||
                                     info.ReleaseNotes.Trim().StartsWith("**Full Changelog**", StringComparison.OrdinalIgnoreCase);

            if (isGenericChangelog)
            {
                // 1. Try fetching CHANGELOG.md from GitHub raw repository
                try
                {
                    string changelogUrl = $"https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/main/CHANGELOG.md";
                    using var clResponse = await HttpClient.GetAsync(changelogUrl, ct).ConfigureAwait(false);
                    if (clResponse.IsSuccessStatusCode)
                    {
                        string clContent = await clResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        string parsedNotes = ExtractReleaseNotesFromChangelog(clContent, cleanTag);
                        if (!string.IsNullOrWhiteSpace(parsedNotes))
                        {
                            info.ReleaseNotes = parsedNotes;
                            isGenericChangelog = false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[UpdateService] Failed to load CHANGELOG.md from GitHub: {ex.Message}");
                }

                // 2. Fallback to local CHANGELOG.md if available
                if (isGenericChangelog)
                {
                    try
                    {
                        string localChangelog = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CHANGELOG.md");
                        if (File.Exists(localChangelog))
                        {
                            string localContent = File.ReadAllText(localChangelog);
                            string parsedNotes = ExtractReleaseNotesFromChangelog(localContent, cleanTag);
                            if (!string.IsNullOrWhiteSpace(parsedNotes))
                            {
                                info.ReleaseNotes = parsedNotes;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[UpdateService] Failed to load local CHANGELOG.md: {ex.Message}");
                    }
                }
            }

            // 3. Load Russian changelog (from raw CHANGELOG_ru.md on GitHub or local CHANGELOG_ru.md)
            try
            {
                string changelogRuUrl = $"https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/main/CHANGELOG_ru.md";
                using var clRuResponse = await HttpClient.GetAsync(changelogRuUrl, ct).ConfigureAwait(false);
                if (clRuResponse.IsSuccessStatusCode)
                {
                    string clRuContent = await clRuResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    string parsedNotesRu = ExtractReleaseNotesFromChangelog(clRuContent, cleanTag);
                    if (!string.IsNullOrWhiteSpace(parsedNotesRu))
                    {
                        info.ReleaseNotesRu = parsedNotesRu;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateService] Failed to load CHANGELOG_ru.md from GitHub: {ex.Message}");
            }

            if (string.IsNullOrWhiteSpace(info.ReleaseNotesRu))
            {
                try
                {
                    string localChangelogRu = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CHANGELOG_ru.md");
                    if (File.Exists(localChangelogRu))
                    {
                        string localRuContent = File.ReadAllText(localChangelogRu);
                        string parsedNotesRu = ExtractReleaseNotesFromChangelog(localRuContent, cleanTag);
                        if (!string.IsNullOrWhiteSpace(parsedNotesRu))
                        {
                            info.ReleaseNotesRu = parsedNotesRu;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[UpdateService] Failed to load local CHANGELOG_ru.md: {ex.Message}");
                }
            }

            if (root.TryGetProperty("html_url", out var htmlProp))
            {
                info.HtmlUrl = htmlProp.GetString() ?? info.HtmlUrl;
            }

            if (root.TryGetProperty("published_at", out var pubProp) && pubProp.TryGetDateTime(out var pubDate))
            {
                info.PublishedAt = pubDate;
            }

            // Find best asset (prefer .exe installer, fallback to .zip)
            if (root.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
            {
                JsonElement? bestAsset = null;
                foreach (var asset in assetsProp.EnumerateArray())
                {
                    string name = asset.TryGetProperty("name", out var np) ? np.GetString() ?? "" : "";
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        bestAsset = asset;
                        break;
                    }
                    if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && bestAsset == null)
                    {
                        bestAsset = asset;
                    }
                }

                if (bestAsset.HasValue)
                {
                    var a = bestAsset.Value;
                    info.AssetName = a.TryGetProperty("name", out var an) ? an.GetString() : null;
                    info.DownloadUrl = a.TryGetProperty("browser_download_url", out var du) ? du.GetString() : null;
                    info.FileSizeBytes = a.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0;
                }
            }

            info.IsUpdateAvailable = IsNewerVersion(cleanTag, currentVer);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdateService] CheckForUpdates error: {ex.Message}");
        }

        return info;
    }

    public async Task<string> DownloadUpdateAsync(
        UpdateInfo update,
        IProgress<(double Percentage, long DownloadedBytes, long TotalBytes)>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(update.DownloadUrl))
        {
            throw new InvalidOperationException("No download URL available for update.");
        }

        string updatesDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YMusic", "Updates");
        Directory.CreateDirectory(updatesDir);

        // Clean up any old installer files before starting a new download
        try
        {
            foreach (var existingFile in Directory.EnumerateFiles(updatesDir))
            {
                try { File.Delete(existingFile); } catch { }
            }
        }
        catch { }

        string fileName = update.AssetName ?? $"WinYandex-v{update.LatestVersion}-Setup.exe";
        string filePath = Path.Combine(updatesDir, fileName);

        using var response = await HttpClient.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long totalBytes = response.Content.Headers.ContentLength ?? update.FileSizeBytes;

        using var contentStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        byte[] buffer = new byte[81920];
        long downloaded = 0;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, bytesRead, ct).ConfigureAwait(false);
            downloaded += bytesRead;

            if (totalBytes > 0 && progress != null)
            {
                double pct = Math.Clamp((double)downloaded / totalBytes * 100.0, 0.0, 100.0);
                progress.Report((pct, downloaded, totalBytes));
            }
        }

        return filePath;
    }

    public void InstallAndRestart(string installerFilePath)
    {
        throw new InvalidOperationException("Automatic installation is disabled because downloaded updates are not verified.");
    }

    private static bool IsNewerVersion(string latestStr, string currentStr)
    {
        if (string.IsNullOrWhiteSpace(latestStr)) return false;

        try
        {
            var latestVer = ParseSmartVersion(latestStr);
            var currentVer = ParseSmartVersion(currentStr);
            return latestVer > currentVer;
        }
        catch
        {
            // Fallback string comparison
            return !string.Equals(latestStr, currentStr, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static Version ParseSmartVersion(string ver)
    {
        string clean = ver.Trim().TrimStart('v', 'V');
        int dashIdx = clean.IndexOf('-');
        if (dashIdx > 0) clean = clean.Substring(0, dashIdx);

        var parts = clean.Split('.');
        int major = parts.Length > 0 && int.TryParse(parts[0], out int maj) ? maj : 0;
        int minor = parts.Length > 1 && int.TryParse(parts[1], out int min) ? min : 0;
        int patch = 0;
        int sub = 0;

        if (parts.Length > 2 && int.TryParse(parts[2], out int p))
        {
            // If patch is 50..99 (legacy 2-digit format: 51 -> 5.1, 60 -> 6.0, 72 -> 7.2, 80 -> 8.0), decode it
            if (major == 0 && minor == 9 && p >= 50 && p <= 99)
            {
                patch = p / 10;
                sub = p % 10;
                if (parts.Length > 3 && int.TryParse(parts[3], out int s))
                {
                    sub = sub * 10 + s;
                }
            }
            else
            {
                patch = p;
                if (parts.Length > 3 && int.TryParse(parts[3], out int s))
                {
                    sub = s;
                }
            }
        }

        return new Version(major, minor, patch, sub);
    }

    public static string ExtractReleaseNotesFromChangelog(string changelog, string version)
    {
        if (string.IsNullOrWhiteSpace(changelog) || string.IsNullOrWhiteSpace(version))
        {
            return string.Empty;
        }

        string cleanVersion = version.TrimStart('v', 'V');
        string pattern = @"(?ms)^##\s*\[?" + Regex.Escape(cleanVersion) + @"\]?[^\r\n]*\r?\n(.*?)(?=\r?\n---\s*(\r?\n|\Z)|\r?\n##\s*\[|\Z)";
        var match = Regex.Match(changelog, pattern);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        return string.Empty;
    }

    public static void CleanOldUpdates()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                // Wait 5 seconds after startup for any previous installer to finish and release file locks
                await Task.Delay(5000);
                string updatesDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YMusic", "Updates");
                if (Directory.Exists(updatesDir))
                {
                    foreach (var file in Directory.EnumerateFiles(updatesDir))
                    {
                        try
                        {
                            File.Delete(file);
                            Debug.WriteLine($"[UpdateService] Cleaned up old update file: {file}");
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateService] CleanOldUpdates error: {ex.Message}");
            }
        });
    }
}
