namespace SkyCD.Plugin.FileFormat.Playlist.Pls.Models;

public sealed record PlsPlaylistEntry(string Path, string? Title, long SizeBytes);
