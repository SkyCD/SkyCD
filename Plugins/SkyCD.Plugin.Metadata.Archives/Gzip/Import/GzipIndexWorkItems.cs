using System.Buffers;
using System.Collections.Generic;
using System.IO.Compression;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System;
using SkyCD.SDK.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.Archives.Exceptions;
using SkyCD.Plugin.Metadata.Archives.Gzip.Parsing;
using SkyCD.Plugin.Metadata.Archives.Models;
using SkyCD.Plugin.Storage;

namespace SkyCD.Plugin.Metadata.Archives.Gzip.Import;

/// <summary>
/// Extracts the single gzip member into a unique temporary directory with bounded resource usage and
/// exposes it as a host index work item. Disposing the returned set deletes the directory.
/// </summary>
internal static class GzipIndexWorkItems
{
    private const int CopyBufferBytes = 81_920;

    public static async Task<IndexWorkItemSet> CreateAsync(
        IndexWorkItemRequest request,
        ArchiveIndexLimits limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        limits.Validate();

        var workspace = new ArchiveExtractionWorkspace("gzip");

        Stream? bufferedSource = null;
        try
        {
            await using var source = await request.OpenSource(cancellationToken).ConfigureAwait(false);
            var archiveStream = source;
            if (!source.CanSeek)
            {
                bufferedSource = await NonSeekableSourceBuffer.BufferAsync(source,
                    workspace.GetPath(".skycd-gzip-source"), cancellationToken).ConfigureAwait(false);
                archiveStream = bufferedSource;
            }

            var header = GzipMemberHeader.Read(archiveStream);
            if (header.Status != GzipHeaderStatus.Valid
                || archiveStream.Length < GzipMemberHeader.MinimumLength)
            {
                workspace.Dispose();
                return IndexWorkItemSet.Empty;
            }

            var name = (ArchiveEntryName.TryNormalize(header.OriginalName, out var storedName) ? storedName : ArchiveEntryName.DeriveFromFileName(request.FileName, ".gzip", ".gz"));
            if (!TryResolveTarget(workspace.Root, name, out var fullPath))
            {
                workspace.Dispose();
                return IndexWorkItemSet.Empty;
            }

            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

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
            var item = new IndexWorkItem(name, written, token => OpenExtractedAsync(fullPath, token),
                header.ModifiedUtc);
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

    private static bool TryResolveTarget(string tempRoot, string name, out string fullPath)
    {
        fullPath = string.Empty;
        try
        {
            var candidate = Path.GetFullPath(Path.Combine(tempRoot, name));
            var root = Path.GetFullPath(tempRoot);
            if (!root.EndsWith(Path.DirectorySeparatorChar))
            {
                root += Path.DirectorySeparatorChar;
            }

            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!candidate.StartsWith(root, comparison))
            {
                return false;
            }

            fullPath = candidate;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
        {
            return false;
        }
    }

    private static async Task<long> ExtractAsync(
        Stream archiveStream,
        string fullPath,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        archiveStream.Position = 0;
        using var input = new GZipStream(archiveStream, CompressionMode.Decompress, leaveOpen: true);
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
            or InvalidOperationException;

}
