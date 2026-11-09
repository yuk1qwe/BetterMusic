#nullable enable

namespace YMusic.WinUI.Services.Interfaces;

public sealed class TrackSpoof
{
    public string? Title { get; set; }
    public string? CoverUri { get; set; }
}

public sealed class AlbumSpoof
{
    public string? CoverUri { get; set; }
    public List<List<long>> Volumes { get; set; } = new();
}

public sealed class ArtistInsertion
{
    public List<ArtistTrackInsertion> Tracks { get; set; } = new();
}

public sealed class ArtistTrackInsertion
{
    public string ReleaseId { get; set; } = string.Empty;
    public int Index { get; set; }
}

public interface IFckCensorService
{
    Task InitializeAsync();

    bool IsReplaced(string trackId);
    string? GetReplacementUrl(string trackId);
    TrackSpoof? GetTrackSpoof(string trackId);
    AlbumSpoof? GetAlbumSpoof(long albumId);
    ArtistInsertion? GetArtistInsertions(long artistId);

    Task<bool> ReplaceTrackWithFileAsync(string trackId, string sourceFilePath);
    Task<bool> RemoveReplacementAsync(string trackId);
    Task<bool> ReportCensorshipAsync(string trackId, bool replaced = false);
    bool IsReported(string trackId);

    event EventHandler<string>? TrackReplacementChanged;
}
