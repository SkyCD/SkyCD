using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Plugin.Storage;

/// <summary>
/// Copies a non-seekable index source into the extraction workspace so archive readers that need to seek,
/// or walk the data twice, can read it back from disk.
/// </summary>
internal static class NonSeekableSourceBuffer
{
    /// <summary>Buffer size used for the copy and for both file streams.</summary>
    public const int CopyBufferBytes = 81_920;

    /// <summary>
    /// Writes the remaining content of <paramref name="source"/> to <paramref name="path"/> and returns an
    /// asynchronous read stream over that file. The caller owns the returned stream.
    /// </summary>
    public static async Task<FileStream> BufferAsync(
        Stream source,
        string path,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await using (var buffer = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                         CopyBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await source.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            CopyBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
    }
}
