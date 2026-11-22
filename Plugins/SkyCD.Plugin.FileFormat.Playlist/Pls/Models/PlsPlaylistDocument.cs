using System.Collections.Generic;

namespace SkyCD.Plugin.FileFormat.Playlist.Pls.Models;

public sealed record PlsPlaylistDocument(IReadOnlyList<PlsPlaylistEntry> Entries);
