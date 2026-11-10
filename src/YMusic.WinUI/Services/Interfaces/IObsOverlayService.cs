#nullable enable

namespace YMusic.WinUI.Services.Interfaces;

public interface IObsOverlayService : IDisposable
{
    bool IsRunning { get; }
    int Port { get; }
    string WidgetUrl { get; }

    void Initialize();
    void Start();
    void Stop();
    void Restart();
}

