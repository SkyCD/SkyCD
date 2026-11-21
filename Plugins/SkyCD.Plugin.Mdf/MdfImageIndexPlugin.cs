using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Mdf.Import;
using SkyCD.Plugin.Mdf.Models;
using SkyCD.Plugin.Mdf.Parsing;

namespace SkyCD.Plugin.Mdf;

/// <summary>
/// Read-only Alcohol 120% disk-image plugin. An image is an MDS descriptor plus an MDF data file; the
/// plugin resolves the pair from either file and lists the ISO 9660/Joliet filesystem stored in the
/// image's data track. When the host has metadata providers enabled, each contained file is
/// contributed as a work item materialized one at a time.
/// </summary>
public sealed class MdfImageIndexPlugin : IFileFormatPluginCapability, IIndexWorkItemProvider
{
    private readonly MdfIndexLimits limits;

    public MdfImageIndexPlugin() : this(MdfIndexLimits.Default)
    {
    }

    public MdfImageIndexPlugin(MdfIndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "skycd-mdf",
            DisplayName: "MDF/MDS Disk Image Index",
            Extensions: [".mds"],
            MimeTypes: ["application/x-alcohol-mds"],
            CanRead: true,
            CanWrite: false);

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsMdfName(request.FileName) && !IsMdfName(request.SourcePath))
        {
            return Task.FromResult(IndexWorkItemSet.Empty);
        }

        return MdfIndexWorkItems.CreateAsync(request, limits, cancellationToken);
    }

    public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FileFormatWriteResult
        {
            Success = false,
            Error = "MDF/MDS disk image index plugin is read-only."
        });
    }

    public async Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var imagePath = MdfCompanion.ResolveImagePath(request.SourcePath, request.FileName);
            var descriptorPath = MdfCompanion.ResolveDescriptorPath(request.SourcePath, request.FileName);
            if (descriptorPath is not null)
            {
                MdsDescriptorReader.Read(descriptorPath);
            }

            var track = MdfDataTrack.Probe(imagePath, new FileInfo(imagePath).Length);
            var entries = MdfFileSystems.ReadEntries(imagePath, track, limits, cancellationToken);
            request.Progress?.Report(100);

            var rows = entries
                .Select(entry => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["kind"] = entry.IsDirectory ? "folder" : "file",
                    ["fullPath"] = entry.Path,
                    ["name"] = entry.Path.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()
                               ?? string.Empty,
                    ["sizeBytes"] = entry.SizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["modifiedUtc"] = entry.ModifiedUtc?.UtcDateTime.ToString("O")
                                      ?? string.Empty
                })
                .OrderBy(row => row["fullPath"]?.ToString(), StringComparer.Ordinal)
                .ThenBy(row => row["kind"]?.ToString(), StringComparer.Ordinal)
                .ToList();

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
        catch (Exception exception)
        {
            return new FileFormatReadResult
            {
                Success = false,
                Error = exception.Message
            };
        }
    }

    private static bool IsMdfName(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        (string.Equals(Path.GetExtension(name), ".mdf", StringComparison.OrdinalIgnoreCase)
         || string.Equals(Path.GetExtension(name), ".mds", StringComparison.OrdinalIgnoreCase));
}
