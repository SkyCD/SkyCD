using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.FileFormat.Vhd.Exceptions;
using SkyCD.Plugin.FileFormat.Vhd.Models;

namespace SkyCD.Plugin.FileFormat.Vhd.Import;

/// <summary>
/// Materializes contained files for host indexing one at a time. Each open factory extracts only the
/// requested file into the provider-owned temporary directory, and the returned stream deletes its
/// file as soon as the host disposes it.
/// </summary>
internal static class VhdIndexWorkItems
{
    private const int CopyBufferBytes = 81_920;

    public static async Task<IndexWorkItemSet> CreateAsync(
        IndexWorkItemRequest request,
        VhdIndexLimits limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        limits.Validate();

        var tempRoot = Path.Combine(Path.GetTempPath(), $"skycd-vhd-index-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        VhdImage? image = null;
        var gate = new SemaphoreSlim(1, 1);

        try
        {
            image = await OpenImageAsync(request, cancellationToken).ConfigureAwait(false);
            if (!image.HasFileSystems)
            {
                Cleanup(image, tempRoot);
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
                    token => ExtractAsync(openedImage, captured, tempRoot, gate, limits.MaxEntryBytes, token),
                    captured.ModifiedUtc));
            }

            image = null;
            return new IndexWorkItemSet(items, () =>
            {
                openedImage.Dispose();
                DeleteTemporaryDirectory(tempRoot);
                return ValueTask.CompletedTask;
            });
        }
        catch (OperationCanceledException)
        {
            Cleanup(image, tempRoot);
            throw;
        }
        catch (Exception exception) when (IsImageFailure(exception))
        {
            Cleanup(image, tempRoot);
            return IndexWorkItemSet.Empty;
        }
        catch (Exception)
        {
            Cleanup(image, tempRoot);
            throw;
        }
    }

    private static async Task<VhdImage> OpenImageAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.SourcePath) && File.Exists(request.SourcePath)
            && IsImageName(request.SourcePath))
        {
            return VhdImage.OpenFromPath(request.SourcePath);
        }

        var stream = await request.OpenSource(cancellationToken).ConfigureAwait(false);
        try
        {
            return VhdImage.Open(stream, request.SourcePath);
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<Stream> ExtractAsync(
        VhdImage image,
        VhdFileEntry entry,
        string tempRoot,
        SemaphoreSlim gate,
        long maxEntryBytes,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var path = Path.Combine(tempRoot, $"entry-{Guid.NewGuid():N}.tmp");
        FileStream? output = null;

        try
        {
            output = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                CopyBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose);
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

            TryDeleteFile(path);
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
                    throw new VhdEntryLimitExceededException();
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static bool IsImageName(string name) =>
        string.Equals(Path.GetExtension(name), ".vhd", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Path.GetExtension(name), ".vhdx", StringComparison.OrdinalIgnoreCase);

    private static bool IsImageFailure(Exception exception) =>
        exception is IOException or InvalidDataException or NotSupportedException or ArgumentException
            or UnauthorizedAccessException or NotImplementedException or InvalidOperationException
            or FormatException or OverflowException;

    private static void Cleanup(VhdImage? image, string tempRoot)
    {
        image?.Dispose();
        DeleteTemporaryDirectory(tempRoot);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup.
        }
    }

    /// <summary>Deletes a temporary extraction directory. Cleanup is best-effort and never throws.</summary>
    private static void DeleteTemporaryDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                TryDeleteFile(file);
            }

            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup: leave the rest for OS temp cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup: leave the rest for OS temp cleanup.
        }
    }

    private sealed class ExtractionBudget
    {
        private long totalBytes;

        public bool TryReserve(long bytes, VhdIndexLimits limits)
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
