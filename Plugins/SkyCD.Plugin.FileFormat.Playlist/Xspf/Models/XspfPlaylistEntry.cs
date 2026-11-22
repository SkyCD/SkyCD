namespace SkyCD.Plugin.FileFormat.Playlist.Xspf.Models;

public sealed record XspfPlaylistEntry(string Path, string? Title, string? Creator, int? DurationMilliseconds)
{
    public string Location => Path;
}
