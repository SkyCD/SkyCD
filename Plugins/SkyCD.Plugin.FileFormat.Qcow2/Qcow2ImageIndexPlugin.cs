using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.FileFormat.Qcow2.Import;
using SkyCD.Plugin.FileFormat.Qcow2.Models;

namespace SkyCD.Plugin.FileFormat.Qcow2;

/// <summary>
/// Read-only QCOW2 disk image plugin. Lists the files and folders stored in the image's supported file
/// systems as a catalog hierarchy and, when the host has metadata providers enabled, contributes one
/// contained file at a time to the indexing sequence.
/// </summary>
public sealed class Qcow2ImageIndexPlugin : IFileFormatPluginCapability, IIndexWorkItemProvider
{
    private readonly Qcow2IndexLimits limits;

    public Qcow2ImageIndexPlugin() : this(Qcow2IndexLimits.Default)
    {
    }

    public Qcow2ImageIndexPlugin(Qcow2IndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "skycd-qcow2",
            DisplayName: "QCOW2 Disk Image",
            Extensions: [".qcow2"],
            MimeTypes: ["application/x-qemu-disk", "application/x-qcow2"],
            CanRead: true,
            CanWrite: false);

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsQcow2Name(request.FileName) && !IsQcow2Name(request.SourcePath))
        {
            return Task.FromResult(IndexWorkItemSet.Empty);
        }

        return Qcow2IndexWorkItems.CreateAsync(request, limits, cancellationToken);
    }

    public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FileFormatWriteResult
        {
            Success = false,
            Error = "QCOW2 disk image plugin is read-only."
        });
    }

    public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            using var image = Qcow2Image.Open(request.Source, request.SourcePath);
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
                    ["name"] = Qcow2EntryPath.GetLeafName(entry.FullPath),
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

    private static bool IsQcow2Name(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && string.Equals(Path.GetExtension(name), ".qcow2", StringComparison.OrdinalIgnoreCase);
}
