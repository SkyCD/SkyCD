using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Compressors.Xz;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;

namespace SkyCD.Plugin.Xz;

/// <summary>
/// Read-only xz plugin. Lists the single compressed stream as a catalog entry and, when the host has
/// metadata providers enabled, hooks into the indexing sequence by contributing the safely extracted
/// stream as an extra work item.
/// </summary>
public sealed class XzArchiveIndexPlugin : IFileFormatPluginCapability, IIndexWorkItemProvider
{
    private const string XzFormatId = "skycd-xz";
    private const int CopyBufferBytes = 81_920;
    private readonly XzIndexLimits limits;

    public XzArchiveIndexPlugin() : this(XzIndexLimits.Default)
    {
    }

    public XzArchiveIndexPlugin(XzIndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: XzFormatId,
            DisplayName: "XZ Archive Index",
            Extensions: [".xz", ".txz"],
            MimeTypes: ["application/x-xz"],
            CanRead: true,
            CanWrite: false);

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsXzFile(request.FileName))
        {
            return Task.FromResult(IndexWorkItemSet.Empty);
        }

        return XzIndexWorkItems.CreateAsync(request, limits, cancellationToken);
    }

    public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FileFormatWriteResult
        {
            Success = false,
            Error = "XZ archive index plugin is read-only."
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

            switch (XzStreamHeader.Read(source))
            {
                case XzHeaderStatus.Damaged:
                    return Failure("XZ_DAMAGED: Unrecognized xz stream header.");
                case XzHeaderStatus.Unsupported:
                    return Failure("XZ_UNSUPPORTED: Unsupported xz stream flags.");
            }

            // xz stores no uncompressed size in the header; the size is only known after decoding.
            var size = await CountUncompressedAsync(source, cancellationToken).ConfigureAwait(false);
            var name = XzEntryName.Resolve(request.FileName);

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
            return Failure($"XZ_UNSUPPORTED: {exception.Message}");
        }
        catch (Exception exception) when (IsArchiveFailure(exception))
        {
            return Failure($"XZ_DAMAGED: {exception.Message}");
        }
        finally
        {
            buffered?.Dispose();
        }
    }

    private static async Task<long> CountUncompressedAsync(Stream source, CancellationToken cancellationToken)
    {
        source.Position = 0;
        await using var input = new XZStream(source);
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

    private static bool IsXzFile(string fileName) =>
        fileName.EndsWith(".xz", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".txz", StringComparison.OrdinalIgnoreCase);

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
