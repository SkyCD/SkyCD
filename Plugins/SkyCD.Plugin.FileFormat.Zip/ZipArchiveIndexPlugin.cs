using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.FileFormat.Zip.Import;
using SkyCD.Plugin.FileFormat.Zip.Models;

namespace SkyCD.Plugin.FileFormat.Zip;

/// <summary>
/// Read-only ZIP archive plugin. Lists archive entries as a catalog hierarchy and, when the host has
/// metadata providers enabled, hooks into the indexing sequence by contributing safely extracted entries
/// as extra work items.
/// </summary>
public sealed class ZipArchiveIndexPlugin : IFileFormatPluginCapability, IIndexWorkItemProvider
{
    private readonly ZipIndexLimits limits;

    public ZipArchiveIndexPlugin() : this(ZipIndexLimits.Default)
    {
    }

    public ZipArchiveIndexPlugin(ZipIndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "skycd-zip",
            DisplayName: "ZIP Archive Index",
            Extensions: [".zip"],
            MimeTypes: ["application/zip"],
            CanRead: true,
            CanWrite: false);

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(Path.GetExtension(request.FileName), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(IndexWorkItemSet.Empty);
        }

        return ZipIndexWorkItems.CreateAsync(request, limits, cancellationToken);
    }

    public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FileFormatWriteResult
        {
            Success = false,
            Error = "ZIP archive index plugin is read-only."
        });
    }

    public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var archive = new ZipArchive(request.Source, ZipArchiveMode.Read, leaveOpen: true);
            var rows = new List<Dictionary<string, object?>>();
            var seenDirectories = new HashSet<string>(StringComparer.Ordinal);

            foreach (var entry in archive.Entries)
            {
                var normalizedPath = entry.FullName.Replace('\\', '/').TrimEnd('/');
                if (string.IsNullOrWhiteSpace(normalizedPath))
                {
                    continue;
                }

                var parts = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var isDirectoryEntry = entry.FullName.EndsWith("/", StringComparison.Ordinal);

                for (var index = 0; index < parts.Length - 1; index++)
                {
                    var directoryPath = string.Join("/", parts.Take(index + 1));
                    if (!seenDirectories.Add(directoryPath))
                    {
                        continue;
                    }

                    rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["kind"] = "folder",
                        ["fullPath"] = directoryPath,
                        ["name"] = parts[index],
                        ["sizeBytes"] = "0",
                        ["modifiedUtc"] = entry.LastWriteTime.UtcDateTime.ToString("O")
                    });
                }

                var leafName = parts[^1];
                if (isDirectoryEntry)
                {
                    if (seenDirectories.Add(normalizedPath))
                    {
                        rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["kind"] = "folder",
                            ["fullPath"] = normalizedPath,
                            ["name"] = leafName,
                            ["sizeBytes"] = "0",
                            ["modifiedUtc"] = entry.LastWriteTime.UtcDateTime.ToString("O")
                        });
                    }
                }
                else
                {
                    rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["kind"] = "file",
                        ["fullPath"] = normalizedPath,
                        ["name"] = leafName,
                        ["sizeBytes"] = entry.Length.ToString(),
                        ["modifiedUtc"] = entry.LastWriteTime.UtcDateTime.ToString("O")
                    });
                }
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
        catch (Exception exception)
        {
            return Task.FromResult(new FileFormatReadResult
            {
                Success = false,
                Error = exception.Message
            });
        }
    }
}
