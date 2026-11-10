#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using YMusic.WinUI.Models;

namespace YMusic.WinUI.Services.Interfaces;

public interface IUpdateService
{
    string CurrentVersion { get; }
    Task<UpdateInfo> CheckForUpdatesAsync(CancellationToken ct = default);
    Task<string> DownloadUpdateAsync(UpdateInfo update, IProgress<(double Percentage, long DownloadedBytes, long TotalBytes)>? progress = null, CancellationToken ct = default);
    void InstallAndRestart(string installerFilePath);
}
