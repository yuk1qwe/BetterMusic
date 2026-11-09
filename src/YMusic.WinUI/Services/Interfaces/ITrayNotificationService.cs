#nullable enable

using YMusic.WinUI.Models;
using YMusic.WinUI.Views;

namespace YMusic.WinUI.Services.Interfaces;

public interface ITrayNotificationService : IDisposable
{
    bool IsExplicitExit { get; set; }

    void Initialize(ShellWindow shellWindow);

    void UpdateTrack(Track? track, bool isPlaying);

    void ShowNotification(string title, string message);

    void RestoreWindow();

    void MinimizeToTray();

    void InitializeThumbnailButtons();

    void ExitApplication();
}
