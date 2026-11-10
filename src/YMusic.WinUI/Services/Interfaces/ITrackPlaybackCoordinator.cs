#nullable enable

using YMusic.WinUI.Models;

namespace YMusic.WinUI.Services.Interfaces;

public interface ITrackPlaybackCoordinator
{
    void Register(IEnumerable<Track> collection);
    void Unregister(IEnumerable<Track> collection);
    void Sync(IEnumerable<Track>? collection = null);
}
