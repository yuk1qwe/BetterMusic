#nullable enable

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using YMusic.WinUI.Models;

namespace YMusic.WinUI.Services.Interfaces;

public sealed class SloplessTrackResult
{
    public bool IsAi { get; set; }
    public double Score { get; set; }
    public int Percent => (int)System.Math.Round(Score * 100);
}

public sealed class SloplessArtistResult
{
    public bool IsAi { get; set; }
    public int AiTracks { get; set; }
    public int TotalTracks { get; set; }
    public int Percent => TotalTracks > 0 ? (int)System.Math.Round((double)AiTracks / TotalTracks * 100) : 0;
}

public sealed class SloplessEvaluationResult
{
    public bool IsAi { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int Score { get; set; }
    public string Tooltip { get; set; } = string.Empty;
}

public interface ISloplessService
{
    Task<SloplessTrackResult> CheckTrackAsync(string trackId, IEnumerable<string>? artistIds = null, CancellationToken ct = default);
    Task<SloplessArtistResult> CheckArtistAsync(string artistId, double threshold, CancellationToken ct = default);
    Task<SloplessEvaluationResult> EvaluateTrackAsync(Track track, CancellationToken ct = default);
    Task CheckTracksAsync(IEnumerable<Track> tracks, CancellationToken ct = default);
}

