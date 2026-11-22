using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.FileFormat.Gzip.Import;
using SkyCD.Plugin.FileFormat.Gzip.Models;
using SkyCD.Plugin.FileFormat.Gzip.Parsing;

namespace SkyCD.Plugin.FileFormat.Gzip;

/// <summary>
/// Read-only gzip plugin. Lists the single compressed member as a catalog entry and, when the host has
/// metadata providers enabled, hooks into the indexing sequence by contributing the safely extracted
/// member as an extra work item.
/// </summary>
public sealed class GzipArchiveIndexPlugin : IFileFormatPluginCapability, IIndexWorkItemProvider
{
    private const string GzipFormatId = "skycd-gzip";
    private readonly GzipIndexLimits limits;

    public GzipArchiveIndexPlugin() : this(GzipIndexLimits.Default)
    {
    }

    public GzipArchiveIndexPlugin(GzipIndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: GzipFormatId,
            DisplayName: "GZIP Archive Index",
            Extensions: [".gz", ".gzip"],
            MimeTypes: ["application/gzip", "application/x-gzip"],
            CanRead: true,
            CanWrite: false);

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsGzipFile(request.FileName))
        {
            return Task.FromResult(IndexWorkItemSet.Empty);
        }

        return GzipIndexWorkItems.CreateAsync(request, limits, cancellationToken);
    }

    public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FileFormatWriteResult
        {
            Success = false,
            Error = "GZIP archive index plugin is read-only."
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
                buffered.Position = 0;
                source = buffered;
            }

            var header = GzipMemberHeader.Read(source);
            switch (header.Status)
            {
                case GzipHeaderStatus.Damaged:
                    return Failure("GZIP_DAMAGED: Unrecognized gzip member header.");
                case GzipHeaderStatus.Unsupported:
                    return Failure("GZIP_UNSUPPORTED: Unsupported gzip compression method.");
            }

            if (source.Length < GzipMemberHeader.MinimumLength)
            {
                return Failure("GZIP_DAMAGED: The gzip member is truncated.");
            }

            var name = GzipEntryName.Resolve(header.OriginalName, request.FileName);
            var leafStart = name.LastIndexOf('/');
            var leafName = leafStart >= 0 ? name[(leafStart + 1)..] : name;
            var size = GzipMemberHeader.TryReadUncompressedSize(source) ?? 0L;

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
                        ["name"] = leafName,
                        ["sizeBytes"] = size.ToString(),
                        ["modifiedUtc"] = header.ModifiedUtc?.UtcDateTime.ToString("O")
                    }
                }
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsArchiveFailure(exception))
        {
            return Failure($"GZIP_DAMAGED: {exception.Message}");
        }
        finally
        {
            buffered?.Dispose();
        }
    }

    private static bool IsGzipFile(string fileName) =>
        (fileName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
         || fileName.EndsWith(".gzip", StringComparison.OrdinalIgnoreCase))
        && !fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
        && !fileName.EndsWith(".tar.gzip", StringComparison.OrdinalIgnoreCase);

    private static FileFormatReadResult Failure(string error) => new()
    {
        Success = false,
        Error = error
    };

    private static bool IsArchiveFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException
            or InvalidOperationException;
}
