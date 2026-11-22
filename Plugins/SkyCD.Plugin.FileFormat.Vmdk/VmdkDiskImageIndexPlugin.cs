using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.FileFormat.Vmdk.Import;
using SkyCD.Plugin.FileFormat.Vmdk.Models;

namespace SkyCD.Plugin.FileFormat.Vmdk;

/// <summary>
/// Read-only VMDK disk image plugin. Lists the files and folders stored in the image's supported file
/// systems as a catalog hierarchy and, when the host has metadata providers enabled, contributes one
/// contained file at a time to the indexing sequence.
/// </summary>
public sealed class VmdkDiskImageIndexPlugin : IFileFormatPluginCapability, IIndexWorkItemProvider
{
    private readonly VmdkIndexLimits limits;

    public VmdkDiskImageIndexPlugin() : this(VmdkIndexLimits.Default)
    {
    }

    public VmdkDiskImageIndexPlugin(VmdkIndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "skycd-vmdk",
            DisplayName: "VMDK Disk Image",
            Extensions: [".vmdk"],
            MimeTypes: ["application/x-vmdk", "application/x-virtualbox-vmdk"],
            CanRead: true,
            CanWrite: false);

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsVmdkName(request.FileName) && !IsVmdkName(request.SourcePath))
        {
            return Task.FromResult(IndexWorkItemSet.Empty);
        }

        return VmdkIndexWorkItems.CreateAsync(request, limits, cancellationToken);
    }

    public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FileFormatWriteResult
        {
            Success = false,
            Error = "VMDK disk image plugin is read-only."
        });
    }

    public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            using var image = VmdkImage.Open(request.Source, request.SourcePath);
            if (!image.HasFileSystems)
            {
                return Task.FromResult(new FileFormatReadResult
                {
                    Success = false,
                    Error = "The disk image contains no supported file system."
                });
            }

            var rows = new List<Dictionary<string, object?>>();
            foreach (var entry in image.EnumerateEntries(limits, cancellationToken))
            {
                rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["kind"] = entry.IsDirectory ? "folder" : "file",
                    ["fullPath"] = entry.FullPath,
                    ["name"] = VmdkEntryPath.GetLeafName(entry.FullPath),
                    ["sizeBytes"] = entry.SizeBytes.ToString(),
                    ["modifiedUtc"] = entry.ModifiedUtc?.UtcDateTime.ToString("O") ?? string.Empty
                });
            }

            var ordered = rows
                .OrderBy(row => row["fullPath"]?.ToString(), StringComparer.Ordinal)
                .ThenBy(row => row["kind"]?.ToString(), StringComparer.Ordinal)
                .ToList();

            return Task.FromResult(new FileFormatReadResult
            {
                Success = true,
                Payload = ordered
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Task.FromResult(new FileFormatReadResult
            {
                Success = false,
                Error = exception.Message
            });
        }
    }

    private static bool IsVmdkName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && string.Equals(Path.GetExtension(name), ".vmdk", StringComparison.OrdinalIgnoreCase);
}
