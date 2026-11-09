#nullable enable

using YMusic.WinUI.Models;

namespace YMusic.WinUI.Services.Interfaces;

public interface IPlaybackStateStorageService
{
    PlaybackSessionState? LoadPlaybackState();
    void SavePlaybackState(PlaybackSessionState state);
    void ClearPlaybackState();
}
