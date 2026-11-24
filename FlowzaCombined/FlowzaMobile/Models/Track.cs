namespace FlowzaMobile.Models;

/// <summary>Same shape as the desktop Track record (Providers.cs), minus the WPF-only ArtThumb property.</summary>
public record Track(string Id, string Title, string Artist, string? Art, string? OpenUri, int DurationMs = 0)
{
    public string DurationText => DurationMs > 0 ? $"{DurationMs / 60000}:{DurationMs / 1000 % 60:00}" : "";
}

public record PlaylistInfo(string Id, string Name, string Snapshot, int TrackCount = 0)
{
    public string CountText => TrackCount == 1 ? "1 song" : $"{TrackCount} songs";
}
