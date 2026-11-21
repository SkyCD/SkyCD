using System.Threading;
using SkyCD.Plugin.AudioCd.Models;

namespace SkyCD.Plugin.AudioCd.Parsing;

/// <summary>
/// Reads the table of contents of an audio CD from a drive root. Implementations return <c>null</c> for a data
/// disc, an empty drive, an unsupported platform or any read failure, so a scan is never interrupted by a drive
/// that is not an audio CD.
/// </summary>
public interface IDiscTocReader
{
    /// <summary>Reads the TOC of the audio CD mounted at <paramref name="rootPath"/>, or null when there is none.</summary>
    DiscToc? Read(string rootPath, CancellationToken cancellationToken);
}
