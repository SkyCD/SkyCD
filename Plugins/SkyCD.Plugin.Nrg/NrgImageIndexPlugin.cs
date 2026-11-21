using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Nrg.Import;
using SkyCD.Plugin.Nrg.Models;
using SkyCD.Plugin.Nrg.Parsing;

namespace SkyCD.Plugin.Nrg;

/// <summary>
/// Read-only Nero NRG disk-image plugin. Lists the ISO 9660/Joliet filesystem stored in the image's data
/// track as a catalog hierarchy and, when metadata providers are enabled, contributes each stored file as
/// a work item materialized one at a time.
/// </summary>
public sealed class NrgImageIndexPlugin : IFileFormatPluginCapability, IIndexWorkItemProvider
{
    private readonly NrgIndexLimits limits;

    public NrgImageIndexPlugin() : this(NrgIndexLimits.Default)
    {
    }

    public NrgImageIndexPlugin(NrgIndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "skycd-nrg",
            DisplayName: "NRG Disk Image Index",
            Extensions: [".nrg"],
            MimeTypes: ["application/x-nrg"],
            CanRead: true,
            CanWrite: false);

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(Path.GetExtension(request.FileName), ".nrg", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(IndexWorkItemSet.Empty);
        }

        return NrgIndexWorkItems.CreateAsync(request, limits, cancellationToken);
    }

    public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FileFormatWriteResult
        {
            Success = false,
            Error = "NRG disk image index plugin is read-only."
        });
    }

    public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rows = NrgFileSystems.ReadEntries(request.Source)
                .Select(entry => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["kind"] = entry.IsDirectory ? "folder" : "file",
                    ["fullPath"] = entry.Path,
                    ["name"] = entry.Path.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()
                               ?? string.Empty,
                    ["sizeBytes"] = entry.SizeBytes.ToString(),
                    ["modifiedUtc"] = entry.ModifiedUtc?.ToString("O")
                })
                .OrderBy(row => row["fullPath"]?.ToString(), StringComparer.Ordinal)
                .ThenBy(row => row["kind"]?.ToString(), StringComparer.Ordinal)
                .ToList();

            return Task.FromResult(new FileFormatReadResult
            {
                Success = true,
                Payload = rows
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or NotSupportedException
                                              or ArgumentException)
        {
            return Task.FromResult(new FileFormatReadResult
            {
                Success = false,
                Error = exception.Message
            });
        }
    }
}
