#nullable enable

namespace YMusic.WinUI.Services.Interfaces;

public interface IGameOverlayService : IDisposable
{
    void Initialize();
    void ShowOverlay(string? actionBadge = null);
    void HideOverlay();
}

