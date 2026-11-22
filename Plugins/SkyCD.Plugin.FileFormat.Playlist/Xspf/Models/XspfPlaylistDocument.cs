using System.Collections.Generic;

namespace SkyCD.Plugin.FileFormat.Playlist.Xspf.Models;

public sealed record XspfPlaylistDocument(IReadOnlyList<XspfPlaylistEntry> Entries);
