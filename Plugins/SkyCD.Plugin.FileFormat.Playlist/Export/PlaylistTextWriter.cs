using System.IO;
using System.Text;

namespace SkyCD.Plugin.FileFormat.Playlist.Export;

internal static class PlaylistTextWriter
{
    public static StreamWriter Create(Stream target)
    {
        return new StreamWriter(target, new UTF8Encoding(false), leaveOpen: true);
    }
}
