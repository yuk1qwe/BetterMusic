#nullable enable

using YMusic.WinUI.Models;

namespace YMusic.WinUI.Services.Interfaces;

public interface IDiscordRpcService : IDisposable
{
    bool IsConnected { get; }

    void Initialize();

    void UpdatePresence(Track? track, bool isPlaying, TimeSpan position, TimeSpan duration);

    void ClearPresence();
}
