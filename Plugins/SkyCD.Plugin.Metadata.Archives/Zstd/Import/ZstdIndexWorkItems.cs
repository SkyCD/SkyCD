using SharpCompress.Compressors.ZStandard;
using System.Buffers;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System;
using SkyCD.SDK.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.Archives.Exceptions;
using SkyCD.Plugin.Metadata.Archives.Models;
using SkyCD.Plugin.Metadata.Archives.Zstd.Parsing;
using SkyCD.Plugin.Storage;

namespace SkyCD.Plugin.Metadata.Archives.Zstd.Import;

/// <summary>
/// Extracts the Zstandard stream into a unique temporary directory with bounded resource usage and
/// exposes it as a host index work item. Disposing the returned set deletes the directory.
/// </summary>
internal static class ZstdIndexWorkItems
{
    private const int CopyBufferBytes = 81_920;

    public static async Task<IndexWorkItemSet> CreateAsync(
        IndexWorkItemRequest request,
        ArchiveIndexLimits limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        limits.Validate();

        var workspace = new ArchiveExtractionWorkspace("zstd");

        Stream? bufferedSource = null;
        try
        {
            await using var source = await request.OpenSource(cancellationToken).ConfigureAwait(false);
            var archiveStream = source;
            if (!source.CanSeek)
            {
                bufferedSource = await NonSeekableSourceBuffer.BufferAsync(source,
                    workspace.GetPath(".skycd-zstd-source"), cancellationToken).ConfigureAwait(false);
                archiveStream = bufferedSource;
            }

            if (ZstdFrameHeader.Read(archiveStream) != ZstdHeaderStatus.Valid)
            {
                workspace.Dispose();
                return IndexWorkItemSet.Empty;
            }

            var name = ArchiveEntryName.DeriveFromFileName(request.FileName, ".tzst", ".zst");
            var fullPath = Path.GetFullPath(Path.Combine(workspace.Root, name));

            long written;
            try
            {
                written = await ExtractAsync(archiveStream, fullPath, limits.MaxEntryBytes, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ArchiveEntryLimitExceededException)
            {
                workspace.Dispose();
                return IndexWorkItemSet.Empty;
            }

            request.Progress?.Report(100);
            var item = new IndexWorkItem(name, written, token => OpenExtractedAsync(fullPath, token));
            return new IndexWorkItemSet([item], () =>
            {
                workspace.Dispose();
                return ValueTask.CompletedTask;
            });
        }
        catch (OperationCanceledException)
        {
            workspace.Dispose();
            throw;
        }
        catch (Exception exception) when (IsArchiveFailure(exception))
        {
            workspace.Dispose();
            return IndexWorkItemSet.Empty;
        }
        catch (Exception)
        {
            workspace.Dispose();
            throw;
        }
        finally
        {
            bufferedSource?.Dispose();
        }
    }

    private static async Task<long> ExtractAsync(
        Stream archiveStream,
        string fullPath,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        archiveStream.Position = 0;
        await using var input = new DecompressionStream(archiveStream, CopyBufferBytes,
            checkEndOfStream: true, leaveOpen: true);
        await using var output = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            CopyBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);

        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferBytes);
        try
        {
            long written = 0;
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                       .ConfigureAwait(false)) > 0)
            {
                written += read;
                if (written > maxBytes)
                {
                    throw new ArchiveEntryLimitExceededException();
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            return written;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static Task<Stream> OpenExtractedAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            CopyBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    private static bool IsArchiveFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException
            or InvalidOperationException or SharpCompress.Common.SharpCompressException
            or ZstdException or System.Security.Cryptography.CryptographicException;

}
