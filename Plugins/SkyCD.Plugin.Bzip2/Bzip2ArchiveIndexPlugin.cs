using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Compressors;
using SharpCompress.Compressors.BZip2;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;

namespace SkyCD.Plugin.Bzip2;

/// <summary>
/// Read-only bzip2 plugin. Lists the single compressed stream as a catalog entry and, when the host
/// has metadata providers enabled, hooks into the indexing sequence by contributing the safely
/// extracted stream as an extra work item.
/// </summary>
public sealed class Bzip2ArchiveIndexPlugin : IFileFormatPluginCapability, IIndexWorkItemProvider
{
    private const string Bzip2FormatId = "skycd-bzip2";
    private const int CopyBufferBytes = 81_920;
    private readonly Bzip2IndexLimits limits;

    public Bzip2ArchiveIndexPlugin() : this(Bzip2IndexLimits.Default)
    {
    }

    public Bzip2ArchiveIndexPlugin(Bzip2IndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: Bzip2FormatId,
            DisplayName: "BZIP2 Archive Index",
            Extensions: [".bz2", ".bz", ".tbz", ".tbz2"],
            MimeTypes: ["application/x-bzip2"],
            CanRead: true,
            CanWrite: false);

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsBzip2File(request.FileName))
        {
            return Task.FromResult(IndexWorkItemSet.Empty);
        }

        return Bzip2IndexWorkItems.CreateAsync(request, limits, cancellationToken);
    }

    public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FileFormatWriteResult
        {
            Success = false,
            Error = "BZIP2 archive index plugin is read-only."
        });
    }

    public async Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        MemoryStream? buffered = null;
        try
        {
            var source = request.Source;
            if (!source.CanSeek)
            {
                buffered = new MemoryStream();
                await source.CopyToAsync(buffered, cancellationToken).ConfigureAwait(false);
                source = buffered;
            }

            if (!Bzip2StreamHeader.IsValid(source))
            {
                return Failure("BZIP2_DAMAGED: Unrecognized bzip2 stream header.");
            }

            // bzip2 stores no uncompressed size in the header; the size is only known after decoding.
            var size = await CountUncompressedAsync(source, cancellationToken).ConfigureAwait(false);
            var name = Bzip2EntryName.Resolve(request.FileName);

            request.Progress?.Report(100);

            return new FileFormatReadResult
            {
                Success = true,
                Payload = new List<Dictionary<string, object?>>(1)
                {
                    new(StringComparer.OrdinalIgnoreCase)
                    {
                        ["kind"] = "file",
                        ["fullPath"] = name,
                        ["name"] = name,
                        ["sizeBytes"] = size.ToString(),
                        ["modifiedUtc"] = null
                    }
                }
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (NotSupportedException exception)
        {
            return Failure($"BZIP2_UNSUPPORTED: {exception.Message}");
        }
        catch (Exception exception) when (IsArchiveFailure(exception))
        {
            return Failure($"BZIP2_DAMAGED: {exception.Message}");
        }
        finally
        {
            buffered?.Dispose();
        }
    }

    private static async Task<long> CountUncompressedAsync(Stream source, CancellationToken cancellationToken)
    {
        source.Position = 0;
        await using var input = BZip2Stream.Create(source, CompressionMode.Decompress,
            decompressConcatenated: true, leaveOpen: true, tolerateTruncatedStream: false);
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferBytes);
        try
        {
            long total = 0;
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                       .ConfigureAwait(false)) > 0)
            {
                total += read;
            }

            return total;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static bool IsBzip2File(string fileName)
    {
        foreach (var suffix in new[] { ".bz2", ".bz", ".tbz", ".tbz2" })
        {
            if (fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static FileFormatReadResult Failure(string error) => new()
    {
        Success = false,
        Error = error
    };

    private static bool IsArchiveFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException
            or InvalidOperationException or SharpCompress.Common.SharpCompressException
            or System.Security.Cryptography.CryptographicException;
}
