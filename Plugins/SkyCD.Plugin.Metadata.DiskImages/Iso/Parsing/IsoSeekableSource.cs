using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Plugin.Metadata.DiskImages.Iso.Parsing;

/// <summary>
/// Guarantees a seekable view of an image source. Seekable sources are used directly; forward-only
/// sources are buffered into a delete-on-close temporary file that disappears with the returned stream.
/// </summary>
internal static class IsoSeekableSource
{
    public const int BufferBytes = 81_920;

    /// <summary>
    /// Returns a stream positioned at the start and whether the caller owns it. The caller must dispose
    /// the stream only when <c>Owned</c> is <c>true</c>.
    /// </summary>
    public static async Task<(Stream Stream, bool Owned)> EnsureAsync(Stream source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.CanSeek)
        {
            source.Position = 0;
            return (source, false);
        }

        var path = Path.Combine(Path.GetTempPath(), $"skycd-iso-source-{Guid.NewGuid():N}.tmp");
        var buffer = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
            BufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose);
        try
        {
            await source.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            buffer.Position = 0;
            return (buffer, true);
        }
        catch
        {
            await buffer.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
