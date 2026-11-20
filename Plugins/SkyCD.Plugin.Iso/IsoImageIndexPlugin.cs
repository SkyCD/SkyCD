using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Iso.Exceptions;

namespace SkyCD.Plugin.Iso;

/// <summary>
/// Read-only ISO 9660 and UDF disk-image plugin. Lists the image's files and folders as a catalog
/// hierarchy and, when the host has metadata providers enabled, hooks into the indexing sequence by
/// contributing each stored file as a work item materialized one at a time.
/// </summary>
public sealed class IsoImageIndexPlugin : IFileFormatPluginCapability, IIndexWorkItemProvider
{
    private const string IsoFormatId = "skycd-iso";
    private readonly IsoIndexLimits limits;

    public IsoImageIndexPlugin() : this(IsoIndexLimits.Default)
    {
    }

    public IsoImageIndexPlugin(IsoIndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: IsoFormatId,
            DisplayName: "ISO 9660 / UDF Disk Image Index",
            Extensions: [".iso"],
            MimeTypes: ["application/x-iso9660-image", "application/x-udf"],
            CanRead: true,
            CanWrite: false);

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(Path.GetExtension(request.FileName), ".iso", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(IndexWorkItemSet.Empty);
        }

        return IsoIndexWorkItems.CreateAsync(request, limits, cancellationToken);
    }

    public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FileFormatWriteResult
        {
            Success = false,
            Error = "ISO 9660 / UDF disk image index plugin is read-only."
        });
    }

    public async Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        Stream? owned = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (source, isOwned) = await IsoSeekableSource.EnsureAsync(request.Source, cancellationToken)
                .ConfigureAwait(false);
            if (isOwned)
            {
                owned = source;
            }

            var rows = IsoFileSystems.ReadEntries(source)
                .Select(BuildRow)
                .OrderBy(row => row["fullPath"]?.ToString(), StringComparer.Ordinal)
                .ThenBy(row => row["kind"]?.ToString(), StringComparer.Ordinal)
                .ToList();

            request.Progress?.Report(100);
            return new FileFormatReadResult
            {
                Success = true,
                Payload = rows
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IsoUnsupportedImageException exception)
        {
            return Failure($"ISO_UNSUPPORTED: {exception.Message}");
        }
        catch (Exception exception) when (IsoFileSystems.IsImageFailure(exception))
        {
            return Failure($"ISO_DAMAGED: {exception.Message}");
        }
        finally
        {
            if (owned is not null)
            {
                await owned.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static Dictionary<string, object?> BuildRow(IsoImageEntry entry)
    {
        var name = entry.Path.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty;
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["kind"] = entry.IsDirectory ? "folder" : "file",
            ["fullPath"] = entry.Path,
            ["name"] = name,
            ["sizeBytes"] = entry.SizeBytes.ToString(CultureInfo.InvariantCulture),
            ["modifiedUtc"] = entry.ModifiedUtc?.ToString("O", CultureInfo.InvariantCulture)
        };
    }

    private static FileFormatReadResult Failure(string error) => new()
    {
        Success = false,
        Error = error
    };
}
