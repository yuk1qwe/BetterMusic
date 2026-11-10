#nullable enable

using System;

namespace YMusic.WinUI.Services.Interfaces;

public interface ILyricsOffsetService
{
    double GetOffset(string? trackId, string? title = null, string? artist = null);
    void SetOffset(string? trackId, string? title, string? artist, double offsetSeconds);
    void ResetOffset(string? trackId, string? title, string? artist);
    event EventHandler<string>? OffsetChanged;
}
