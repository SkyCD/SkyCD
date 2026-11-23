using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.DiskImages.Qcow2.Exceptions;
using SkyCD.Plugin.Metadata.DiskImages.Qcow2.Models;
using SkyCD.Plugin.Metadata.DiskImages.Exceptions;
using SkyCD.Plugin.Metadata.DiskImages.Models;
using SkyCD.Plugin.Storage;

namespace SkyCD.Plugin.Metadata.DiskImages.Qcow2.Import;

/// <summary>
/// Materializes contained files for host indexing one at a time. Each open factory extracts only the
/// requested file into the provider-owned temporary directory, and the returned stream deletes its
/// file as soon as the host disposes it.
/// </summary>
internal static class Qcow2IndexWorkItems
{
    public static async Task<IndexWorkItemSet> CreateAsync(
        IndexWorkItemRequest request,
        DiskImageIndexLimits limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        limits.Validate();

        var workspace = new ArchiveExtractionWorkspace("qcow2");

        Qcow2Image? image = null;
        var gate = new SemaphoreSlim(1, 1);

        try
        {
            image = await OpenImageAsync(request, cancellationToken).ConfigureAwait(false);
            if (!image.HasFileSystems)
            {
                Cleanup(image, workspace);
                return IndexWorkItemSet.Empty;
            }

            var openedImage = image;
            var items = new List<IndexWorkItem>();
            var budget = new ExtractionBudget();
            foreach (var entry in openedImage.EnumerateEntries(limits, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.IsDirectory || !budget.TryReserve(entry.SizeBytes, limits))
                {
                    continue;
                }

                var captured = entry;
                items.Add(new IndexWorkItem(captured.FullPath, captured.SizeBytes,
                    token => ExtractAsync(openedImage, captured, workspace, gate, limits.MaxEntryBytes, token),
                    captured.ModifiedUtc));
            }

            image = null;
            return new IndexWorkItemSet(items, () =>
            {
                openedImage.Dispose();
                workspace.Dispose();
                return ValueTask.CompletedTask;
            });
        }
        catch (OperationCanceledException)
        {
            Cleanup(image, workspace);
            throw;
        }
        catch (Exception exception) when (IsImageFailure(exception))
        {
            Cleanup(image, workspace);
            return IndexWorkItemSet.Empty;
        }
        catch (Exception)
        {
            Cleanup(image, workspace);
            throw;
        }
    }

    private static async Task<Qcow2Image> OpenImageAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.SourcePath) && File.Exists(request.SourcePath)
            && string.Equals(Path.GetExtension(request.SourcePath), ".qcow2", StringComparison.OrdinalIgnoreCase))
        {
            return Qcow2Image.OpenFromPath(request.SourcePath);
        }

        var stream = await request.OpenSource(cancellationToken).ConfigureAwait(false);
        try
        {
            return Qcow2Image.OpenFromStream(stream, ownsStream: true);
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<Stream> ExtractAsync(
        Qcow2Image image,
        Qcow2FileEntry entry,
        ArchiveExtractionWorkspace workspace,
        SemaphoreSlim gate,
        long maxEntryBytes,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var path = workspace.GetPath($"entry-{Guid.NewGuid():N}.tmp");
        FileStream? output = null;

        try
        {
            output = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                NonSeekableSourceBuffer.CopyBufferBytes,
                FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose);
            using var input = image.OpenEntry(entry);
            await CopyBoundedAsync(input, output, maxEntryBytes, cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            output.Position = 0;

            var stream = output;
            output = null;
            return stream;
        }
        catch
        {
            if (output is not null)
            {
                await output.DisposeAsync().ConfigureAwait(false);
            }

            throw;
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task CopyBoundedAsync(
        Stream input,
        Stream output,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(NonSeekableSourceBuffer.CopyBufferBytes);
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
                    throw new DiskImageEntryLimitExceededException();
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static bool IsImageFailure(Exception exception) =>
        exception is IOException or InvalidDataException or NotSupportedException or ArgumentException
            or UnauthorizedAccessException or NotImplementedException or InvalidOperationException
            or FormatException or OverflowException;

    private static void Cleanup(Qcow2Image? image, ArchiveExtractionWorkspace workspace)
    {
        image?.Dispose();
        workspace.Dispose();
    }

    private sealed class ExtractionBudget
    {
        private long totalBytes;

        public bool TryReserve(long bytes, DiskImageIndexLimits limits)
        {
            if (bytes < 0 || bytes > limits.MaxEntryBytes || totalBytes + bytes > limits.MaxTotalBytes)
            {
                return false;
            }

            totalBytes += bytes;
            return true;
        }
    }
}
