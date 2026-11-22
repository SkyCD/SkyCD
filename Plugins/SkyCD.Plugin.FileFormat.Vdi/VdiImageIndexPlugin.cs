using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.FileFormat.Vdi.Import;
using SkyCD.Plugin.FileFormat.Vdi.Models;

namespace SkyCD.Plugin.FileFormat.Vdi;

/// <summary>
/// Read-only VirtualBox VDI disk image plugin. Lists the files and folders stored in the image's
/// supported file systems as a catalog hierarchy and, when the host has metadata providers enabled,
/// contributes one contained file at a time to the indexing sequence.
/// </summary>
public sealed class VdiImageIndexPlugin : IFileFormatPluginCapability, IIndexWorkItemProvider
{
    private readonly VdiIndexLimits limits;

    public VdiImageIndexPlugin() : this(VdiIndexLimits.Default)
    {
    }

    public VdiImageIndexPlugin(VdiIndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "skycd-vdi",
            DisplayName: "VirtualBox VDI Disk Image",
            Extensions: [".vdi"],
            MimeTypes: ["application/x-virtualbox-vdi", "application/x-vdi"],
            CanRead: true,
            CanWrite: false);

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsImageName(request.FileName) && !IsImageName(request.SourcePath))
        {
            return Task.FromResult(IndexWorkItemSet.Empty);
        }

        return VdiIndexWorkItems.CreateAsync(request, limits, cancellationToken);
    }

    public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FileFormatWriteResult
        {
            Success = false,
            Error = "VDI disk image plugin is read-only."
        });
    }

    public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            using var image = VdiImage.Open(request.Source, request.SourcePath);
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
                    ["name"] = VdiEntryPath.GetLeafName(entry.FullPath),
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

    private static bool IsImageName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && string.Equals(Path.GetExtension(name), ".vdi", StringComparison.OrdinalIgnoreCase);
}
