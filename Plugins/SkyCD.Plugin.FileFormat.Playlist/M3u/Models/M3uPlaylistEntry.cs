namespace SkyCD.Plugin.FileFormat.Playlist.M3u.Models;

public sealed record M3uPlaylistEntry(string Path, string? Title, int? DurationSeconds);
