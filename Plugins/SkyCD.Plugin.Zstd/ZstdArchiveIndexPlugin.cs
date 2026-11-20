using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Compressors.ZStandard;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;

namespace SkyCD.Plugin.Zstd;

/// <summary>
/// Read-only Zstandard plugin. Lists the single compressed stream as a catalog entry and, when the host
/// has metadata providers enabled, hooks into the indexing sequence by contributing the safely
/// extracted stream as an extra work item.
/// </summary>
public sealed class ZstdArchiveIndexPlugin : IFileFormatPluginCapability, IIndexWorkItemProvider
{
    private const string ZstdFormatId = "skycd-zstd";
    private const int CopyBufferBytes = 81_920;
    private readonly ZstdIndexLimits limits;

    public ZstdArchiveIndexPlugin() : this(ZstdIndexLimits.Default)
    {
    }

    public ZstdArchiveIndexPlugin(ZstdIndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: ZstdFormatId,
            DisplayName: "Zstandard Archive Index",
            Extensions: [".zst", ".tzst"],
            MimeTypes: ["application/zstd"],
            CanRead: true,
            CanWrite: false);

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsZstdFile(request.FileName))
        {
            return Task.FromResult(IndexWorkItemSet.Empty);
        }

        return ZstdIndexWorkItems.CreateAsync(request, limits, cancellationToken);
    }

    public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FileFormatWriteResult
        {
            Success = false,
            Error = "Zstandard archive index plugin is read-only."
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

            var status = ZstdFrameHeader.Read(source);
            if (status == ZstdHeaderStatus.Unsupported)
            {
                return Failure("ZSTD_UNSUPPORTED: Unsupported Zstandard frame header.");
            }

            if (status != ZstdHeaderStatus.Valid)
            {
                return Failure("ZSTD_DAMAGED: Unrecognized Zstandard frame header.");
            }

            // Zstandard frames may omit the content size; count the payload by decoding once.
            var size = await CountUncompressedAsync(source, cancellationToken).ConfigureAwait(false);
            var name = ZstdEntryName.Resolve(request.FileName);

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
            return Failure($"ZSTD_UNSUPPORTED: {exception.Message}");
        }
        catch (Exception exception) when (IsArchiveFailure(exception))
        {
            return Failure($"ZSTD_DAMAGED: {exception.Message}");
        }
        finally
        {
            buffered?.Dispose();
        }
    }

    private static async Task<long> CountUncompressedAsync(Stream source, CancellationToken cancellationToken)
    {
        source.Position = 0;
        await using var input = new DecompressionStream(source, CopyBufferBytes,
            checkEndOfStream: true, leaveOpen: true);
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

    private static bool IsZstdFile(string fileName)
    {
        foreach (var suffix in new[] { ".zst", ".tzst" })
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
            or ZstdException or System.Security.Cryptography.CryptographicException;
}
