using System.Collections.Generic;

namespace SkyCD.Plugin.FileFormat.Playlist.M3u.Models;

public sealed record M3uPlaylistDocument(IReadOnlyList<M3uPlaylistEntry> Entries);
