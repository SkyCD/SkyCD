using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Archives;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;

namespace SkyCD.Plugin.Zip;

/// <summary>
/// Read-only ZIP archive plugin. Lists archive entries as a catalog hierarchy and safely extracts supported
/// entries into a temporary directory so enabled metadata plugins can index them.
/// </summary>
public sealed class ZipArchiveIndexPlugin : IFileFormatPluginCapability, IArchivePluginCapability
{
    private readonly ArchiveLimits limits;

    public ZipArchiveIndexPlugin() : this(ArchiveLimits.Default)
    {
    }

    public ZipArchiveIndexPlugin(ArchiveLimits limits)
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

    public ArchiveFormatDescriptor SupportedArchive =>
        new(
            FormatId: "skycd-zip",
            DisplayName: "ZIP Archive",
            Extensions: [".zip"],
            MimeTypes: ["application/zip"],
            limits);

    public Task<ArchiveInspectionResult> InspectAsync(ArchiveInspectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ZipArchiveInspector.Inspect(request.Source, limits));
    }

    public Task<ArchiveExtractionResult> ExtractToTemporaryDirectoryAsync(ArchiveExtractionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ZipArchiveInspector.ExtractAsync(request.Source, request.TargetDirectory, limits,
            request.Progress, cancellationToken);
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
